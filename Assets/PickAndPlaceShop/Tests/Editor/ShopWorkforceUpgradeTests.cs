using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopWorkforceUpgradeTests
    {
        [Test]
        public void WorkforceConfig_UsesRequestedCostsWagesAndVisiblePool()
        {
            ShopWorkforceConfig config = Resources.Load<ShopWorkforceConfig>(ShopWorkforceConfig.ResourcePath);
            Assert.NotNull(config);
            Assert.AreEqual(600, config.HireCost(ShopStaffRole.Cashier));
            Assert.AreEqual(800, config.HireCost(ShopStaffRole.Stocker));
            Assert.AreEqual(1000, config.HireCost(ShopStaffRole.Collector));
            Assert.AreEqual(80, config.DailyWage(ShopStaffRole.Cashier));
            Assert.AreEqual(100, config.DailyWage(ShopStaffRole.Stocker));
            Assert.AreEqual(120, config.DailyWage(ShopStaffRole.Collector));
            Assert.AreEqual(1.5f, config.CashierDurationMultiplier, 0.001f);
            Assert.AreEqual(6, config.AppearancePrefabs.Length);
            foreach (GameObject appearance in config.AppearancePrefabs) Assert.NotNull(appearance);
        }

        [Test]
        public void ExpansionCatalog_UsesRequestedPriceSequence()
        {
            ShopProgressionCatalog catalog = AssetDatabase.LoadAssetAtPath<ShopProgressionCatalog>(
                "Assets/PickAndPlaceShop/Resources/Progression/ShopProgressionCatalog.asset");
            Assert.NotNull(catalog);
            int[] expected = { 0, 400, 800, 1500, 1200, 2500 };
            Assert.AreEqual(expected.Length, catalog.ExpansionTiers.Count);
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], catalog.ExpansionTiers[i].RequiredFunds);
        }

        [Test]
        public void SaveVersion_IncludesUpgradeAndStaffState()
        {
            ShopProgressionSaveData data = new()
            {
                playerUpgradeLevel = 2,
                operationsUpgradeLevel = 3,
                staffHiredMask = 7,
                staffAttendanceMask = 5
            };
            string json = JsonUtility.ToJson(data);
            ShopProgressionSaveData restored = JsonUtility.FromJson<ShopProgressionSaveData>(json);
            Assert.AreEqual(ShopProgressionSaveStore.CurrentVersion, restored.version);
            Assert.AreEqual(2, restored.playerUpgradeLevel);
            Assert.AreEqual(3, restored.operationsUpgradeLevel);
            Assert.AreEqual(7, restored.staffHiredMask);
            Assert.AreEqual(5, restored.staffAttendanceMask);
        }
    }
}
