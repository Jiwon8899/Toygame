using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(370)]
    public sealed class ShopExpansionVisualController : MonoBehaviour
    {
        private static ShopExpansionVisualController instance;
        private readonly List<GameObject> generated = new();
        private readonly List<Vector3> customerBrowsePoints = new();
        private readonly Dictionary<GameObject, bool> sceneDefaults = new();
        private ShopExpansionVisualConfig config;
        private int appliedLevel;
        private float nextPoll;
        private CanvasGroup revealGroup;
        private Text revealText;

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
            Rebuild(level);
            if (previousLevel > 0 && level > previousLevel) StartCoroutine(PlayExpansionReveal(level));
        }

        private void Rebuild(int level)
        {
            ClearGenerated();
            CaptureSceneDefaults();
            RestoreSceneDefaults();
            ApplyStageObjectRules(level);
            Vector3 origin = ShopNightSalesSystem.Instance.DisplayWorkPosition;
            if (level >= 2) CreateShelf(origin + new Vector3(-2.1f, 0f, 2.1f), 2);
            if (level >= 3) CreateBundledZone(config != null ? config.Level3ZoneCenter : new Vector3(11.7f, 0f, 3.2f),
                3, "캡슐 회수 구역");
            if (level >= 4) CreateBundledZone(config != null ? config.Level4ZoneCenter : new Vector3(11.7f, 0f, 0.2f),
                4, "굿즈 감정 구역");
            if (level >= 5) CreateBundledZone(config != null ? config.Level5ZoneCenter : new Vector3(11.7f, 0f, 6.1f),
                5, "위탁 판매 구역");
            if (level >= 6) CreateRoomExtension(origin + Vector3.right * 5f, 2);
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
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 1f, -0.35f);
            trigger.size = new Vector3(2f, 2f, 1.5f);
            root.AddComponent<ShopInteractable>().Configure(ShopAction.DisplayShelf,
                "[E] 공용 창고 상품 진열");
            Color wood = new(0.38f, 0.18f, 0.09f);
            CreatePart(root.transform, "SideL", new Vector3(-0.9f, 1f, 0f), new Vector3(0.16f, 2f, 0.65f), wood, false);
            CreatePart(root.transform, "SideR", new Vector3(0.9f, 1f, 0f), new Vector3(0.16f, 2f, 0.65f), wood, false);
            for (int i = 0; i < 3; i++)
                CreatePart(root.transform, "Shelf" + i, new Vector3(0f, 0.25f + i * 0.72f, 0f),
                    new Vector3(1.95f, 0.12f, 0.72f), new Color(0.82f, 0.55f, 0.28f), false);
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
        }

        private void CreateBundledZone(Vector3 center, int level, string label)
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
            Color wall = config != null ? config.RoomWallColor : new Color(0.19f, 0.12f, 0.18f);
            CreatePart(root.transform, "Floor", Vector3.down * 0.08f,
                new Vector3(size.x, 0.16f, size.y), floor, true);
            CreatePart(root.transform, "OuterWall", new Vector3(size.x * 0.5f - 0.09f, 1.5f, 0f),
                new Vector3(0.18f, 3f, size.y), wall, true);
            CreatePart(root.transform, "Canopy", new Vector3(0f, 2.9f, 0f),
                new Vector3(size.x, 0.14f, size.y), new Color(wall.r * 1.25f, wall.g * 1.25f, wall.b * 1.25f), false);

            GameObject signObject = new("ZoneSign");
            signObject.transform.SetParent(root.transform, false);
            signObject.transform.localPosition = new Vector3(0f, 2.45f, -size.y * 0.48f);
            TextMesh sign = signObject.AddComponent<TextMesh>();
            sign.text = "Lv." + level + "  " + label + "\n진열대 + 특화 시설";
            sign.anchor = TextAnchor.MiddleCenter;
            sign.alignment = TextAlignment.Center;
            sign.characterSize = 0.085f;
            sign.fontSize = 48;
            sign.color = new Color(1f, 0.88f, 0.48f);
            CreateShelf(center + new Vector3(-1.65f, 0f, 0f), level);
            customerBrowsePoints.Add(center + new Vector3(-0.6f, 0f, 0.35f));
            customerBrowsePoints.Add(center + new Vector3(0.8f, 0f, -0.35f));
            generated.Add(root);
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

        private static void CreatePart(Transform parent, string name, Vector3 localPosition,
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
        }

        private void ClearGenerated()
        {
            for (int i = 0; i < generated.Count; i++) if (generated[i] != null) Destroy(generated[i]);
            generated.Clear();
            customerBrowsePoints.Clear();
        }
    }
}
