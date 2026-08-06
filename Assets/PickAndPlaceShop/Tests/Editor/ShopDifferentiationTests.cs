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
        public void ReviewStars_UseSalesAndDailyGoalOnly()
        {
            ShopDifferentiationConfig config = ShopDifferentiationConfig.Load();
            Assert.That(config, Is.Not.Null);
            Assert.That(config.ReviewStars(0, 10), Is.EqualTo(1));
            Assert.That(config.ReviewStars(4, 10), Is.EqualTo(2));
            Assert.That(config.ReviewStars(7, 10), Is.EqualTo(3));
            Assert.That(config.ReviewStars(10, 10), Is.EqualTo(4));
            Assert.That(config.ReviewStars(15, 10), Is.EqualTo(5));
        }

        [Test]
        public void ReviewRating_IsSystemOwnedAndHistoryIsSaveable()
        {
            Assert.That(ShopProgressionSaveStore.CurrentVersion, Is.GreaterThanOrEqualTo(10));
            ShopProgressionSaveData save = new()
            {
                latestReviewDay = 10,
                reviewHistory = "[10일차 ★★★★★ | 판매 15/10 · 유행 고양이 굿즈] 목표를 넘겼어요."
            };
            Assert.That(save.latestReviewDay, Is.EqualTo(10));
            Assert.That(save.reviewHistory, Does.Contain("판매 15/10"));
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
        public void FixedDisplay_UsesSavedHotbarAndAnchorSlots()
        {
            ShopProgressionSaveData save = new() { hotbarProduct0 = 2001, selectedHotbarSlot = 0 };
            Assert.That(save.hotbarProduct0, Is.EqualTo(2001));
            Assert.That(save.selectedHotbarSlot, Is.EqualTo(0));
            string display = System.IO.File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopProductDisplayVisualController.cs");
            StringAssert.Contains("ShopDisplayShelfAnchors", display);
            StringAssert.DoesNotContain("ghostPosition", display);
            string visuals = System.IO.File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopProductVisuals.cs");
            StringAssert.Contains("CreateFallbackVisual", visuals,
                "A product without an imported model must remain visible in the hand and fixed display.");
            Assert.That(ShopProgressionSaveStore.CurrentVersion, Is.GreaterThanOrEqualTo(16));
        }

        [Test]
        public void ReviewBoard_IsAuthoredInMainStreetSceneAndRuntimeOnlyBindsIt()
        {
            string scene = System.IO.File.ReadAllText(
                "Assets/PickAndPlaceShop/Scenes/PickAndPlaceShop_MainStreetSlice_Multiplayer.unity");
            StringAssert.Contains(@"\uC190\uB2D8 \uB9AC\uBDF0 \uAC8C\uC2DC\uD310", scene);
            StringAssert.Contains("m_Name: Review Surface", scene);
            string controller = System.IO.File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopDifferentiationController.cs");
            StringAssert.Contains("GameObject.Find(\"손님 리뷰 게시판\")", controller);
            StringAssert.Contains("interactable.Configure(ShopAction.ReviewBoard", controller);
        }
    }
}
