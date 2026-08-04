using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopCatThemeCatalogTests
    {
        private const string ProductFolder =
            "Assets/PickAndPlaceShop/Resources/Products/CatCatalog";

        [Test]
        public void Products_UseExactStableIdsAndGeneratedVisuals()
        {
            ShopProductDefinition[] products = LoadCatProducts();
            Assert.AreEqual(200, products.Length);
            Assert.AreEqual(200, products.Select(product => product.StableItemId).Distinct().Count());
            Assert.IsTrue(products.All(product => !product.PlaceholderArtwork));
            Assert.IsTrue(products.All(product => product.VisualPrefab != null));
            Assert.IsTrue(products.All(product => product.Icon != null));
            Assert.IsTrue(products.All(product => product.MaxStack == 10));
            Assert.IsTrue(products.All(product => product.StableItemId.StartsWith("cat_") &&
                                                  product.StableItemId.Length >= 13));
        }

        [Test]
        public void GeneratedVisuals_AreNormalizedAndPhysicsFree()
        {
            GameObject[] wrappers = AssetDatabase.FindAssets("t:Prefab",
                    new[] { "Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(item => item != null)
                .ToArray();
            Assert.AreEqual(80, wrappers.Length);
            Assert.IsTrue(wrappers.All(item =>
                item.GetComponentsInChildren<Collider>(true).Length == 0));
            Assert.IsTrue(wrappers.All(item =>
                item.GetComponentsInChildren<Rigidbody>(true).Length == 0));
            Assert.IsTrue(wrappers.SelectMany(item => item.GetComponentsInChildren<MeshFilter>(true))
                .All(filter => filter.sharedMesh == null || !filter.sharedMesh.isReadable));
            Assert.AreEqual(201,
                File.ReadAllLines("Assets/PickAndPlaceShop/Docs/model_assignment.csv").Length);
        }

        [Test]
        public void Csv_MatchesTwoHundredProductAssets()
        {
            const string path = "Assets/PickAndPlaceShop/Docs/cat_products.csv";
            Assert.IsTrue(File.Exists(path));
            string[] lines = File.ReadAllLines(path);
            Assert.AreEqual(201, lines.Length);
            Assert.IsTrue(lines[1].StartsWith("cat_plush_001,"));
            Assert.IsTrue(lines[^1].StartsWith("cat_retro_040,"));
        }

        [Test]
        public void MachinePools_CoverEveryCatProduct()
        {
            HashSet<ShopProductDefinition> assigned = new();
            CollectSerializedArrays<ShopGachaMachineConfig>(assigned,
                "commonProductDefinitions", "uncommonProductDefinitions", "rareProductDefinitions");
            CollectSerializedArrays<ShopKujiPoolConfig>(assigned,
                "commonCatalog", "uncommonCatalog", "rareCatalog", "premiumCatalog");
            foreach (string path in AssetDatabase.FindAssets("t:ShopClawPrizePool",
                         new[] { "Assets/PickAndPlaceShop/Data/CatTheme/PrizePools" })
                     .Select(AssetDatabase.GUIDToAssetPath))
            {
                ShopClawPrizePool pool = AssetDatabase.LoadAssetAtPath<ShopClawPrizePool>(path);
                foreach (ShopClawPrizePoolEntry entry in pool.Entries)
                    if (entry?.Prize?.Product != null) assigned.Add(entry.Prize.Product);
            }
            CollectionAssert.IsEmpty(LoadCatProducts().Where(product => !assigned.Contains(product))
                .Select(product => product.StableItemId).ToArray());
        }

        [Test]
        public void MainStreetText_HasNoLegacyProductThemeLabels()
        {
            const string path =
                "Assets/PickAndPlaceShop/Scenes/PickAndPlaceShop_MainStreetSlice_Multiplayer.unity";
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            string[] legacy =
            {
                "동물 친구들", "음식 캐릭터", "우주 탐험대", "달토끼",
                "레트로 로봇", "오늘의 한정", "별빛 가챠관"
            };
            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) continue;
                    SerializedProperty text = new SerializedObject(component).FindProperty("m_Text");
                    if (text == null || text.propertyType != SerializedPropertyType.String) continue;
                    foreach (string oldLabel in legacy)
                        Assert.IsFalse(text.stringValue.Contains(oldLabel),
                            component.name + " still contains " + oldLabel);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static ShopProductDefinition[] LoadCatProducts() =>
            AssetDatabase.FindAssets("t:ShopProductDefinition", new[] { ProductFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopProductDefinition>)
                .Where(product => product != null).ToArray();

        private static void CollectSerializedArrays<T>(ISet<ShopProductDefinition> assigned,
            params string[] propertyNames) where T : Object
        {
            foreach (string path in AssetDatabase.FindAssets("t:" + typeof(T).Name,
                         new[] { "Assets/PickAndPlaceShop/Data/Arcade" })
                     .Select(AssetDatabase.GUIDToAssetPath))
            {
                T target = AssetDatabase.LoadAssetAtPath<T>(path);
                SerializedObject serialized = new(target);
                foreach (string propertyName in propertyNames)
                {
                    SerializedProperty property = serialized.FindProperty(propertyName);
                    if (property == null || !property.isArray) continue;
                    for (int i = 0; i < property.arraySize; i++)
                    {
                        ShopProductDefinition product = property.GetArrayElementAtIndex(i)
                            .objectReferenceValue as ShopProductDefinition;
                        if (product != null) assigned.Add(product);
                    }
                }
            }
        }
    }
}
