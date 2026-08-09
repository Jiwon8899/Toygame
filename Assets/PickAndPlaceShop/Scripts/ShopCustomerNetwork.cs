using Unity.Collections;
using Unity.Netcode;
using System.Collections;
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
        public NetworkVariable<int> AppearanceIndex = new(0);
        public NetworkVariable<FixedString512Bytes> DialogueText =
            new(new FixedString512Bytes(string.Empty));
        public NetworkVariable<int> DialogueRevision = new(0);
        public NetworkVariable<bool> IsRobber = new(false);
        public NetworkVariable<int> RobbedProductId = new(-1);
        public NetworkVariable<int> RobbedVisualIndex = new(-1);
        public NetworkVariable<int> HeldProductId = new(-1);
        public NetworkVariable<int> HeldVisualIndex = new(-1);

        [Header("Appearance")]
        [SerializeField] private Transform appearanceRoot;
        [SerializeField] private GameObject[] appearancePrefabs;

        [Header("Collision movement")]
        [SerializeField] private CharacterController characterController;
        [Min(0.1f)] [SerializeField] private float obstacleAvoidanceSeconds = 0.55f;
        [Min(0.25f)] [SerializeField] private float arrivalDistance = 0.72f;

        [Header("Animation stability")]
        [Min(0.01f)] [SerializeField] private float walkEnterSpeed = 0.15f;
        [Min(0f)] [SerializeField] private float idleReturnSpeed = 0.05f;
        [Min(0.05f)] [SerializeField] private float minimumAnimationStateSeconds = 0.2f;

        [Header("Movement recovery")]
        [Min(2f)] [SerializeField] private float movementStateTimeoutSeconds = 12f;
        [Min(2f)] [SerializeField] private float leaveRecoveryTimeoutSeconds = 15f;

        private float movementSpeed;
        private float patienceSeconds;
        private float stateElapsed;
        private float queueEnteredAt;
        private float matchScore;
        private Vector3 target;
        private Vector3 requestedTarget = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        private float nextTargetRefresh;
        private ShopCustomerPreference preference;
        private bool giveUpReported;
        private GameObject activeAppearance;
        private Animator visualAnimator;
        private Vector3 lastVisualPosition;
        private float visualSpeed;
        private float visualStationarySeconds;
        private bool visualMoving;
        private float visualStateChangedAt;
        private float avoidanceTimer;
        private float avoidanceSign = 1f;
        private float collisionStuckSeconds;
        private float navigationNoProgressSeconds;
        private int navigationRouteAttempt;
        private int navigationFallbackStage;
        private Vector3 routeWaypoint;
        private bool hasRouteWaypoint;
        private bool movementCommanded;
        private readonly Collider[] targetOverlapBuffer = new Collider[32];
        private TextMesh robberMarker;
        private bool attackReactionActive;
        private GameObject heldProductVisual;

        public float PatienceSeconds => patienceSeconds;
        public float MatchScore => matchScore;
        public float QueueWaitSeconds => Mathf.Max(0f, Time.time - queueEnteredAt);
        public ShopCustomerPreference Preference => preference;
        public int ActiveAppearanceIndex => AppearanceIndex.Value;
        public string ActiveAppearanceName => activeAppearance != null ? activeAppearance.name : string.Empty;
        public float VisualSpeed => visualSpeed;
        public string CustomerId => "customer:" + NetworkObjectId;

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
            DialogueRevision.OnValueChanged += HandleDialogueChanged;
            IsRobber.OnValueChanged += HandleRobberChanged;
            HeldProductId.OnValueChanged += HandleHeldProductChanged;
            HeldVisualIndex.OnValueChanged += HandleHeldVisualChanged;
            ApplyAppearance(AppearanceIndex.Value);
            HandleRobberChanged(false, IsRobber.Value);
            RefreshHeldProductVisual();
            lastVisualPosition = transform.position;

            if (IsServer && NetworkManager.NetworkConfig.NetworkTopology == NetworkTopologyTypes.DistributedAuthority)
                NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.SessionOwner, true);
        }

        public override void OnNetworkDespawn()
        {
            AppearanceIndex.OnValueChanged -= HandleAppearanceChanged;
            DialogueRevision.OnValueChanged -= HandleDialogueChanged;
            IsRobber.OnValueChanged -= HandleRobberChanged;
            HeldProductId.OnValueChanged -= HandleHeldProductChanged;
            HeldVisualIndex.OnValueChanged -= HandleHeldVisualChanged;
            if (heldProductVisual != null) Destroy(heldProductVisual);
            base.OnNetworkDespawn();
        }

        public void ServerInitialize(ShopCustomerArchetypeDefinition archetype, int budget,
            Vector3 entrance, Vector3 firstBrowsePoint)
        {
            if (!IsServer || archetype == null) return;

            CustomerType.Value = archetype.CustomerType;
            Budget.Value = budget;
            movementSpeed = archetype.MovementSpeed;
            patienceSeconds = archetype.PatienceSeconds;
            preference = new ShopCustomerPreference(
                budget,
                archetype.PreferredPrice,
                archetype.PriceSensitivity,
                ShopProductLocalization.IsCatTheme(archetype.PreferredCategory)
                    ? archetype.PreferredCategory : ShopProductCategory.CatGoods,
                archetype.RarityPreference,
                archetype.ConditionPreference,
                archetype.GiftPreference);

            if (appearancePrefabs != null && appearancePrefabs.Length > 0)
                AppearanceIndex.Value = Random.Range(0, appearancePrefabs.Length);

            if (characterController != null) characterController.enabled = false;
            transform.position = entrance;
            if (characterController != null) characterController.enabled = true;
            SetTarget(firstBrowsePoint);
            lastVisualPosition = transform.position;
            SetState(ShopCustomerState.Enter);
        }

        public void ServerConfigureRobber(bool robber)
        {
            if (!IsServer) return;
            IsRobber.Value = robber;
            if (!robber) return;
            ShopSideContentConfig config = ShopSideContentConfig.Load();
            movementSpeed *= config != null ? config.RobberSpeedMultiplier : 1.65f;
            DialogueText.Value = new FixedString512Bytes("…");
            DialogueRevision.Value++;
            ShopNetworkGame.Instance?.ServerSetEvent("도둑이야! 빨간 느낌표가 붙은 손님을 막으세요!");
            Debug.Log("[SideContent:Robber] spawned customer=" + NetworkObjectId, this);
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || ShopNightSalesSystem.Instance == null)
            {
                UpdateVisualAnimation();
                return;
            }

            movementCommanded = false;
            stateElapsed += Time.deltaTime;
            switch (State.Value)
            {
                case ShopCustomerState.Enter:
                    if (MoveTo(target))
                    {
                        SetTarget(ShopNightSalesSystem.Instance.ServerGetBrowsePoint(NetworkObjectId));
                        TraceSalesFlow("Browse", target);
                        SetState(ShopCustomerState.Browse);
                    }
                    else if (stateElapsed >= movementStateTimeoutSeconds) ApplyNavigationFallback();
                    break;
                case ShopCustomerState.Browse:
                    bool browseReached = MoveTo(target);
                    if (browseReached && stateElapsed >= 1.25f)
                    {
                        SetTarget(ShopNightSalesSystem.Instance.ServerGetInspectPoint(NetworkObjectId));
                        TraceSalesFlow("Inspect", target);
                        SetState(ShopCustomerState.InspectProduct);
                    }
                    else if (stateElapsed >= movementStateTimeoutSeconds) ApplyNavigationFallback();
                    break;
                case ShopCustomerState.InspectProduct:
                    bool inspectReached = MoveTo(target);
                    if (inspectReached && stateElapsed >= 0.9f)
                    {
                        if (ShopNightSalesSystem.Instance.ServerTrySelectAndReserve(this, out int productId,
                                out string productName, out float score))
                        {
                            DesiredProductId.Value = productId;
                            DesiredProductName.Value = new FixedString64Bytes(productName);
                            matchScore = score;
                            if (IsRobber.Value && ShopNightSalesSystem.Instance.ServerRobberSteal(
                                    this, productId, out ShopContainerItem stolen))
                            {
                                RobbedProductId.Value = productId;
                                RobbedVisualIndex.Value = stolen.VisualPrefabIndex;
                                ServerSetHeldProduct(productId, stolen.VisualPrefabIndex);
                                DesiredProductName.Value = new FixedString64Bytes(productName);
                                SetTarget(ShopNightSalesSystem.Instance.ExitPosition);
                                SetState(ShopCustomerState.Leave);
                                ShopNetworkGame.Instance?.ServerSetEvent("도둑이 " + productName +
                                    "을 훔쳐 달아납니다!");
                                Debug.Log("[SideContent:Robber] stole product=" + productId +
                                          " customer=" + NetworkObjectId, this);
                                break;
                            }
                            queueEnteredAt = Time.time;
                            ShopNightSalesSystem.Instance.ServerJoinQueue(this);
                            TraceSalesFlow("Pickup", transform.position);
                            SetState(ShopCustomerState.Queue);
                        }
                        else
                        {
                            ServerGiveUp("No suitable in-budget stock");
                        }
                    }
                    else if (stateElapsed >= movementStateTimeoutSeconds) ApplyNavigationFallback();
                    break;
                case ShopCustomerState.Queue:
                    SetTarget(ShopNightSalesSystem.Instance.ServerGetQueuePosition(this));
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
                    SetTarget(ShopNightSalesSystem.Instance.ExitPosition);
                    if (MoveTo(target))
                    {
                        ShopNightSalesSystem.Instance.ServerCustomerReachedExit(this);
                    }
                    else if (stateElapsed >= leaveRecoveryTimeoutSeconds) ApplyNavigationFallback();
                    break;
            }
            UpdateVisualAnimation();
        }

        public void ServerBeginCheckout(Vector3 checkoutPosition)
        {
            if (!IsServer || State.Value != ShopCustomerState.Queue) return;
            SetTarget(checkoutPosition);
            SetState(ShopCustomerState.Checkout);
        }

        public void ServerCompleteCheckout()
        {
            if (!IsServer || State.Value != ShopCustomerState.Checkout) return;
            ShopLiveOperationsNetwork.Instance?.ServerRequestCustomerDialogue(this,
                ShopCustomerDialogueEvent.PurchaseCompleted,
                DesiredProductName.Value.ToString(), true,
                ShopNightSalesSystem.Instance != null &&
                ShopNightSalesSystem.Instance.ServerIsCategoryDisplayed(Preference.PreferredCategory));
            SetTarget(ShopNightSalesSystem.Instance.ExitPosition);
            SetState(ShopCustomerState.Leave);
        }

        public void ServerGiveUp(string reason)
        {
            if (!IsServer || State.Value == ShopCustomerState.Leave ||
                State.Value == ShopCustomerState.GiveUp) return;
            if (!giveUpReported)
            {
                giveUpReported = true;
                ShopLiveOperationsNetwork.Instance?.ServerRequestCustomerDialogue(this,
                    ShopCustomerDialogueEvent.ExitWithoutPurchase,
                    DesiredProductName.Value.ToString(), false,
                    ShopNightSalesSystem.Instance != null &&
                    ShopNightSalesSystem.Instance.ServerIsCategoryDisplayed(Preference.PreferredCategory));
                ShopNightSalesSystem.Instance.ServerRegisterGiveUp(this, reason);
            }
            DesiredProductName.Value = new FixedString64Bytes("Gave up");
            SetTarget(ShopNightSalesSystem.Instance.ExitPosition);
            SetState(ShopCustomerState.GiveUp);
        }

        public void ServerRecoverTo(Vector3 safePosition)
        {
            if (!IsServer) return;
            if (characterController != null) characterController.enabled = false;
            transform.position = safePosition;
            if (characterController != null) characterController.enabled = true;
            avoidanceTimer = 0f;
            lastVisualPosition = safePosition;
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        public bool ServerApplyPlayerAttack(ulong attackerClientId, Vector3 direction)
        {
            if (!IsServer || attackReactionActive || State.Value == ShopCustomerState.Checkout) return false;
            StartCoroutine(ServerAttackReaction(attackerClientId,
                Vector3.ProjectOnPlane(direction, Vector3.up).normalized));
            return true;
        }

        private IEnumerator ServerAttackReaction(ulong attackerClientId, Vector3 direction)
        {
            attackReactionActive = true;
            ShopSideContentConfig settings = ShopSideContentConfig.Load();
            float duration = settings != null ? settings.CustomerKnockbackSeconds : 0.35f;
            float distance = settings != null ? settings.CustomerKnockbackDistance : 2.2f;
            if (direction.sqrMagnitude < 0.01f) direction = -transform.forward;
            Vector3 start = transform.position;
            Vector3 end = start + direction * distance;
            bool useController = characterController != null && characterController.enabled;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Vector3 desired = Vector3.Lerp(start, end, 1f - (1f - t) * (1f - t)) +
                                  Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.45f;
                if (useController) characterController.Move(desired - transform.position);
                else transform.position = desired;
                yield return null;
            }
            if (useController) characterController.Move(end - transform.position);
            else transform.position = end;

            if (IsRobber.Value)
            {
                ShopProductDefinition product = ShopProductVisuals.Find(RobbedProductId.Value);
                ShopNetworkGame game = ShopNetworkGame.Instance;
                bool recovered = game != null && product != null && game.ServerTryAcquireSharedContainer(
                    product, RobbedVisualIndex.Value, ShopContainerKind.SharedStorage,
                    game.SharedStorageCapacity);
                if (recovered)
                {
                    int reward = settings != null ? settings.RobberArrestReward : 300;
                    game.Coins.Value += reward;
                    game.ServerSetEvent("강도 검거! 상품을 창고로 회수하고 +" + reward + "원을 받았습니다.");
                }
                Debug.Log("[SideContent:RobberArrest] customer=" + NetworkObjectId +
                          " recovered=" + recovered, this);
            }
            else
            {
                ShopNightSalesSystem.Instance?.ServerRegisterAttackExit(this);
                DesiredProductName.Value = new FixedString64Bytes("구매 포기");
                SetTarget(ShopNightSalesSystem.Instance.ExitPosition);
                SetState(ShopCustomerState.GiveUp);
                Debug.Log("[SideContent:CustomerHit] normal customer left without extra penalty", this);
            }
            yield return new WaitForSeconds(0.35f);
            if (this != null && IsSpawned)
                ShopNightSalesSystem.Instance?.ServerCustomerReachedExit(this);
        }

        private void HandleRobberChanged(bool previous, bool current)
        {
            if (!current)
            {
                if (robberMarker != null) Destroy(robberMarker.gameObject);
                return;
            }
            if (robberMarker != null) return;
            GameObject marker = new("RobberWarningMarker");
            marker.transform.SetParent(transform, false);
            marker.transform.localPosition = Vector3.up * 2.25f;
            robberMarker = marker.AddComponent<TextMesh>();
            robberMarker.text = "!";
            robberMarker.anchor = TextAnchor.MiddleCenter;
            robberMarker.alignment = TextAlignment.Center;
            robberMarker.fontSize = 96;
            robberMarker.characterSize = 0.12f;
            robberMarker.color = Color.red;
            marker.AddComponent<ShopWorldTextBillboard>();
        }

        private void SetState(ShopCustomerState next)
        {
            State.Value = next;
            stateElapsed = 0f;
            navigationFallbackStage = 0;
        }

        private void TraceSalesFlow(string stage, Vector3 position)
        {
            if (!Debug.isDebugBuild) return;
            Debug.Log($"[SalesFlow:{stage}] customer={NetworkObjectId} state={State.Value} position={position}", this);
        }

        private bool MoveTo(Vector3 destination)
        {
            Vector3 before = transform.position;
            Vector3 remaining = destination - before;
            remaining.y = 0f;
            float arrivalSqr = arrivalDistance * arrivalDistance;
            if (remaining.sqrMagnitude <= arrivalSqr) return true;
            movementCommanded = true;

            if (characterController == null || !characterController.enabled)
            {
                Debug.LogError("[ShopCustomerNetwork] CharacterController가 없어 충돌 이동을 수행할 수 없습니다.", this);
                return false;
            }

            if (hasRouteWaypoint)
            {
                Vector3 toWaypoint = routeWaypoint - before;
                toWaypoint.y = 0f;
                if (toWaypoint.sqrMagnitude <= 0.3f * 0.3f) RefreshRoute(destination);
            }
            Vector3 movementTarget = hasRouteWaypoint ? routeWaypoint : destination;
            Vector3 movementRemaining = movementTarget - before;
            movementRemaining.y = 0f;
            Vector3 desiredDirection = movementRemaining.sqrMagnitude > 0.001f
                ? movementRemaining.normalized
                : remaining.normalized;
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
                collisionStuckSeconds += Time.deltaTime;
                if (avoidanceTimer <= 0f || collisionStuckSeconds >= 0.3f)
                {
                    avoidanceSign *= -1f;
                    avoidanceTimer = obstacleAvoidanceSeconds;
                    collisionStuckSeconds = 0f;
                }
            }

            Vector3 actualDirection = transform.position - before;
            actualDirection.y = 0f;
            if (actualDirection.sqrMagnitude > 0.0001f)
            {
                collisionStuckSeconds = 0f;
                navigationNoProgressSeconds = 0f;
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(actualDirection),
                    Time.deltaTime * 10f);
            }
            else
            {
                navigationNoProgressSeconds += Time.deltaTime;
                if (navigationNoProgressSeconds >= 1.1f)
                {
                    navigationNoProgressSeconds = 0f;
                    navigationRouteAttempt++;
                    RefreshRoute(destination);
                    if (navigationRouteAttempt >= 3) ApplyNavigationFallback();
                }
            }

            Vector3 flatRemaining = destination - transform.position;
            flatRemaining.y = 0f;
            return flatRemaining.sqrMagnitude <= arrivalSqr;
        }

        private void SetTarget(Vector3 requested)
        {
            if ((requested - requestedTarget).sqrMagnitude <= 0.0025f &&
                Time.time < nextTargetRefresh) return;
            requestedTarget = requested;
            nextTargetRefresh = Time.time + 0.5f;
            target = ResolveCollisionSafeTarget(requested);
            navigationNoProgressSeconds = 0f;
            navigationRouteAttempt = 0;
            RefreshRoute(target);
        }

        private void RefreshRoute(Vector3 destination)
        {
            float radius = characterController != null ? characterController.radius : 0.3f;
            float height = characterController != null ? characterController.height : 1.8f;
            hasRouteWaypoint = ShopNpcRoutePlanner.TryGetNextWaypoint(transform.position, destination,
                radius, height, navigationRouteAttempt, transform, out routeWaypoint,
                out ShopNpcRouteStatus status);
            if (hasRouteWaypoint && status == ShopNpcRouteStatus.Direct) hasRouteWaypoint = false;
            if (Debug.isDebugBuild && status != ShopNpcRouteStatus.Direct &&
                status != ShopNpcRouteStatus.NavMeshComplete)
                Debug.Log("[CustomerNavigation] route=" + status + " attempt=" +
                          navigationRouteAttempt + " waypoint=" + routeWaypoint.ToString("F2"), this);
        }

        private void ApplyNavigationFallback()
        {
            if (!IsServer || ShopNightSalesSystem.Instance == null) return;
            stateElapsed = 0f;
            navigationNoProgressSeconds = 0f;
            navigationRouteAttempt = 0;
            navigationFallbackStage++;
            switch (State.Value)
            {
                case ShopCustomerState.Enter:
                case ShopCustomerState.Browse:
                    if (navigationFallbackStage == 1)
                    {
                        SetTarget(ShopNightSalesSystem.Instance.ServerGetBrowsePoint(
                            NetworkObjectId + 7919UL));
                        return;
                    }
                    ServerGiveUp("Navigation route unavailable");
                    return;
                case ShopCustomerState.InspectProduct:
                    if (navigationFallbackStage == 1)
                    {
                        SetTarget(ShopNightSalesSystem.Instance.ServerGetInspectPoint(
                            NetworkObjectId + 7919UL));
                        return;
                    }
                    ServerGiveUp("Product area unreachable");
                    return;
                case ShopCustomerState.Queue:
                case ShopCustomerState.Checkout:
                    ServerGiveUp("Checkout route unavailable");
                    return;
                case ShopCustomerState.Leave:
                case ShopCustomerState.GiveUp:
                    TraceSalesFlow("SafeExitFallback", ShopNightSalesSystem.Instance.ExitPosition);
                    ShopNightSalesSystem.Instance.ServerCustomerReachedExit(this);
                    return;
            }
        }

        private Vector3 ResolveCollisionSafeTarget(Vector3 requested)
        {
            if (characterController == null) return requested;
            float radius = Mathf.Max(0.1f, characterController.radius);
            float height = Mathf.Max(radius * 2f, characterController.height);
            const float radialStep = 0.3f;
            const float maximumCorrection = 3f;
            Vector3 best = requested;
            float bestScore = float.MaxValue;
            for (float correction = 0f; correction <= maximumCorrection; correction += radialStep)
            {
                int directions = correction <= 0.001f ? 1 : 12;
                for (int directionIndex = 0; directionIndex < directions; directionIndex++)
                {
                    float angle = directions == 1 ? 0f : directionIndex * (360f / directions);
                    Vector3 offset = directions == 1
                        ? Vector3.zero
                        : Quaternion.Euler(0f, angle, 0f) * Vector3.forward * correction;
                    Vector3 candidate = requested + offset;
                    Vector3 bottom = candidate + Vector3.up * radius;
                    Vector3 top = candidate + Vector3.up * Mathf.Max(radius, height - radius);
                    int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius + 0.06f,
                        targetOverlapBuffer, ~0, QueryTriggerInteraction.Ignore);
                    bool blocked = false;
                    for (int i = 0; i < count; i++)
                    {
                        Collider hit = targetOverlapBuffer[i];
                        if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform) ||
                            hit.GetComponentInParent<ShopCustomerNetwork>() != null ||
                            hit.bounds.max.y <= candidate.y + 0.12f) continue;
                        blocked = true;
                        break;
                    }
                    if (blocked) continue;
                    float score = correction * 2f + Vector3.Distance(transform.position, candidate);
                    if (score >= bestScore) continue;
                    best = candidate;
                    bestScore = score;
                }
                if (bestScore < float.MaxValue) break;
            }
            if (bestScore < float.MaxValue)
            {
                if ((best - requested).sqrMagnitude > 0.01f && Debug.isDebugBuild)
                    Debug.Log("[CustomerNavigation] target corrected radially by " +
                              Vector3.Distance(best, requested).ToString("F2") + "m", this);
                return best;
            }

            Debug.LogWarning("[CustomerNavigation] no free target around requested point; " +
                             "preserving destination so movement cannot report false arrival", this);
            return requested;
        }

        private void HandleAppearanceChanged(int previous, int current)
        {
            ApplyAppearance(current);
        }

        public void ServerSetHeldProduct(int productId, int visualIndex)
        {
            if (!IsServer) return;
            HeldVisualIndex.Value = visualIndex;
            HeldProductId.Value = productId;
            RefreshHeldProductVisual();
        }

        private void HandleHeldProductChanged(int previous, int current) => RefreshHeldProductVisual();

        private void HandleHeldVisualChanged(int previous, int current) => RefreshHeldProductVisual();

        public void ServerSetDialogue(string message)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(message)) return;
            DialogueText.Value = new FixedString512Bytes(message);
            DialogueRevision.Value++;
        }

        private void HandleDialogueChanged(int previous, int current)
        {
            if (current <= previous || DialogueText.Value.Length == 0) return;
            ShopOperationsConfig config = ShopOperationsConfig.Load();
            ShopCustomerDialogueBubble.Show(transform, DialogueText.Value.ToString(),
                config != null ? config.DialogueBubbleSeconds : 3f,
                config != null ? config.MaximumDialogueBubbles : 2);
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
            visualMoving = false;
            visualStationarySeconds = 0f;
            visualStateChangedAt = Time.unscaledTime;
            RefreshHeldProductVisual();
        }

        private void RefreshHeldProductVisual()
        {
            if (heldProductVisual != null) Destroy(heldProductVisual);
            heldProductVisual = null;
            if (HeldProductId.Value < 0 || activeAppearance == null) return;

            ShopProductDefinition product = ShopProductVisuals.Find(HeldProductId.Value);
            Transform hand = FindRightHand(activeAppearance.transform);
            if (product == null || hand == null) return;
            heldProductVisual = ShopProductVisuals.Instantiate(product, hand);
            if (heldProductVisual == null) return;
            heldProductVisual.name = "Customer Held Product";

            Renderer[] renderers = heldProductVisual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                if (longest > 0.001f)
                    heldProductVisual.transform.localScale *= 0.26f / longest;
            }
            heldProductVisual.transform.SetLocalPositionAndRotation(
                new Vector3(0.04f, 0.02f, 0.08f), Quaternion.Euler(20f, 90f, 0f));
        }

        private static Transform FindRightHand(Transform root)
        {
            Animator animator = root != null ? root.GetComponentInChildren<Animator>(true) : null;
            if (animator != null && animator.isHuman)
            {
                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand != null) return hand;
            }
            if (root == null) return null;
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                if (candidate.name.IndexOf("RightHand", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    candidate.name.IndexOf("Hand_R", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return candidate;
            return null;
        }

        private void UpdateVisualAnimation()
        {
            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 displacement = transform.position - lastVisualPosition;
            displacement.y = 0f;
            float measuredSpeed = displacement.magnitude / deltaTime;
            if (IsServer && characterController != null && characterController.enabled)
            {
                Vector3 controllerVelocity = characterController.velocity;
                controllerVelocity.y = 0f;
                measuredSpeed = controllerVelocity.magnitude;
            }
            visualSpeed = Mathf.MoveTowards(visualSpeed, measuredSpeed, deltaTime * 8f);
            lastVisualPosition = transform.position;
            float heldSeconds = Time.unscaledTime - visualStateChangedAt;
            bool explicitWalk = !IsServer || movementCommanded;
            if (!visualMoving && explicitWalk && measuredSpeed >= walkEnterSpeed &&
                heldSeconds >= minimumAnimationStateSeconds)
            {
                visualMoving = true;
                visualStationarySeconds = 0f;
                visualStateChangedAt = Time.unscaledTime;
            }
            else if (visualMoving && (!explicitWalk || measuredSpeed <= idleReturnSpeed))
            {
                visualStationarySeconds += deltaTime;
                if (visualStationarySeconds >= minimumAnimationStateSeconds &&
                    heldSeconds >= minimumAnimationStateSeconds)
                {
                    visualMoving = false;
                    visualStateChangedAt = Time.unscaledTime;
                }
            }
            else if (measuredSpeed > idleReturnSpeed) visualStationarySeconds = 0f;

            if (visualAnimator != null) visualAnimator.SetBool(MovingParameter, visualMoving);
        }
    }
}
