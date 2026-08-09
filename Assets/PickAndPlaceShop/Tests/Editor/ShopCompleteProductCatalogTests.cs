using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopCompleteProductCatalogTests
    {
        [Test]
        public void EveryRegisteredProduct_HasAUsableIconAndUniqueId()
        {
            ShopProductDefinition[] products = AssetDatabase.FindAssets("t:ShopProductDefinition",
                    new[] { "Assets/PickAndPlaceShop/Resources/Products" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopProductDefinition>)
                .Where(product => product != null)
                .ToArray();

            Assert.GreaterOrEqual(products.Length, 200);
            Assert.AreEqual(products.Length,
                products.Select(product => product.ProductId).Distinct().Count(),
                "Product IDs must be unique across every acquisition source.");
            CollectionAssert.IsEmpty(products.Where(product => product.Icon == null)
                .Select(product => product.ProductId + "|" + product.DisplayName).ToArray(),
                "A product without an icon becomes an empty hotbar/inventory slot.");
            CollectionAssert.IsEmpty(products.Where(product => product.Icon != null &&
                                                               product.Icon.texture == null)
                .Select(product => product.ProductId + "|" + product.DisplayName).ToArray(),
                "Every UI icon reference must resolve to an actual texture.");
            CollectionAssert.IsEmpty(products.Where(product => UnityEngine.Resources.Load<UnityEngine.Sprite>(
                    $"ProductIcons/Generated/ProductIcon_{product.ProductId:D4}") == null)
                .Select(product => product.ProductId + "|" + product.DisplayName).ToArray(),
                "Build-safe UI icons must be directly loadable from Resources.");
            CollectionAssert.IsEmpty(products
                .Where(product => product.ProductId != 9001 && product.VisualPrefab == null &&
                                  product.PrizePrefab == null)
                .Select(product => product.ProductId + "|" + product.DisplayName).ToArray(),
                "Sellable products must have either a display or prize model.");
        }
    }
}
