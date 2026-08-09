using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Store Naming Config",
        fileName = "ShopStoreNamingConfig")]
    public sealed class ShopStoreNamingConfig : ScriptableObject
    {
        private const string ResourcePath = "Progression/ShopStoreNamingConfig";

        [SerializeField, Min(1)] private int maximumNameLength = 10;
        [SerializeField] private string defaultPlayerShopName = "픽앤플레이스";
        [SerializeField] private string defaultRivalShopName = "고양이 조달상점";

        public int MaximumNameLength => Mathf.Max(1, maximumNameLength);
        public string DefaultPlayerShopName => string.IsNullOrWhiteSpace(defaultPlayerShopName)
            ? "픽앤플레이스"
            : defaultPlayerShopName.Trim();
        public string DefaultRivalShopName => string.IsNullOrWhiteSpace(defaultRivalShopName)
            ? "고양이 조달상점"
            : defaultRivalShopName.Trim();

        public static ShopStoreNamingConfig Load() =>
            Resources.Load<ShopStoreNamingConfig>(ResourcePath);
    }
}
