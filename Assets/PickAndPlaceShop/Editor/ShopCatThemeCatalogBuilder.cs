#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PickAndPlaceShop.Editor
{
    public static class ShopCatThemeCatalogBuilder
    {
        private const string ProductFolder =
            "Assets/PickAndPlaceShop/Resources/Products/CatCatalog";
        private const string PrizeFolder =
            "Assets/PickAndPlaceShop/Data/CatTheme/ClawPrizes";
        private const string PoolFolder =
            "Assets/PickAndPlaceShop/Data/CatTheme/PrizePools";
        private const string CsvPath =
            "Assets/PickAndPlaceShop/Docs/cat_products.csv";
        private const string CatalogPath =
            "Assets/PickAndPlaceShop/Resources/Progression/ShopProgressionCatalog.asset";

        private sealed class CategorySpec
        {
            public readonly string Id;
            public readonly string Label;
            public readonly ShopProductCategory Category;
            public readonly string[] Names;

            public CategorySpec(string id, string label, ShopProductCategory category, string names)
            {
                Id = id;
                Label = label;
                Category = category;
                Names = names.Split('|');
            }
        }

        private static readonly CategorySpec[] Categories =
        {
            new("cat_plush", "고양이 인형", ShopProductCategory.CatPlush,
                "치즈냥 앉은 인형|치즈냥 잠자는 인형|치즈냥 식빵 인형|치즈냥 벌러덩 인형|치즈냥 기지개 인형|" +
                "까망냥 앉은 인형|까망냥 잠자는 인형|까망냥 식빵 인형|까망냥 벌러덩 인형|까망냥 기지개 인형|" +
                "하양냥 앉은 인형|하양냥 잠자는 인형|하양냥 식빵 인형|하양냥 벌러덩 인형|하양냥 기지개 인형|" +
                "회색냥 앉은 인형|회색냥 잠자는 인형|회색냥 식빵 인형|회색냥 벌러덩 인형|회색냥 기지개 인형|" +
                "삼색냥 앉은 인형|삼색냥 잠자는 인형|삼색냥 식빵 인형|삼색냥 벌러덩 인형|삼색냥 기지개 인형|" +
                "턱시도냥 앉은 인형|턱시도냥 잠자는 인형|턱시도냥 식빵 인형|턱시도냥 벌러덩 인형|턱시도냥 기지개 인형|" +
                "샴냥 앉은 인형|샴냥 잠자는 인형|샴냥 식빵 인형|샴냥 벌러덩 인형|샴냥 기지개 인형|" +
                "블루냥 앉은 인형|블루냥 잠자는 인형|블루냥 식빵 인형|블루냥 벌러덩 인형|블루냥 기지개 인형|" +
                "줄무늬냥 앉은 인형|줄무늬냥 잠자는 인형|줄무늬냥 식빵 인형|줄무늬냥 벌러덩 인형|줄무늬냥 기지개 인형|" +
                "크림냥 앉은 인형|크림냥 잠자는 인형|크림냥 식빵 인형|크림냥 벌러덩 인형|크림냥 기지개 인형"),
            new("cat_figure", "고양이 피규어", ShopProductCategory.CatFigure,
                "우주비행사 냥이|요리사 냥이|마법사 냥이|해적 냥이|소방관 냥이|경찰 냥이|의사 냥이|닌자 냥이|사무라이 냥이|기사 냥이|" +
                "발레리나 냥이|록스타 냥이|탐정 냥이|화가 냥이|야구 냥이|축구 냥이|수영 냥이|스키 냥이|카우보이 냥이|임금님 냥이|" +
                "천사 냥이|악마 냥이|공룡잠옷 냥이|개구리잠옷 냥이|꿀벌 냥이|무당벌레 냥이|상어잠옷 냥이|토끼잠옷 냥이|판다잠옷 냥이|교복 냥이|" +
                "과학자 냥이|바리스타 냥이|집배원 냥이|정원사 냥이|낚시꾼 냥이|레이서 냥이|파일럿 냥이|세일러 냥이|바이킹 냥이|히어로 냥이"),
            new("cat_goods", "냥냥 잡화", ShopProductCategory.CatGoods,
                "냥이 얼굴 머그|꼬리 손잡이 머그|발자국 접시|냥귀 유리컵|냥이 키링|윙크냥 배지|빼꼼냥 스티커|잠냥 쿠션|지퍼입 동전지갑|실루엣 에코백|" +
                "폰 받침 냥이|냥이 캡 볼펜|발도장 노트|앉은냥 지우개|냥귀 헤어핀|냥이 얼굴 코스터|꼬리 시계|뚱냥 저금통|발바닥 비누받침|하품냥 칫솔꽂이|" +
                "냥귀 그릇|냥이 숟가락|누운냥 젓가락받침|뚱냥 자석|대롱냥 책갈피|앉은냥 캔들|냥귀 손거울|냥이 부채|냥이 얼굴 네임택|냥발 슬리퍼|" +
                "발바닥 양말|냥주머니 앞치마|냥발 오븐장갑|냥이 도시락|냥귀 물병|꼬리 보온병|냥가족 쟁반|길쭉냥 티슈케이스|꼬리 열쇠걸이|냥이 정리함"),
            new("cat_seasonal", "계절 한정 냥이", ShopProductCategory.CatSeasonal,
                "벚꽃 냥이|화관 냥이|소풍 냥이|나비 냥이|튤립 냥이|봄모자 냥이|새친구 냥이|수박 냥이|튜브 냥이|밀짚모자 냥이|" +
                "빙수 냥이|선글라스 냥이|비치볼 냥이|아이스바 냥이|부채질 냥이|단풍 냥이|도토리 냥이|독서 냥이|군고구마 냥이|낙엽 냥이|" +
                "버섯 냥이|수확 냥이|산타 냥이|눈사람 냥이|목도리 냥이|선물 냥이|코코아 냥이|귀도리 냥이|썰매 냥이|눈꽃우산 냥이"),
            new("cat_retro", "레트로 냥이", ShopProductCategory.CatRetro,
                "브라운관 냥이|다이얼전화 냥이|카세트 냥이|붐박스 냥이|LP 냥이|오락기 냥이|게임패드 냥이|픽셀액자 냥이|폴라로이드 냥이|필름카메라 냥이|" +
                "타자기 냥이|자명종 냥이|주크박스 냥이|네온사인 냥이|자판기 냥이|뽑기통 냥이|다루마 냥이|마네키네코 냥이|종이등 냥이|종이접기 냥이|" +
                "목마 냥이|양철로봇 냥이|만화경 냥이|오르골 냥이|스노우볼 냥이|우표 냥이|우체통 냥이|우유병 냥이|병뚜껑 냥이|성냥갑 냥이|" +
                "구슬 냥이|라무네 냥이|불량식품 냥이|딱지 냥이|켄다마 냥이|팽이 냥이|운세쪽지 냥이|주판 냥이|책가방 냥이|스쿠터 냥이")
        };

        private static readonly Dictionary<string, HashSet<int>> UltraRare = new()
        {
            ["cat_plush"] = new HashSet<int> { 50 },
            ["cat_figure"] = new HashSet<int> { 20, 21, 40 },
            ["cat_goods"] = new HashSet<int>(),
            ["cat_seasonal"] = new HashSet<int> { 1, 23 },
            ["cat_retro"] = new HashSet<int> { 17, 18, 24, 25 }
        };

        private static readonly Dictionary<string, HashSet<int>> Rare = new()
        {
            ["cat_plush"] = new HashSet<int> { 5, 10, 15, 20, 25, 30, 35, 40 },
            ["cat_figure"] = new HashSet<int> { 1, 3, 4, 10, 12, 13, 31, 37 },
            ["cat_goods"] = new HashSet<int> { 1, 5, 8, 18, 30, 34, 36, 40 },
            ["cat_seasonal"] = new HashSet<int> { 2, 8, 9, 16, 17, 24, 26, 29 },
            ["cat_retro"] = new HashSet<int> { 1, 4, 6, 9, 13, 14, 16, 22 }
        };

        private static readonly Dictionary<string, HashSet<int>> Uncommon = new()
        {
            ["cat_plush"] = new HashSet<int> { 1, 6, 11, 16, 21, 26, 31, 36 },
            ["cat_figure"] = new HashSet<int> { 2, 5, 7, 11, 14, 19, 32, 36 },
            ["cat_goods"] = new HashSet<int> { 2, 6, 10, 12, 17, 21, 27, 35 },
            ["cat_seasonal"] = new HashSet<int> { 3, 6, 10, 13, 18, 20, 25, 28 },
            ["cat_retro"] = new HashSet<int> { 2, 3, 7, 10, 12, 15, 21, 32 }
        };

        [MenuItem("Tools/Pick And Place Shop/Apply Cat Theme Catalog")]
        public static void Apply()
        {
            EnsureFolder(ProductFolder);
            EnsureFolder(PrizeFolder);
            EnsureFolder(PoolFolder);
            EnsureFolder("Assets/PickAndPlaceShop/Docs");
            List<ShopProductDefinition> products = BuildProducts();
            ConfigureProgressionCatalog(products);
            ConfigureGacha(products);
            ConfigureKuji(products);
            ConfigureClawMachines(products);
            ConfigureCustomerPreferences();
            ConfigureWorldText();
            WriteCsv(products);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Validate(products);
            Debug.Log("[CatTheme] APPLIED products=200 categories=5 common=110 uncommon=40 rare=40 ultra=10");
        }

        private static List<ShopProductDefinition> BuildProducts()
        {
            ShopPrizePhysicsProfile standard = AssetDatabase.LoadAssetAtPath<ShopPrizePhysicsProfile>(
                "Assets/PickAndPlaceShop/Data/Generated/PhysicsProfiles/PrizePhysics_Standard.asset");
            List<ShopProductDefinition> products = new(200);
            int numericId = 5001;
            foreach (CategorySpec category in Categories)
            {
                for (int i = 0; i < category.Names.Length; i++)
                {
                    int number = i + 1;
                    string stableId = category.Id + "_" + number.ToString("000");
                    string path = ProductFolder + "/Product_" + stableId + ".asset";
                    ShopProductDefinition product = LoadOrCreate<ShopProductDefinition>(path);
                    ShopProductRarity rarity = RarityFor(category.Id, number);
                    product.EditorConfigure(numericId++, category.Names[i], category.Category,
                        PriceFor(rarity), rarity, ShopProductCondition.Mint, true);
                    product.EditorConfigurePrizeData(stableId, null, standard, 10);
                    product.EditorSetPlaceholderArtwork(true);
                    EditorUtility.SetDirty(product);
                    products.Add(product);
                }
            }
            return products;
        }

        private static void ConfigureProgressionCatalog(List<ShopProductDefinition> products)
        {
            ShopProgressionCatalog catalog = AssetDatabase.LoadAssetAtPath<ShopProgressionCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("진행 카탈로그를 찾을 수 없습니다.");
            List<ShopCollectionCategory> categories = Categories
                .Select(category => new ShopCollectionCategory(category.Id, category.Label)).ToList();
            List<ShopCollectionItem> items = products.Select(product => new ShopCollectionItem(
                product.StableItemId, product.DisplayName,
                ShopProductLocalization.CategoryId(product.Category), ProgressRarity(product.Rarity))).ToList();
            List<ShopDistrictUnlock> districts = catalog.DistrictUnlocks.Where(item => item != null)
                .Select(item => new ShopDistrictUnlock(item.DistrictId, DistrictName(item.DisplayName),
                    item.RequiredReputation, item.Placeholder, true)).ToList();
            const string decisions =
                "고양이 붐 테마: cat_plush 50 / cat_figure 40 / cat_goods 40 / " +
                "cat_seasonal 30 / cat_retro 40. 200종과 희귀도 110/40/40/10 유지. " +
                "아트 미제작 상품은 placeholderArtwork로 캡슐과 데이터 이름표를 사용.";
            catalog.EditorConfigure(decisions, catalog.Stages, catalog.ExpansionTiers, districts,
                items, catalog.GoalPool, catalog.CollectionMilestones, catalog.MasteryTiers,
                catalog.DailyGoalCount, catalog.WeeklyGoalCount, categories);
            EditorUtility.SetDirty(catalog);
        }

        private static void ConfigureGacha(List<ShopProductDefinition> products)
        {
            ConfigureGachaAsset("Gacha_Animals.asset", "냥냥 프렌즈 가챠", products.Where(p =>
                p.Category == ShopProductCategory.CatFigure && IndexOf(p) is >= 23 and <= 30 ||
                p.Category == ShopProductCategory.CatGoods && !IsFoodGoods(IndexOf(p))),
                new Color(0.35f, 0.86f, 0.72f));
            ConfigureGachaAsset("Gacha_Food.asset", "간식 냥이 가챠", products.Where(p =>
                p.Category == ShopProductCategory.CatGoods && IsFoodGoods(IndexOf(p))),
                new Color(1f, 0.58f, 0.34f));
            ConfigureGachaAsset("Gacha_Space.asset", "우주 냥사대 가챠", products.Where(p =>
                p.Category == ShopProductCategory.CatFigure &&
                (IndexOf(p) <= 22 || IndexOf(p) >= 31)), new Color(0.28f, 0.58f, 1f));
            ConfigureGachaAsset("Gacha_Limited.asset", "계절 냥이 가챠", products.Where(p =>
                p.Category == ShopProductCategory.CatSeasonal), new Color(1f, 0.44f, 0.72f));
        }

        private static void ConfigureGachaAsset(string fileName, string label,
            IEnumerable<ShopProductDefinition> source, Color color)
        {
            ShopGachaMachineConfig config = AssetDatabase.LoadAssetAtPath<ShopGachaMachineConfig>(
                "Assets/PickAndPlaceShop/Data/Arcade/" + fileName);
            if (config == null) return;
            ShopProductDefinition[] pool = source.Distinct().ToArray();
            ShopProductDefinition[] common = pool.Where(p => p.Rarity == ShopProductRarity.Common).ToArray();
            ShopProductDefinition[] uncommon = pool.Where(p => p.Rarity == ShopProductRarity.Uncommon).ToArray();
            ShopProductDefinition[] rare = pool.Where(p => p.Rarity == ShopProductRarity.Rare).ToArray();
            config.EditorConfigure(config.MachineId, label, config.AttemptCost, config.DailyStock,
                config.UncommonChance, config.RareChance, Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<string>(), color);
            config.EditorConfigureProducts(common, uncommon, rare);
            EditorUtility.SetDirty(config);
        }

        private static void ConfigureKuji(List<ShopProductDefinition> products)
        {
            ShopProductDefinition[] pool = products.Where(product =>
                product.Category == ShopProductCategory.CatSeasonal ||
                product.Rarity >= ShopProductRarity.Rare).Distinct().ToArray();
            foreach (string path in AssetDatabase.FindAssets("t:ShopKujiPoolConfig",
                         new[] { "Assets/PickAndPlaceShop/Data/Arcade" })
                     .Select(AssetDatabase.GUIDToAssetPath))
            {
                ShopKujiPoolConfig config = AssetDatabase.LoadAssetAtPath<ShopKujiPoolConfig>(path);
                if (config == null) continue;
                bool premium = config.PoolId.Contains("robot");
                string label = premium ? "집사 프리미엄 쿠지" : "계절 냥이 쿠지";
                ShopProductDefinition[] common = pool.Where(p => p.Rarity == ShopProductRarity.Common).ToArray();
                ShopProductDefinition[] uncommon = pool.Where(p => p.Rarity == ShopProductRarity.Uncommon).ToArray();
                ShopProductDefinition[] rare = pool.Where(p => p.Rarity == ShopProductRarity.Rare).ToArray();
                ShopProductDefinition[] ultra = pool.Where(p => p.Rarity == ShopProductRarity.UltraRare).ToArray();
                ShopProductDefinition[] premiumPool = ultra.Concat(rare).ToArray();
                config.EditorConfigure(config.PoolId, label, config.TicketPrice, config.InitialStock,
                    premiumPool[0].DisplayName, rare[0].DisplayName, uncommon[0].DisplayName,
                    common[0].DisplayName, common[Math.Min(1, common.Length - 1)].DisplayName,
                    ultra[0].DisplayName, config.CeilingDraws, ultra[ultra.Length - 1].DisplayName);
                config.EditorConfigureProducts(premiumPool[0], rare[0], uncommon[0], common[0],
                    common[Math.Min(1, common.Length - 1)], ultra[0], ultra[ultra.Length - 1]);
                config.EditorConfigureCatalog(common, uncommon, rare, premiumPool);
                EditorUtility.SetDirty(config);
            }
        }

        private static void ConfigureClawMachines(List<ShopProductDefinition> products)
        {
            ShopPrizePhysicsProfile standard = AssetDatabase.LoadAssetAtPath<ShopPrizePhysicsProfile>(
                "Assets/PickAndPlaceShop/Data/Generated/PhysicsProfiles/PrizePhysics_Standard.asset");
            Dictionary<string, ShopClawPrizeDefinition> definitions = new(StringComparer.Ordinal);
            foreach (ShopProductDefinition product in products.Where(product =>
                         product.Category is ShopProductCategory.CatPlush or ShopProductCategory.CatRetro))
            {
                ShopClawPrizeDefinition prize = LoadOrCreate<ShopClawPrizeDefinition>(
                    PrizeFolder + "/ClawPrize_" + product.StableItemId + ".asset");
                float mass = standard != null ? standard.Mass : 0.78f;
                float size = standard != null ? standard.VisualSize : 0.66f;
                float difficulty = standard != null ? standard.GripDifficulty : 0.58f;
                float modifier = standard != null ? standard.GripScoreModifier : 0f;
                float friction = standard != null ? standard.Friction : 0.72f;
                prize.EditorConfigure(product.DisplayName, product, mass, size, difficulty,
                    modifier, friction, RarityColor(product.Rarity));
                EditorUtility.SetDirty(prize);
                definitions[product.StableItemId] = prize;
            }
            ShopClawPrizePool plush = ConfigureClawPool("cat_plush", products, definitions,
                ShopProductCategory.CatPlush);
            ShopClawPrizePool retro = ConfigureClawPool("cat_retro", products, definitions,
                ShopProductCategory.CatRetro);
            foreach (string path in AssetDatabase.FindAssets("t:ShopClawMachineConfig",
                         new[] { "Assets/PickAndPlaceShop/Data" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                ShopClawMachineConfig config = AssetDatabase.LoadAssetAtPath<ShopClawMachineConfig>(path);
                if (config == null) continue;
                bool retroMachine = config.MachineId is 103 or 105;
                SerializedObject serialized = new(config);
                serialized.FindProperty("displayName").stringValue = retroMachine
                    ? "레트로 냥이 뽑기" : "포근한 냥이 인형뽑기";
                serialized.FindProperty("prizePool").objectReferenceValue = retroMachine ? retro : plush;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(config);
            }
        }

        private static ShopClawPrizePool ConfigureClawPool(string id,
            IEnumerable<ShopProductDefinition> products,
            IReadOnlyDictionary<string, ShopClawPrizeDefinition> definitions,
            ShopProductCategory category)
        {
            ShopClawPrizePool pool = LoadOrCreate<ShopClawPrizePool>(
                PoolFolder + "/ClawPrizePool_" + id + ".asset");
            List<ShopClawPrizePoolEntry> entries = products.Where(p => p.Category == category)
                .Select(p => new ShopClawPrizePoolEntry(definitions[p.StableItemId], WeightFor(p.Rarity)))
                .ToList();
            pool.EditorConfigure(id, entries, 7, 18, 0.045f);
            EditorUtility.SetDirty(pool);
            return pool;
        }

        private static void ConfigureCustomerPreferences()
        {
            ShopProductCategory[] preferences = Categories.Select(category => category.Category).ToArray();
            int index = 0;
            foreach (string path in AssetDatabase.FindAssets("t:ShopCustomerArchetypeDefinition",
                         new[] { "Assets/PickAndPlaceShop/Data" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                ShopCustomerArchetypeDefinition customer =
                    AssetDatabase.LoadAssetAtPath<ShopCustomerArchetypeDefinition>(path);
                if (customer == null) continue;
                SerializedObject serialized = new(customer);
                serialized.FindProperty("preferredCategory").enumValueIndex =
                    (int)preferences[index++ % preferences.Length];
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(customer);
            }
        }

        private static void ConfigureWorldText()
        {
            const string scenePath =
                "Assets/PickAndPlaceShop/Scenes/PickAndPlaceShop_MainStreetSlice_Multiplayer.unity";
            Scene existing = SceneManager.GetSceneByPath(scenePath);
            bool openedForEdit = !existing.IsValid() || !existing.isLoaded;
            Scene scene = openedForEdit
                ? EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive)
                : existing;
            Dictionary<string, string> replacements = new(StringComparer.Ordinal)
            {
                ["별빛 프리미엄"] = "냥냥 프리미엄",
                ["별빛 가챠관 · 가챠 4종 / 쿠지\n2종"] = "냥냥 굿즈관 · 가챠 4종 / 쿠지\n2종",
                ["가챠 · 쿠지 상품 조달 코너"] = "고양이 굿즈 조달 코너",
                ["E · 별빛 가챠관 출구"] = "E · 냥냥 굿즈관 출구",
                ["음식 캐릭터 가챠"] = "간식 냥이 가챠",
                ["달토끼 쿠지샵"] = "계절 냥이 쿠지샵",
                ["E · 별빛 가챠관 입구"] = "E · 냥냥 굿즈관 입구",
                ["별빛 뽑기 골목"] = "냥냥 뽑기 골목",
                ["달토끼 쿠지"] = "계절 냥이 쿠지",
                ["가챠 · 쿠지 전문점"] = "고양이 굿즈 전문점",
                ["오늘의 한정 가챠"] = "계절 냥이 가챠",
                ["우주 탐험대 가챠"] = "우주 냥사대 가챠",
                ["별빛 가챠샵"] = "냥냥 가챠샵",
                ["우리 소품샵 · 리뉴얼"] = "우리 냥냥 소품샵",
                ["레트로 로봇 쿠지"] = "집사 프리미엄 쿠지",
                ["가챠 4종 · 쿠지 2종 체험관"] = "냥이 가챠 4종 · 쿠지 2종 체험관",
                ["동물 친구들 가챠"] = "냥냥 프렌즈 가챠"
            };
            Dictionary<string, string> partialReplacements = new(StringComparer.Ordinal)
            {
                ["별빛 가챠관"] = "냥냥 굿즈관",
                ["별빛 뽑기 골목"] = "냥냥 뽑기 골목",
                ["동물 친구들"] = "냥냥 프렌즈",
                ["음식 캐릭터"] = "간식 냥이",
                ["우주 탐험대"] = "우주 냥사대",
                ["달토끼"] = "계절 냥이",
                ["레트로 로봇"] = "집사 프리미엄",
                ["오늘의 한정"] = "계절 냥이",
                ["별빛 프리미엄"] = "냥냥 프리미엄"
            };
            bool changed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                SerializedObject serialized = new(component);
                SerializedProperty text = serialized.FindProperty("m_Text");
                if (text == null || text.propertyType != SerializedPropertyType.String) continue;
                string replacement = replacements.TryGetValue(text.stringValue, out string exact)
                    ? exact : text.stringValue;
                foreach (KeyValuePair<string, string> pair in partialReplacements)
                    replacement = replacement.Replace(pair.Key, pair.Value);
                if (string.Equals(replacement, text.stringValue, StringComparison.Ordinal)) continue;
                text.stringValue = replacement;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(component);
                changed = true;
            }
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            if (openedForEdit) EditorSceneManager.CloseScene(scene, true);
        }

        private static void WriteCsv(IReadOnlyList<ShopProductDefinition> products)
        {
            StringBuilder csv = new("id,display_name,category_id,category_name,rarity,rarity_name,sale_price,placeholder\r\n");
            foreach (ShopProductDefinition product in products)
            {
                csv.Append(product.StableItemId).Append(',')
                    .Append(Escape(product.DisplayName)).Append(',')
                    .Append(ShopProductLocalization.CategoryId(product.Category)).Append(',')
                    .Append(Escape(ShopProductLocalization.CategoryLabel(product.Category))).Append(',')
                    .Append(product.Rarity).Append(',')
                    .Append(ShopProductLocalization.RarityLabel(product.Rarity)).Append(',')
                    .Append(product.SalePrice).Append(',')
                    .Append(product.PlaceholderArtwork ? "true" : "false").Append("\r\n");
            }
            File.WriteAllText(CsvPath, csv.ToString(), new UTF8Encoding(true));
        }

        private static void Validate(IReadOnlyList<ShopProductDefinition> products)
        {
            List<string> errors = new();
            if (products.Count != 200) errors.Add("상품 수=" + products.Count);
            int[] expectedCategoryCounts = { 50, 40, 40, 30, 40 };
            for (int i = 0; i < Categories.Length; i++)
            {
                int count = products.Count(product => product.Category == Categories[i].Category);
                if (count != expectedCategoryCounts[i])
                    errors.Add(Categories[i].Id + "=" + count);
            }
            Dictionary<ShopProductRarity, int> expectedRarities = new()
            {
                [ShopProductRarity.Common] = 110,
                [ShopProductRarity.Uncommon] = 40,
                [ShopProductRarity.Rare] = 40,
                [ShopProductRarity.UltraRare] = 10
            };
            foreach (KeyValuePair<ShopProductRarity, int> expected in expectedRarities)
            {
                int count = products.Count(product => product.Rarity == expected.Key);
                if (count != expected.Value) errors.Add(expected.Key + "=" + count);
            }
            if (products.Select(product => product.StableItemId).Distinct().Count() != 200)
                errors.Add("중복 상품 ID");
            if (products.Any(product => !product.PlaceholderArtwork))
                errors.Add("placeholder 플래그 누락");
            HashSet<ShopProductDefinition> assigned = CollectAssignedProducts();
            List<string> unassigned = products.Where(product => !assigned.Contains(product))
                .Select(product => product.StableItemId).ToList();
            if (unassigned.Count > 0)
                errors.Add("미배정 상품=" + string.Join("/", unassigned));
            if (errors.Count > 0)
                throw new InvalidOperationException("고양이 카탈로그 검증 실패: " + string.Join(", ", errors));
            Debug.Log("[CatTheme] VALID products=200 categoryCounts=50/40/40/30/40 rarity=110/40/40/10 unassigned=0");
        }

        private static ShopProductRarity RarityFor(string categoryId, int number)
        {
            if (UltraRare[categoryId].Contains(number)) return ShopProductRarity.UltraRare;
            if (Rare[categoryId].Contains(number)) return ShopProductRarity.Rare;
            if (Uncommon[categoryId].Contains(number)) return ShopProductRarity.Uncommon;
            return ShopProductRarity.Common;
        }

        private static int PriceFor(ShopProductRarity rarity) => rarity switch
        {
            ShopProductRarity.UltraRare => 800,
            ShopProductRarity.Rare => 480,
            ShopProductRarity.Uncommon => 280,
            _ => 160
        };

        private static int WeightFor(ShopProductRarity rarity) => rarity switch
        {
            ShopProductRarity.UltraRare => 1,
            ShopProductRarity.Rare => 3,
            ShopProductRarity.Uncommon => 7,
            _ => 14
        };

        private static ShopProgressRarity ProgressRarity(ShopProductRarity rarity) => rarity switch
        {
            ShopProductRarity.UltraRare => ShopProgressRarity.Premium,
            ShopProductRarity.Rare => ShopProgressRarity.Rare,
            ShopProductRarity.Uncommon => ShopProgressRarity.Uncommon,
            _ => ShopProgressRarity.Common
        };

        private static Color RarityColor(ShopProductRarity rarity) => rarity switch
        {
            ShopProductRarity.UltraRare => new Color(1f, 0.72f, 0.08f),
            ShopProductRarity.Rare => new Color(0.63f, 0.28f, 0.92f),
            ShopProductRarity.Uncommon => new Color(0.18f, 0.52f, 1f),
            _ => new Color(0.96f, 0.96f, 0.96f)
        };

        private static int IndexOf(ShopProductDefinition product)
        {
            string id = product != null ? product.StableItemId : string.Empty;
            int separator = id.LastIndexOf('_');
            return separator >= 0 && int.TryParse(id[(separator + 1)..], out int value) ? value : 0;
        }

        private static bool IsFoodGoods(int index) => index <= 4 ||
            index is >= 16 and <= 23 || index >= 32;

        private static HashSet<ShopProductDefinition> CollectAssignedProducts()
        {
            HashSet<ShopProductDefinition> assigned = new();
            foreach (string path in AssetDatabase.FindAssets("t:ShopGachaMachineConfig",
                         new[] { "Assets/PickAndPlaceShop/Data/Arcade" })
                     .Select(AssetDatabase.GUIDToAssetPath))
            {
                SerializedObject serialized = new(AssetDatabase.LoadAssetAtPath<ShopGachaMachineConfig>(path));
                AddReferences(serialized, assigned, "commonProductDefinitions",
                    "uncommonProductDefinitions", "rareProductDefinitions");
            }
            foreach (string path in AssetDatabase.FindAssets("t:ShopKujiPoolConfig",
                         new[] { "Assets/PickAndPlaceShop/Data/Arcade" })
                     .Select(AssetDatabase.GUIDToAssetPath))
            {
                SerializedObject serialized = new(AssetDatabase.LoadAssetAtPath<ShopKujiPoolConfig>(path));
                AddReferences(serialized, assigned, "commonCatalog", "uncommonCatalog",
                    "rareCatalog", "premiumCatalog");
            }
            foreach (string path in AssetDatabase.FindAssets("t:ShopClawPrizePool",
                         new[] { PoolFolder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                ShopClawPrizePool pool = AssetDatabase.LoadAssetAtPath<ShopClawPrizePool>(path);
                if (pool == null) continue;
                foreach (ShopClawPrizePoolEntry entry in pool.Entries)
                    if (entry?.Prize?.Product != null) assigned.Add(entry.Prize.Product);
            }
            return assigned;
        }

        private static void AddReferences(SerializedObject serialized,
            ISet<ShopProductDefinition> assigned, params string[] propertyNames)
        {
            if (serialized == null || serialized.targetObject == null) return;
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

        private static string DistrictName(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return source;
            if (source.Contains("벚꽃")) return "벚꽃 냥이길";
            if (source.Contains("강변")) return "강변 냥이 상가";
            if (source.Contains("수집가") || source == "Collector Street") return "집사의 거리";
            if (source.Contains("잠긴 건물")) return "옛 문구점";
            return source;
        }

        private static string Escape(string value) =>
            "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            string current = "Assets";
            foreach (string segment in path.Split('/').Skip(1))
            {
                string next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segment);
                current = next;
            }
        }
    }
}
#endif
