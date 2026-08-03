#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Editor
{
    public static class ShopWorkforceInstaller
    {
        private const string AssetPath = "Assets/PickAndPlaceShop/Resources/Operations/ShopWorkforceConfig.asset";

        [MenuItem("Tools/Pick And Place Shop/Install Workforce Config")]
        public static void Install()
        {
            EnsureFolder("Assets/PickAndPlaceShop/Resources/Operations");
            ShopWorkforceConfig config = AssetDatabase.LoadAssetAtPath<ShopWorkforceConfig>(AssetPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ShopWorkforceConfig>();
                AssetDatabase.CreateAsset(config, AssetPath);
            }

            SerializedObject serialized = new(config);
            SerializedProperty appearances = serialized.FindProperty("appearancePrefabs");
            appearances.arraySize = 6;
            for (int i = 0; i < 6; i++)
                appearances.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/PickAndPlaceShop/GeneratedCharacters/CustomerPerson" + (i + 1) + ".prefab");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Workforce] 설정과 Person 1~6 외형 풀을 설치했습니다.");
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }
    }
}
#endif
