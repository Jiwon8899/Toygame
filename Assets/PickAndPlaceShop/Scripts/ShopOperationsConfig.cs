using System;
using System.Collections.Generic;
using UnityEngine;

namespace PickAndPlaceShop
{
    public enum ShopMultiPrizePolicy
    {
        SingleAndReturnExtras,
        AwardAll
    }

    public enum ShopAutomationState
    {
        NotInstalled,
        Off,
        Running,
        PausedForManualPlay,
        PausedForClosing,
        StoppedNoFunds,
        StoppedStorageFull
    }

    public enum ShopAcquisitionSource
    {
        Manual,
        Automation
    }

    [Serializable]
    public struct ShopRarityWeights
    {
        [Min(0)] public int common;
        [Min(0)] public int uncommon;
        [Min(0)] public int rare;
        [Min(0)] public int ultraRare;

        public int Total => Mathf.Max(0, common) + Mathf.Max(0, uncommon) +
                            Mathf.Max(0, rare) + Mathf.Max(0, ultraRare);

        public ShopProductRarity Pick(System.Random random, bool allowUltraRare)
        {
            int allowedUltra = allowUltraRare ? Mathf.Max(0, ultraRare) : 0;
            int total = Mathf.Max(0, common) + Mathf.Max(0, uncommon) +
                        Mathf.Max(0, rare) + allowedUltra;
            if (total <= 0) return ShopProductRarity.Common;
            int roll = random != null ? random.Next(total) : UnityEngine.Random.Range(0, total);
            if ((roll -= Mathf.Max(0, common)) < 0) return ShopProductRarity.Common;
            if ((roll -= Mathf.Max(0, uncommon)) < 0) return ShopProductRarity.Uncommon;
            if ((roll -= Mathf.Max(0, rare)) < 0) return ShopProductRarity.Rare;
            return ShopProductRarity.UltraRare;
        }
    }

    [CreateAssetMenu(menuName = "Pick And Place Shop/Operations Config", fileName = "ShopOperationsConfig")]
    public sealed class ShopOperationsConfig : ScriptableObject
    {
        public const string ResourcePath = "Operations/ShopOperationsConfig";

        [Header("Eight minute day")]
        [Min(1f)] [SerializeField] private float preparationSeconds = 120f;
        [Min(1f)] [SerializeField] private float openingSeconds = 300f;
        [Min(1f)] [SerializeField] private float closingSeconds = 60f;
        [SerializeField] private int[] salesGoalByStage = { 5, 8, 12, 16, 22, 30 };

        [Header("Trend")]
        [Range(0f, 1f)] [SerializeField] private float trendPriceBonus = 0.15f;
        [Range(1f, 4f)] [SerializeField] private float trendCustomerWeight = 1.7f;
        [Range(0f, 20f)] [SerializeField] private float trendSatisfactionBonus = 4f;

        [Header("Customers")]
        [Min(30)] [SerializeField] private int persistentCustomerCount = 30;
        [Min(1)] [SerializeField] private int regularPurchaseThreshold = 3;
        [Range(1, 24)] [SerializeField] private int maximumConcurrentCustomers = 6;
        [Range(0f, 1f)] [SerializeField] private float satisfactionWaitWeight = 0.55f;
        [Range(0f, 1f)] [SerializeField] private float satisfactionVarietyWeight = 0.30f;
        [Range(0f, 1f)] [SerializeField] private float satisfactionRarityWeight = 0.15f;
        [Range(1f, 2f)] [SerializeField] private float regularPriceMultiplier = 1.1f;
        [Range(0f, 1f)] [SerializeField] private float regularExtraPurchaseChance = 0.25f;

        [Header("Interaction")]
        [Min(0.5f)] [SerializeField] private float interactionDistance = 2.5f;
        [Range(-1f, 1f)] [SerializeField] private float interactionFacingThreshold = 0.2f;

        [Header("Online orders")]
        [Min(1)] [SerializeField] private int orderUnlockExpansionLevel = 2;
        [Min(0)] [SerializeField] private int orderUnlockReputation = 10;
        [Range(1, 5)] [SerializeField] private int baseConcurrentOrders = 2;
        [Range(1, 8)] [SerializeField] private int orderRoomConcurrentOrders = 5;
        [Min(1)] [SerializeField] private int orderRoomExpansionLevel = 5;
        [Range(1, 7)] [SerializeField] private int minimumOrderDeadlineDays = 1;
        [Range(1, 7)] [SerializeField] private int maximumOrderDeadlineDays = 3;
        [Range(1f, 3f)] [SerializeField] private float orderPriceMultiplier = 1.35f;
        [Min(0)] [SerializeField] private int orderReputationReward = 2;
        [Min(0)] [SerializeField] private int orderFailureReputationPenalty = 1;

