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
        StoppedSoldOut,
        StoppedStorageFull
    }

    public enum ShopAcquisitionSource
    {
        Manual,
        Automation,
        Consignment
    }

    public enum ShopCustomerDialogueEvent
    {
        ExitWithoutPurchase,
        HighSatisfactionPurchase,
        LongWaitComplaint
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

        [Header("Economy")]
        [Min(0)] [SerializeField] private int newGameStartingFunds = 10000;

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

        [Header("Daily machine stock")]
        [Range(1, 40)] [SerializeField] private int machineDailyCapsuleCapacity = 20;

        [Header("Optional checkout negotiation")]
        [Range(1, 5)] [SerializeField] private int negotiationAttemptsPerSale = 3;
        [Range(0.05f, 0.45f)] [SerializeField] private float negotiationSuccessHalfWidth = 0.22f;
        [Range(0f, 0.5f)] [SerializeField] private float negotiationMinimumBonus = 0.10f;
        [Range(0f, 0.5f)] [SerializeField] private float negotiationMaximumBonus = 0.30f;
        [Range(0.2f, 3f)] [SerializeField] private float negotiationMarkerCyclesPerSecond = 0.75f;

        [Header("Narrative AI")]
        [SerializeField] private bool narrativeAIEnabled = true;
        [SerializeField] private string narrativeEndpoint = "https://api.anthropic.com/v1/messages";
        [SerializeField] private string narrativeModel = "claude-haiku-4-5-20251001";
        [SerializeField] private string narrativeApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";
        [TextArea(1, 2)] [SerializeField] private string narrativeSystemPrompt =
            "게임 상태만 근거로 이모지 없이 자연스러운 한국어 한 문장을 작성하세요.";
        [Range(1, 100)] [SerializeField] private int narrativeMaxTokens = 96;
        [Range(1f, 10f)] [SerializeField] private float narrativeTimeoutSeconds = 3f;
        [Range(1, 10)] [SerializeField] private int narrativeRequestsPerSecond = 1;
        [Range(1, 60)] [SerializeField] private int narrativeRequestsPerMinute = 10;
        [Range(1f, 10f)] [SerializeField] private float dialogueBubbleSeconds = 3f;
        [Range(1, 5)] [SerializeField] private int maximumDialogueBubbles = 2;
        [Range(50, 100)] [SerializeField] private int highSatisfactionDialogueThreshold = 85;
        [SerializeField] private string[] exitWithoutPurchaseFallbacks =
        {
            "{선호카테고리} 상품이 진열되면 다음에는 꼭 들를게요.",
            "오늘은 제가 찾던 {선호카테고리} 상품이 없어서 아쉬워요.",
            "{오늘뉴스} 소식을 듣고 왔는데 원하는 상품을 못 찾았어요.",
            "제 취향인 {선호카테고리} 진열을 다음에는 기대할게요.",
            "오늘은 빈손이지만 {선호카테고리} 상품이 들어오면 다시 올게요."
        };
        [SerializeField] private string[] highSatisfactionPurchaseFallbacks =
        {
            "{상품명}을 찾아서 정말 만족스러워요.",
            "제 취향인 {선호카테고리} 상품을 잘 골랐어요.",
            "{오늘뉴스} 소문처럼 {상품명}이 마음에 쏙 들어요.",
            "기다린 보람이 있을 만큼 {상품명}이 마음에 들어요.",
            "다음 방문에도 이런 {선호카테고리} 상품을 만나고 싶어요."
        };
        [SerializeField] private string[] longWaitComplaintFallbacks =
        {
            "{대기시간}초나 기다려서 조금 지쳤어요.",
            "{상품명}을 사고 싶었지만 줄이 너무 오래 걸렸어요.",
            "{오늘뉴스} 소문 때문에 붐비는 건 알지만 계산은 더 빨랐으면 해요.",
            "{선호카테고리} 상품은 좋았지만 대기 시간이 아쉬워요.",
            "다음에는 {대기시간}초보다 빨리 계산할 수 있으면 좋겠어요."
        };
        [SerializeField] private string[] trendNewsFallbacks =
        {
            "포근한 고양이 인형 인증 사진이 퍼지며 봉제 인형이 오늘의 화제가 됐어요.",
            "새로운 고양이 인형 수집 영상이 인기를 끌며 봉제 인형을 찾는 손님이 늘었어요.",
            "동네 축제의 고양이 인형 전시가 입소문을 타며 봉제 인형 유행이 시작됐어요.",
            "정교한 고양이 피규어 사진이 화제가 되어 피규어 수집 열기가 높아졌어요.",
            "한정 고양이 피규어 개봉 영상이 인기라 오늘은 피규어를 찾는 손님이 많아요.",
            "수집가 모임의 고양이 피규어 전시 소식이 퍼지며 피규어가 유행이에요.",
            "책상 꾸미기 사진에 나온 고양이 소품이 화제가 되어 굿즈 수요가 늘었어요.",
            "고양이 굿즈 선물 추천이 입소문을 타며 작은 소품이 오늘의 인기 상품이에요.",
            "수집가의 거리에서 고양이 굿즈 교환 행사가 열려 소품을 찾는 손님이 늘었어요.",
            "계절 한정 고양이 장식 사진이 퍼지며 시즌 상품이 오늘의 유행이 됐어요.",
            "벚꽃길의 계절 장식 소식이 화제가 되어 고양이 시즌 상품이 인기예요.",
            "날씨에 맞춘 고양이 소품 추천이 유행하며 계절 상품을 찾는 손님이 많아요.",
            "옛 문구점 감성의 고양이 소품이 다시 주목받으며 레트로 상품이 인기예요.",
            "복고풍 고양이 굿즈 사진이 입소문을 타며 레트로 수집 열기가 높아졌어요.",
            "추억의 고양이 문구 전시 소식이 퍼져 오늘은 레트로 상품이 유행이에요."
        };

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
        public int NewGameStartingFunds => Mathf.Max(0, newGameStartingFunds);
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
        public int MachineDailyCapsuleCapacity => Mathf.Clamp(machineDailyCapsuleCapacity, 1, 40);
        public int NegotiationAttemptsPerSale => Mathf.Clamp(negotiationAttemptsPerSale, 1, 5);
        public float NegotiationSuccessHalfWidth => Mathf.Clamp(negotiationSuccessHalfWidth, 0.05f, 0.45f);
        public float NegotiationMinimumBonus => Mathf.Clamp(negotiationMinimumBonus, 0f, 0.5f);
        public float NegotiationMaximumBonus => Mathf.Max(NegotiationMinimumBonus,
            Mathf.Clamp(negotiationMaximumBonus, 0f, 0.5f));
        public float NegotiationMarkerCyclesPerSecond => Mathf.Clamp(negotiationMarkerCyclesPerSecond, 0.2f, 3f);
        public bool NarrativeAIEnabled => narrativeAIEnabled;
        public string NarrativeEndpoint => narrativeEndpoint;
        public string NarrativeModel => narrativeModel;
        public string NarrativeApiKeyEnvironmentVariable => narrativeApiKeyEnvironmentVariable;
        public string NarrativeSystemPrompt => narrativeSystemPrompt;
        public int NarrativeMaxTokens => Mathf.Clamp(narrativeMaxTokens, 1, 100);
        public float NarrativeTimeoutSeconds => Mathf.Max(1f, narrativeTimeoutSeconds);
        public int NarrativeRequestsPerSecond => Mathf.Max(1, narrativeRequestsPerSecond);
        public int NarrativeRequestsPerMinute => Mathf.Max(1, narrativeRequestsPerMinute);
        public float DialogueBubbleSeconds => Mathf.Max(1f, dialogueBubbleSeconds);
        public int MaximumDialogueBubbles => Mathf.Max(1, maximumDialogueBubbles);
        public int HighSatisfactionDialogueThreshold => Mathf.Clamp(highSatisfactionDialogueThreshold, 50, 100);
        public ShopRarityWeights StandardRarityWeights => standardRarityWeights;

        public string CustomerDialogueFallback(ShopCustomerDialogueEvent eventType, int seed)
        {
            string[] source = eventType switch
            {
                ShopCustomerDialogueEvent.HighSatisfactionPurchase => highSatisfactionPurchaseFallbacks,
                ShopCustomerDialogueEvent.LongWaitComplaint => longWaitComplaintFallbacks,
                _ => exitWithoutPurchaseFallbacks
            };
            if (source == null || source.Length == 0) return "오늘 가게 경험을 다음 방문에도 기억할게요.";
            return source[Mathf.Abs(seed) % source.Length];
        }

        public string TrendNewsFallback(ShopProductCategory category, int day)
        {
            int categoryIndex = category switch
            {
                ShopProductCategory.CatPlush => 0,
                ShopProductCategory.CatFigure => 1,
                ShopProductCategory.CatGoods => 2,
                ShopProductCategory.CatSeasonal => 3,
                ShopProductCategory.CatRetro => 4,
                _ => 2
            };
            int offset = categoryIndex * 3 + Mathf.Abs(day) % 3;
            return trendNewsFallbacks != null && trendNewsFallbacks.Length > offset
                ? trendNewsFallbacks[offset]
                : ShopProductLocalization.CategoryLabel(category) + " 상품이 동네에서 입소문을 타고 있어요.";
        }

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
