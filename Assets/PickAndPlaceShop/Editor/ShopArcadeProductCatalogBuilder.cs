#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Editor
{
    public static class ShopArcadeProductCatalogBuilder
    {
        private const string ArcadeDataFolder = "Assets/PickAndPlaceShop/Data/Arcade";
        private const string ProductFolder = "Assets/PickAndPlaceShop/Resources/Products/Arcade";

        private static readonly Dictionary<string, string> LowPolyKoreanNames = new()
        {
            { "baby rake", "아기 갈퀴" }, { "backpack", "배낭" },
            { "bitcoin", "비트코인 장식" }, { "bomb", "폭탄 모형" },
            { "bottle", "병" }, { "bowl", "그릇" }, { "Colliders box", "수납 상자" },
            { "crown", "왕관" }, { "dice blue", "파란 주사위" },
            { "diver's mask", "잠수 마스크" }, { "dynamite", "다이너마이트 모형" },
            { "Floor cube", "큐브 장식" }, { "frying pan", "프라이팬" },
            { "hat", "모자" }, { "headphones", "헤드폰" },
            { "hex wrench set", "육각 렌치 세트" }, { "idol", "아이돌 피규어" },
            { "piggy bank", "돼지 저금통" }, { "pot", "냄비" },
            { "smartphone", "스마트폰" }, { "smiley", "스마일 장식" },
            { "soccer boot", "축구화" }, { "steering wheel", "핸들" },
            { "symbol +", "더하기 기호" }, { "symbol -", "빼기 기호" },
            { "symbol =", "등호 기호" }, { "tape", "테이프" },
            { "telescopic ladder", "접이식 사다리" }, { "tower ruler", "타워 자" },
            { "water mine", "기뢰 모형" }, { "watering can", "물뿌리개" },
            { "wheel", "바퀴" }
        };

        [MenuItem("Tools/Pick And Place Shop/Build Arcade Product Catalog")]
        public static void Build()
        {
            EnsureFolder(ProductFolder);
            LocalizeLowPolyProducts();
            ShopProductDefinition[] visuals = AssetDatabase.FindAssets("t:ShopProductDefinition",
                    new[] { "Assets/PickAndPlaceShop/Resources/Products/Generated" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopProductDefinition>)
                .Where(item => item != null && item.PrizePrefab != null)
                .OrderBy(item => item.ProductId).ToArray();
            if (visuals.Length == 0)
                throw new InvalidOperationException("아케이드 상품에 연결할 경품 프리팹이 없습니다.");

            int nextId = 2000;
            int visualCursor = 0;
            foreach (ShopGachaMachineConfig config in LoadAll<ShopGachaMachineConfig>())
            {
                SerializedObject serialized = new(config);
                ShopProductDefinition[] common = CreateArray(config.MachineId, "common",
                    serialized.FindProperty("commonProducts"), ShopProductRarity.Common,
                    CategoryFor(config.MachineId), ref nextId, visuals, ref visualCursor);
                ShopProductDefinition[] uncommon = CreateArray(config.MachineId, "uncommon",
                    serialized.FindProperty("uncommonProducts"), ShopProductRarity.Uncommon,
                    CategoryFor(config.MachineId), ref nextId, visuals, ref visualCursor);
                ShopProductDefinition[] rare = CreateArray(config.MachineId, "rare",
                    serialized.FindProperty("rareProducts"), ShopProductRarity.Rare,
                    CategoryFor(config.MachineId), ref nextId, visuals, ref visualCursor);
                config.EditorConfigureProducts(common, uncommon, rare);
                EditorUtility.SetDirty(config);
            }

            foreach (ShopKujiPoolConfig config in LoadAll<ShopKujiPoolConfig>())
            {
                SerializedObject serialized = new(config);
                ShopProductCategory category = config.PoolId.Contains("robot")
                    ? ShopProductCategory.Retro : ShopProductCategory.Plush;
                ShopProductDefinition s = CreateOne(config.PoolId, "s",
                    serialized.FindProperty("sPrize").stringValue, ShopProductRarity.Rare,
                    category, ref nextId, visuals, ref visualCursor);
                ShopProductDefinition a = CreateOne(config.PoolId, "a",
                    serialized.FindProperty("aPrize").stringValue, ShopProductRarity.Uncommon,
                    category, ref nextId, visuals, ref visualCursor);
                ShopProductDefinition b = CreateOne(config.PoolId, "b",
                    serialized.FindProperty("bPrize").stringValue, ShopProductRarity.Uncommon,
                    category, ref nextId, visuals, ref visualCursor);
                ShopProductDefinition c = CreateOne(config.PoolId, "c",
                    serialized.FindProperty("cPrize").stringValue, ShopProductRarity.Common,
                    category, ref nextId, visuals, ref visualCursor);
                ShopProductDefinition d = CreateOne(config.PoolId, "d",
                    serialized.FindProperty("dPrize").stringValue, ShopProductRarity.Common,
                    category, ref nextId, visuals, ref visualCursor);
                ShopProductDefinition last = CreateOne(config.PoolId, "last",
                    serialized.FindProperty("lastPrize").stringValue, ShopProductRarity.Rare,
                    category, ref nextId, visuals, ref visualCursor);
                ShopProductDefinition ceiling = CreateOne(config.PoolId, "ceiling",
                    serialized.FindProperty("ceilingPrize").stringValue, ShopProductRarity.Rare,
                    category, ref nextId, visuals, ref visualCursor);
                config.EditorConfigureProducts(s, a, b, c, d, last, ceiling);
                EditorUtility.SetDirty(config);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Validate();
        }

        [MenuItem("Tools/Pick And Place Shop/Validate Product Display Data")]
        public static void Validate()
        {
            List<string> errors = new();
            HashSet<int> ids = new();
            foreach (ShopProductDefinition product in AssetDatabase.FindAssets("t:ShopProductDefinition",
                         new[] { "Assets/PickAndPlaceShop" }).Select(AssetDatabase.GUIDToAssetPath)
                         .Select(AssetDatabase.LoadAssetAtPath<ShopProductDefinition>))
            {
                if (product == null) continue;
                if (string.IsNullOrWhiteSpace(product.DisplayName))
                    errors.Add(AssetDatabase.GetAssetPath(product) + ": 표시 이름 누락");
                if (!ids.Add(product.ProductId))
                    errors.Add(AssetDatabase.GetAssetPath(product) + ": 중복 ProductId " + product.ProductId);
                if (string.IsNullOrWhiteSpace(ShopProductLocalization.CategoryLabel(product.Category)) ||
                    string.IsNullOrWhiteSpace(ShopProductLocalization.RarityLabel(product.Rarity)))
                    errors.Add(AssetDatabase.GetAssetPath(product) + ": 분류/희귀도 한국어 표시 누락");
            }
            foreach (ShopGachaMachineConfig config in LoadAll<ShopGachaMachineConfig>())
                foreach (ShopGachaRarity rarity in Enum.GetValues(typeof(ShopGachaRarity)))
                    if (config.ProductDefinitionFor(rarity, 0) == null)
                        errors.Add(config.name + ": " + rarity + " 상품 풀 참조 누락");
            foreach (ShopKujiPoolConfig config in LoadAll<ShopKujiPoolConfig>())
            {
                foreach (ShopKujiRank rank in Enum.GetValues(typeof(ShopKujiRank)))
                    if (config.PrizeDefinitionFor(rank) == null)
                        errors.Add(config.name + ": " + rank + " 상품 참조 누락");
                if (config.LastPrizeDefinition == null || config.CeilingPrizeDefinition == null)
                    errors.Add(config.name + ": 마지막상/천장 상품 참조 누락");
            }
            if (errors.Count > 0)
                throw new InvalidOperationException("상품 표시 데이터 검증 실패\n" + string.Join("\n", errors));
            Debug.Log("[ProductCatalog] VALID products=" + ids.Count +
                      " gacha=" + LoadAll<ShopGachaMachineConfig>().Length +
                      " kuji=" + LoadAll<ShopKujiPoolConfig>().Length);
        }

        private static T[] LoadAll<T>() where T : UnityEngine.Object =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { ArcadeDataFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>).Where(item => item != null)
                .OrderBy(item => item.name).ToArray();

        private static ShopProductDefinition[] CreateArray(string ownerId, string poolId,
            SerializedProperty names, ShopProductRarity rarity, ShopProductCategory category,
            ref int nextId, ShopProductDefinition[] visuals, ref int visualCursor)
        {
            ShopProductDefinition[] result = new ShopProductDefinition[names.arraySize];
            for (int i = 0; i < result.Length; i++)
                result[i] = CreateOne(ownerId, poolId + "_" + i,
                    names.GetArrayElementAtIndex(i).stringValue, rarity, category,
                    ref nextId, visuals, ref visualCursor);
            return result;
        }

        private static ShopProductDefinition CreateOne(string ownerId, string slotId, string label,
            ShopProductRarity rarity, ShopProductCategory category, ref int nextId,
            ShopProductDefinition[] visuals, ref int visualCursor)
        {
            string assetPath = ProductFolder + "/Product_" + ownerId + "_" + slotId + ".asset";
            ShopProductDefinition product = AssetDatabase.LoadAssetAtPath<ShopProductDefinition>(assetPath);
            if (product == null)
            {
                product = ScriptableObject.CreateInstance<ShopProductDefinition>();
                AssetDatabase.CreateAsset(product, assetPath);
            }
            ShopProductDefinition visual = visuals[visualCursor++ % visuals.Length];
            int price = rarity == ShopProductRarity.Rare ? 280 :
                rarity == ShopProductRarity.Uncommon ? 180 : 100;
            product.EditorConfigure(nextId++, label, category, price, rarity,
                ShopProductCondition.Mint, true);
            product.EditorConfigurePrizeData("arcade:" + ownerId + ":" + slotId,
                visual.PrizePrefab, visual.PhysicsProfile, 5);
            EditorUtility.SetDirty(product);
            return product;
        }

        private static ShopProductCategory CategoryFor(string machineId)
        {
            if (machineId.Contains("animal")) return ShopProductCategory.Animal;
            if (machineId.Contains("space")) return ShopProductCategory.Space;
            if (machineId.Contains("limited")) return ShopProductCategory.Seasonal;
            return ShopProductCategory.Decoration;
        }

        private static void LocalizeLowPolyProducts()
        {
            foreach (ShopProductDefinition product in AssetDatabase.FindAssets("t:ShopProductDefinition",
                         new[] { "Assets/PickAndPlaceShop/Resources/Products/Generated" })
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Select(AssetDatabase.LoadAssetAtPath<ShopProductDefinition>))
            {
                if (product == null || !LowPolyKoreanNames.TryGetValue(product.DisplayName, out string label))
                    continue;
                product.EditorConfigure(product.ProductId, label, product.Category, product.SalePrice,
                    product.Rarity, product.Condition, product.GiftWrappable);
                EditorUtility.SetDirty(product);
            }
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
