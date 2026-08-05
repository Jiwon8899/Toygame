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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RepairAfterSceneLoad()
        {
            if (!Application.isEditor) RepairInvalidShaders();
        }

        public static int RepairInvalidShaders()
        {
            if (runtimeLit == null) runtimeLit = Resources.Load<Material>(RuntimeLitPath);
            if (runtimeLit == null) return 0;
            int repaired = 0;
            Renderer[] sceneRenderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < sceneRenderers.Length; i++) repaired += RepairRenderer(sceneRenderers[i]);
            ShopProductDefinition[] products = Resources.LoadAll<ShopProductDefinition>("Products/CatCatalog");
            for (int i = 0; i < products.Length; i++)
            {
                if (products[i] == null || products[i].VisualPrefab == null) continue;
                Renderer[] productRenderers = products[i].VisualPrefab.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < productRenderers.Length; r++) repaired += RepairRenderer(productRenderers[r]);
            }
            if (repaired > 0) Debug.Log("[BuildSafeMaterials] 잘못된 셰이더 " + repaired + "개를 복구했습니다.");
            return repaired;
        }

        private static int RepairRenderer(Renderer renderer)
        {
            if (renderer == null) return 0;
            Material[] materials = renderer.sharedMaterials;
            int repaired = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                string shaderName = material != null && material.shader != null ? material.shader.name : string.Empty;
                if (!string.IsNullOrEmpty(shaderName) &&
                    shaderName.IndexOf("InternalError", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                Color color = Color.gray;
                if (material != null)
                {
                    if (material.HasProperty("_BaseColor")) color = material.GetColor("_BaseColor");
                    else if (material.HasProperty("_Color")) color = material.color;
                }
                materials[i] = runtimeLit;
                renderer.sharedMaterials = materials;
                Properties.Clear();
                renderer.GetPropertyBlock(Properties, i);
                Properties.SetColor("_BaseColor", color);
                Properties.SetColor("_Color", color);
                renderer.SetPropertyBlock(Properties, i);
                repaired++;
            }
            return repaired;
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
