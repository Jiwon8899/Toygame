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
        public int livePhase;
        public float livePhaseSecondsRemaining;
        public int trendCategory;
        public int dailySalesGoal = 1;
        public int dailySalesProgress;
        public int nextOrderId = 1;
        public List<ShopCustomerProfileSave> customerProfiles = new();
        public List<ShopOnlineOrderSave> onlineOrders = new();
        public List<ShopAutomationMachineSave> automationMachines = new();
    }

    public static class ShopProgressionSaveStore
    {
        public const int CurrentVersion = 4;
        private const string FileName = "ShopProgressionSave.json";

        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

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
                Directory.CreateDirectory(Application.persistentDataPath);
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
            data.customerProfiles ??= new List<ShopCustomerProfileSave>();
            data.onlineOrders ??= new List<ShopOnlineOrderSave>();
            data.automationMachines ??= new List<ShopAutomationMachineSave>();
        }
    }
}
