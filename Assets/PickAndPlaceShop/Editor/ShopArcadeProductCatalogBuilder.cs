#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;

namespace PickAndPlaceShop.Editor
{
    // 기존 메뉴/테스트 진입점은 유지하되 실제 데이터 원본은 고양이 통합 카탈로그 하나만 사용한다.
    public static class ShopArcadeProductCatalogBuilder
    {
        [MenuItem("Tools/Pick And Place Shop/Build Arcade Product Catalog")]
        public static void Build() => ShopCatThemeCatalogBuilder.Apply();

        [MenuItem("Tools/Pick And Place Shop/Validate Product Display Data")]
        public static void Validate()
        {
            ShopProductDefinition[] products = AssetDatabase.FindAssets("t:ShopProductDefinition",
                    new[] { "Assets/PickAndPlaceShop/Resources/Products/CatCatalog" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopProductDefinition>)
                .Where(product => product != null).ToArray();
            if (products.Length != 200 || products.Any(product =>
                    string.IsNullOrWhiteSpace(product.DisplayName) ||
                    !ShopProductLocalization.IsCatTheme(product.Category)))
                throw new InvalidOperationException("고양이 상품 표시 데이터 검증 실패");
            UnityEngine.Debug.Log("[ProductCatalog] VALID catProducts=200");
        }
    }
}
#endif
