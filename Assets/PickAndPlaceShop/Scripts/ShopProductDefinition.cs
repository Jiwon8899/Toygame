using UnityEngine;

namespace PickAndPlaceShop
{
    public enum ShopProductCategory
    {
        Plush,
        CapsuleToy,
        Decoration,
        Animal,
        Space,
        Retro,
        Seasonal,
        Other,
        CatPlush,
        CatFigure,
        CatGoods,
        CatSeasonal,
        CatRetro
    }

    public enum ShopProductRarity
    {
        Common,
        Uncommon,
        Rare,
        UltraRare
    }

    public enum ShopProductCondition
    {
        Used,
        Good,
        Mint
    }

    public enum ShopCustomerType
    {
        Student,
        GiftShopper,
        Collector
    }

    [CreateAssetMenu(menuName = "Pick And Place Shop/Product", fileName = "Product")]
    public sealed class ShopProductDefinition : ScriptableObject
    {
        [SerializeField] private int productId;
        [SerializeField] private string displayName = "Product";
        [SerializeField] private ShopProductCategory category;
        [Min(1)] [SerializeField] private int salePrice = 100;
        [SerializeField] private ShopProductRarity rarity;
        [SerializeField] private ShopProductCondition condition = ShopProductCondition.Good;
        [SerializeField] private bool giftWrappable;
        [SerializeField] private string stableItemId;
        [SerializeField] private GameObject prizePrefab;
        [SerializeField] private ShopPrizePhysicsProfile physicsProfile;
        [SerializeField] private GameObject visualPrefab;
        [SerializeField] private Sprite icon;
        [SerializeField] private Color tint = Color.white;
        [Min(1)] [SerializeField] private int maxStack = 10;
        [SerializeField] private bool placeholderArtwork;
        [SerializeField] private bool exclusiveReward;

        public int ProductId => productId;
        public string DisplayName => displayName;
        public ShopProductCategory Category => category;
        public int SalePrice => salePrice;
        public ShopProductRarity Rarity => rarity;
        public ShopProductCondition Condition => condition;
        public bool GiftWrappable => giftWrappable;
        public string StableItemId => string.IsNullOrWhiteSpace(stableItemId)
            ? "product:" + productId
            : stableItemId;
        public GameObject PrizePrefab => prizePrefab;
        public ShopPrizePhysicsProfile PhysicsProfile => physicsProfile;
        public GameObject VisualPrefab => visualPrefab;
        public Sprite Icon => icon;
        public Color Tint => tint;
        public int MaxStack => Mathf.Max(1, maxStack);
        public bool PlaceholderArtwork => placeholderArtwork;
        public bool ExclusiveReward => exclusiveReward;

#if UNITY_EDITOR
        public void EditorConfigure(int id, string label, ShopProductCategory productCategory, int price,
            ShopProductRarity productRarity, ShopProductCondition productCondition, bool canWrap)
        {
            productId = id;
            displayName = label;
            category = productCategory;
            salePrice = Mathf.Max(1, price);
            rarity = productRarity;
            condition = productCondition;
            giftWrappable = canWrap;
        }

        public void EditorConfigurePrizeData(string itemId, GameObject prefab,
            ShopPrizePhysicsProfile profile, int stackLimit)
        {
            stableItemId = itemId;
            prizePrefab = prefab;
            physicsProfile = profile;
            maxStack = Mathf.Max(1, stackLimit);
        }

        public void EditorSetPlaceholderArtwork(bool placeholder) =>
            placeholderArtwork = placeholder;

        public void EditorSetExclusiveReward(bool exclusive) => exclusiveReward = exclusive;

        public void EditorConfigureVisual(GameObject prefab, Sprite sprite, Color color)
        {
            visualPrefab = prefab;
            icon = sprite;
            tint = color;
            placeholderArtwork = prefab == null;
        }
#endif
    }
}
