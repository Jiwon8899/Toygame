using UnityEngine;

namespace PickAndPlaceShop
{
    public sealed class ShopCustomerDebugView : MonoBehaviour
    {
        [SerializeField] private ShopCustomerNetwork customer;
        [SerializeField] private TextMesh stateLabel;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Material studentMaterial;
        [SerializeField] private Material giftShopperMaterial;
        [SerializeField] private Material collectorMaterial;

        private ShopCustomerType appliedType = (ShopCustomerType)(-1);
        private bool fontApplied;

        public void Configure(ShopCustomerNetwork target, TextMesh label, Renderer body,
            Material student, Material giftShopper, Material collector)
        {
            customer = target;
            stateLabel = label;
            bodyRenderer = body;
            studentMaterial = student;
            giftShopperMaterial = giftShopper;
            collectorMaterial = collector;
        }

        private void LateUpdate()
        {
            if (customer == null || stateLabel == null) return;
            if (!fontApplied && ShopKoreanFontApplier.KoreanFont != null)
            {
                stateLabel.font = ShopKoreanFontApplier.KoreanFont;
                stateLabel.GetComponent<Renderer>().sharedMaterial = ShopKoreanFontApplier.KoreanFont.material;
                fontApplied = true;
            }
            ShopNightSalesSystem system = ShopNightSalesSystem.Instance;
            bool visible = system == null || system.DebugLabelsEnabled.Value;
            stateLabel.gameObject.SetActive(visible);
            if (!visible) return;

            stateLabel.text = StateText(customer.State.Value) + "\n" + customer.DesiredProductName.Value;
            Camera camera = Camera.main;
            if (camera != null)
            {
                stateLabel.transform.rotation = Quaternion.LookRotation(stateLabel.transform.position - camera.transform.position);
            }

            if (bodyRenderer != null && appliedType != customer.CustomerType.Value)
            {
                appliedType = customer.CustomerType.Value;
                Material material = appliedType switch
                {
                    ShopCustomerType.Student => studentMaterial,
                    ShopCustomerType.GiftShopper => giftShopperMaterial,
                    ShopCustomerType.Collector => collectorMaterial,
                    _ => studentMaterial
                };
                if (material != null) bodyRenderer.sharedMaterial = material;
            }
        }

        private static string StateText(ShopCustomerState state)
        {
            return state switch
            {
                ShopCustomerState.Enter => "입장 중",
                ShopCustomerState.Browse => "상품 탐색 중",
                ShopCustomerState.InspectProduct => "상품 확인 중",
                ShopCustomerState.Queue => "계산 대기 중",
                ShopCustomerState.Checkout => "계산 중",
                ShopCustomerState.Leave => "퇴장 중",
                ShopCustomerState.GiveUp => "구매 포기",
                _ => state.ToString()
            };
        }
    }
}
