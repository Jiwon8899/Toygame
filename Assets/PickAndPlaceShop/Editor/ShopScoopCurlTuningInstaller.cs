using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Editor
{
    public static class ShopScoopCurlTuningInstaller
    {
        private const string OuterMaterialPath =
            "Assets/PickAndPlaceShop/Physics/ScoopOuterGlide.physicMaterial";

        [MenuItem("Tools/Pick And Place Shop/Apply Scoop Curl Tuning")]
        public static void ApplyTuning()
        {
            PhysicsMaterial outerMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(
                OuterMaterialPath);
            string[] guids = AssetDatabase.FindAssets("t:ShopClawMachineConfig");
            int updated = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ShopClawMachineConfig config =
                    AssetDatabase.LoadAssetAtPath<ShopClawMachineConfig>(path);
                if (config == null) continue;
                Undo.RecordObject(config, "Apply scoop curl tuning");
                config.EditorConfigureScoopCurl(
                    0.55f,
                    28f,
                    -10f,
                    34f,
                    90f,
                    3f,
                    1.4f,
                    outerMaterial);
                float guardDepth = 0.8f;
                config.EditorConfigureSpawnGuard(
                    true,
                    config.ZBounds.y + guardDepth * 0.25f,
                    Mathf.Min(2.2f, config.XBounds.y - config.XBounds.x - 0.1f),
                    guardDepth,
                    0.9f,
                    0.24f,
                    6f,
                    0.08f);
                EditorUtility.SetDirty(config);
                updated++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ScoopPhysics] TUNING_APPLIED configs=" + updated +
                      " angularSpeed=34 acceleration=90 dig=28 carry=-10 depenetration=1.4");
        }
    }
}
