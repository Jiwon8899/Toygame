using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    public enum ShopCustomerState
    {
        Enter,
        Browse,
        InspectProduct,
        Queue,
        Checkout,
        Leave,
        GiveUp
    }

    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShopCustomerNetwork : NetworkBehaviour
    {
        private static readonly int MovingParameter = Animator.StringToHash("Moving");

        public NetworkVariable<ShopCustomerState> State = new(ShopCustomerState.Enter);
        public NetworkVariable<ShopCustomerType> CustomerType = new(ShopCustomerType.Student);
        public NetworkVariable<int> Budget = new(100);
        public NetworkVariable<int> DesiredProductId = new(-1);
        public NetworkVariable<FixedString64Bytes> DesiredProductName = new(new FixedString64Bytes("찾는 중..."));
        public NetworkVariable<int> Satisfaction = new(0);
        public NetworkVariable<int> AppearanceIndex = new(0);
        public NetworkVariable<FixedString64Bytes> PersistentCustomerId =
            new(new FixedString64Bytes("customer:unassigned"));
        public NetworkVariable<int> PreferredCategory = new((int)ShopProductCategory.Other);

        [Header("Appearance")]
        [SerializeField] private Transform appearanceRoot;
        [SerializeField] private GameObject[] appearancePrefabs;

        [Header("Collision movement")]
        [SerializeField] private CharacterController characterController;
        [Min(0.1f)] [SerializeField] private float obstacleAvoidanceSeconds = 0.55f;

        private float movementSpeed;
        private float patienceSeconds;
        private float stateElapsed;
        private float queueEnteredAt;
        private float matchScore;
        private Vector3 target;
        private ShopCustomerPreference preference;
        private bool giveUpReported;
        private GameObject activeAppearance;
        private Animator visualAnimator;
        private Vector3 lastVisualPosition;
        private float visualSpeed;
        private float avoidanceTimer;
        private float avoidanceSign = 1f;

        public float PatienceSeconds => patienceSeconds;
        public float MatchScore => matchScore;
        public float QueueWaitSeconds => Mathf.Max(0f, Time.time - queueEnteredAt);
        public ShopCustomerPreference Preference => preference;
        public int ActiveAppearanceIndex => AppearanceIndex.Value;
        public string ActiveAppearanceName => activeAppearance != null ? activeAppearance.name : string.Empty;
        public float VisualSpeed => visualSpeed;
        public string CustomerId => PersistentCustomerId.Value.ToString();

#if UNITY_EDITOR
        public void EditorConfigureAppearance(Transform root, GameObject[] prefabs, CharacterController controller)
        {
            appearanceRoot = root;
            appearancePrefabs = prefabs;
            characterController = controller;
        }
#endif

        private void Awake()
        {
            if (characterController == null) characterController = GetComponent<CharacterController>();
            lastVisualPosition = transform.position;
        }

        public override void OnNetworkSpawn()
        {
            AppearanceIndex.OnValueChanged += HandleAppearanceChanged;
            ApplyAppearance(AppearanceIndex.Value);
            lastVisualPosition = transform.position;

            if (IsServer && NetworkManager.NetworkConfig.NetworkTopology == NetworkTopologyTypes.DistributedAuthority)
                NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.SessionOwner, true);
        }

        public override void OnNetworkDespawn()
        {
            AppearanceIndex.OnValueChanged -= HandleAppearanceChanged;
            base.OnNetworkDespawn();
        }

        public void ServerInitialize(ShopCustomerArchetypeDefinition archetype, int budget,
            Vector3 entrance, Vector3 firstBrowsePoint)
        {
            ServerInitialize(archetype, budget, entrance, firstBrowsePoint,
                new ShopCustomerProfileSelection("customer:" + NetworkObjectId,
                    archetype != null ? archetype.PreferredCategory : ShopProductCategory.Other, 0, 70));
        }

        public void ServerInitialize(ShopCustomerArchetypeDefinition archetype, int budget,
            Vector3 entrance, Vector3 firstBrowsePoint, ShopCustomerProfileSelection profile)
        {
            if (!IsServer || archetype == null) return;

            CustomerType.Value = archetype.CustomerType;
            Budget.Value = budget;
            movementSpeed = archetype.MovementSpeed;
            patienceSeconds = archetype.PatienceSeconds;
            PersistentCustomerId.Value = new FixedString64Bytes(profile.CustomerId ?? "customer:unassigned");
            PreferredCategory.Value = (int)profile.PreferredCategory;
            preference = new ShopCustomerPreference(
                budget,
                archetype.PreferredPrice,
                archetype.PriceSensitivity,
                profile.PreferredCategory,
                archetype.RarityPreference,
                archetype.ConditionPreference,
                archetype.GiftPreference);

            if (appearancePrefabs != null && appearancePrefabs.Length > 0)
                AppearanceIndex.Value = Random.Range(0, appearancePrefabs.Length);

            if (characterController != null) characterController.enabled = false;
            transform.position = entrance;
            if (characterController != null) characterController.enabled = true;
            target = firstBrowsePoint;
            lastVisualPosition = transform.position;
            SetState(ShopCustomerState.Enter);
        }

        private void Update()
        {
            UpdateVisualAnimation();
            if (!IsServer || !IsSpawned || ShopNightSalesSystem.Instance == null) return;

            stateElapsed += Time.deltaTime;
            switch (State.Value)
            {
                case ShopCustomerState.Enter:
                    if (MoveTo(target))
                    {
                        target = ShopNightSalesSystem.Instance.ServerGetBrowsePoint(NetworkObjectId);
                        SetState(ShopCustomerState.Browse);
                    }
                    break;
                case ShopCustomerState.Browse:
                    if (MoveTo(target) && stateElapsed >= 1.25f)
                    {
                        target = ShopNightSalesSystem.Instance.ServerGetInspectPoint(NetworkObjectId);
                        SetState(ShopCustomerState.InspectProduct);
                    }
                    break;
                case ShopCustomerState.InspectProduct:
                    if (MoveTo(target) && stateElapsed >= 0.9f)
                    {
                        if (ShopNightSalesSystem.Instance.ServerTrySelectAndReserve(this, out int productId,
                                out string productName, out float score))
                        {
                            DesiredProductId.Value = productId;
                            DesiredProductName.Value = new FixedString64Bytes(productName);
                            matchScore = score;
                            queueEnteredAt = Time.time;
                            ShopNightSalesSystem.Instance.ServerJoinQueue(this);
                            SetState(ShopCustomerState.Queue);
                        }
                        else
                        {
                            ServerGiveUp("No suitable in-budget stock");
                        }
                    }
                    break;
                case ShopCustomerState.Queue:
                    target = ShopNightSalesSystem.Instance.ServerGetQueuePosition(this);
                    MoveTo(target);
                    if (QueueWaitSeconds > patienceSeconds)
                    {
                        ServerGiveUp("Queue patience expired");
                    }
                    break;
                case ShopCustomerState.Checkout:
                    MoveTo(target);
                    break;
                case ShopCustomerState.Leave:
                case ShopCustomerState.GiveUp:
                    target = ShopNightSalesSystem.Instance.ExitPosition;
                    if (MoveTo(target))
                    {
                        ShopNightSalesSystem.Instance.ServerCustomerReachedExit(this);
                    }
                    break;
            }
        }

        public void ServerBeginCheckout(Vector3 checkoutPosition)
        {
            if (!IsServer || State.Value != ShopCustomerState.Queue) return;
            target = checkoutPosition;
            SetState(ShopCustomerState.Checkout);
        }

        public void ServerCompleteCheckout(int satisfaction)
        {
            if (!IsServer || State.Value != ShopCustomerState.Checkout) return;
            Satisfaction.Value = satisfaction;
            target = ShopNightSalesSystem.Instance.ExitPosition;
            SetState(ShopCustomerState.Leave);
        }

        public void ServerGiveUp(string reason)
        {
            if (!IsServer || giveUpReported || State.Value == ShopCustomerState.Leave) return;
            giveUpReported = true;
            ShopNightSalesSystem.Instance.ServerRegisterGiveUp(this, reason);
            DesiredProductName.Value = new FixedString64Bytes("Gave up");
            target = ShopNightSalesSystem.Instance.ExitPosition;
            SetState(ShopCustomerState.GiveUp);
        }

        private void SetState(ShopCustomerState next)
        {
            State.Value = next;
            stateElapsed = 0f;
        }

        private bool MoveTo(Vector3 destination)
        {
            Vector3 before = transform.position;
            Vector3 remaining = destination - before;
            remaining.y = 0f;
            if (remaining.sqrMagnitude <= 0.04f) return true;

            if (characterController == null || !characterController.enabled)
            {
                Debug.LogError("[ShopCustomerNetwork] CharacterController가 없어 충돌 이동을 수행할 수 없습니다.", this);
                return false;
            }

            Vector3 desiredDirection = remaining.normalized;
            if (avoidanceTimer > 0f)
            {
                avoidanceTimer -= Time.deltaTime;
                desiredDirection = Quaternion.Euler(0f, 68f * avoidanceSign, 0f) * desiredDirection;
            }

            float step = Mathf.Min(movementSpeed * Time.deltaTime, remaining.magnitude);
            CollisionFlags collision = characterController.Move(
                desiredDirection * step + Vector3.down * (2f * Time.deltaTime));
            if ((collision & CollisionFlags.Sides) != 0)
            {
                avoidanceTimer = obstacleAvoidanceSeconds;
                avoidanceSign *= -1f;
            }

            Vector3 actualDirection = transform.position - before;
            actualDirection.y = 0f;
            if (actualDirection.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(actualDirection),
                    Time.deltaTime * 10f);
            }

            Vector3 flatRemaining = destination - transform.position;
            flatRemaining.y = 0f;
            return flatRemaining.sqrMagnitude <= 0.04f;
        }

        private void HandleAppearanceChanged(int previous, int current)
        {
            ApplyAppearance(current);
        }

        private void ApplyAppearance(int index)
        {
            if (activeAppearance != null) Destroy(activeAppearance);
            activeAppearance = null;
            visualAnimator = null;

            if (appearanceRoot == null || appearancePrefabs == null ||
                index < 0 || index >= appearancePrefabs.Length || appearancePrefabs[index] == null)
            {
                Debug.LogError("[ShopCustomerNetwork] 고객 외형 설정이 올바르지 않습니다.", this);
                return;
            }

            activeAppearance = Instantiate(appearancePrefabs[index], appearanceRoot);
            activeAppearance.name = $"Person {index + 1}";
            activeAppearance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            visualAnimator = activeAppearance.GetComponentInChildren<Animator>(true);
            if (visualAnimator == null)
            {
                Debug.LogError($"[ShopCustomerNetwork] {activeAppearance.name}에 Animator가 없습니다.", activeAppearance);
                return;
            }

            visualAnimator.applyRootMotion = false;
            visualAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            visualAnimator.SetBool(MovingParameter, false);
        }

        private void UpdateVisualAnimation()
        {
            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 displacement = transform.position - lastVisualPosition;
            displacement.y = 0f;
            visualSpeed = displacement.magnitude / deltaTime;
            lastVisualPosition = transform.position;

            if (visualAnimator != null)
                visualAnimator.SetBool(MovingParameter, visualSpeed > 0.08f);
        }
    }
}
