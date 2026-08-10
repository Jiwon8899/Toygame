using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace PickAndPlaceShop
{
    public sealed class ShopWorldTextBillboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            Vector3 direction = transform.position - camera.transform.position;
            if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction);
        }
    }

    public sealed class ShopTrashSearchPoint : MonoBehaviour
    {
        public void Interact() => ShopNetworkGame.Instance?.RequestTrashSearch();

        public bool ServerApplyAttack(ulong playerClientId)
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            return game != null && game.ServerTryTrashSearch(playerClientId);
        }
    }

    public sealed class ShopRivalShelfInteractable : MonoBehaviour
    {
        public int ShelfIndex { get; private set; }
        public Transform VisualAnchor { get; private set; }
        public void Configure(int index, Transform visualAnchor)
        {
            ShelfIndex = index;
            VisualAnchor = visualAnchor;
        }
        public void Interact() => ShopNetworkGame.Instance?.RequestRivalTheft(ShelfIndex);
    }

    [DisallowMultipleComponent]
    public sealed class ShopSideContentRuntime : MonoBehaviour
    {
        private static readonly string[] RivalShelfNames =
        {
            "RV_shelf_0", "RV_shelf_1", "RV_shelf_2", "RV_shelf_3",
            "RV_shelf2_0", "RV_shelf2_1", "RV_shelf2_2", "RV_shelf2_3"
        };

        private ShopNetworkGame game;
        private readonly List<TextMesh> trashLabels = new();
        private readonly List<Transform> trashRoots = new();
        private readonly List<ShopRivalShelfInteractable> rivalShelves = new();
        private readonly Dictionary<int, GameObject> rivalVisuals = new();
        private int observedRivalRevision = -1;
        private Vector3 rivalShopCenter;
        private bool hasRivalShopCenter;
        private bool localPlayerWasNearRivalShop;
        private GameObject rivalOwner;
        private Animator rivalOwnerAnimator;
        private CharacterController rivalOwnerController;
        private readonly List<Vector3> rivalOwnerPatrol = new();
        private int rivalOwnerPatrolIndex;
        private float nextRivalOwnerCatchTime;
        private static readonly int MovingParameter = Animator.StringToHash("Moving");

        public static void Ensure(ShopNetworkGame target)
        {
            if (target != null && target.GetComponent<ShopSideContentRuntime>() == null)
                target.gameObject.AddComponent<ShopSideContentRuntime>();
        }

        private void Awake() => game = GetComponent<ShopNetworkGame>();

        private void Start()
        {
            ConfigureTrash();
            ConfigureRivalShelves();
            ConfigureRivalOwner();
            RefreshAll();
        }

        private void Update()
        {
            if (game == null) return;
            string trashStatus = $"오늘 {game.TrashIncomeToday.Value:N0} / {game.SideContentConfig.TrashDailyCap:N0}원";
            for (int i = trashLabels.Count - 1; i >= 0; i--)
            {
                if (trashLabels[i] == null) trashLabels.RemoveAt(i);
                else trashLabels[i].text = trashStatus;
            }
            if (observedRivalRevision != game.RivalStockRevision.Value) RefreshRivalVisuals();
            ObserveLocalRivalShopVisit();
            UpdateRivalOwner();
        }

        private void ConfigureRivalOwner()
        {
            GameObject baseObject = FindNamedObject("RV_base");
            if (baseObject == null) return;
            Bounds bounds = CalculateBounds(baseObject);
            float insetX = Mathf.Min(1.2f, bounds.extents.x * 0.35f);
            float insetZ = Mathf.Min(1.2f, bounds.extents.z * 0.35f);
            float y = bounds.max.y + 0.03f;
            rivalOwnerPatrol.Clear();
            rivalOwnerPatrol.Add(new Vector3(bounds.min.x + insetX, y, bounds.min.z + insetZ));
            rivalOwnerPatrol.Add(new Vector3(bounds.max.x - insetX, y, bounds.min.z + insetZ));
            rivalOwnerPatrol.Add(new Vector3(bounds.max.x - insetX, y, bounds.max.z - insetZ));
            rivalOwnerPatrol.Add(new Vector3(bounds.min.x + insetX, y, bounds.max.z - insetZ));

            rivalOwner = new GameObject("RivalShopOwner");
            rivalOwner.transform.SetParent(transform, false);
            rivalOwner.transform.position = rivalOwnerPatrol[0];
            ShopWorkforceConfig workforce = ShopWorkforceConfig.Load();
            GameObject[] appearances = workforce != null ? workforce.AppearancePrefabs : null;
            GameObject visual = appearances != null && appearances.Length > 0
                ? appearances[appearances.Length - 1] : null;
            if (visual != null) Instantiate(visual, rivalOwner.transform);
            else
            {
                GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fallback.name = "RivalOwnerFallback";
                fallback.transform.SetParent(rivalOwner.transform, false);
                fallback.transform.localPosition = Vector3.up;
            }
            foreach (Collider collider in rivalOwner.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (Rigidbody body in rivalOwner.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            rivalOwnerController = rivalOwner.AddComponent<CharacterController>();
            rivalOwnerController.center = new Vector3(0f, 0.95f, 0f);
            rivalOwnerController.height = 1.8f;
            rivalOwnerController.radius = 0.32f;
            rivalOwnerController.stepOffset = 0.25f;
            rivalOwnerAnimator = rivalOwner.GetComponentInChildren<Animator>(true);
            if (rivalOwnerAnimator != null) rivalOwnerAnimator.applyRootMotion = false;
        }

        private void UpdateRivalOwner()
        {
            if (rivalOwner == null || rivalOwnerPatrol.Count == 0 || game == null) return;
            ShopPlayerInteractor player = FindObjectsByType<ShopPlayerInteractor>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate != null && candidate.IsOwner);
            bool hasStolen = player != null && game.IsServer && game.ServerHasRivalVisitStolenItems(player.OwnerClientId);
            Vector3 target = hasStolen ? player.transform.position : rivalOwnerPatrol[rivalOwnerPatrolIndex];
            target.y = rivalOwner.transform.position.y;
            Vector3 delta = target - rivalOwner.transform.position;
            delta.y = 0f;
            if (!hasStolen && delta.sqrMagnitude < 0.2f)
            {
                rivalOwnerPatrolIndex = (rivalOwnerPatrolIndex + 1) % rivalOwnerPatrol.Count;
                target = rivalOwnerPatrol[rivalOwnerPatrolIndex];
                delta = target - rivalOwner.transform.position;
                delta.y = 0f;
            }
            bool moving = delta.sqrMagnitude > 0.04f;
            if (moving)
            {
                Vector3 direction = delta.normalized;
                rivalOwnerController.Move(direction * Mathf.Min(1.35f * Time.deltaTime, delta.magnitude) +
                                          Vector3.down * (2f * Time.deltaTime));
                rivalOwner.transform.rotation = Quaternion.Slerp(rivalOwner.transform.rotation,
                    Quaternion.LookRotation(direction, Vector3.up), Time.deltaTime * 8f);
            }
            SetMoving(rivalOwnerAnimator, moving);
            if (hasStolen && delta.sqrMagnitude <= 1.1f * 1.1f && Time.unscaledTime >= nextRivalOwnerCatchTime)
            {
                nextRivalOwnerCatchTime = Time.unscaledTime + 2f;
                game.RequestRivalOwnerCatch();
            }
        }

        private static void SetMoving(Animator animator, bool moving)
        {
            if (animator == null) return;
            for (int i = 0; i < animator.parameterCount; i++)
                if (animator.parameters[i].nameHash == MovingParameter &&
                    animator.parameters[i].type == AnimatorControllerParameterType.Bool)
                {
                    animator.SetBool(MovingParameter, moving);
                    break;
                }
        }

        private void ConfigureTrash()
        {
            trashLabels.Clear();
            trashRoots.Clear();
            Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            List<GameObject> targets = allTransforms
                .Where(candidate => candidate != null && IsCityTrashModel(candidate))
                .Select(candidate => candidate.gameObject)
                .OrderBy(candidate => candidate.name, StringComparer.Ordinal)
                .ToList();
            for (int i = 0; i < targets.Count; i++) ConfigureTrashTarget(targets[i], i);
        }

        private static bool IsCityTrashModel(Transform candidate)
        {
            if (candidate.name != "S2_p" && candidate.name != "S2_p.001") return false;
            Transform parent = candidate.parent;
            while (parent != null)
            {
                if (parent.name == "CITY_Props") return true;
                parent = parent.parent;
            }
            return false;
        }

        private void ConfigureTrashTarget(GameObject target, int index)
        {
            if (target == null) return;

            Renderer modelRenderer = target.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(candidate => candidate != null &&
                                             candidate.gameObject.name != "TrashDailyIncomeLabel");
            if (modelRenderer == null) return;
            Bounds modelBounds = modelRenderer.bounds;

            string rootName = $"TrashInteractionRoot_{index + 1}";
            Transform trashRoot = transform.Find(rootName);
            if (trashRoot == null)
            {
                GameObject rootObject = new(rootName);
                trashRoot = rootObject.transform;
                trashRoot.SetParent(transform, false);
            }
            trashRoots.Add(trashRoot);
            trashRoot.position = modelBounds.center;
            trashRoot.rotation = Quaternion.identity;
            trashRoot.localScale = Vector3.one;

            ShopTrashSearchPoint point = trashRoot.GetComponent<ShopTrashSearchPoint>();
            if (point == null) point = trashRoot.gameObject.AddComponent<ShopTrashSearchPoint>();
            BoxCollider solidCollider = trashRoot.GetComponent<BoxCollider>();
            if (solidCollider == null) solidCollider = trashRoot.gameObject.AddComponent<BoxCollider>();
            solidCollider.isTrigger = false;
            solidCollider.center = Vector3.zero;
            solidCollider.size = new Vector3(Mathf.Max(0.5f, modelBounds.size.x * 0.92f),
                Mathf.Max(0.65f, modelBounds.size.y), Mathf.Max(0.5f, modelBounds.size.z * 0.92f));

            NavMeshObstacle obstacle = trashRoot.GetComponent<NavMeshObstacle>();
            if (obstacle == null) obstacle = trashRoot.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = solidCollider.center;
            obstacle.size = solidCollider.size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            Transform triggerTransform = trashRoot.Find("InteractionTrigger");
            if (triggerTransform == null)
            {
                GameObject triggerObject = new("InteractionTrigger");
                triggerTransform = triggerObject.transform;
                triggerTransform.SetParent(trashRoot, false);
            }
            triggerTransform.localPosition = Vector3.zero;
            triggerTransform.localRotation = Quaternion.identity;
            triggerTransform.localScale = Vector3.one;
            BoxCollider interactionTrigger = triggerTransform.GetComponent<BoxCollider>();
            if (interactionTrigger == null)
                interactionTrigger = triggerTransform.gameObject.AddComponent<BoxCollider>();
            interactionTrigger.isTrigger = true;
            interactionTrigger.center = Vector3.zero;
            interactionTrigger.size = solidCollider.size + new Vector3(1.2f, 0.7f, 1.2f);
            ShopInteractable interactable = triggerTransform.GetComponent<ShopInteractable>();
            if (interactable == null)
                interactable = triggerTransform.gameObject.AddComponent<ShopInteractable>();
            interactable.Configure(ShopAction.TrashSearch, "쓰레기통 뒤지기");

            Transform labelTransform = trashRoot.Find("TrashDailyIncomeLabel");
            if (labelTransform == null)
            {
                GameObject labelObject = new("TrashDailyIncomeLabel");
                labelTransform = labelObject.transform;
                labelTransform.SetParent(trashRoot, false);
            }
            labelTransform.localPosition = Vector3.up * (solidCollider.size.y * 0.5f + 0.42f);
            labelTransform.localScale = Vector3.one;
            TextMesh trashLabel = labelTransform.GetComponent<TextMesh>();
            if (trashLabel == null) trashLabel = labelTransform.gameObject.AddComponent<TextMesh>();
            trashLabel.anchor = TextAnchor.MiddleCenter;
            trashLabel.alignment = TextAlignment.Center;
            trashLabel.characterSize = 0.075f;
            trashLabel.fontSize = 48;
            trashLabel.color = new Color(1f, 0.82f, 0.28f);
            trashLabels.Add(trashLabel);
            if (labelTransform.GetComponent<ShopWorldTextBillboard>() == null)
                labelTransform.gameObject.AddComponent<ShopWorldTextBillboard>();
        }

        private void ConfigureRivalShelves()
        {
            rivalShelves.Clear();
            Bounds combinedBounds = default;
            bool foundBounds = false;
            for (int index = 0; index < RivalShelfNames.Length; index++)
            {
                GameObject target = FindNamedObject(RivalShelfNames[index]);
                if (target == null) continue;
                ShopRivalShelfInteractable shelf = target.GetComponent<ShopRivalShelfInteractable>() ??
                                                    target.AddComponent<ShopRivalShelfInteractable>();
                Bounds shelfBounds = CalculateBounds(target);
                if (!foundBounds)
                {
                    combinedBounds = shelfBounds;
                    foundBounds = true;
                }
                else combinedBounds.Encapsulate(shelfBounds);
                Transform anchor = target.transform.Find("RivalProductAnchor");
                if (anchor == null)
                {
                    GameObject anchorObject = new GameObject("RivalProductAnchor");
                    anchor = anchorObject.transform;
                    anchor.SetParent(target.transform, false);
                }
                anchor.position = new Vector3(shelfBounds.center.x, shelfBounds.max.y + 0.02f,
                    shelfBounds.center.z);
                anchor.rotation = Quaternion.identity;
                shelf.Configure(index, anchor);
                ShopInteractable interactable = target.GetComponent<ShopInteractable>() ??
                                                target.AddComponent<ShopInteractable>();
                interactable.Configure(ShopAction.RivalShelf, "경쟁 가게 상품 훔치기");
                if (target.GetComponentInChildren<Collider>(true) == null)
                {
                    BoxCollider collider = target.AddComponent<BoxCollider>();
                    collider.center = target.transform.InverseTransformPoint(shelfBounds.center);
                    collider.size = new Vector3(Mathf.Max(0.2f, shelfBounds.size.x), 0.35f,
                        Mathf.Max(0.2f, shelfBounds.size.z));
                }
                rivalShelves.Add(shelf);
            }
            hasRivalShopCenter = foundBounds;
            if (foundBounds) rivalShopCenter = combinedBounds.center;
        }

        private void ObserveLocalRivalShopVisit()
        {
            if (!hasRivalShopCenter || game == null || !game.IsSpawned) return;
            ShopPlayerInteractor localPlayer = FindObjectsByType<ShopPlayerInteractor>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(player => player != null && player.IsOwner);
            if (localPlayer == null) return;

            Vector3 delta = localPlayer.transform.position - rivalShopCenter;
            delta.y = 0f;
            bool isNear = delta.sqrMagnitude <= 64f;
            if (isNear && !localPlayerWasNearRivalShop)
                game.RequestRivalVisitRefresh();
            localPlayerWasNearRivalShop = isNear;
        }

        private void RefreshAll()
        {
            if (game != null && game.IsServer) game.ServerEnsureSideContentDay();
            RefreshRivalVisuals();
        }

        private void RefreshRivalVisuals()
        {
            observedRivalRevision = game != null ? game.RivalStockRevision.Value : -1;
            foreach (GameObject visual in rivalVisuals.Values)
                if (visual != null) Destroy(visual);
            rivalVisuals.Clear();
            if (game == null) return;
            for (int i = 0; i < rivalShelves.Count && i < game.RivalProductIds.Count; i++)
            {
                int productId = game.RivalProductIds[i];
                if (productId < 0 || rivalShelves[i] == null) continue;
                ShopProductDefinition product = ShopProductVisuals.Find(productId);
                Transform anchor = rivalShelves[i].VisualAnchor != null
                    ? rivalShelves[i].VisualAnchor
                    : rivalShelves[i].transform;
                GameObject visual = ShopProductVisuals.Instantiate(product, anchor);
                if (visual == null) continue;
                visual.name = "RivalProductVisual_" + productId;
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.Euler(0f, (productId * 37) % 360, 0f);
                visual.transform.localScale = Vector3.one;
                Bounds bounds = CalculateBounds(visual);
                float longest = Mathf.Max(0.01f, bounds.size.x, bounds.size.y, bounds.size.z);
                visual.transform.localScale *= 0.42f / longest;
                bounds = CalculateBounds(visual);
                Vector3 bottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                visual.transform.position += anchor.position - bottomCenter;
                foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                rivalVisuals[i] = visual;
            }
        }

        private static GameObject FindNamedObject(string objectName) =>
            FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(item => item != null && item.name == objectName)?.gameObject;

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
