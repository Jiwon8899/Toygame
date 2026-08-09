using NUnit.Framework;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopSideContentTests
    {
        [Test]
        public void ClawDud_CommonOnly_AndConfiguredRateIsPlausible()
        {
            ShopSideContentConfig config = ShopSideContentConfig.Load();
            Assert.That(config, Is.Not.Null);
            System.Random random = new(807);
            int commonDuds = 0;
            for (int i = 0; i < 1000; i++)
                if (ShopSideContentRules.IsClawDud(ShopProductRarity.Common,
                        (float)random.NextDouble(), config)) commonDuds++;
            int uncommonOrBetterDuds = 0;
            for (int i = 0; i < 500; i++)
                if (ShopSideContentRules.IsClawDud(ShopProductRarity.Rare,
                        (float)random.NextDouble(), config)) uncommonOrBetterDuds++;

            Assert.That(commonDuds, Is.InRange(30, 80),
                "5% 설정은 1000회에서 통계적으로 타당한 범위여야 합니다.");
            Assert.That(uncommonOrBetterDuds, Is.Zero,
                "고급 이상 캡슐은 꽝 판정 대상이 아닙니다.");
        }

        [Test]
        public void RivalAlert_IncreasesStrictlyWithRarity()
        {
            ShopSideContentConfig config = ShopSideContentConfig.Load();
            Assert.That(config.RivalAlert(ShopProductRarity.Common),
                Is.LessThan(config.RivalAlert(ShopProductRarity.Uncommon)));
            Assert.That(config.RivalAlert(ShopProductRarity.Uncommon),
                Is.LessThan(config.RivalAlert(ShopProductRarity.Rare)));
            Assert.That(config.RivalAlert(ShopProductRarity.Rare),
                Is.LessThan(config.RivalAlert(ShopProductRarity.UltraRare)));
        }

        [Test]
        public void FreeMoneyRoutes_AreCappedAndPositive()
        {
            ShopSideContentConfig config = ShopSideContentConfig.Load();
            Assert.That(config.TrashReward, Is.GreaterThan(0));
            Assert.That(config.TrashDailyCap, Is.GreaterThanOrEqualTo(config.TrashReward));
            Assert.That(config.TrashDailyCap % config.TrashReward, Is.Zero);
            Assert.That(config.RobberDailyMaximum, Is.InRange(0, 3));
        }

        [Test]
        public void DiscountChoices_ApplyDistinctSmallPriceChanges()
        {
            ShopSideContentConfig config = ShopSideContentConfig.Load();
            const int basePrice = 1000;
            int accepted = ShopSideContentRules.ApplySaleMultiplier(basePrice, 1f - config.FullDiscount);
            int partial = ShopSideContentRules.ApplySaleMultiplier(basePrice, 1f - config.PartialDiscount);
            int refused = ShopSideContentRules.ApplySaleMultiplier(basePrice, 1f);

            Assert.That(accepted, Is.LessThan(partial));
            Assert.That(partial, Is.LessThan(refused));
            Assert.That(refused, Is.EqualTo(basePrice));
        }
    }
}
