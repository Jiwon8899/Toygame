using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Side Content Config", fileName = "ShopSideContentConfig")]
    public sealed class ShopSideContentConfig : ScriptableObject
    {
        public const string ResourcePath = "SideContent/ShopSideContentConfig";

        [Header("Trash search")]
        [Range(0f, 1f)] [SerializeField] private float trashSuccessChance = 0.2f;
        [Min(1)] [SerializeField] private int trashRewardMinimum = 25;
        [Min(1)] [SerializeField] private int trashRewardMaximum = 100;
        [Min(1)] [SerializeField] private int trashDailyCap = 500;
        [Min(0.1f)] [SerializeField] private float trashSearchCooldown = 2.5f;

        [Header("Claw dud")]
        [Range(0f, 0.25f)] [SerializeField] private float commonCapsuleDudChance = 0.05f;

        [Header("Robber customer")]
        [Range(0f, 1f)] [SerializeField] private float robberChance = 0.06f;
        [Min(0)] [SerializeField] private int robberDailyMaximum = 2;
        [Min(1f)] [SerializeField] private float robberSpeedMultiplier = 1.65f;
        [Min(0.1f)] [SerializeField] private float customerKnockbackDistance = 2.2f;
        [Min(0.05f)] [SerializeField] private float customerKnockbackSeconds = 0.35f;
        [Min(0)] [SerializeField] private int robberArrestReward = 300;

        [Header("Discount requester")]
        [Range(0f, 1f)] [SerializeField] private float discountRequestChance = 0.12f;
        [Range(0f, 0.5f)] [SerializeField] private float fullDiscount = 0.15f;
        [Range(0f, 0.5f)] [SerializeField] private float partialDiscount = 0.07f;

        [Header("Rival shop")]
        [SerializeField] private Vector4 rivalRarityWeights = new(0.65f, 0.23f, 0.1f, 0.02f);
        [SerializeField] private Vector4 rivalAlertByRarity = new(8f, 16f, 32f, 64f);
        [Min(0f)] [SerializeField] private float rivalOwnerCatchAlert = 0f;

        [Header("Skewer")]
        [Min(0f)] [SerializeField] private float skewerAlertPerSecond = 9f;
        [Min(0.1f)] [SerializeField] private float skewerMachineRange = 3.2f;

        [Header("Collection sets")]
        [Range(5, 10)] [SerializeField] private int smallSetSize = 5;
        [Min(0)] [SerializeField] private int smallSetReward = 250;
        [Range(0f, 0.5f)] [SerializeField] private float categoryCompletionSaleBonus = 0.08f;

        public float TrashSuccessChance => Mathf.Clamp01(trashSuccessChance);
        public int TrashRewardMinimum => Mathf.Max(1, trashRewardMinimum);
        public int TrashRewardMaximum => Mathf.Max(TrashRewardMinimum, trashRewardMaximum);
        public int TrashReward => TrashRewardMaximum;
        public int TrashDailyCap => Mathf.Max(TrashRewardMaximum, trashDailyCap);
        public float TrashSearchCooldown => Mathf.Max(0.1f, trashSearchCooldown);
        public float CommonCapsuleDudChance => Mathf.Clamp(commonCapsuleDudChance, 0f, 0.25f);
        public float RobberChance => Mathf.Clamp01(robberChance);
        public int RobberDailyMaximum => Mathf.Max(0, robberDailyMaximum);
        public float RobberSpeedMultiplier => Mathf.Max(1f, robberSpeedMultiplier);
        public float CustomerKnockbackDistance => Mathf.Max(0.1f, customerKnockbackDistance);
        public float CustomerKnockbackSeconds => Mathf.Max(0.05f, customerKnockbackSeconds);
        public int RobberArrestReward => Mathf.Max(0, robberArrestReward);
        public float DiscountRequestChance => Mathf.Clamp01(discountRequestChance);
        public float FullDiscount => Mathf.Clamp(fullDiscount, 0f, 0.5f);
        public float PartialDiscount => Mathf.Clamp(partialDiscount, 0f, FullDiscount);
        public float RivalOwnerCatchAlert => Mathf.Max(0f, rivalOwnerCatchAlert);
        public float SkewerAlertPerSecond => Mathf.Max(0f, skewerAlertPerSecond);
        public float SkewerMachineRange => Mathf.Max(0.1f, skewerMachineRange);
        public int SmallSetSize => Mathf.Clamp(smallSetSize, 5, 10);
        public int SmallSetReward => Mathf.Max(0, smallSetReward);
        public float CategoryCompletionSaleBonus => Mathf.Clamp(categoryCompletionSaleBonus, 0f, 0.5f);

        public float RivalAlert(ShopProductRarity rarity)
        {
            int index = Mathf.Clamp((int)rarity, 0, 3);
            return Mathf.Max(0f, rivalAlertByRarity[index]);
        }

        public ShopProductRarity PickRivalRarity(float roll)
        {
            float total = Mathf.Max(0.0001f, rivalRarityWeights.x + rivalRarityWeights.y +
                rivalRarityWeights.z + rivalRarityWeights.w);
            float value = Mathf.Clamp01(roll) * total;
            if (value < rivalRarityWeights.x) return ShopProductRarity.Common;
            value -= rivalRarityWeights.x;
            if (value < rivalRarityWeights.y) return ShopProductRarity.Uncommon;
            value -= rivalRarityWeights.y;
            return value < rivalRarityWeights.z ? ShopProductRarity.Rare : ShopProductRarity.UltraRare;
        }

        public static ShopSideContentConfig Load() => Resources.Load<ShopSideContentConfig>(ResourcePath);
    }

    public static class ShopSideContentRules
    {
        public static bool IsClawDud(ShopProductRarity rarity, float roll, ShopSideContentConfig config) =>
            config != null && rarity == ShopProductRarity.Common &&
            Mathf.Clamp01(roll) < config.CommonCapsuleDudChance;

        public static int ApplySaleMultiplier(int basePrice, float multiplier) =>
            Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0, basePrice) * Mathf.Clamp(multiplier, 0.5f, 2f)));
    }
}
