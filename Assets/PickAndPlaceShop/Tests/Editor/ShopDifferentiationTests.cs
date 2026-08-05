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

        [Test]
        public void StampCard_UsesSavedPurchasesAndDataVipThreshold()
        {
            ShopDifferentiationConfig config = ShopDifferentiationConfig.Load();
            Assert.That(config, Is.Not.Null);
            Assert.That(config.VipPurchaseThreshold, Is.EqualTo(6));
            ShopCustomerProfileSave profile = new() { customerId = "cat_01", purchaseCount = 6 };
            Assert.That(profile.purchaseCount, Is.GreaterThanOrEqualTo(config.VipPurchaseThreshold));
        }

        [Test]
        public void ReviewRating_IsSystemOwnedAndHistoryIsSaveable()
        {
            Assert.That(ShopProgressionSaveStore.CurrentVersion, Is.GreaterThanOrEqualTo(10));
            ShopProgressionSaveData save = new()
            {
                latestReviewDay = 10,
                reviewHistory = "[10일차 ★★★★★ | 대기 2.0초 · 진열 5종 · 만족 94] 만족스러웠어요."
            };
            Assert.That(save.latestReviewDay, Is.EqualTo(10));
            Assert.That(save.reviewHistory, Does.Contain("대기 2.0초"));
            Assert.That(save.reviewHistory, Does.Contain("★★★★★"));
        }

        [Test]
        public void AppraisedItem_IsUniqueNonStackingAndUsesDataPriceMultiplier()
        {
            ShopProductDefinition product = AssetDatabase.LoadAssetAtPath<ShopProductDefinition>(
                "Assets/PickAndPlaceShop/Resources/Products/Arcade/Product_kuji_moon_rabbit_a.asset");
            Assert.That(product, Is.Not.Null);
            ShopContainerItem normal = new(1, ShopContainerKind.PersonalInventory, 0, product, 0);
            ShopContainerItem another = new(1, ShopContainerKind.PersonalInventory, 1, product, 0);
            Assert.That(ShopContainerRules.CanStack(normal, another), Is.True);

            ShopDifferentiationConfig config = ShopDifferentiationConfig.Load();
            ShopAppraisalGrade grade = config.AppraisalGradeFor(product.Rarity, 0.99f);
            ShopContainerItem appraised = normal;
            appraised.AppraisalGrade = grade;
            appraised.InstanceId = 42;
            appraised.MaxStack = 1;
            Assert.That(appraised.IsAppraised, Is.True);
            Assert.That(ShopContainerRules.CanStack(appraised, normal), Is.False);
            Assert.That(config.AppraisalPriceMultiplier(ShopAppraisalGrade.S), Is.EqualTo(1.4f));
            Assert.That(ShopProgressionSaveStore.CurrentVersion, Is.GreaterThanOrEqualTo(11));
        }

        [Test]
        public void Consignment_UsesOnlyTwoHundredItemCatalogAndPremiumPrice()
        {
            ShopProductDefinition[] catalog = UnityEngine.Resources.LoadAll<ShopProductDefinition>(
                "Products/CatCatalog");
            ShopDifferentiationConfig config = ShopDifferentiationConfig.Load();
            Assert.That(catalog.Length, Is.EqualTo(200));
            Assert.That(config.ConsignmentUnlockReputation, Is.EqualTo(30));
            Assert.That(config.ConsignmentSlots, Is.EqualTo(3));
            Assert.That(config.ConsignmentPrice(ShopProductRarity.Common), Is.GreaterThan(100));
            Assert.That((int)ShopAcquisitionSource.Consignment, Is.GreaterThan(
                (int)ShopAcquisitionSource.Automation));
            Assert.That(ShopProgressionSaveStore.CurrentVersion, Is.GreaterThanOrEqualTo(12));
        }

        [Test]
        public void Curation_UsesDataDrivenScoresAndPersistsExactPlacement()
        {
            ShopDifferentiationConfig config = ShopDifferentiationConfig.Load();
            Assert.That(config, Is.Not.Null);
            Assert.That(config.MaximumCurationPlacements, Is.EqualTo(30));
            Assert.That(config.AutomaticLayoutScore, Is.EqualTo(45));
            Assert.That(config.CurationScoreWeights.x + config.CurationScoreWeights.y +
                        config.CurationScoreWeights.z + config.CurationScoreWeights.w,
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.CurationGrade(44), Is.EqualTo("C"));
            Assert.That(config.CurationGrade(45), Is.EqualTo("B"));
            Assert.That(config.CurationGrade(65), Is.EqualTo("A"));
            Assert.That(config.CurationGrade(85), Is.EqualTo("S"));

            ShopProgressionSaveData save = new();
            save.curationPlacements.Add(new ShopCurationPlacementSave
            {
                placementId = 7,
                productId = 2001,
                position = new UnityEngine.Vector3(5.3f, 1.59f, 3.12f),
                size = new UnityEngine.Vector3(0.3f, 0.4f, 0.3f),
                yaw = 35f,
                rarity = (int)ShopProductRarity.Rare,
                appraisalGrade = (int)ShopAppraisalGrade.A,
                instanceId = 99,
                automatic = false
            });
            Assert.That(save.curationPlacements[0].position.x, Is.EqualTo(5.3f));
            Assert.That(save.curationPlacements[0].yaw, Is.EqualTo(35f));
            Assert.That(save.curationPlacements[0].automatic, Is.False);
            Assert.That(ShopProgressionSaveStore.CurrentVersion, Is.GreaterThanOrEqualTo(13));
        }
    }
}