        [Header("Automation")]
        [Min(0)] [SerializeField] private int automationUnlockReputation = 40;
        [Min(1)] [SerializeField] private int automationPurchasePrice = 1800;
        [Min(1f)] [SerializeField] private float automationAttemptInterval = 60f;
        [Range(0.01f, 1f)] [SerializeField] private float manualAverageSuccessRate = 0.90f;
        [Range(0.01f, 1f)] [SerializeField] private float automationSuccessMultiplier = 0.5f;
        [Range(1, 30)] [SerializeField] private int automationBufferSlots = 10;
        [Range(1, 30)] [SerializeField] private int automationNearFullSlots = 8;
        [SerializeField] private bool automatedSuccessCountsForDailyGoal;

        [Header("Collection rarity: 110 / 40 / 40 / 10")]
        [SerializeField] private ShopRarityWeights standardRarityWeights = new()
        {
            common = 110,
            uncommon = 40,
            rare = 40,
            ultraRare = 10
        };

        public float PreparationSeconds => preparationSeconds;
        public float OpeningSeconds => openingSeconds;
        public float ClosingSeconds => closingSeconds;
        public float TrendPriceBonus => trendPriceBonus;
        public float TrendCustomerWeight => trendCustomerWeight;
        public float TrendSatisfactionBonus => trendSatisfactionBonus;
        public int PersistentCustomerCount => Mathf.Max(30, persistentCustomerCount);
        public int RegularPurchaseThreshold => Mathf.Max(1, regularPurchaseThreshold);
        public int MaximumConcurrentCustomers => Mathf.Max(1, maximumConcurrentCustomers);
        public float SatisfactionWaitWeight => satisfactionWaitWeight;
        public float SatisfactionVarietyWeight => satisfactionVarietyWeight;
        public float SatisfactionRarityWeight => satisfactionRarityWeight;
        public float RegularPriceMultiplier => regularPriceMultiplier;
        public float RegularExtraPurchaseChance => regularExtraPurchaseChance;
        public float InteractionDistance => Mathf.Max(0.5f, interactionDistance);
        public float InteractionFacingThreshold => interactionFacingThreshold;
        public int OrderUnlockExpansionLevel => orderUnlockExpansionLevel;
        public int OrderUnlockReputation => orderUnlockReputation;
        public int BaseConcurrentOrders => baseConcurrentOrders;
        public int OrderRoomConcurrentOrders => orderRoomConcurrentOrders;
        public int OrderRoomExpansionLevel => orderRoomExpansionLevel;
        public int MinimumOrderDeadlineDays => minimumOrderDeadlineDays;
        public int MaximumOrderDeadlineDays => Mathf.Max(minimumOrderDeadlineDays, maximumOrderDeadlineDays);
        public float OrderPriceMultiplier => orderPriceMultiplier;
        public int OrderReputationReward => orderReputationReward;
        public int OrderFailureReputationPenalty => orderFailureReputationPenalty;
        public int AutomationUnlockReputation => automationUnlockReputation;
        public int AutomationPurchasePrice => automationPurchasePrice;
        public float AutomationAttemptInterval => automationAttemptInterval;
        public float ManualAverageSuccessRate => manualAverageSuccessRate;
        public float AutomationSuccessMultiplier => automationSuccessMultiplier;
        public int AutomationBufferSlots => automationBufferSlots;
        public int AutomationNearFullSlots => Mathf.Min(automationBufferSlots, automationNearFullSlots);
        public bool AutomatedSuccessCountsForDailyGoal => automatedSuccessCountsForDailyGoal;
        public ShopRarityWeights StandardRarityWeights => standardRarityWeights;

        public int SalesGoalForStage(int zeroBasedStage)
        {
            if (salesGoalByStage == null || salesGoalByStage.Length == 0) return 1;
            return Mathf.Max(1, salesGoalByStage[Mathf.Clamp(zeroBasedStage, 0, salesGoalByStage.Length - 1)]);
        }

        public float AutomaticSuccessRate => Mathf.Clamp01(manualAverageSuccessRate * automationSuccessMultiplier);

        public static ShopOperationsConfig Load()
        {
            return Resources.Load<ShopOperationsConfig>(ResourcePath);
        }
    }
}
