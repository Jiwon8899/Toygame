#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Editor
{
    public static class ShopWorldSafetyInstaller
    {
        private const string SpawnPadPath = "Assets/Shooter/Art/Environment/SpawnPad/Pfb_SpawnPad.prefab";
        private const string ConfigPath = "Assets/PickAndPlaceShop/Resources/World/ShopWorldConfig.asset";

        [MenuItem("Tools/Pick And Place Shop/Install World Safety")]
        public static void Install()
        {
            EnsureFolder("Assets/PickAndPlaceShop/Resources/World");
            if (AssetDatabase.LoadAssetAtPath<ShopWorldConfig>(ConfigPath) == null)
            {
                ShopWorldConfig config = ScriptableObject.CreateInstance<ShopWorldConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            GameObject root = PrefabUtility.LoadPrefabContents(SpawnPadPath);
            try
            {
                if (root.GetComponent<ShopSpawnPadMarker>() == null)
                    root.AddComponent<ShopSpawnPadMarker>();
                PrefabUtility.SaveAsPrefabAsset(root, SpawnPadPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab ClawMachine_",
                         new[] { "Assets/PickAndPlaceShop/Prefabs/ClawMachines" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                try
                {
                    foreach (Transform item in prefab.GetComponentsInChildren<Transform>(true))
                    {
                        if (item.name != "HorizontalRailX" && item.name != "HorizontalRailZ") continue;
                        foreach (Collider collider in item.GetComponents<Collider>())
                        {
                            Object.DestroyImmediate(collider);
                            changed = true;
                        }
                    }
                    if (changed) PrefabUtility.SaveAsPrefabAsset(prefab, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefab);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[WorldSafety] Spawn pads hidden at runtime, safety data installed, rail visuals collider-free.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
