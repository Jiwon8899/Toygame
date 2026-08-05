using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PickAndPlaceShop
{
    [Serializable]
    public sealed class ShopProgressGoalSave
    {
        public string definitionId;
        public string displayName;
        public int conditionType;
        public int target;
        public string categoryId;
        public int baseline;
        public bool completed;
    }

    [Serializable]
    public sealed class ShopContainerItemSave
    {
        public ulong ownerClientId;
        public int container;
        public int slotIndex;
        public int productId;
        public int visualPrefabIndex;
        public int quantity;
        public int maxStack = 1;
        public int unitPrice;
        public int rarity;
        public string displayName;
        public ulong instanceId;
        public int appraisalGrade;
    }

    [Serializable]
    public sealed class ShopClawPrizeSave
    {
        public int productId;
        public int rarity;
        public int visualPrefabIndex;
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
    }

    [Serializable]
    public sealed class ShopClawMachineSave
    {
        public int machineId;
        public List<ShopClawPrizeSave> prizes = new();
    }

    [Serializable]
    public sealed class ShopKujiStationSave
    {
        public string poolId;
        public int setNumber = 1;
        public int stockS;
        public int stockA;
        public int stockB;
        public int stockC;
        public int stockD;
        public int drawsSinceCeiling;
        public bool lastPrizeAwarded;
        public bool refilling;
        public float refillSecondsRemaining;
    }

    [Serializable]
    public sealed class ShopProgressionSaveData
    {
        public int version = ShopProgressionSaveStore.CurrentVersion;
        public int currentDay = 1;
        public int teamFunds;
        public int reputation;
        public int lifetimeRevenue;
        public int unitsSold;
        public int rareItemsAcquired;
        public int rareItemsSold;
        public int satisfactionTotal;
        public int satisfactionSamples;
        public int onlineOrdersCompleted;
        public int clawSuccesses;
        public int currentStageIndex;
        public int expansionLevel = 1;
        public int expansionVouchers;
        public int randomBoxes;
        public int dailyGoalCycle;
        public int weeklyGoalCycle;
        public bool dailySetRewardClaimed;
        public bool weeklySetRewardClaimed;
        public List<string> regularCustomerIds = new();
        public List<string> unlockedDistrictIds = new();
        public List<string> ownedCollectionItemIds = new();
        public List<int> grantedCollectionMilestones = new();
        public List<ShopProgressGoalSave> dailyGoals = new();
        public List<ShopProgressGoalSave> weeklyGoals = new();
        public List<ShopContainerItemSave> containerItems = new();
        public List<ShopClawMachineSave> clawMachines = new();
        public List<ShopKujiStationSave> kujiStations = new();
        public int livePhase;
        public float livePhaseSecondsRemaining;
        public int trendCategory;
        public int previousTrendCategory;
        public string trendNews;
        public int dailySalesGoal = 1;
        public int dailySalesProgress;
        public int nextOrderId = 1;
        public List<ShopCustomerProfileSave> customerProfiles = new();
        public List<ShopOnlineOrderSave> onlineOrders = new();
        public List<ShopAutomationMachineSave> automationMachines = new();
        public int playerUpgradeLevel;
        public int operationsUpgradeLevel;
        public int facilityUpgradeLevel;
        public int clawUpgradeLevel;
        public int gachaUpgradeLevel;
        public int kujiUpgradeLevel;
        public int staffHiredMask;
        public int staffAttendanceMask;
        public int tutorialStep;
        public bool tutorialCompleted;
        public int upcycleDecorMask;
        public int lastOneAwards;
        public string recentLastOneRecords;
        public string reviewHistory;
        public int latestReviewDay;
        public int appraisalSequence;
    }

    public static class ShopProgressionSaveStore
    {
        public const int CurrentVersion = 11;
        private const string FileName = "ShopProgressionSave.json";
        private const string StableSaveFolderName = "ToyGame";

        // PlayerSettings.productName is now the localized formal title. Keep the original
        // save directory so existing players continue from the same file without a one-off move.
        public static string SaveDirectory
        {
            get
            {
                DirectoryInfo companyDirectory = Directory.GetParent(Application.persistentDataPath);
                return companyDirectory != null
                    ? Path.Combine(companyDirectory.FullName, StableSaveFolderName)
                    : Application.persistentDataPath;
            }
        }

        public static string SavePath => Path.Combine(SaveDirectory, FileName);
        public static bool HasUsableSave => TryLoad(out _);

        public static bool TryLoad(out ShopProgressionSaveData data)
        {
            data = null;
            try
            {
                if (!File.Exists(SavePath)) return false;
                string json = File.ReadAllText(SavePath);
                data = JsonUtility.FromJson<ShopProgressionSaveData>(json);
                if (data == null || data.version < 1 || data.version > CurrentVersion)
                {
                    Debug.LogWarning("[Progression] 알 수 없는 저장 버전입니다. 안전한 기본값으로 시작합니다.");
                    data = null;
                    return false;
                }
                EnsureCollections(data);
                if (data.version < 5) MigrateToCatTheme(data);
                data.version = CurrentVersion;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                data = null;
                return false;
            }
        }

        public static bool Save(ShopProgressionSaveData data)
        {
            if (data == null) return false;
            try
            {
                data.version = CurrentVersion;
                Directory.CreateDirectory(SaveDirectory);
                string temporaryPath = SavePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));
                if (File.Exists(SavePath))
                {
                    string backupPath = SavePath + ".bak";
                    File.Replace(temporaryPath, SavePath, backupPath, true);
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                }
                else
                {
                    File.Move(temporaryPath, SavePath);
                }
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(SavePath)) File.Delete(SavePath);
                if (File.Exists(SavePath + ".tmp")) File.Delete(SavePath + ".tmp");
                if (File.Exists(SavePath + ".bak")) File.Delete(SavePath + ".bak");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void EnsureCollections(ShopProgressionSaveData data)
        {
            data.regularCustomerIds ??= new List<string>();
            data.unlockedDistrictIds ??= new List<string>();
            data.ownedCollectionItemIds ??= new List<string>();
            data.grantedCollectionMilestones ??= new List<int>();
            data.dailyGoals ??= new List<ShopProgressGoalSave>();
            data.weeklyGoals ??= new List<ShopProgressGoalSave>();
            data.containerItems ??= new List<ShopContainerItemSave>();
            data.clawMachines ??= new List<ShopClawMachineSave>();
            data.kujiStations ??= new List<ShopKujiStationSave>();
            data.customerProfiles ??= new List<ShopCustomerProfileSave>();
            data.onlineOrders ??= new List<ShopOnlineOrderSave>();
            data.automationMachines ??= new List<ShopAutomationMachineSave>();
        }

        private static void MigrateToCatTheme(ShopProgressionSaveData data)
        {
            for (int i = 0; i < data.ownedCollectionItemIds.Count; i++)
                data.ownedCollectionItemIds[i] = MigrateCollectionItemId(
                    data.ownedCollectionItemIds[i]);
            MigrateGoals(data.dailyGoals);
            MigrateGoals(data.weeklyGoals);
            data.trendCategory = (int)MigrateCategory((ShopProductCategory)data.trendCategory);
            for (int i = 0; i < data.customerProfiles.Count; i++)
            {
                ShopCustomerProfileSave profile = data.customerProfiles[i];
                if (profile != null)
                    profile.preferredCategory = (int)MigrateCategory(
                        (ShopProductCategory)profile.preferredCategory);
            }

            // 기존 주문은 구 상품 이름과 ProductId를 함께 저장하므로 새 테마에서 재생성한다.
            data.onlineOrders.Clear();
        }

        private static void MigrateGoals(List<ShopProgressGoalSave> goals)
        {
            if (goals == null) return;
            for (int i = 0; i < goals.Count; i++)
            {
                ShopProgressGoalSave goal = goals[i];
                if (goal != null) goal.categoryId = MigrateCategoryId(goal.categoryId);
            }
        }

        private static string MigrateCollectionItemId(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return itemId;
            const string prefix = "collection:";
            if (!itemId.StartsWith(prefix, StringComparison.Ordinal)) return itemId;
            string[] segments = itemId.Split(':');
            return segments.Length == 3
                ? MigrateCategoryId(segments[1]) + "_" + segments[2]
                : itemId;
        }

        private static string MigrateCategoryId(string categoryId) => categoryId switch
        {
            "animal" => "cat_plush",
            "space" => "cat_figure",
            "retro" => "cat_retro",
            "seasonal" => "cat_seasonal",
            "other" => "cat_goods",
            _ => categoryId ?? string.Empty
        };

        private static ShopProductCategory MigrateCategory(ShopProductCategory category) => category switch
        {
            ShopProductCategory.Animal or ShopProductCategory.Plush => ShopProductCategory.CatPlush,
            ShopProductCategory.Space or ShopProductCategory.CapsuleToy => ShopProductCategory.CatFigure,
            ShopProductCategory.Retro => ShopProductCategory.CatRetro,
            ShopProductCategory.Seasonal => ShopProductCategory.CatSeasonal,
            ShopProductCategory.Other or ShopProductCategory.Decoration => ShopProductCategory.CatGoods,
            _ when ShopProductLocalization.IsCatTheme(category) => category,
            _ => ShopProductCategory.CatGoods
        };
    }
}
