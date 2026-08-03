using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopProgressionRulesTests
    {
        private ShopProgressionCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            catalog = Resources.Load<ShopProgressionCatalog>(
                "Progression/ShopProgressionCatalog");
            Assert.That(catalog, Is.Not.Null, "진행 카탈로그 Resources 에셋이 필요합니다.");
        }

        [Test]
        public void Catalog_HasRequiredDataDrivenAxes()
        {
            Assert.That(catalog.Stages.Count, Is.EqualTo(7));
            Assert.That(catalog.ExpansionTiers.Count, Is.EqualTo(6));
            Assert.That(catalog.CollectionItems.Count, Is.GreaterThan(0));
            Assert.That(catalog.CollectionItems.Count, Is.Not.EqualTo(350),
                "컬렉션 총량은 실제 등록 에셋 수를 사용해야 합니다.");
            Assert.That(catalog.DailyGoalCount, Is.EqualTo(3));
            Assert.That(catalog.WeeklyGoalCount, Is.EqualTo(2));
            Assert.That(catalog.GoalPool.Any(goal =>
                goal.ConditionType == ShopProgressConditionType.CategoryItemsOwned &&
                goal.CategoryId == "claw"), Is.True,
                "구현된 상품 획득 흐름은 일일 공동 목표에서 사용해야 합니다.");
            Assert.That(catalog.GoalPool.Any(goal =>
                goal.ConditionType == ShopProgressConditionType.OnlineOrdersCompleted), Is.False,
                "아직 없는 온라인 주문 기능은 목표 풀에서 작동시키지 않습니다.");
        }

        [Test]
        public void CollectionCategoryDisplayNames_AreDataDrivenAndComplete()
        {
            Assert.That(catalog.CollectionCategories.Count, Is.GreaterThan(0));
            Assert.That(catalog.CollectionCategories.Select(category => category.CategoryId).Distinct().Count(),
                Is.EqualTo(catalog.CollectionCategories.Count));
            foreach (ShopCollectionCategory category in catalog.CollectionCategories)
            {
                Assert.That(category.DisplayName, Is.Not.Empty);
                Assert.That(category.DisplayName, Is.Not.EqualTo(category.CategoryId),
                    "내부 카테고리 ID가 표시명으로 노출되면 안 됩니다: " + category.CategoryId);
            }
            foreach (ShopCollectionItem item in catalog.CollectionItems)
            {
                string displayName = catalog.GetCategoryDisplayName(item.CategoryId);
                Assert.That(displayName, Is.Not.Empty);
                Assert.That(displayName, Is.Not.EqualTo(item.CategoryId),
                    "카테고리 표시명 데이터가 누락되었습니다: " + item.CategoryId);
                Assert.That(item.DisplayName, Is.Not.EqualTo(item.ItemId),
                    "상품 표시명 데이터가 누락되었습니다: " + item.ItemId);
            }
        }

        [Test]
        public void DistrictTruthTable_HasExactlyNineActiveUnlocks()
        {
            ShopDistrictUnlock[] active = catalog.DistrictUnlocks
                .Where(district => !district.Placeholder).ToArray();
            Assert.That(active.Select(district => district.RequiredReputation),
                Is.EqualTo(new[] { 5, 10, 20, 30, 40, 50, 60, 70, 90 }));
            Assert.That(active.Select(district => district.DistrictId).Distinct().Count(),
                Is.EqualTo(9));
            Assert.That(catalog.DistrictUnlocks.Count(district => district.Placeholder),
                Is.EqualTo(3));
        }

        [Test]
        public void StoreExpansion_IsSeparateFromDistrictUnlock()
        {
            ShopExpansionTier levelFive = catalog.ExpansionTiers.Single(tier => tier.Level == 5);
            Assert.That(levelFive.RequiredFunds, Is.EqualTo(1200));
            Assert.That(levelFive.Features.HasFlag(ShopExpansionFeature.PackingTable), Is.True);
            Assert.That(catalog.DistrictUnlocks.Any(district =>
                district.RequiredReputation == 40 && !district.Placeholder), Is.True);
        }

        [Test]
        public void Conditions_RequireEveryEntry()
        {
            List<ShopProgressCondition> conditions = new()
            {
                new(ShopProgressConditionType.Reputation, 10, "평판"),
                new(ShopProgressConditionType.UnitsSold, 5, "판매")
            };
            Assert.That(ShopProgressionRules.AreAllConditionsMet(conditions,
                condition => condition.Type == ShopProgressConditionType.Reputation ? 10 : 4),
                Is.False);
            Assert.That(ShopProgressionRules.AreAllConditionsMet(conditions,
                condition => condition.Target), Is.True);
        }

        [Test]
        public void CollectionMilestones_AreReturnedOnlyWhenNotGranted()
        {
            HashSet<int> granted = new() { 25 };
            List<ShopCollectionMilestone> reached =
                ShopProgressionRules.FindNewCollectionMilestones(
                    76, catalog.CollectionMilestones, granted);
            Assert.That(reached.Select(milestone => milestone.Percent),
                Is.EqualTo(new[] { 50, 75 }));
            foreach (ShopCollectionMilestone milestone in reached)
                granted.Add(milestone.Percent);
            Assert.That(ShopProgressionRules.FindNewCollectionMilestones(
                76, catalog.CollectionMilestones, granted), Is.Empty);
        }

        [Test]
        public void CollectionPercent_UsesRegisteredCount()
        {
            Assert.That(ShopProgressionRules.CollectionPercent(13, 50), Is.EqualTo(26));
            Assert.That(ShopProgressionRules.CollectionPercent(1, 0), Is.EqualTo(0));
            Assert.That(ShopProgressionRules.CollectionPercent(99, 50), Is.EqualTo(100));
        }

        [Test]
        public void MasteryTitles_UseRequiredThresholds()
        {
            Assert.That(ShopProgressionRules.FindMasteryTier(0, catalog.MasteryTiers).Successes,
                Is.EqualTo(0));
            Assert.That(ShopProgressionRules.FindMasteryTier(299, catalog.MasteryTiers).Successes,
                Is.EqualTo(100));
            Assert.That(ShopProgressionRules.FindMasteryTier(1000, catalog.MasteryTiers).Successes,
                Is.EqualTo(1000));
        }

        [Test]
        public void GoalTarget_IsStableAndWithinConfiguredRange()
        {
            ShopGoalDefinition goal = catalog.GoalPool.First();
            int first = ShopProgressionRules.DeterministicGoalTarget(goal, 17);
            int second = ShopProgressionRules.DeterministicGoalTarget(goal, 17);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Is.InRange(goal.MinimumTarget, goal.MaximumTarget));
        }

        [Test]
        public void CurveChange_IsReadFromCatalogWithoutRuleCodeChange()
        {
            ShopProgressionCatalog temporary = ScriptableObject.CreateInstance<ShopProgressionCatalog>();
            temporary.EditorConfigure("test",
                new[]
                {
                    new ShopProgressStage("stage_test", "테스트",
                        new[]
                        {
                            new ShopProgressCondition(ShopProgressConditionType.Reputation,
                                123, "평판")
                        },
                        new ShopProgressReward[0])
                },
                new ShopExpansionTier[0], new ShopDistrictUnlock[0],
                new ShopCollectionItem[0], new ShopGoalDefinition[0],
                new ShopCollectionMilestone[0], new ShopMasteryTier[0], 1, 1);
            try
            {
                Assert.That(temporary.Stages[0].Conditions[0].Target, Is.EqualTo(123));
                Assert.That(ShopProgressionRules.AreAllConditionsMet(
                    temporary.Stages[0].Conditions, _ => 122), Is.False);
                Assert.That(ShopProgressionRules.AreAllConditionsMet(
                    temporary.Stages[0].Conditions, _ => 123), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(temporary);
            }
        }

        [Test]
        public void SavePayload_RoundTripsAllSharedProgressFields()
        {
            ShopProgressionSaveData source = new()
            {
                currentDay = 8,
                teamFunds = 4321,
                reputation = 44,
                lifetimeRevenue = 12345,
                unitsSold = 67,
                rareItemsAcquired = 8,
                rareItemsSold = 4,
                regularCustomerIds = new List<string> { "regular:a" },
                unlockedDistrictIds = new List<string> { "district:gacha" },
                ownedCollectionItemIds = new List<string> { "item:a" },
                grantedCollectionMilestones = new List<int> { 25 },
                dailyGoals = new List<ShopProgressGoalSave>
                {
                    new() { definitionId = "daily:sales", target = 5, completed = true }
                }
            };
            string json = JsonUtility.ToJson(source);
            ShopProgressionSaveData restored =
                JsonUtility.FromJson<ShopProgressionSaveData>(json);
            Assert.That(restored.version, Is.EqualTo(ShopProgressionSaveStore.CurrentVersion));
            Assert.That(restored.currentDay, Is.EqualTo(8));
            Assert.That(restored.teamFunds, Is.EqualTo(4321));
            Assert.That(restored.reputation, Is.EqualTo(44));
            Assert.That(restored.regularCustomerIds, Is.EquivalentTo(source.regularCustomerIds));
            Assert.That(restored.unlockedDistrictIds, Is.EquivalentTo(source.unlockedDistrictIds));
            Assert.That(restored.ownedCollectionItemIds, Is.EquivalentTo(source.ownedCollectionItemIds));
            Assert.That(restored.grantedCollectionMilestones, Is.EquivalentTo(new[] { 25 }));
            Assert.That(restored.dailyGoals.Single().completed, Is.True);
        }
    }
}
