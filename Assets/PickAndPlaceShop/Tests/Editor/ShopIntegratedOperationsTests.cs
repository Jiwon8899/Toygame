using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopIntegratedOperationsTests
    {
        private ShopOperationsConfig operations;
        private ShopProgressionCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            operations = Resources.Load<ShopOperationsConfig>(ShopOperationsConfig.ResourcePath);
            catalog = Resources.Load<ShopProgressionCatalog>("Progression/ShopProgressionCatalog");
            Assert.NotNull(operations);
            Assert.NotNull(catalog);
        }

        [Test]
        public void Day_IsExactlyEightMinutesAndScaledGoalsExist()
        {
            Assert.AreEqual(480f, operations.PreparationSeconds + operations.OpeningSeconds +
                operations.ClosingSeconds);
            Assert.AreEqual(120f, operations.PreparationSeconds);
            Assert.AreEqual(300f, operations.OpeningSeconds);
            Assert.AreEqual(60f, operations.ClosingSeconds);
            Assert.Less(operations.SalesGoalForStage(0), operations.SalesGoalForStage(5));
        }

        [Test]
        public void CustomerAndAutomationBalance_MatchesConfirmedRules()
        {
            Assert.AreEqual(1, operations.SuccessfulSaleReputationReward);
            Assert.AreEqual(1, operations.NoPurchaseReputationPenalty);
            Assert.AreEqual(0.15f, operations.TrendPriceBonus, 0.0001f);
            Assert.AreEqual(60f, operations.AutomationAttemptInterval);
            Assert.AreEqual(0.5f, operations.AutomationSuccessMultiplier, 0.0001f);
            Assert.Less(operations.AutomaticSuccessRate, operations.ManualAverageSuccessRate);
            Assert.AreEqual(10, operations.AutomationBufferSlots);
            Assert.IsFalse(operations.AutomatedSuccessCountsForDailyGoal);
        }

        [Test]
        public void AutomationRarity_NeverSelectsUltraRare()
        {
            System.Random random = new(13579);
            for (int i = 0; i < 10000; i++)
                Assert.AreNotEqual(ShopProductRarity.UltraRare,
                    operations.StandardRarityWeights.Pick(random, false));
        }

        [Test]
        public void PrizeMachines_AreAvailableDuringPreparationAndOpening()
        {
            Assert.IsTrue(ShopClawRules.CanOperateDuring(ShopPhase.PrizeHunt));
            Assert.IsTrue(ShopClawRules.CanOperateDuring(ShopPhase.Setup));
            Assert.IsTrue(ShopClawRules.CanOperateDuring(ShopPhase.Open));
            Assert.IsFalse(ShopClawRules.CanOperateDuring(ShopPhase.Summary));
            Assert.IsFalse(ShopClawRules.CanOperateDuring(ShopPhase.Complete));
        }

        [Test]
        public void Collection_HasExactCategoryAndRarityDistribution()
        {
            Assert.AreEqual(200, catalog.CollectionItems.Count);
            Assert.AreEqual(50, catalog.CollectionItems.Count(item => item.CategoryId == "cat_plush"));
            Assert.AreEqual(40, catalog.CollectionItems.Count(item => item.CategoryId == "cat_figure"));
            Assert.AreEqual(40, catalog.CollectionItems.Count(item => item.CategoryId == "cat_goods"));
            Assert.AreEqual(30, catalog.CollectionItems.Count(item => item.CategoryId == "cat_seasonal"));
            Assert.AreEqual(40, catalog.CollectionItems.Count(item => item.CategoryId == "cat_retro"));
            Assert.AreEqual(110, catalog.CollectionItems.Count(item => item.Rarity == ShopProgressRarity.Common));
            Assert.AreEqual(40, catalog.CollectionItems.Count(item => item.Rarity == ShopProgressRarity.Uncommon));
            Assert.AreEqual(40, catalog.CollectionItems.Count(item => item.Rarity == ShopProgressRarity.Rare));
            Assert.AreEqual(10, catalog.CollectionItems.Count(item => item.Rarity == ShopProgressRarity.Premium));
        }

        [Test]
        public void ProductsStackToTen_AndAllRaritiesAreRepresented()
        {
            string[] guids = AssetDatabase.FindAssets("t:ShopProductDefinition",
                new[] { "Assets/PickAndPlaceShop" });
            ShopProductDefinition[] products = guids.Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopProductDefinition>)
                .Where(product => product != null).ToArray();
            Assert.IsNotEmpty(products);
            Assert.IsTrue(products.All(product => product.MaxStack == 10));
            foreach (ShopProductRarity rarity in System.Enum.GetValues(typeof(ShopProductRarity)))
                Assert.IsTrue(products.Any(product => product.Rarity == rarity), rarity.ToString());
        }

        [Test]
        public void ClawPrefabs_HaveVisibleAutomationDeviceComponent()
        {
            string[] paths = AssetDatabase.FindAssets("t:Prefab",
                    new[] { "Assets/PickAndPlaceShop/Prefabs/ClawMachines" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => System.IO.Path.GetFileNameWithoutExtension(path)
                    .StartsWith("ClawMachine_", System.StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(5, paths.Length);
            foreach (string path in paths)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try { Assert.NotNull(root.GetComponent<ShopClawAutomationDevice>(), path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        [Test]
        public void SavePayload_RoundTripsCurrentLiveOperationsVersion()
        {
            ShopProgressionSaveData source = new()
            {
                livePhase = (int)ShopPhase.Open,
                livePhaseSecondsRemaining = 155f,
                trendCategory = (int)ShopProductCategory.CatRetro,
                previousTrendCategory = (int)ShopProductCategory.CatGoods,
                trendNews = "복고풍 고양이 굿즈가 화제예요.",
                dailySalesGoal = 12,
                dailySalesProgress = 5
            };
            source.automationMachines.Add(new ShopAutomationMachineSave
            {
                machineId = 101, installed = true, enabled = true, elapsedSeconds = 24f
            });
            string json = JsonUtility.ToJson(source);
            ShopProgressionSaveData restored = JsonUtility.FromJson<ShopProgressionSaveData>(json);
            Assert.AreEqual(ShopProgressionSaveStore.CurrentVersion, restored.version);
            Assert.AreEqual(155f, restored.livePhaseSecondsRemaining);
            Assert.AreEqual("복고풍 고양이 굿즈가 화제예요.", restored.trendNews);
            Assert.IsTrue(restored.automationMachines.Single().installed);
        }

        [Test]
        public void NarrativeConfiguration_IsBoundedAndHasCompleteFallbackCoverage()
        {
            Assert.AreEqual("claude-haiku-4-5-20251001", operations.NarrativeModel);
            Assert.LessOrEqual(operations.NarrativeMaxTokens, 100);
            Assert.AreEqual(3f, operations.NarrativeTimeoutSeconds);
            Assert.AreEqual("ANTHROPIC_API_KEY", operations.NarrativeApiKeyEnvironmentVariable);
            foreach (ShopCustomerDialogueEvent eventType in
                     System.Enum.GetValues(typeof(ShopCustomerDialogueEvent)))
            {
                System.Collections.Generic.HashSet<string> fallbacks = new();
                for (int i = 0; i < 5; i++) fallbacks.Add(operations.CustomerDialogueFallback(eventType, i));
                Assert.AreEqual(5, fallbacks.Count, eventType.ToString());
            }
            foreach (ShopProductCategory category in new[]
                     {
                         ShopProductCategory.CatPlush, ShopProductCategory.CatFigure,
                         ShopProductCategory.CatGoods, ShopProductCategory.CatSeasonal,
                         ShopProductCategory.CatRetro
                     })
            {
                System.Collections.Generic.HashSet<string> news = new();
                for (int day = 0; day < 3; day++) news.Add(operations.TrendNewsFallback(category, day));
                Assert.AreEqual(3, news.Count, category.ToString());
            }
        }

        [Test]
        public void FinalGameName_UsesConfirmedSpacingAndHangul()
        {
            Assert.AreEqual("미야옹 츄르 부자가 될거야 : 소품샵 뽑기 시뮬레이터",
                ShopGameIdentity.KoreanFormalName);
            StringAssert.Contains("츄", ShopGameIdentity.KoreanFormalName);
            StringAssert.DoesNotContain("될 거야", ShopGameIdentity.KoreanFormalName);
            Assert.AreEqual(1, ShopGameIdentity.KoreanFormalName.Split(':').Length - 1);
            StringAssert.Contains(" : ", ShopGameIdentity.KoreanFormalName);
        }
    }
}
