using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    public enum ShopHudStackSlot
    {
        Network = 0,
        Objective = 1,
        NightSales = 2,
        OnlineOrder = 3
    }

    [DefaultExecutionOrder(-9000)]
    public sealed class ShopHudStack : MonoBehaviour
    {
        private static ShopHudStack instance;
        private readonly Dictionary<Object, GameObject> items = new();
        private readonly Dictionary<GameObject, ShopHudStackSlot> slots = new();
        private RectTransform stack;
        private GameObject canvasRoot;
        private string observedScene = string.Empty;

        public static ShopHudStack Instance
        {
            get
            {
                EnsureInstance();
                return instance;
            }
        }

        public static bool TryGetExisting(out ShopHudStack existing)
        {
            existing = instance;
            return existing != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() => EnsureInstance();

        private static void EnsureInstance()
        {
            if (instance != null) return;
            GameObject host = new("[UI] Shared HUD Stack");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopHudStack>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Build();
        }

        private void Update()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene == observedScene) return;
            observedScene = scene;
            canvasRoot.SetActive(!string.Equals(scene, ShopLaunchContext.MainMenuScene,
                System.StringComparison.Ordinal));
        }

        public GameObject CreateItem(Object owner, ShopHudStackSlot slot, string name, float height)
        {
            if (owner == null) return null;
            if (items.TryGetValue(owner, out GameObject existing) && existing != null) return existing;

            GameObject panel = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(LayoutElement));
            panel.transform.SetParent(stack, false);
            slots[panel] = slot;
            LayoutElement layout = panel.GetComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
            layout.flexibleWidth = 1f;
            Image background = panel.GetComponent<Image>();
            background.color = ShopUiSkin.CreamCard;
            ShopUiSkin.Round(background, 20);
            items[owner] = panel;
            Reorder();
            return panel;
        }

        public void RemoveItem(Object owner)
        {
            if (owner == null || !items.TryGetValue(owner, out GameObject item)) return;
            items.Remove(owner);
            if (item != null) slots.Remove(item);
            if (item != null) Destroy(item);
        }

        private void Reorder()
        {
            List<GameObject> ordered = new(slots.Keys);
            ordered.Sort((left, right) => slots[left].CompareTo(slots[right]));
            for (int i = 0; i < ordered.Count; i++)
                if (ordered[i] != null) ordered[i].transform.SetSiblingIndex(i);
        }

        private void Build()
        {
            canvasRoot = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(transform, false);
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 16000;
            CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject safe = new("SafeArea", typeof(RectTransform), typeof(ShopSafeArea));
            safe.transform.SetParent(canvasRoot.transform, false);
            GameObject stackObject = new("RightStatusStack", typeof(RectTransform),
                typeof(VerticalLayoutGroup));
            stackObject.transform.SetParent(safe.transform, false);
            stack = stackObject.GetComponent<RectTransform>();
            stack.anchorMin = stack.anchorMax = stack.pivot = Vector2.one;
            stack.anchoredPosition = new Vector2(-24f, -24f);
            stack.sizeDelta = new Vector2(452f, 760f);
            VerticalLayoutGroup layout = stackObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.UpperRight;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }
    }
}
