using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PickAndPlaceShop.Editor
{
    public static class ShopIntegratedSystemsInstaller
    {
        public const string ScenePath =
            "Assets/PickAndPlaceShop/Scenes/PickAndPlaceShop_MainStreetSlice_Multiplayer.unity";
        private const string OperationsPath =
            "Assets/PickAndPlaceShop/Resources/Operations/ShopOperationsConfig.asset";

        [MenuItem("Tools/Pick And Place Shop/Apply Integrated Operations")]
        public static void Apply()
        {
            EnsureFolder("Assets/PickAndPlaceShop/Resources/Operations");
            ShopOperationsConfig operations = AssetDatabase.LoadAssetAtPath<ShopOperationsConfig>(OperationsPath);
            if (operations == null)
            {
                operations = ScriptableObject.CreateInstance<ShopOperationsConfig>();
                AssetDatabase.CreateAsset(operations, OperationsPath);
            }
            EditorUtility.SetDirty(operations);

            ConfigureProducts();
            ConfigureCatalog();
            ConfigureMachineConfigs();
            ConfigureMachinePrefabs();
            ConfigureScene(operations);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[IntegratedOperations] Applied operations config, 200-item collection, machines and scene.");
        }

        private static void ConfigureProducts()
        {
            string[] guids = AssetDatabase.FindAssets("t:ShopProductDefinition",
                new[] { "Assets/PickAndPlaceShop" });
            List<ShopProductDefinition> products = guids
                .Select(guid => AssetDatabase.LoadAssetAtPath<ShopProductDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid)))
                .Where(product => product != null)
                .OrderBy(product => product.ProductId)
                .ToList();
            for (int i = 0; i < products.Count; i++)
            {
                ShopProductDefinition product = products[i];
                SerializedObject serialized = new(product);
                serialized.FindProperty("maxStack").intValue = 10;
                serialized.FindProperty("rarity").enumValueIndex = i < 10
                    ? (int)ShopProductRarity.UltraRare
                    : i < 25 ? (int)ShopProductRarity.Rare
                    : i < 40 ? (int)ShopProductRarity.Uncommon
                    : (int)ShopProductRarity.Common;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(product);
            }
        }

        private static void ConfigureCatalog()
        {
            ShopCatThemeCatalogBuilder.Apply();
        }

        private static void ConfigureMachineConfigs()
        {
            string[] guids = AssetDatabase.FindAssets("t:ShopClawMachineConfig",
                new[] { "Assets/PickAndPlaceShop/Data" });
            int index = 0;
            foreach (string guid in guids)
            {
                ShopClawMachineConfig config = AssetDatabase.LoadAssetAtPath<ShopClawMachineConfig>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (config == null) continue;
                bool premium = config.name.Contains("레어") || config.name.Contains("Premium") || index == 0;
                ShopRarityWeights weights = premium
                    ? new ShopRarityWeights { common = 45, uncommon = 30, rare = 20, ultraRare = 5 }
                    : new ShopRarityWeights { common = 110, uncommon = 40, rare = 40, ultraRare = 10 };
                config.EditorConfigureRarity(weights, premium
                    ? ShopMultiPrizePolicy.AwardAll
                    : ShopMultiPrizePolicy.SingleAndReturnExtras);
                EditorUtility.SetDirty(config);
                index++;
            }
        }

        private static void ConfigureMachinePrefabs()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab",
                new[] { "Assets/PickAndPlaceShop/Prefabs/ClawMachines" });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ShopClawMachineNetwork machine = root.GetComponent<ShopClawMachineNetwork>();
                    if (machine == null) continue;
                    if (root.GetComponent<ShopClawAutomationDevice>() == null)
                        root.AddComponent<ShopClawAutomationDevice>();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        private static void ConfigureScene(ShopOperationsConfig operations)
        {
            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ShopNetworkGame game = Object.FindFirstObjectByType<ShopNetworkGame>();
            if (game != null)
            {
                ShopLiveOperationsNetwork live = game.GetComponent<ShopLiveOperationsNetwork>();
                if (live == null) live = game.gameObject.AddComponent<ShopLiveOperationsNetwork>();
                SerializedObject serialized = new(live);
                serialized.FindProperty("config").objectReferenceValue = operations;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(game.gameObject);
            }

            GameObject station = GameObject.Find("OnlineOrderPackingStation");
            if (station == null)
            {
                ShopInteractable register = Object.FindObjectsByType<ShopInteractable>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(item => item.Action == ShopAction.Register);
                station = GameObject.CreatePrimitive(PrimitiveType.Cube);
                station.name = "OnlineOrderPackingStation";
                station.transform.position = register != null
                    ? register.transform.position + register.transform.right * 1.6f
                    : new Vector3(0f, 0.55f, 0f);
                station.transform.localScale = new Vector3(1.25f, 1.05f, 0.75f);
                ShopInteractable interactable = station.AddComponent<ShopInteractable>();
                interactable.Configure(ShopAction.OnlineOrder, "온라인 주문 포장/발송");
                GameObject label = new("PackingStationLabel");
                label.transform.SetParent(station.transform, false);
                label.transform.localPosition = new Vector3(0f, 0.7f, 0f);
                TextMesh text = label.AddComponent<TextMesh>();
                text.text = "온라인 주문 포장대";
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.characterSize = 0.12f;
                text.fontSize = 48;
                label.transform.localScale = Vector3.one;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void EnsureFolder(string path)
        {
            string current = "Assets";
            foreach (string part in path.Split('/').Skip(1))
            {
                string next = current + "/" + part;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part);
                current = next;
            }
        }
    }
}
