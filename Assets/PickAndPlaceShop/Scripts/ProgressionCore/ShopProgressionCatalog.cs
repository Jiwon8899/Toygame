using System;
using System.Collections.Generic;
using UnityEngine;

namespace PickAndPlaceShop
{
    public enum ShopProgressConditionType
    {
        Reputation,
        LifetimeRevenue,
        UnitsSold,
        RareItemsAcquired,
        RareItemsSold,
        RegularCustomers,
        AverageSatisfaction,
        OnlineOrdersCompleted,
        CollectionPercent,
        CategoryItemsOwned,
        ClawSuccesses
    }

    public enum ShopProgressRewardType
    {
        Reputation,
        TeamFunds,
        RandomBox,
        UnlockDistrict,
        ExpansionVoucher,
        RareItem
    }

    public enum ShopProgressRarity
    {
        Common,
        Uncommon,
        Rare,
        Premium
    }

    [Flags]
    public enum ShopExpansionFeature
    {
        None = 0,
        Checkout = 1 << 0,
        PackingTable = 1 << 1,
        ShowWindow = 1 << 2,
        SecondFloor = 1 << 3,
        OnlineOrderRoom = 1 << 4
    }

    [Serializable]
    public sealed class ShopProgressCondition
    {
        [SerializeField] private ShopProgressConditionType type;
        [SerializeField, Min(0)] private int target;
        [SerializeField] private string categoryId;
        [SerializeField] private string displayName;

        public ShopProgressConditionType Type => type;
        public int Target => target;
        public string CategoryId => categoryId ?? string.Empty;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? type.ToString() : displayName;

        public ShopProgressCondition(ShopProgressConditionType conditionType, int targetValue,
            string label, string category = "")
        {
            type = conditionType;
            target = Mathf.Max(0, targetValue);
            displayName = label;
            categoryId = category;
        }
    }

    [Serializable]
    public sealed class ShopProgressReward
    {
        [SerializeField] private ShopProgressRewardType type;
        [SerializeField, Min(0)] private int amount;
        [SerializeField] private string targetId;
        [SerializeField] private string displayName;

        public ShopProgressRewardType Type => type;
        public int Amount => amount;
        public string TargetId => targetId ?? string.Empty;
        public string DisplayName => displayName ?? string.Empty;

        public ShopProgressReward(ShopProgressRewardType rewardType, int rewardAmount,
            string label, string stableTargetId = "")
        {
            type = rewardType;
            amount = Mathf.Max(0, rewardAmount);
            targetId = stableTargetId;
            displayName = label;
        }
    }

    [Serializable]
    public sealed class ShopProgressStage
    {
        [SerializeField] private string stageId;
        [SerializeField] private string displayName;
        [SerializeField] private List<ShopProgressCondition> conditions = new();
        [SerializeField] private List<ShopProgressReward> rewards = new();

        public string StageId => stageId;
        public string DisplayName => displayName;
        public IReadOnlyList<ShopProgressCondition> Conditions => conditions;
        public IReadOnlyList<ShopProgressReward> Rewards => rewards;

        public ShopProgressStage(string id, string label, IEnumerable<ShopProgressCondition> requirements,
            IEnumerable<ShopProgressReward> stageRewards)
        {
            stageId = id;
            displayName = label;
            conditions = requirements != null ? new List<ShopProgressCondition>(requirements) : new();
            rewards = stageRewards != null ? new List<ShopProgressReward>(stageRewards) : new();
        }
    }

    [Serializable]
    public sealed class ShopExpansionTier
    {
        [SerializeField, Range(1, 6)] private int level = 1;
        [SerializeField, Min(0)] private int requiredReputation;
        [SerializeField, Min(0)] private int requiredFunds;
        [SerializeField, Min(1)] private int displaySlots = 4;
        [SerializeField, Min(1)] private int storageSlots = 30;
        [SerializeField, Min(1)] private int checkoutCount = 1;
        [SerializeField] private ShopExpansionFeature features = ShopExpansionFeature.Checkout;

        public int Level => level;
        public int RequiredReputation => requiredReputation;
        public int RequiredFunds => requiredFunds;
        public int DisplaySlots => displaySlots;
        public int StorageSlots => storageSlots;
        public int CheckoutCount => checkoutCount;
        public ShopExpansionFeature Features => features;

        public ShopExpansionTier(int tier, int reputation, int funds, int displays, int storage,
            int checkouts, ShopExpansionFeature unlockedFeatures)
        {
            level = Mathf.Clamp(tier, 1, 6);
            requiredReputation = Mathf.Max(0, reputation);
            requiredFunds = Mathf.Max(0, funds);
            displaySlots = Mathf.Max(1, displays);
            storageSlots = Mathf.Max(1, storage);
            checkoutCount = Mathf.Max(1, checkouts);
            features = unlockedFeatures;
        }
    }

