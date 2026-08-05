using UnityEngine;

namespace PickAndPlaceShop
{
    public static class ShopBuildSafeMaterials
    {
        private const string RuntimeLitPath = "ProductMaterials/Generated/RuntimeLitBase";
        private static Material runtimeLit;
        private static readonly MaterialPropertyBlock Properties = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            runtimeLit = null;
            Properties.Clear();
        }

        public static void ApplyLitColor(Renderer renderer, Color color, bool emissive = false)
        {
            if (renderer == null) return;
            if (runtimeLit == null) runtimeLit = Resources.Load<Material>(RuntimeLitPath);
            if (runtimeLit == null)
            {
                Debug.LogError("[BuildSafeMaterials] RuntimeLitBase 리소스를 찾지 못했습니다.", renderer);
                renderer.enabled = false;
                return;
            }

            renderer.sharedMaterial = runtimeLit;
            renderer.GetPropertyBlock(Properties);
            Properties.SetColor("_BaseColor", color);
            Properties.SetColor("_Color", color);
            Properties.SetColor("_EmissionColor", emissive ? color * 1.6f : Color.black);
            renderer.SetPropertyBlock(Properties);
        }
    }
}
