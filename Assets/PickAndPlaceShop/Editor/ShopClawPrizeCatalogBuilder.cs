#if UNITY_EDITOR
using UnityEditor;

namespace PickAndPlaceShop.Editor
{
    // 이전 메뉴를 누르더라도 저폴리 잡화 풀이 다시 살아나지 않도록 통합 빌더로 위임한다.
    public static class ShopClawPrizeCatalogBuilder
    {
        [MenuItem("Pick And Place Shop/Rebuild Claw Prize Catalog")]
        public static void RebuildPrizeCatalog() => ShopCatThemeCatalogBuilder.Apply();
    }
}
#endif