    [Serializable]
    public sealed class ShopDistrictUnlock
    {
        [SerializeField] private string districtId;
        [SerializeField] private string displayName;
        [SerializeField, Min(0)] private int requiredReputation;
        [SerializeField] private bool placeholder;
        [SerializeField] private bool provisionalName;

        public string DistrictId => districtId;
        public string DisplayName => displayName;
        public int RequiredReputation => requiredReputation;
        public bool Placeholder => placeholder;
        public bool ProvisionalName => provisionalName;

        public ShopDistrictUnlock(string id, string label, int reputation, bool isPlaceholder = false,
            bool isProvisionalName = false)
        {
            districtId = id;
            displayName = label;
            requiredReputation = isPlaceholder ? int.MaxValue : Mathf.Max(0, reputation);
            placeholder = isPlaceholder;
            provisionalName = isProvisionalName;
        }
    }

    [Serializable]
    public sealed class ShopCollectionCategory
    {
        [SerializeField] private string categoryId;
        [SerializeField] private string displayName;

        public string CategoryId => categoryId ?? string.Empty;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? CategoryId
            : displayName;

        public ShopCollectionCategory(string id, string label)
        {
            categoryId = id;
            displayName = label;
        }
    }

    [Serializable]
    public sealed class ShopCollectionItem
    {
        [SerializeField] private string itemId;
        [SerializeField] private string displayName;
        [SerializeField] private string categoryId;
        [SerializeField] private ShopProgressRarity rarity;

        public string ItemId => itemId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? ItemId : displayName;
        public string CategoryId => categoryId;
        public ShopProgressRarity Rarity => rarity;

        public ShopCollectionItem(string id, string label, string category, ShopProgressRarity itemRarity)
        {
            itemId = id;
            displayName = label;
            categoryId = category;
            rarity = itemRarity;
        }
    }

    [Serializable]
    public sealed class ShopGoalDefinition
    {
        [SerializeField] private string goalId;
        [SerializeField] private string displayName;
        [SerializeField] private bool weekly;
        [SerializeField] private ShopProgressConditionType conditionType;
        [SerializeField, Min(1)] private int minimumTarget = 1;
        [SerializeField, Min(1)] private int maximumTarget = 1;
        [SerializeField] private string categoryId;
        [SerializeField] private List<ShopProgressReward> rewards = new();

        public string GoalId => goalId;
        public string DisplayName => displayName;
        public bool Weekly => weekly;
        public ShopProgressConditionType ConditionType => conditionType;
        public int MinimumTarget => minimumTarget;
        public int MaximumTarget => Mathf.Max(minimumTarget, maximumTarget);
        public string CategoryId => categoryId ?? string.Empty;
        public IReadOnlyList<ShopProgressReward> Rewards => rewards;

        public ShopGoalDefinition(string id, string label, bool isWeekly,
            ShopProgressConditionType type, int minimum, int maximum,
            IEnumerable<ShopProgressReward> goalRewards, string category = "")
        {
            goalId = id;
            displayName = label;
            weekly = isWeekly;
            conditionType = type;
            minimumTarget = Mathf.Max(1, minimum);
            maximumTarget = Mathf.Max(minimumTarget, maximum);
            categoryId = category;
            rewards = goalRewards != null ? new List<ShopProgressReward>(goalRewards) : new();
        }
    }

    [Serializable]
    public sealed class ShopMasteryTier
    {
        [SerializeField, Min(0)] private int successes;
        [SerializeField] private string title;

        public int Successes => successes;
        public string Title => title;

        public ShopMasteryTier(int requiredSuccesses, string masteryTitle)
        {
            successes = Mathf.Max(0, requiredSuccesses);
            title = masteryTitle;
        }
    }

    [Serializable]
    public sealed class ShopCollectionMilestone
    {
        [SerializeField, Range(1, 100)] private int percent;
        [SerializeField, Min(0)] private int reputationReward;

        public int Percent => percent;
        public int ReputationReward => reputationReward;

        public ShopCollectionMilestone(int targetPercent, int reputation)
        {
            percent = Mathf.Clamp(targetPercent, 1, 100);
            reputationReward = Mathf.Max(0, reputation);
        }
    }

