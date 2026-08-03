using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopArcadeProductCatalogTests
    {
        [Test]
        public void EveryProductHasUniqueIdAndLocalizedDisplayData()
        {
            ShopProductDefinition[] products = AssetDatabase.FindAssets("t:ShopProductDefinition",
                    new[] { "Assets/PickAndPlaceShop" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopProductDefinition>)
                .Where(item => item != null).ToArray();
            Assert.IsTrue(products.All(item => !string.IsNullOrWhiteSpace(item.DisplayName)));
            Assert.AreEqual(products.Length, products.Select(item => item.ProductId).Distinct().Count());
            Assert.IsTrue(products.All(item =>
                !string.IsNullOrWhiteSpace(ShopProductLocalization.CategoryLabel(item.Category)) &&
                !string.IsNullOrWhiteSpace(ShopProductLocalization.RarityLabel(item.Rarity))));
            Assert.IsFalse(products.Any(item => item.DisplayName == "backpack"));
        }

        [Test]
        public void GachaPoolsUseDirectProductReferencesWithoutBackpackFallback()
        {
            ShopGachaMachineConfig[] configs = LoadAll<ShopGachaMachineConfig>();
            Assert.AreEqual(4, configs.Length);
            foreach (ShopGachaMachineConfig config in configs)
            foreach (ShopGachaRarity rarity in Enum.GetValues(typeof(ShopGachaRarity)))
            {
                ShopProductDefinition product = config.ProductDefinitionFor(rarity, 0);
                Assert.NotNull(product, config.name + " / " + rarity);
                Assert.AreEqual(product.DisplayName, config.ProductFor(rarity, 0));
                Assert.AreNotEqual("배낭", product.DisplayName);
            }
        }

        [Test]
        public void KujiPoolsUseDirectProductReferencesForAllRewards()
        {
            ShopKujiPoolConfig[] configs = LoadAll<ShopKujiPoolConfig>();
            Assert.AreEqual(2, configs.Length);
            foreach (ShopKujiPoolConfig config in configs)
            {
                foreach (ShopKujiRank rank in Enum.GetValues(typeof(ShopKujiRank)))
                {
                    ShopProductDefinition product = config.PrizeDefinitionFor(rank);
                    Assert.NotNull(product, config.name + " / " + rank);
                    Assert.AreEqual(product.DisplayName, config.PrizeFor(rank));
                }
                Assert.NotNull(config.LastPrizeDefinition, config.name);
                Assert.NotNull(config.CeilingPrizeDefinition, config.name);
            }
        }

        private static T[] LoadAll<T>() where T : UnityEngine.Object =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name,
                    new[] { "Assets/PickAndPlaceShop/Data/Arcade" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(item => item != null).ToArray();
    }
}
