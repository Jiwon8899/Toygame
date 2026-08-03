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
            Assert.GreaterOrEqual(operations.PersistentCustomerCount, 30);
            Assert.AreEqual(3, operations.RegularPurchaseThreshold);
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
            Assert.AreEqual(50, catalog.CollectionItems.Count(item => item.CategoryId == "animal"));
            Assert.AreEqual(40, catalog.CollectionItems.Count(item => item.CategoryId == "space"));
            Assert.AreEqual(40, catalog.CollectionItems.Count(item => item.CategoryId == "retro"));
            Assert.AreEqual(30, catalog.CollectionItems.Count(item => item.CategoryId == "seasonal"));
            Assert.AreEqual(40, catalog.CollectionItems.Count(item => item.CategoryId == "other"));
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
        public void SavePayload_RoundTripsLiveOperationsVersionFour()
        {
            ShopProgressionSaveData source = new()
            {
                livePhase = (int)ShopPhase.Open,
                livePhaseSecondsRemaining = 155f,
                trendCategory = (int)ShopProductCategory.Retro,
                dailySalesGoal = 12,
                dailySalesProgress = 5
            };
            source.customerProfiles.Add(new ShopCustomerProfileSave
            {
                customerId = "customer:007", preferredCategory = (int)ShopProductCategory.Animal,
                purchaseCount = 3, regular = true, lastSatisfaction = 88
            });
            source.automationMachines.Add(new ShopAutomationMachineSave
            {
                machineId = 101, installed = true, enabled = true, elapsedSeconds = 24f
            });
            string json = JsonUtility.ToJson(source);
            ShopProgressionSaveData restored = JsonUtility.FromJson<ShopProgressionSaveData>(json);
            Assert.AreEqual(4, restored.version);
            Assert.AreEqual(155f, restored.livePhaseSecondsRemaining);
            Assert.IsTrue(restored.customerProfiles.Single().regular);
            Assert.IsTrue(restored.automationMachines.Single().installed);
        }
    }
}