    [CreateAssetMenu(fileName = "ShopProgressionCatalog",
        menuName = "Pick And Place Shop/Progression/Progression Catalog")]
    public sealed class ShopProgressionCatalog : ScriptableObject
    {
        [TextArea(3, 8)] [SerializeField] private string designDecisions;
        [SerializeField] private List<ShopProgressStage> stages = new();
        [SerializeField] private List<ShopExpansionTier> expansionTiers = new();
        [SerializeField] private List<ShopDistrictUnlock> districtUnlocks = new();
        [SerializeField] private List<ShopCollectionCategory> collectionCategories = new();
        [SerializeField] private List<ShopCollectionItem> collectionItems = new();
        [SerializeField] private List<ShopGoalDefinition> goalPool = new();
        [SerializeField] private List<ShopCollectionMilestone> collectionMilestones = new();
        [SerializeField] private List<ShopMasteryTier> masteryTiers = new();
        [SerializeField, Min(1)] private int dailyGoalCount = 3;
        [SerializeField, Min(1)] private int weeklyGoalCount = 2;

        public string DesignDecisions => designDecisions;
        public IReadOnlyList<ShopProgressStage> Stages => stages;
        public IReadOnlyList<ShopExpansionTier> ExpansionTiers => expansionTiers;
        public IReadOnlyList<ShopDistrictUnlock> DistrictUnlocks => districtUnlocks;
        public IReadOnlyList<ShopCollectionCategory> CollectionCategories => collectionCategories;
        public IReadOnlyList<ShopCollectionItem> CollectionItems => collectionItems;
        public IReadOnlyList<ShopGoalDefinition> GoalPool => goalPool;
        public IReadOnlyList<ShopCollectionMilestone> CollectionMilestones => collectionMilestones;
        public IReadOnlyList<ShopMasteryTier> MasteryTiers => masteryTiers;
        public int DailyGoalCount => Mathf.Max(1, dailyGoalCount);
        public int WeeklyGoalCount => Mathf.Max(1, weeklyGoalCount);

        public string GetCategoryDisplayName(string categoryId)
        {
            for (int i = 0; i < collectionCategories.Count; i++)
            {
                ShopCollectionCategory category = collectionCategories[i];
                if (category != null &&
                    string.Equals(category.CategoryId, categoryId, StringComparison.Ordinal))
                    return category.DisplayName;
            }
            return categoryId ?? string.Empty;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            HashSet<string> knownCategories = new(StringComparer.Ordinal);
            for (int i = 0; i < collectionCategories.Count; i++)
            {
                ShopCollectionCategory category = collectionCategories[i];
                if (category == null || string.IsNullOrWhiteSpace(category.CategoryId)) continue;
                knownCategories.Add(category.CategoryId);
                if (string.Equals(category.DisplayName, category.CategoryId, StringComparison.Ordinal))
                    Debug.LogWarning("[Progression] 컬렉션 카테고리 표시명이 비어 있습니다: " +
                                     category.CategoryId, this);
            }

            for (int i = 0; i < collectionItems.Count; i++)
            {
                ShopCollectionItem item = collectionItems[i];
                if (item == null) continue;
                if (string.Equals(item.DisplayName, item.ItemId, StringComparison.Ordinal))
                    Debug.LogWarning("[Progression] 컬렉션 상품 표시명이 비어 있습니다: " +
                                     item.ItemId, this);
                if (!string.IsNullOrWhiteSpace(item.CategoryId) &&
                    !knownCategories.Contains(item.CategoryId))
                    Debug.LogWarning("[Progression] 표시명 데이터가 없는 컬렉션 카테고리: " +
                                     item.CategoryId, this);
            }
        }

        public void EditorConfigure(string decisions, IEnumerable<ShopProgressStage> stageData,
            IEnumerable<ShopExpansionTier> expansionData, IEnumerable<ShopDistrictUnlock> districtData,
            IEnumerable<ShopCollectionItem> itemData, IEnumerable<ShopGoalDefinition> goals,
            IEnumerable<ShopCollectionMilestone> milestones, IEnumerable<ShopMasteryTier> mastery,
            int dailyCount, int weeklyCount,
            IEnumerable<ShopCollectionCategory> categoryData = null)
        {
            designDecisions = decisions;
            stages = new List<ShopProgressStage>(stageData);
            expansionTiers = new List<ShopExpansionTier>(expansionData);
            districtUnlocks = new List<ShopDistrictUnlock>(districtData);
            if (categoryData != null)
                collectionCategories = new List<ShopCollectionCategory>(categoryData);
            collectionItems = new List<ShopCollectionItem>(itemData);
            goalPool = new List<ShopGoalDefinition>(goals);
            collectionMilestones = new List<ShopCollectionMilestone>(milestones);
            masteryTiers = new List<ShopMasteryTier>(mastery);
            dailyGoalCount = Mathf.Max(1, dailyCount);
            weeklyGoalCount = Mathf.Max(1, weeklyCount);
        }
#endif
    }
}
