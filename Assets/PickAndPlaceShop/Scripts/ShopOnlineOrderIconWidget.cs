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
        private GameObject panel;
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
            panel.SetActive(false);
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
            if (panel == null) return;
            if (observed == null || observed.OnlineOrders.Count == 0)
            {
                panel.SetActive(false);
                return;
            }

            ShopOnlineOrderState order = observed.OnlineOrders[0];
            ShopProductDefinition product = ShopProductVisuals.Find(order.ProductId);
            label.text = product != null
                ? $"온라인 주문\n{product.DisplayName} ×{order.Quantity}"
                : "온라인 주문\n상품을 확인하세요";
            panel.SetActive(true);
        }

        private void BuildUi()
        {
            panel = ShopHudStack.Instance.CreateItem(this, ShopHudStackSlot.OnlineOrder,
                "OnlineOrder", 100f);
            ShopUiSkin.AddIcon("Package", panel.transform, ShopUiIcon.Package, ShopUiSkin.Pink,
                new Vector2(64f, 64f), new Vector2(16f, -18f), new Vector2(0f, 1f));

            label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(panel.transform, false);
            label.font = ShopUiFonts.Bold;
            label.fontSize = 18;
            label.fontStyle = FontStyle.Normal;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = ShopUiSkin.TextBody;
            label.raycastTarget = false;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(96f, 12f);
            rect.offsetMax = new Vector2(-18f, -12f);
        }

        private void OnDestroy()
        {
            if (observed != null) observed.OnlineOrders.OnListChanged -= Changed;
            if (ShopHudStack.TryGetExisting(out ShopHudStack hudStack)) hudStack.RemoveItem(this);
            if (instance == this) instance = null;
        }
    }
}
