using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.AI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(370)]
    public sealed class ShopExpansionVisualController : MonoBehaviour
    {
        private static ShopExpansionVisualController instance;
        private readonly List<GameObject> generated = new();
        private readonly List<Vector3> customerBrowsePoints = new();
        private readonly List<Bounds> activeShopFloorBounds = new();
        private readonly Dictionary<GameObject, bool> sceneDefaults = new();
        private ShopExpansionVisualConfig config;
        private int appliedLevel;
        private float nextPoll;
        private CanvasGroup revealGroup;
        private Text revealText;
        private GameObject sharedShell;
        private GameObject sharedBoundary;

        public static int CustomerBrowsePointCount => instance != null
            ? instance.customerBrowsePoints.Count
            : 0;

        public static bool TryGetCustomerBrowsePoint(int index, out Vector3 position)
        {
            position = default;
            if (instance == null || index < 0 || index >= instance.customerBrowsePoints.Count) return false;
            position = instance.customerBrowsePoints[index];
            return true;
        }

        public static bool TryContainsActiveShopArea(Vector3 position, out bool inside)
        {
            inside = false;
            if (instance == null || instance.activeShopFloorBounds.Count == 0) return false;
            inside = instance.ContainsShopFloor(position);
            return true;
        }

        public static bool TryGetNearestShopExit(Vector3 from, out Vector3 exit)
        {
            exit = from;
            if (instance == null || instance.activeShopFloorBounds.Count == 0) return false;
            return instance.FindNearestShopExit(from, out exit);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[Progression] Store Expansion Visuals");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopExpansionVisualController>();
        }

        private void Awake()
        {
            config = Resources.Load<ShopExpansionVisualConfig>("Progression/ShopExpansionVisualConfig");
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (instance == this) instance = null;
        }

        private void HandleSceneLoaded(Scene _, LoadSceneMode __)
        {
            appliedLevel = 0;
            ClearGenerated();
            ClearSharedShell();
            sceneDefaults.Clear();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.5f;
            int level = ShopProgressionManager.Instance?.ExpansionLevel ?? 1;
            if (ShopNightSalesSystem.Instance == null)
            {
                if (generated.Count > 0) ClearGenerated();
                appliedLevel = 0;
                return;
            }
            if (level == appliedLevel) return;
            int previousLevel = appliedLevel;
            appliedLevel = level;
            Rebuild(level, previousLevel);
            if (previousLevel > 0 && level > previousLevel) StartCoroutine(PlayExpansionReveal(level));
        }

        private void Rebuild(int level, int previousLevel)
        {
            ClearGenerated();
            CaptureSceneDefaults();
            RestoreSceneDefaults();
            ApplyStageObjectRules(level);
            Vector3 origin = ShopNightSalesSystem.Instance.DisplayWorkPosition;
            if (level >= 2) CreateShelf(new Vector3(2.3f, 0.05f, 6.98f), 2);
            if (level >= 3)
            {
                CreateBundledZone(config != null ? config.Level3ZoneCenter : new Vector3(11.7f, 0f, 3.2f),
                    3, "캡슐 회수 구역", previousLevel > 0 && level > previousLevel && level == 3);
                CreateShelf(new Vector3(1.55f, 0f, -1.88f), 3);
            }
            if (level >= 4) CreateBundledZone(config != null ? config.Level4ZoneCenter : new Vector3(11.7f, 0f, 0.2f),
                4, "굿즈 감정 구역", previousLevel > 0 && level > previousLevel && level == 4);
            if (level >= 5) CreateBundledZone(config != null ? config.Level5ZoneCenter : new Vector3(11.7f, 0f, 6.1f),
                5, "위탁 판매 구역", previousLevel > 0 && level > previousLevel && level == 5);
            if (level >= 6) CreateRoomExtension(origin + Vector3.right * 5f, 2);
            EnsureSharedShell(level);
            RefreshActiveShopFloorBounds();
            ShopProductDisplayVisualController.RequestRefresh();
        }

        private void RefreshActiveShopFloorBounds()
        {
            activeShopFloorBounds.Clear();
            Scene activeScene = SceneManager.GetActiveScene();
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.gameObject.scene != activeScene) continue;
                string objectName = renderer.gameObject.name;
                if (objectName == "ShopFloor" || objectName == "ShopFloor (1)")
                    AddShopFloorBounds(renderer.bounds);
            }

            for (int i = 0; i < generated.Count; i++)
            {
                GameObject root = generated[i];
                if (root == null || !root.activeInHierarchy) continue;
                Renderer[] generatedRenderers = root.GetComponentsInChildren<Renderer>(true);
                for (int j = 0; j < generatedRenderers.Length; j++)
                    if (generatedRenderers[j] != null && generatedRenderers[j].gameObject.name == "Floor")
                        AddShopFloorBounds(generatedRenderers[j].bounds);
            }
        }

        private void AddShopFloorBounds(Bounds bounds)
        {
            if (bounds.size.x <= 0.1f || bounds.size.z <= 0.1f) return;
            bounds.Expand(new Vector3(0.2f, 4f, 0.2f));
            activeShopFloorBounds.Add(bounds);
        }

        private bool ContainsShopFloor(Vector3 position)
        {
            for (int i = 0; i < activeShopFloorBounds.Count; i++)
            {
                Bounds bounds = activeShopFloorBounds[i];
                if (position.x >= bounds.min.x && position.x <= bounds.max.x &&
                    position.z >= bounds.min.z && position.z <= bounds.max.z)
                    return true;
            }
            return false;
        }

        private bool FindNearestShopExit(Vector3 from, out Vector3 exit)
        {
            const float outsidePadding = 1.25f;
            exit = from;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < activeShopFloorBounds.Count; i++)
            {
                Bounds bounds = activeShopFloorBounds[i];
                Vector3 clamped = bounds.ClosestPoint(from);
                Vector3[] candidates =
                {
                    new(bounds.min.x - outsidePadding, from.y, clamped.z),
                    new(bounds.max.x + outsidePadding, from.y, clamped.z),
                    new(clamped.x, from.y, bounds.min.z - outsidePadding),
                    new(clamped.x, from.y, bounds.max.z + outsidePadding)
                };
                for (int j = 0; j < candidates.Length; j++)
                {
                    if (ContainsShopFloor(candidates[j])) continue;
                    float distance = (candidates[j] - from).sqrMagnitude;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    exit = candidates[j];
                }
            }
            return bestDistance < float.MaxValue;
        }

        private void CaptureSceneDefaults()
        {
            if (sceneDefaults.Count > 0) return;
            Scene activeScene = SceneManager.GetActiveScene();
            Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject target = transforms[i].gameObject;
                if (target.scene == activeScene && !sceneDefaults.ContainsKey(target))
                    sceneDefaults.Add(target, target.activeSelf);
            }
        }

        private void RestoreSceneDefaults()
        {
            foreach (KeyValuePair<GameObject, bool> pair in sceneDefaults)
                if (pair.Key != null) pair.Key.SetActive(pair.Value);
        }

        private void ApplyStageObjectRules(int level)
        {
            if (config == null) return;
            ShopExpansionVisualConfig.StageRule[] rules = config.StageRules;
            for (int i = 0; i < rules.Length; i++)
            {
                ShopExpansionVisualConfig.StageRule rule = rules[i];
                if (rule == null || level < rule.minimumLevel) continue;
                SetNamedObjectsActive(rule.activateObjectNames, true);
                SetNamedObjectsActive(rule.deactivateObjectNames, false);
            }
        }

        private void SetNamedObjectsActive(string[] objectNames, bool active)
        {
            if (objectNames == null || objectNames.Length == 0) return;
            foreach (GameObject target in sceneDefaults.Keys)
            {
                if (target == null) continue;
                for (int i = 0; i < objectNames.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(objectNames[i]) && target.name == objectNames[i])
                    {
                        target.SetActive(active);
                        break;
                    }
                }
            }
        }

        private void CreateShelf(Vector3 position, int tier)
        {
            GameObject root = new("ExpandedDisplayShelf_L" + tier);
            root.SetActive(false);
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            GameObject interaction = new("InteractionTrigger");
            interaction.transform.SetParent(root.transform, false);
            interaction.transform.localPosition = new Vector3(0f, 1f, -0.58f);
            BoxCollider trigger = interaction.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.2f, 2f, 0.75f);
            interaction.AddComponent<ShopInteractable>().Configure(ShopAction.DisplayShelf,
                "[E] 공용 창고 상품 진열");
            Color wood = new(0.38f, 0.18f, 0.09f);
            CreatePart(root.transform, "SideL", new Vector3(-0.9f, 1f, 0f), new Vector3(0.16f, 2f, 0.65f), wood, true);
            CreatePart(root.transform, "SideR", new Vector3(0.9f, 1f, 0f), new Vector3(0.16f, 2f, 0.65f), wood, true);
            for (int i = 0; i < 3; i++)
                CreatePart(root.transform, "Shelf" + i, new Vector3(0f, 0.25f + i * 0.72f, 0f),
                    new Vector3(1.95f, 0.12f, 0.72f), new Color(0.82f, 0.55f, 0.28f), true);
            AddCarvingObstacle(root, new Vector3(0f, 1f, 0f), new Vector3(2.08f, 2.05f, 0.78f));
            ShopDisplayShelfAnchors anchors = root.AddComponent<ShopDisplayShelfAnchors>();
            anchors.Configure(10 + Mathf.Max(0, tier - 2) * 6, 2, false);
            root.SetActive(true);
            generated.Add(root);
        }

        private void CreateRoomExtension(Vector3 origin, int index)
        {
            GameObject root = new("StoreRoomExtension_" + index);
            root.transform.SetParent(transform, false);
            root.transform.position = origin + new Vector3(index == 1 ? -2.5f : 2.5f, -0.08f, 4.8f);
            Color floor = config != null
                ? (index == 1 ? config.FirstRoomFloorColor : config.SecondRoomFloorColor)
                : new Color(0.55f, 0.36f, 0.23f);
            Color wall = config != null ? config.RoomWallColor : new Color(0.19f, 0.12f, 0.18f);
            CreatePart(root.transform, "Floor", Vector3.zero, new Vector3(5f, 0.16f, 4.5f), floor, true);
            CreatePart(root.transform, "BackWall", new Vector3(0f, 1.6f, 2.2f), new Vector3(5f, 3.2f, 0.18f), wall, true);
            CreatePart(root.transform, "SideWall", new Vector3(index == 1 ? -2.4f : 2.4f, 1.6f, 0f),
                new Vector3(0.18f, 3.2f, 4.5f), wall, true);
            customerBrowsePoints.Add(root.transform.position + new Vector3(0f, 0f, 0.55f));
            generated.Add(root);
            CreateShelf(root.transform.position + new Vector3(0f, 0.08f, -1.2f), 6);
        }

        private void CreateBundledZone(Vector3 center, int level, string label, bool showSign)
        {
            GameObject root = new("ExpansionBundle_L" + level);
            root.transform.SetParent(transform, false);
            root.transform.position = center;
            Vector2 size = config != null ? config.ZoneFloorSize : new Vector2(6f, 2.6f);
            Color floor = level switch
            {
                3 => new Color(0.24f, 0.48f, 0.45f),
                4 => new Color(0.40f, 0.30f, 0.52f),
                _ => new Color(0.56f, 0.38f, 0.20f)
            };
            CreatePart(root.transform, "Floor", Vector3.down * 0.08f,
                new Vector3(size.x, 0.16f, size.y), floor, true);
            if (showSign) CreateTemporaryZoneSign(root.transform, level, label, size);
            if (level >= 4) CreateShelf(center + new Vector3(-1.65f, 0f, 0f), level);
            customerBrowsePoints.Add(center + new Vector3(-0.6f, 0f, 0.35f));
            customerBrowsePoints.Add(center + new Vector3(0.8f, 0f, -0.35f));
            generated.Add(root);
        }

        private void EnsureSharedShell(int level)
        {
            RemoveLegacySharedVisuals();
            if (level < 3)
            {
                if (sharedShell != null) sharedShell.SetActive(false);
                return;
            }
            if (sharedShell == null)
            {
                sharedShell = new GameObject("ExpansionSharedShell");
                sharedShell.transform.SetParent(transform, false);
            }
            if (sharedBoundary == null)
            {
                Transform existingBoundary = sharedShell.transform.Find("ExpansionBoundary (Invisible)");
                sharedBoundary = existingBoundary != null
                    ? existingBoundary.gameObject
                    : new GameObject("ExpansionBoundary (Invisible)");
                sharedBoundary.transform.SetParent(sharedShell.transform, false);
                if (sharedBoundary.GetComponent<BoxCollider>() == null)
                    sharedBoundary.AddComponent<BoxCollider>();
                sharedBoundary.isStatic = true;
            }
            sharedShell.SetActive(true);
            Vector2 zoneSize = config != null ? config.ZoneFloorSize : new Vector2(6f, 2.6f);
            Vector3[] centers =
            {
                config != null ? config.Level3ZoneCenter : new Vector3(11.7f, 0f, 3.2f),
                config != null ? config.Level4ZoneCenter : new Vector3(11.7f, 0f, 0.2f),
                config != null ? config.Level5ZoneCenter : new Vector3(11.7f, 0f, 6.1f)
            };
            float minX = centers[0].x - zoneSize.x * 0.5f;
            float maxX = centers[0].x + zoneSize.x * 0.5f;
            float minZ = centers[0].z - zoneSize.y * 0.5f;
            float maxZ = centers[0].z + zoneSize.y * 0.5f;
            int count = Mathf.Clamp(level - 2, 1, centers.Length);
            for (int i = 1; i < count; i++)
            {
                minX = Mathf.Min(minX, centers[i].x - zoneSize.x * 0.5f);
                maxX = Mathf.Max(maxX, centers[i].x + zoneSize.x * 0.5f);
                minZ = Mathf.Min(minZ, centers[i].z - zoneSize.y * 0.5f);
                maxZ = Mathf.Max(maxZ, centers[i].z + zoneSize.y * 0.5f);
            }
            sharedShell.transform.position = Vector3.zero;
            sharedBoundary.transform.position = new Vector3(maxX - 0.09f, 1.5f, (minZ + maxZ) * 0.5f);
            sharedBoundary.transform.localScale = new Vector3(0.18f, 3f, maxZ - minZ);
            AddCarvingObstacle(sharedBoundary, Vector3.zero, Vector3.one);
        }

        private void RemoveLegacySharedVisuals()
        {
            if (sharedShell == null)
            {
                Transform existingShell = transform.Find("ExpansionSharedShell");
                if (existingShell != null) sharedShell = existingShell.gameObject;
            }
            if (sharedShell == null) return;

            for (int i = sharedShell.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = sharedShell.transform.GetChild(i).gameObject;
                if (child.name != "Canopy" && child.name != "OuterWall") continue;
                child.SetActive(false);
                Destroy(child);
            }
        }

        private void CreateTemporaryZoneSign(Transform parent, int level, string label, Vector2 size)
        {
            GameObject signObject = new("ZoneSign");
            signObject.transform.SetParent(parent, false);
            signObject.transform.localPosition = new Vector3(0f, 2.45f, -size.y * 0.48f);
            TextMesh sign = signObject.AddComponent<TextMesh>();
            sign.text = "Lv." + level + "  " + label + "\n진열대와 특화 시설이 열렸어요";
            sign.anchor = TextAnchor.MiddleCenter;
            sign.alignment = TextAlignment.Center;
            sign.characterSize = 0.045f;
            sign.fontSize = 40;
            sign.color = new Color(1f, 0.88f, 0.48f);
            ShopUiFonts.Apply(sign, ShopUiFontWeight.Bold);
            StartCoroutine(FadeAndRemoveZoneSign(signObject, sign));
        }

        private System.Collections.IEnumerator FadeAndRemoveZoneSign(GameObject signObject, TextMesh sign)
        {
            yield return new WaitForSecondsRealtime(config != null ? config.ZoneSignVisibleSeconds : 5f);
            float fade = config != null ? config.ZoneSignFadeSeconds : 1f;
            float elapsed = 0f;
            Color original = sign.color;
            while (signObject != null && elapsed < fade)
            {
                elapsed += Time.unscaledDeltaTime;
                sign.color = new Color(original.r, original.g, original.b, 1f - Mathf.Clamp01(elapsed / fade));
                yield return null;
            }
            if (signObject != null) Destroy(signObject);
        }

        private System.Collections.IEnumerator PlayExpansionReveal(int level)
        {
            EnsureRevealUi();
            if (revealGroup == null) yield break;
            revealText.text = "가게 확장 완료!\n새 구역 Lv." + level + " 개방";
            revealGroup.gameObject.SetActive(true);
            float duration = config != null ? config.RevealDuration : 1.5f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                revealGroup.alpha = normalized < 0.2f
                    ? normalized / 0.2f
                    : normalized > 0.72f ? 1f - (normalized - 0.72f) / 0.28f : 1f;
                yield return null;
            }
            revealGroup.alpha = 0f;
            revealGroup.gameObject.SetActive(false);
        }

        private void EnsureRevealUi()
        {
            if (revealGroup != null) return;
            GameObject canvasObject = new("ExpansionRevealCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 24000;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            revealGroup = canvasObject.GetComponent<CanvasGroup>();
            revealGroup.blocksRaycasts = false;

            GameObject banner = new("ExpansionBanner", typeof(RectTransform), typeof(Image));
            banner.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = banner.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.72f);
            rect.anchorMax = new Vector2(0.5f, 0.72f);
            rect.sizeDelta = new Vector2(780f, 150f);
            banner.GetComponent<Image>().color = new Color(0.02f, 0.11f, 0.13f, 0.94f);
            GameObject textObject = new("ExpansionRevealText", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(banner.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            revealText = textObject.GetComponent<Text>();
            revealText.font = ShopUiFonts.Bold;
            revealText.fontSize = 36;
            revealText.fontStyle = FontStyle.Normal;
            revealText.alignment = TextAnchor.MiddleCenter;
            revealText.color = new Color(1f, 0.84f, 0.32f);
            canvasObject.SetActive(false);
        }

        private static GameObject CreatePart(Transform parent, string name, Vector3 localPosition,
            Vector3 scale, Color color, bool keepCollider)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
            ShopBuildSafeMaterials.ApplyLitColor(part.GetComponent<Renderer>(), color);
            if (!keepCollider)
            {
                Collider collider = part.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
            }
            part.isStatic = true;
            return part;
        }

        private static void AddCarvingObstacle(GameObject target, Vector3 center, Vector3 size)
        {
            if (target == null) return;
            NavMeshObstacle obstacle = target.GetComponent<NavMeshObstacle>();
            if (obstacle == null) obstacle = target.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = center;
            obstacle.size = size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
        }

        private void ClearGenerated()
        {
            for (int i = 0; i < generated.Count; i++) if (generated[i] != null) Destroy(generated[i]);
            generated.Clear();
            customerBrowsePoints.Clear();
            activeShopFloorBounds.Clear();
        }

        private void ClearSharedShell()
        {
            if (sharedShell != null) Destroy(sharedShell);
            sharedShell = null;
            sharedBoundary = null;
        }
    }
}
