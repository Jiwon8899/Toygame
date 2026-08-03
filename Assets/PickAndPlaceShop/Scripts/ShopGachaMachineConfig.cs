using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Gacha Machine", fileName = "GachaMachine")]
    public sealed class ShopGachaMachineConfig : ScriptableObject
    {
        [SerializeField] private string machineId = "gacha_default";
        [SerializeField] private string displayName = "동물 친구들 가챠";
        [SerializeField, Min(1)] private int attemptCost = 80;
        [SerializeField, Min(1)] private int dailyStock = 12;
        [SerializeField, Range(0f, 1f)] private float uncommonChance = 0.25f;
        [SerializeField, Range(0f, 1f)] private float rareChance = 0.08f;
        [SerializeField] private string[] commonProducts = { "작은 고양이 키링", "포근한 강아지 배지" };
        [SerializeField] private string[] uncommonProducts = { "반짝 토끼 피규어" };
        [SerializeField] private string[] rareProducts = { "별빛 여우 한정판" };
        [SerializeField] private ShopProductDefinition[] commonProductDefinitions;
        [SerializeField] private ShopProductDefinition[] uncommonProductDefinitions;
        [SerializeField] private ShopProductDefinition[] rareProductDefinitions;
        [SerializeField] private Color capsuleColor = new(0.35f, 0.85f, 0.95f, 1f);

        public string MachineId => machineId;
        public string DisplayName => displayName;
        public int AttemptCost => attemptCost;
        public int DailyStock => dailyStock;
        public float UncommonChance => uncommonChance;
        public float RareChance => rareChance;
        public Color CapsuleColor => capsuleColor;

        public string ProductFor(ShopGachaRarity rarity, int attemptId)
        {
            ShopProductDefinition definition = ProductDefinitionFor(rarity, attemptId);
            if (definition != null) return definition.DisplayName;
            string[] pool = rarity == ShopGachaRarity.Rare ? rareProducts :
                rarity == ShopGachaRarity.Uncommon ? uncommonProducts : commonProducts;
            if (pool == null || pool.Length == 0) return displayName + " 상품";
            int index = Mathf.Abs(attemptId) % pool.Length;
            return pool[index];
        }

        public ShopProductDefinition ProductDefinitionFor(ShopGachaRarity rarity, int attemptId)
        {
            ShopProductDefinition[] pool = rarity == ShopGachaRarity.Rare
                ? rareProductDefinitions
                : rarity == ShopGachaRarity.Uncommon
                    ? uncommonProductDefinitions
                    : commonProductDefinitions;
            if (pool == null || pool.Length == 0) return null;
            int index = Mathf.Abs(attemptId) % pool.Length;
            return pool[index];
        }

#if UNITY_EDITOR
        public void EditorConfigure(string id, string label, int cost, int stock, float uncommon, float rare,
            string[] common, string[] special, string[] premium, Color color)
        {
            machineId = id;
            displayName = label;
            attemptCost = Mathf.Max(1, cost);
            dailyStock = Mathf.Max(1, stock);
            uncommonChance = Mathf.Clamp01(uncommon);
            rareChance = Mathf.Clamp01(rare);
            commonProducts = common;
            uncommonProducts = special;
            rareProducts = premium;
            capsuleColor = color;
        }

        public void EditorConfigureProducts(ShopProductDefinition[] common,
            ShopProductDefinition[] uncommon, ShopProductDefinition[] rare)
        {
            commonProductDefinitions = common;
            uncommonProductDefinitions = uncommon;
            rareProductDefinitions = rare;
            commonProducts = ToNames(common);
            uncommonProducts = ToNames(uncommon);
            rareProducts = ToNames(rare);
        }

        private static string[] ToNames(ShopProductDefinition[] products)
        {
            if (products == null) return System.Array.Empty<string>();
            string[] names = new string[products.Length];
            for (int i = 0; i < products.Length; i++)
                names[i] = products[i] != null ? products[i].DisplayName : string.Empty;
            return names;
        }
#endif
    }
}
