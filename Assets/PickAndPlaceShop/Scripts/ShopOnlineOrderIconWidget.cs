using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(430)]
    public sealed class ShopOnlineOrderIconWidget : MonoBehaviour
    {
        private static ShopOnlineOrderIconWidget instance;
        private ShopLiveOperationsNetwork observed;
        private CanvasGroup group;
        private Image icon;
        private Text label;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[Shop] Online Order Product Icon");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopOnlineOrderIconWidget>();
        }

        private void Awake()
        {
            BuildUi();
            group.alpha = 0f;
        }

        private void Update()
        {
            ShopLiveOperationsNetwork current = ShopLiveOperationsNetwork.Instance;
            if (observed == current) return;
            if (observed != null) observed.OnlineOrders.OnListChanged -= Changed;
            observed = current;
            if (observed != null) observed.OnlineOrders.OnListChanged += Changed;
            Refresh();
        }

        private void Changed(NetworkListEvent<ShopOnlineOrderState> _) => Refresh();

        private void Refresh()
        {
            if (observed == null || observed.OnlineOrders.Count == 0)
            {
                group.alpha = 0f;
                return;
            }
            ShopOnlineOrderState order = observed.OnlineOrders[0];
            ShopProductDefinition product = ShopProductVisuals.Find(order.ProductId);
            icon.sprite = product != null ? product.Icon : null;
            icon.color = icon.sprite != null ? Color.white : new Color(0.2f, 0.24f, 0.3f);
            label.text = product != null
                ? "온라인 주문 · " + product.DisplayName + " x" + order.Quantity
                : "온라인 주문 상품";
            group.alpha = 1f;
        }

        private void BuildUi()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15010;
            group = canvasObject.GetComponent<CanvasGroup>();
            GameObject panel = new("OnlineOrderIcon", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-28f, -156f);
            rect.sizeDelta = new Vector2(310f, 96f);
            panel.GetComponent<Image>().color = new Color(0.025f, 0.05f, 0.075f, 0.94f);
            icon = new GameObject("ProductIcon", typeof(RectTransform), typeof(Image))
                .GetComponent<Image>();
            icon.transform.SetParent(panel.transform, false);
            icon.preserveAspect = true;
            RectTransform iconRect = icon.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(10f, 0f);
            iconRect.sizeDelta = new Vector2(76f, 76f);
            label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(panel.transform, false);
            label.font = ShopUiFonts.Bold;
            label.fontSize = 18;
            label.fontStyle = FontStyle.Normal;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(96f, 8f);
            labelRect.offsetMax = new Vector2(-8f, -8f);
        }

        private void OnDestroy()
        {
            if (observed != null) observed.OnlineOrders.OnListChanged -= Changed;
            if (instance == this) instance = null;
        }
    }
}
