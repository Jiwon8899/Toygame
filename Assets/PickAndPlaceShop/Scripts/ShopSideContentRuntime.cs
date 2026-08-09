using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
        private TextMesh trashLabel;
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
            if (trashLabel != null)
                trashLabel.text = $"오늘 {game.TrashIncomeToday.Value:N0} / {game.SideContentConfig.TrashDailyCap:N0}원";
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
            GameObject target = FindNamedObject("S2_p.001");
            if (target == null) return;
            ShopTrashSearchPoint point = target.GetComponent<ShopTrashSearchPoint>() ??
                                         target.AddComponent<ShopTrashSearchPoint>();
            ShopInteractable interactable = target.GetComponent<ShopInteractable>() ??
                                            target.AddComponent<ShopInteractable>();
            interactable.Configure(ShopAction.TrashSearch, "쓰레기통 뒤지기");
            if (target.GetComponentInChildren<Collider>(true) == null)
            {
                BoxCollider collider = target.AddComponent<BoxCollider>();
                collider.size = new Vector3(0.8f, 1.1f, 0.8f);
                collider.center = Vector3.up * 0.55f;
            }
            GameObject label = new("TrashDailyIncomeLabel");
            label.transform.SetParent(point.transform, false);
            label.transform.localPosition = Vector3.up * 1.45f;
            trashLabel = label.AddComponent<TextMesh>();
            trashLabel.anchor = TextAnchor.MiddleCenter;
            trashLabel.alignment = TextAlignment.Center;
            trashLabel.characterSize = 0.075f;
            trashLabel.fontSize = 48;
            trashLabel.color = new Color(1f, 0.82f, 0.28f);
            label.AddComponent<ShopWorldTextBillboard>();
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
