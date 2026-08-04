using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Editor
{
    public static class ShopExpansionVisualConfigInstaller
    {
        private const string AssetPath = "Assets/PickAndPlaceShop/Resources/Progression/ShopExpansionVisualConfig.asset";

        [MenuItem("Tools/Pick And Place Shop/Ensure Expansion Visual Config")]
        public static void EnsureConfig()
        {
            ShopExpansionVisualConfig config = AssetDatabase.LoadAssetAtPath<ShopExpansionVisualConfig>(AssetPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ShopExpansionVisualConfig>();
                SerializedObject serialized = new(config);
                SerializedProperty rules = serialized.FindProperty("stageRules");
                rules.arraySize = 2;
                ConfigureRule(rules.GetArrayElementAtIndex(0), 5, "WallNorth");
                ConfigureRule(rules.GetArrayElementAtIndex(1), 6, "WallEast");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(config, AssetPath);
                AssetDatabase.SaveAssets();
            }

            Selection.activeObject = config;
            Debug.Log("[Expansion] Visual config ready: " + AssetPath);
        }

        private static void ConfigureRule(SerializedProperty rule, int level, string inactiveObjectName)
        {
            rule.FindPropertyRelative("minimumLevel").intValue = level;
            rule.FindPropertyRelative("activateObjectNames").arraySize = 0;
            SerializedProperty inactive = rule.FindPropertyRelative("deactivateObjectNames");
            inactive.arraySize = 1;
            inactive.GetArrayElementAtIndex(0).stringValue = inactiveObjectName;
        }
    }
}
