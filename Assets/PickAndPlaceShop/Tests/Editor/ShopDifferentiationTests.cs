using NUnit.Framework;
using UnityEditor;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopDifferentiationTests
    {
        [Test]
        public void CapsuleRecycler_UsesSharedSlotContainerAndDataThresholds()
        {
            ShopDifferentiationConfig config = ShopDifferentiationConfig.Load();
            Assert.That(config, Is.Not.Null);
            Assert.That(config.EmptyCapsuleProduct, Is.Not.Null);
            Assert.That(config.CapsuleRecyclerSlots, Is.GreaterThan(0));
            Assert.That(config.UpcycleThresholds, Is.EqualTo(new[] { 20, 50, 100 }));

            Assert.That((int)ShopContainerKind.CapsuleRecycler, Is.GreaterThan(
                (int)ShopContainerKind.AutomationBuffer));
        }

        [Test]
        public void SaveVersion_IncludesDifferentiationState()
        {
            Assert.That(ShopProgressionSaveStore.CurrentVersion, Is.GreaterThanOrEqualTo(9));
            ShopProgressionSaveData save = new() { upcycleDecorMask = 5 };
            Assert.That(save.upcycleDecorMask, Is.EqualTo(5));
        }

        [TestCase("Assets/PickAndPlaceShop/Data/Arcade/Kuji_MoonRabbit.asset", 2020)]
        [TestCase("Assets/PickAndPlaceShop/Data/Arcade/Kuji_RetroRobot.asset", 2027)]
        public void KujiLastOne_IsExclusiveAndOnlyAwardedOnFortiethDraw(string path, int lastProductId)
        {
            ShopKujiPoolConfig pool = AssetDatabase.LoadAssetAtPath<ShopKujiPoolConfig>(path);
            Assert.That(pool, Is.Not.Null);
            Assert.That(pool.InitialStock.Total, Is.EqualTo(40));
            Assert.That(pool.LastPrizeDefinition, Is.Not.Null);
            Assert.That(pool.LastPrizeDefinition.ProductId, Is.EqualTo(lastProductId));
            Assert.That(pool.LastPrizeDefinition.ExclusiveReward, Is.True);

            ShopKujiStock stock = pool.InitialStock;
            for (int draw = 1; draw <= 40; draw++)
            {
                ShopKujiRank rank = ShopAcquisitionRules.SelectKujiRank(0, stock);
                Assert.That(stock.TryTake(rank), Is.True);
                Assert.That(ShopAcquisitionRules.ShouldAwardLastPrize(stock.Total, false),
                    Is.EqualTo(draw == 40), "Last One award timing mismatch at draw " + draw);
            }
        }

        [Test]
        public void KujiSave_CapturesSetAndRefillState()
        {
            ShopProgressionSaveData save = new();
            save.kujiStations.Add(new ShopKujiStationSave
            {
                poolId = "kuji_moon_rabbit",
                setNumber = 3,
                stockS = 0,
                stockA = 1,
                stockB = 2,
                stockC = 3,
                stockD = 4,
                refilling = true,
                refillSecondsRemaining = 6f
            });

            Assert.That(save.kujiStations[0].setNumber, Is.EqualTo(3));
            Assert.That(save.kujiStations[0].refilling, Is.True);
            Assert.That(save.kujiStations[0].refillSecondsRemaining, Is.InRange(5f, 8f));
        }
    }
}
