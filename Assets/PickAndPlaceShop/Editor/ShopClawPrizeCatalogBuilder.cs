using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Editor
{
    public static class ShopClawPrizeCatalogBuilder
    {
        private const string SourcePrefabFolder = "Assets/Low-Poly_Objects_Pack/Prefabs/Built-in";
        private const string ProductFolder = "Assets/PickAndPlaceShop/Resources/Products/Generated";
        private const string PrizeFolder = "Assets/PickAndPlaceShop/Data/Generated/ClawPrizes";
        private const string ProfileFolder = "Assets/PickAndPlaceShop/Data/Generated/PhysicsProfiles";
        private const string PoolFolder = "Assets/PickAndPlaceShop/Data/Generated/PrizePools";
        private const string MaterialFolder = "Assets/PickAndPlaceShop/Data/Generated/PhysicsMaterials";

        [MenuItem("Pick And Place Shop/Rebuild Claw Prize Catalog")]
        public static void RebuildPrizeCatalog()
        {
            EnsureFolder(ProductFolder);
            EnsureFolder(PrizeFolder);
            EnsureFolder(ProfileFolder);
            EnsureFolder(PoolFolder);
            EnsureFolder(MaterialFolder);

            PhysicsMaterial plushMaterial = LoadOrCreatePhysicsMaterial(
                MaterialFolder + "/Prize_Plush.physicMaterial", 0.62f, 0.72f, 0.01f);
            PhysicsMaterial hardMaterial = LoadOrCreatePhysicsMaterial(
                MaterialFolder + "/Prize_Hard.physicMaterial", 0.44f, 0.52f, 0.025f);
            PhysicsMaterial scoopMaterial = LoadOrCreatePhysicsMaterial(
                MaterialFolder + "/Scoop_Surface.physicMaterial", 0.78f, 0.86f, 0f);
            PhysicsMaterial floorMaterial = LoadOrCreatePhysicsMaterial(
                MaterialFolder + "/Machine_Floor.physicMaterial", 0.56f, 0.66f, 0.01f);

            ShopPrizePhysicsProfile light = LoadOrCreate<ShopPrizePhysicsProfile>(
                ProfileFolder + "/PrizePhysics_Light.asset");
            light.EditorConfigure(0.48f, 0.58f, 0.38f, 5f, 0.78f, 0.005f,
                1.20f, 4.00f, false, plushMaterial);
            ShopPrizePhysicsProfile standard = LoadOrCreate<ShopPrizePhysicsProfile>(
                ProfileFolder + "/PrizePhysics_Standard.asset");
            standard.EditorConfigure(0.78f, 0.66f, 0.58f, 0f, 0.72f, 0.008f,
                1.00f, 3.50f, false, plushMaterial);
            ShopPrizePhysicsProfile heavy = LoadOrCreate<ShopPrizePhysicsProfile>(
                ProfileFolder + "/PrizePhysics_Heavy.asset");
            heavy.EditorConfigure(1.18f, 0.72f, 0.82f, -5f, 0.64f, 0.01f,
                0.80f, 2.50f, false, hardMaterial);

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { SourcePrefabFolder });
            GameObject[] prefabs = prefabGuids.Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'),
                    SourcePrefabFolder, StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(prefab => prefab != null)
                .ToArray();

            var definitions = new List<ShopClawPrizeDefinition>();
            for (int i = 0; i < prefabs.Length; i++)
            {
                GameObject prefab = prefabs[i];
                string safeName = Sanitize(prefab.name);
                ShopProductRarity rarity = i % 11 == 0
                    ? ShopProductRarity.Rare
                    : i % 4 == 0 ? ShopProductRarity.Uncommon : ShopProductRarity.Common;
                ShopProductCategory category = CategoryFor(prefab.name, i);
                ShopPrizePhysicsProfile profile = i % 7 == 0 ? heavy : i % 3 == 0 ? light : standard;
                int price = rarity switch
                {
                    ShopProductRarity.Rare => 480,
                    ShopProductRarity.Uncommon => 280,
                    _ => 160
                } + i * 3;

                ShopProductDefinition product = LoadOrCreate<ShopProductDefinition>(
                    ProductFolder + "/Product_" + safeName + ".asset");
                product.EditorConfigure(1000 + i, FriendlyName(prefab.name), category, price, rarity,
                    ShopProductCondition.Mint, true);
                product.EditorConfigurePrizeData("lowpoly:" + prefab.name.ToLowerInvariant(), prefab,
                    profile, rarity == ShopProductRarity.Common ? 5 : 3);
                EditorUtility.SetDirty(product);

                ShopClawPrizeDefinition prize = LoadOrCreate<ShopClawPrizeDefinition>(
                    PrizeFolder + "/ClawPrize_" + safeName + ".asset");
                prize.EditorConfigure(product.DisplayName, product, profile.Mass, profile.VisualSize,
                    profile.GripDifficulty, profile.GripScoreModifier, profile.Friction,
                    Color.HSVToRGB((i * 0.087f) % 1f, 0.38f, 0.96f));
                EditorUtility.SetDirty(prize);
                definitions.Add(prize);
            }

            ShopClawPrizePool general = ConfigurePool("General", definitions
                .Where((definition, index) => index % 3 == 0 || definition.Product.Rarity == ShopProductRarity.Common),
                7, 16, 0.045f);
            ShopClawPrizePool retro = ConfigurePool("Retro", definitions
                .Where((definition, index) => index % 3 == 1 || definition.Product.Category == ShopProductCategory.Retro),
                6, 16, 0.045f);
            ShopClawPrizePool premium = ConfigurePool("Premium", definitions
                .Where((definition, index) => index % 3 == 2 ||
                                               definition.Product.Rarity == ShopProductRarity.Rare),
                6, 18, 0.05f);
            ShopClawPrizePool[] pools = { general, retro, premium };

            string[] machineGuids = AssetDatabase.FindAssets("t:ShopClawMachineConfig",
                new[] { "Assets/PickAndPlaceShop/Data" });
            int machineIndex = 0;
            foreach (string guid in machineGuids.OrderBy(value => value, StringComparer.Ordinal))
            {
                ShopClawMachineConfig machine =
                    AssetDatabase.LoadAssetAtPath<ShopClawMachineConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (machine == null) continue;
                int poolIndex = machine.MachineId is >= 101 and <= 103
                    ? machine.MachineId - 101
                    : machineIndex % pools.Length;
                machine.EditorConfigurePrizeCatalog(pools[poolIndex], scoopMaterial, floorMaterial);
                EditorUtility.SetDirty(machine);
                machineIndex++;
            }

            UpdateExpansionCapacities();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ClawCatalog] REBUILT prefabs=" + prefabs.Length + " products=" +
                      definitions.Count + " pools=3 machines=" + machineIndex);
        }

        private static ShopClawPrizePool ConfigurePool(string id,
            IEnumerable<ShopClawPrizeDefinition> source, int maximum, int attempts, float clearance)
        {
            ShopClawPrizePool pool = LoadOrCreate<ShopClawPrizePool>(
                PoolFolder + "/ClawPrizePool_" + id + ".asset");
            List<ShopClawPrizePoolEntry> entries = source.Distinct().Select(definition =>
                new ShopClawPrizePoolEntry(definition, definition.Product.Rarity switch
                {
                    ShopProductRarity.Rare => 2,
                    ShopProductRarity.Uncommon => 6,
                    _ => 14
                })).ToList();
            pool.EditorConfigure(id.ToLowerInvariant(), entries, maximum, attempts, clearance);
            EditorUtility.SetDirty(pool);
            return pool;
        }

        private static void UpdateExpansionCapacities()
        {
            const string path = "Assets/PickAndPlaceShop/Resources/Progression/ShopProgressionCatalog.asset";
            ShopProgressionCatalog catalog = AssetDatabase.LoadAssetAtPath<ShopProgressionCatalog>(path);
            if (catalog == null) return;
            ShopExpansionTier[] tiers =
            {
                new(1, 0, 0, 4, 30, 1, ShopExpansionFeature.Checkout),
                new(2, 10, 2000, 6, 50, 1,
                    ShopExpansionFeature.Checkout | ShopExpansionFeature.PackingTable),
                new(3, 25, 4500, 10, 80, 2,
                    ShopExpansionFeature.Checkout | ShopExpansionFeature.PackingTable |
                    ShopExpansionFeature.ShowWindow),
                new(4, 40, 8000, 12, 110, 2,
                    ShopExpansionFeature.Checkout | ShopExpansionFeature.PackingTable |
                    ShopExpansionFeature.ShowWindow | ShopExpansionFeature.SecondFloor),
                new(5, 60, 14000, 16, 160, 3,
                    ShopExpansionFeature.Checkout | ShopExpansionFeature.PackingTable |
                    ShopExpansionFeature.ShowWindow | ShopExpansionFeature.SecondFloor |
                    ShopExpansionFeature.OnlineOrderRoom)
            };
            catalog.EditorConfigure(catalog.DesignDecisions, catalog.Stages, tiers,
                catalog.DistrictUnlocks, catalog.CollectionItems, catalog.GoalPool,
                catalog.CollectionMilestones, catalog.MasteryTiers,
                catalog.DailyGoalCount, catalog.WeeklyGoalCount,
                catalog.CollectionCategories);
            EditorUtility.SetDirty(catalog);
        }

        private static ShopProductCategory CategoryFor(string name, int index)
        {
            string lower = name.ToLowerInvariant();
            if (lower.Contains("baby") || lower.Contains("pig") || lower.Contains("smiley"))
                return ShopProductCategory.Animal;
            if (lower.Contains("mine") || lower.Contains("dynamite") || lower.Contains("bomb"))
                return ShopProductCategory.Space;
            if (lower.Contains("idol") || lower.Contains("crown") || lower.Contains("bitcoin"))
                return ShopProductCategory.Seasonal;
            return index % 4 == 0 ? ShopProductCategory.Retro : ShopProductCategory.Decoration;
        }

        private static string FriendlyName(string source)
        {
            return source.Replace('_', ' ').Trim();
        }

        private static string Sanitize(string source)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(source.Select(character =>
                invalid.Contains(character) || character == ' ' ? '_' : character).ToArray());
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static PhysicsMaterial LoadOrCreatePhysicsMaterial(string path, float dynamicFriction,
            float staticFriction, float bounciness)
        {
            PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (material == null)
            {
                material = new PhysicsMaterial(Path.GetFileNameWithoutExtension(path));
                AssetDatabase.CreateAsset(material, path);
            }
            material.dynamicFriction = dynamicFriction;
            material.staticFriction = staticFriction;
            material.bounciness = bounciness;
            material.frictionCombine = PhysicsMaterialCombine.Average;
            material.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string folder)
        {
            string current = "Assets";
            foreach (string segment in folder.Split('/').Skip(1))
            {
                string next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segment);
                current = next;
            }
        }
    }
}
