using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(300)]
    public sealed class ShopProgressionManager : MonoBehaviour
    {
        private const string CatalogResourcePath = "Progression/ShopProgressionCatalog";
        private const float GameBindingDelay = 1.25f;

        public static ShopProgressionManager Instance { get; private set; }

        public event Action StateChanged;
        public event Action<ShopProgressStage> StageAdvanced;
        public event Action<ShopDistrictUnlock> DistrictUnlocked;
        public event Action<ShopCollectionMilestone> CollectionMilestoneReached;
        public event Action<int> ExpansionChanged;
        public event Action<string> NotificationRaised;

        private ShopProgressionCatalog catalog;
        private readonly HashSet<string> unlockedDistrictIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> ownedCollectionItemIds = new(StringComparer.Ordinal);
        private readonly HashSet<int> grantedCollectionMilestones = new();
        private readonly HashSet<string> grantedCollectionSets = new(StringComparer.Ordinal);
        private readonly HashSet<string> completedCollectionCategories = new(StringComparer.Ordinal);
        private readonly List<ShopProgressGoalSave> dailyGoals = new();
        private readonly List<ShopProgressGoalSave> weeklyGoals = new();
        private readonly List<ShopContainerItemSave> pendingContainerItems = new();
        private readonly List<ShopClawMachineSave> pendingClawMachines = new();
        private readonly List<ShopKujiStationSave> pendingKujiStations = new();

        private int currentDay = 1;
        private string playerShopName;
        private string rivalShopName;
        private int teamFunds;
        private int reputation;
        private int lifetimeRevenue;
        private int unitsSold;
        private int rareItemsAcquired;
        private int rareItemsSold;
        private int onlineOrdersCompleted;
        private int clawSuccesses;
        private int currentStageIndex;
        private int expansionLevel = 1;
        private int expansionVouchers;
        private int randomBoxes;
        private int dailyGoalCycle;
        private int weeklyGoalCycle;
        private bool dailySetRewardClaimed;
        private bool weeklySetRewardClaimed;
        private bool loadedFromSave;
        private ShopProgressionSaveData loadedSaveData;
        private bool evaluating;
        private bool dirty;
        private float nextAutosaveTime;
        private ShopNetworkGame boundGame;
        private ShopNetworkGame containersRestoredGame;
        private float bindAt;
        private int observedGameFunds;
        private int observedGameReputation;
        private int observedGameDay;
        private int tutorialStep;
        private bool tutorialCompleted;
        private readonly int[] hotbarProductIds = { -1, -1, -1, -1, -1 };
        private int selectedHotbarSlot = -1;

        public ShopProgressionCatalog Catalog => catalog;
        public int CurrentDay => currentDay;
        public string PlayerShopName => ResolveStoreName(playerShopName, true);
        public string RivalShopName => ResolveStoreName(rivalShopName, false);
        public int TeamFunds => teamFunds;
        public int Reputation => reputation;
        public int LifetimeRevenue => lifetimeRevenue;
        public int UnitsSold => unitsSold;
        public int RareItemsAcquired => rareItemsAcquired;
        public int RareItemsSold => rareItemsSold;
        public int OnlineOrdersCompleted => onlineOrdersCompleted;
        public int ClawSuccesses => clawSuccesses;
        public int CurrentStageIndex => currentStageIndex;
        public int ExpansionLevel => expansionLevel;
        public int ExpansionVouchers => expansionVouchers;
        public int RandomBoxes => randomBoxes;
        public int CollectionOwnedCount => ownedCollectionItemIds.Count;
        public int CollectionRegisteredCount => catalog != null ? catalog.CollectionItems.Count : 0;
        public int CollectionPercent => ShopProgressionRules.CollectionPercent(
            CollectionOwnedCount, CollectionRegisteredCount);
        public IReadOnlyList<ShopProgressGoalSave> DailyGoals => dailyGoals;
        public IReadOnlyList<ShopProgressGoalSave> WeeklyGoals => weeklyGoals;
        public IReadOnlyCollection<string> UnlockedDistrictIds => unlockedDistrictIds;
        public bool LoadedFromSave => loadedFromSave;
        public int TutorialStep => tutorialStep;
        public bool TutorialCompleted => tutorialCompleted;
        public string SavePath => ShopProgressionSaveStore.SavePath;
        public ShopProgressionSaveData GetLoadedSaveData() => loadedSaveData;
        public int SelectedHotbarSlot => selectedHotbarSlot;

        public int GetHotbarProductId(int slot) => slot >= 0 && slot < hotbarProductIds.Length
            ? hotbarProductIds[slot]
            : -1;

        public void SetHotbarProduct(int slot, int productId)
        {
            if (slot < 0 || slot >= hotbarProductIds.Length) return;
            hotbarProductIds[slot] = productId;
            MarkChanged();
        }

        public int AutoAssignHotbarProduct(int productId)
        {
            if (productId < 0) return -1;
            for (int slot = 0; slot < hotbarProductIds.Length; slot++)
                if (hotbarProductIds[slot] == productId) return slot;
            for (int slot = 0; slot < hotbarProductIds.Length; slot++)
            {
                if (hotbarProductIds[slot] >= 0) continue;
                hotbarProductIds[slot] = productId;
                MarkChanged();
                return slot;
            }
            return -1;
        }

        public void SetSelectedHotbarSlot(int slot)
        {
            selectedHotbarSlot = Mathf.Clamp(slot, -1, hotbarProductIds.Length - 1);
            MarkChanged();
        }

        public ShopProgressStage CurrentStage =>
            catalog != null && catalog.Stages.Count > 0
                ? catalog.Stages[Mathf.Clamp(currentStageIndex, 0, catalog.Stages.Count - 1)]
                : null;

        public ShopProgressStage NextStage =>
            catalog != null && currentStageIndex + 1 < catalog.Stages.Count
                ? catalog.Stages[currentStageIndex + 1]
                : null;

        public ShopExpansionTier CurrentExpansion =>
            FindExpansionTier(expansionLevel) ?? (catalog != null && catalog.ExpansionTiers.Count > 0
                ? catalog.ExpansionTiers[0]
                : null);

        public ShopExpansionTier NextExpansion => FindExpansionTier(expansionLevel + 1);
        public int CurrentDisplaySlots => CurrentExpansion?.DisplaySlots ?? 4;
        public int CurrentStorageSlots => CurrentExpansion?.StorageSlots ?? 30;
        public int CurrentCheckoutCount => CurrentExpansion?.CheckoutCount ?? 1;
        public ShopExpansionFeature CurrentFeatures => CurrentExpansion?.Features ?? ShopExpansionFeature.Checkout;
        public string ClawMasteryTitle =>
            ShopProgressionRules.FindMasteryTier(clawSuccesses, catalog?.MasteryTiers)?.Title ?? "초보 뽑기사";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject host = new("[Progression] Shared Shop Progression");
            DontDestroyOnLoad(host);
            host.AddComponent<ShopProgressionManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            catalog = Resources.Load<ShopProgressionCatalog>(CatalogResourcePath);
            if (catalog == null)
            {
                Debug.LogError("[Progression] Resources/" + CatalogResourcePath +
                               " 데이터 에셋을 찾지 못했습니다.", this);
                enabled = false;
                return;
            }
            loadedFromSave = ShopProgressionSaveStore.TryLoad(out ShopProgressionSaveData save);
            if (loadedFromSave)
            {
                loadedSaveData = save;
                ApplySaveData(save);
            }
            EnsureGoalCycles();
            EvaluateProgress();
        }

        private void Update()
        {
            BindOrMirrorGame();
            if (dirty && Time.unscaledTime >= nextAutosaveTime)
            {
                SaveNow();
            }
        }

        private void OnApplicationQuit()
        {
            if (enabled) SaveNow();
        }

        public int GetConditionValue(ShopProgressCondition condition)
        {
            if (condition == null) return 0;
            return GetConditionValue(condition.Type, condition.CategoryId);
        }

        public int GetConditionValue(ShopProgressConditionType type, string categoryId = "")
        {
            return type switch
            {
                ShopProgressConditionType.Reputation => reputation,
                ShopProgressConditionType.LifetimeRevenue => lifetimeRevenue,
                ShopProgressConditionType.UnitsSold => unitsSold,
                ShopProgressConditionType.RareItemsAcquired => rareItemsAcquired,
                ShopProgressConditionType.RareItemsSold => rareItemsSold,
                ShopProgressConditionType.OnlineOrdersCompleted => onlineOrdersCompleted,
                ShopProgressConditionType.CollectionPercent => CollectionPercent,
                ShopProgressConditionType.CategoryItemsOwned => CountOwnedInCategory(categoryId),
                ShopProgressConditionType.ClawSuccesses => clawSuccesses,
                _ => 0
            };
        }

        public void AddReputation(int amount)
        {
            if (amount == 0) return;
            reputation = Mathf.Max(0, reputation + amount);
            if (boundGame != null && boundGame.IsServer)
            {
                boundGame.Reputation.Value = reputation;
                observedGameReputation = reputation;
            }
            MarkChanged();
        }

        public void ChangeTeamFunds(int amount)
        {
            if (amount == 0) return;
            if (boundGame != null && boundGame.IsServer)
            {
                teamFunds = Mathf.Max(0, boundGame.Coins.Value);
                observedGameFunds = teamFunds;
            }
            teamFunds = Mathf.Max(0, teamFunds + amount);
            if (boundGame != null && boundGame.IsServer)
            {
                boundGame.Coins.Value = teamFunds;
                observedGameFunds = teamFunds;
            }
            MarkChanged();
        }

        public bool CaptureAuthoritativeSessionState()
        {
            if (boundGame == null || !boundGame.IsSpawned || !boundGame.IsServer) return false;

            int authoritativeDay = Mathf.Max(1, boundGame.Day.Value);
            bool dayChanged = currentDay != authoritativeDay;
            teamFunds = Mathf.Max(0, boundGame.Coins.Value);
            reputation = Mathf.Max(0, boundGame.Reputation.Value);
            currentDay = authoritativeDay;
            observedGameFunds = teamFunds;
            observedGameReputation = reputation;
            observedGameDay = currentDay;
            if (dayChanged) BeginGoalCyclesForDay(currentDay);
            MarkChanged();
            return true;
        }

        public void AdvanceTutorial(int completionReward)
        {
            if (tutorialCompleted) return;
            tutorialStep++;
            if (tutorialStep >= ShopTutorialRuntime.StepCount)
            {
                tutorialStep = ShopTutorialRuntime.StepCount;
                tutorialCompleted = true;
                ChangeTeamFunds(Mathf.Max(0, completionReward));
                RaiseNotification("튜토리얼 완료! 보상 " + Mathf.Max(0, completionReward) + "원을 받았습니다.");
                SaveNow();
                return;
            }
            MarkChanged();
            SaveNow();
        }

        public void ResetTutorial()
        {
            tutorialStep = 0;
            tutorialCompleted = false;
            MarkChanged();
            SaveNow();
            RaiseNotification("튜토리얼을 처음부터 다시 시작합니다.");
        }

        public void SkipTutorial()
        {
            if (tutorialCompleted) return;
            ShopTutorialConfig tutorialConfig = ShopTutorialConfig.Load();
            int reward = tutorialConfig != null ? tutorialConfig.SkipReward : 0;
            tutorialStep = ShopTutorialRuntime.StepCount;
            tutorialCompleted = true;
            ChangeTeamFunds(reward);
            MarkChanged();
            SaveNow();
            RaiseNotification("튜토리얼을 건너뛰었습니다. 시작 지원금 " + reward +
                              "원을 받고 일반 목표를 표시합니다.");
        }

        public void RecordSale(string itemId, string displayName, string categoryId,
            int revenue, bool rare)
        {
            lifetimeRevenue += Mathf.Max(0, revenue);
            unitsSold++;
            if (rare) rareItemsSold++;
            MarkChanged();
            ShopTutorialRuntime.Report(ShopTutorialAction.ProductSold);
        }

        public void RecordAcquisition(string itemId, string displayName, string categoryId,
            bool rare, int amount = 1)
        {
            int positiveAmount = Mathf.Max(1, amount);
            if (rare) rareItemsAcquired += positiveAmount;
            string registeredId = ResolveCollectionItemId(itemId, displayName, categoryId);
            bool newlyOwned = !string.IsNullOrWhiteSpace(registeredId) && ownedCollectionItemIds.Add(registeredId);
            if (newlyOwned) EvaluateCollectionSets(categoryId);
            MarkChanged();
        }

        public float CollectionSaleMultiplier(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId) ||
                !completedCollectionCategories.Contains(categoryId)) return 1f;
            ShopSideContentConfig config = ShopSideContentConfig.Load();
            return 1f + (config != null ? config.CategoryCompletionSaleBonus : 0.08f);
        }

        private void EvaluateCollectionSets(string categoryId)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(categoryId)) return;
            List<ShopCollectionItem> categoryItems = catalog.CollectionItems
                .Where(item => item != null && item.CategoryId == categoryId).ToList();
            if (categoryItems.Count == 0) return;
            ShopSideContentConfig config = ShopSideContentConfig.Load();
            int setSize = config != null ? config.SmallSetSize : 5;
            int setCount = Mathf.CeilToInt(categoryItems.Count / (float)setSize);
            for (int set = 0; set < setCount; set++)
            {
                int start = set * setSize;
                int end = Mathf.Min(categoryItems.Count, start + setSize);
                bool complete = true;
                for (int i = start; i < end; i++)
                    if (!ownedCollectionItemIds.Contains(categoryItems[i].ItemId)) { complete = false; break; }
                string setId = categoryId + ":set:" + set;
                if (!complete || !grantedCollectionSets.Add(setId)) continue;
                int reward = config != null ? config.SmallSetReward : 250;
                ChangeTeamFunds(reward);
                RaiseNotification("도감 소형 세트 완성! +" + reward + "원");
                Debug.Log("[SideContent:CollectionSet] set=" + setId + " reward=" + reward, this);
            }
            bool categoryComplete = categoryItems.All(item => ownedCollectionItemIds.Contains(item.ItemId));
            if (categoryComplete && completedCollectionCategories.Add(categoryId))
            {
                float bonus = config != null ? config.CategoryCompletionSaleBonus : 0.08f;
                RaiseNotification("도감 카테고리 완성! 해당 상품 판매가 +" +
                                  Mathf.RoundToInt(bonus * 100f) + "% 영구 효과");
                Debug.Log("[SideContent:CollectionCategory] category=" + categoryId + " bonus=" + bonus, this);
            }
        }

        public void RecordOnlineOrder(int amount = 1)
        {
            if (amount <= 0) return;
            onlineOrdersCompleted += amount;
            MarkChanged();
        }

        public void RecordClawResult(bool success)
        {
            if (!success) return;
            clawSuccesses++;
            MarkChanged();
        }

        public bool TryExpandShop(out string message)
        {
            ShopExpansionTier next = NextExpansion;
            if (next == null)
            {
                message = "가게 확장을 모두 완료했습니다.";
                return false;
            }
            if (reputation < next.RequiredReputation)
            {
                message = "평판 " + next.RequiredReputation + "이 필요합니다.";
                return false;
            }
            bool useVoucher = expansionVouchers > 0;
            if (!useVoucher && teamFunds < next.RequiredFunds)
            {
                message = "공동 자금 " + next.RequiredFunds + "원이 필요합니다.";
                return false;
            }
            if (useVoucher) expansionVouchers--;
            else ChangeTeamFunds(-next.RequiredFunds);
            expansionLevel = next.Level;
            message = "가게 확장 Lv." + expansionLevel + " 완료";
            ExpansionChanged?.Invoke(expansionLevel);
            RaiseNotification(message);
            MarkChanged();
            return true;
        }

        public bool IsDistrictUnlocked(string districtId) =>
            !string.IsNullOrWhiteSpace(districtId) && unlockedDistrictIds.Contains(districtId);

        public bool OwnsCollectionItem(string itemId) =>
            !string.IsNullOrWhiteSpace(itemId) && ownedCollectionItemIds.Contains(itemId);

        public int CountOwnedInCategory(string categoryId)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(categoryId)) return 0;
            int count = 0;
            for (int i = 0; i < catalog.CollectionItems.Count; i++)
            {
                ShopCollectionItem item = catalog.CollectionItems[i];
                if (item != null && string.Equals(item.CategoryId, categoryId, StringComparison.Ordinal) &&
                    ownedCollectionItemIds.Contains(item.ItemId)) count++;
            }
            return count;
        }

        public Dictionary<string, Vector2Int> GetCategoryCollectionProgress()
        {
            Dictionary<string, Vector2Int> result = new(StringComparer.Ordinal);
            if (catalog == null) return result;
            for (int i = 0; i < catalog.CollectionItems.Count; i++)
            {
                ShopCollectionItem item = catalog.CollectionItems[i];
                if (item == null) continue;
                result.TryGetValue(item.CategoryId, out Vector2Int value);
                value.y++;
                if (ownedCollectionItemIds.Contains(item.ItemId)) value.x++;
                result[item.CategoryId] = value;
            }
            return result;
        }

        public int GetGoalProgress(ShopProgressGoalSave goal)
        {
            if (goal == null) return 0;
            int current = GetConditionValue((ShopProgressConditionType)goal.conditionType, goal.categoryId);
            return Mathf.Clamp(current - goal.baseline, 0, goal.target);
        }

        public bool SaveNow()
        {
            ShopProgressionSaveData data = CreateSaveData();
            ShopLiveOperationsNetwork liveOperations = ShopLiveOperationsNetwork.Instance;
            if (liveOperations != null && liveOperations.IsSpawned && liveOperations.IsServer)
                liveOperations.WriteSave(data);
            Debug.Log("[Progression] SAVE_BEGIN path=" + ShopProgressionSaveStore.SavePath +
                      " liveServer=" + HasLiveServerGame() +
                      " items=" + data.containerItems.Count +
                      " upgrades=" + data.playerUpgradeLevel + "/" + data.operationsUpgradeLevel + "/" +
                      data.facilityUpgradeLevel + "/" + data.clawUpgradeLevel + "/" +
                      data.gachaUpgradeLevel + "/" + data.kujiUpgradeLevel, this);
            bool saved = ShopProgressionSaveStore.Save(data);
            if (saved)
            {
                dirty = false;
                loadedSaveData = data;
            }
            Debug.Log("[Progression] SAVE_END success=" + saved + " items=" + data.containerItems.Count, this);
            return saved;
        }

        public bool SetStoreNames(string playerName, string rivalName)
        {
            string normalizedPlayer = NormalizeStoreName(playerName, true);
            string normalizedRival = NormalizeStoreName(rivalName, false);
            if (string.IsNullOrWhiteSpace(normalizedPlayer) ||
                string.IsNullOrWhiteSpace(normalizedRival)) return false;

            playerShopName = normalizedPlayer;
            rivalShopName = normalizedRival;
            MarkChanged();
            StateChanged?.Invoke();
            return SaveNow();
        }

        public bool SaveNowWithFeedback()
        {
            bool saved = SaveNow();
            // SaveNow is synchronous. Queueing a separate "saving" toast before the result made the
            // first toast remain visible for a full notification cycle even though disk I/O had ended.
            NotificationRaised?.Invoke(saved
                ? "진행 상황을 저장했습니다."
                : "저장하지 못했습니다.");
            return saved;
        }

        public bool LoadNow()
        {
            if (!ShopProgressionSaveStore.TryLoad(out ShopProgressionSaveData save)) return false;
            loadedSaveData = save;
            ApplySaveData(save);
            loadedFromSave = true;
            ShopNetworkGame liveGame = ShopNetworkGame.Instance;
            if (liveGame != null && liveGame.IsSpawned && liveGame.IsServer)
            {
                boundGame = liveGame;
                bindAt = 0f;
            }
            ApplyStateToBoundGame();
            if (!TryRestoreContainersTo(boundGame, "explicit-load", true))
                Debug.LogError("[Progression] RESTORE_SESSION_FAILED gameReady=" +
                               (boundGame != null && boundGame.IsSpawned && boundGame.IsServer), this);
            EvaluateProgress();
            StateChanged?.Invoke();
            return true;
        }

        public void ResetProgressionForNewProfile(bool deleteSave)
        {
            if (deleteSave) ShopProgressionSaveStore.Delete();
            currentDay = 1;
            teamFunds = boundGame != null ? boundGame.Coins.Value : 0;
            playerShopName = ResolveStoreName(null, true);
            rivalShopName = ResolveStoreName(null, false);
            reputation = 0;
            lifetimeRevenue = 0;
            unitsSold = 0;
            rareItemsAcquired = 0;
            rareItemsSold = 0;
            onlineOrdersCompleted = 0;
            clawSuccesses = 0;
            currentStageIndex = 0;
            expansionLevel = catalog != null && catalog.ExpansionTiers.Count > 0
                ? catalog.ExpansionTiers[0].Level
                : 1;
            expansionVouchers = 0;
            randomBoxes = 0;
            dailyGoalCycle = 0;
            weeklyGoalCycle = 0;
            dailySetRewardClaimed = false;
            weeklySetRewardClaimed = false;
            tutorialStep = 0;
            tutorialCompleted = false;
            for (int i = 0; i < hotbarProductIds.Length; i++) hotbarProductIds[i] = -1;
            selectedHotbarSlot = -1;
            unlockedDistrictIds.Clear();
            ownedCollectionItemIds.Clear();
            grantedCollectionMilestones.Clear();
            grantedCollectionSets.Clear();
            completedCollectionCategories.Clear();
            dailyGoals.Clear();
            weeklyGoals.Clear();
            loadedFromSave = false;
            loadedSaveData = null;
            EnsureGoalCycles();
            EvaluateProgress();
            ApplyStateToBoundGame();
            MarkChanged();
            SaveNow();
        }

        private void BindOrMirrorGame()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsSpawned)
            {
                boundGame = null;
                containersRestoredGame = null;
                return;
            }
            if (boundGame != game)
            {
                boundGame = game;
                containersRestoredGame = null;
                bindAt = Time.unscaledTime + GameBindingDelay;
                return;
            }
            if (bindAt > 0f)
            {
                if (Time.unscaledTime < bindAt) return;
                bindAt = 0f;
                if (loadedFromSave || loadedSaveData != null)
                {
                    ApplyStateToBoundGame();
                    TryRestoreContainersTo(game, "delayed-bind", false);
                }
                else
                {
                    teamFunds = game.Coins.Value;
                    reputation = game.Reputation.Value;
                    currentDay = Mathf.Max(1, game.Day.Value);
                    observedGameFunds = teamFunds;
                    observedGameReputation = reputation;
                    observedGameDay = currentDay;
                    EnsureGoalCycles();
                    MarkChanged();
                }
                return;
            }

            if (game.Coins.Value != observedGameFunds)
            {
                teamFunds = Mathf.Max(0, teamFunds + game.Coins.Value - observedGameFunds);
                observedGameFunds = game.Coins.Value;
                MarkChanged();
            }
            if (game.Reputation.Value != observedGameReputation)
            {
                reputation = Mathf.Max(0, reputation + game.Reputation.Value - observedGameReputation);
                observedGameReputation = game.Reputation.Value;
                MarkChanged();
            }
            if (game.Day.Value != observedGameDay)
            {
                currentDay = Mathf.Max(1, game.Day.Value);
                observedGameDay = game.Day.Value;
                BeginGoalCyclesForDay(currentDay);
                MarkChanged();
            }
        }

        private void ApplyStateToBoundGame()
        {
            if (boundGame == null || !boundGame.IsServer) return;
            boundGame.Coins.Value = teamFunds;
            boundGame.Reputation.Value = reputation;
            boundGame.CampaignRevenue.Value = lifetimeRevenue;
            boundGame.CampaignSold.Value = unitsSold;
            boundGame.CampaignClawSuccesses.Value = clawSuccesses;
            boundGame.Day.Value = Mathf.Max(1, currentDay);
            if (loadedSaveData != null)
            {
                boundGame.ServerRestoreUpgradeState(loadedSaveData.playerUpgradeLevel,
                    loadedSaveData.operationsUpgradeLevel, loadedSaveData.facilityUpgradeLevel,
                    loadedSaveData.clawUpgradeLevel, loadedSaveData.gachaUpgradeLevel,
                    loadedSaveData.kujiUpgradeLevel, loadedSaveData.staffHiredMask,
                    loadedSaveData.staffAttendanceMask);
                boundGame.ServerRestoreStaffAssignments(loadedSaveData.staffAssignmentSlot2,
                    loadedSaveData.staffAssignmentSlot3);
                boundGame.UpcycleDecorMask.Value = Mathf.Max(0, loadedSaveData.upcycleDecorMask);
                boundGame.LastOneAwards.Value = Mathf.Max(0, loadedSaveData.lastOneAwards);
                boundGame.RecentLastOneRecords.Value = new Unity.Collections.FixedString512Bytes(
                    string.IsNullOrWhiteSpace(loadedSaveData.recentLastOneRecords)
                        ? "기록 없음" : loadedSaveData.recentLastOneRecords);
                boundGame.ReviewHistory.Value = new Unity.Collections.FixedString4096Bytes(
                    string.IsNullOrWhiteSpace(loadedSaveData.reviewHistory)
                        ? "아직 등록된 리뷰가 없습니다." : loadedSaveData.reviewHistory);
                boundGame.LatestReviewDay.Value = Mathf.Max(0, loadedSaveData.latestReviewDay);
                boundGame.AppraisalSequence.Value = Mathf.Max(0, loadedSaveData.appraisalSequence);
                RestoreSideContent(boundGame, loadedSaveData);
                RestoreConsignment(boundGame, loadedSaveData);
            }
            observedGameFunds = teamFunds;
            observedGameReputation = reputation;
            observedGameDay = boundGame.Day.Value;
        }

        private void MarkChanged()
        {
            dirty = true;
            nextAutosaveTime = Time.unscaledTime + 0.75f;
            EvaluateProgress();
            StateChanged?.Invoke();
        }

        private void EvaluateProgress()
        {
            if (evaluating || catalog == null) return;
            evaluating = true;
            try
            {
                EvaluateStages();
                EvaluateDistricts();
                EvaluateCollectionMilestones();
                EvaluateGoals(dailyGoals, false);
                EvaluateGoals(weeklyGoals, true);
            }
            finally
            {
                evaluating = false;
            }
        }

        private void EvaluateStages()
        {
            while (currentStageIndex + 1 < catalog.Stages.Count)
            {
                ShopProgressStage next = catalog.Stages[currentStageIndex + 1];
                if (!ShopProgressionRules.AreAllConditionsMet(next.Conditions, GetConditionValue)) break;
                currentStageIndex++;
                ApplyRewards(next.Rewards);
                StageAdvanced?.Invoke(next);
                RaiseNotification("등급 승급 · " + next.DisplayName);
            }
        }

        private void EvaluateDistricts()
        {
            for (int i = 0; i < catalog.DistrictUnlocks.Count; i++)
            {
                ShopDistrictUnlock district = catalog.DistrictUnlocks[i];
                if (district == null || district.Placeholder || reputation < district.RequiredReputation ||
                    !unlockedDistrictIds.Add(district.DistrictId)) continue;
                DistrictUnlocked?.Invoke(district);
                RaiseNotification("상권 개방 · " + district.DisplayName);
                ShopOpenWorldSceneDirector director = ShopOpenWorldSceneDirector.Instance;
                if (director != null && director.IsServer)
                    director.ServerEnsureDistrictLoaded(district.DistrictId);
            }
        }

        private void EvaluateCollectionMilestones()
        {
            List<ShopCollectionMilestone> reached = ShopProgressionRules.FindNewCollectionMilestones(
                CollectionPercent, catalog.CollectionMilestones, grantedCollectionMilestones);
            for (int i = 0; i < reached.Count; i++)
            {
                ShopCollectionMilestone milestone = reached[i];
                grantedCollectionMilestones.Add(milestone.Percent);
                reputation += milestone.ReputationReward;
                if (boundGame != null && boundGame.IsServer)
                {
                    boundGame.Reputation.Value = reputation;
                    observedGameReputation = reputation;
                }
                CollectionMilestoneReached?.Invoke(milestone);
                RaiseNotification("컬렉션 " + milestone.Percent + "% · 평판 +" +
                                  milestone.ReputationReward);
            }
        }

        private void EvaluateGoals(List<ShopProgressGoalSave> goals, bool weekly)
        {
            bool allComplete = goals.Count > 0;
            for (int i = 0; i < goals.Count; i++)
            {
                ShopProgressGoalSave goal = goals[i];
                if (!goal.completed && GetGoalProgress(goal) >= goal.target) goal.completed = true;
                if (!goal.completed) allComplete = false;
            }
            bool alreadyClaimed = weekly ? weeklySetRewardClaimed : dailySetRewardClaimed;
            if (!allComplete || alreadyClaimed) return;

            for (int i = 0; i < goals.Count; i++)
            {
                ShopGoalDefinition definition = FindGoalDefinition(goals[i].definitionId);
                if (definition != null) ApplyRewards(definition.Rewards);
            }
            if (weekly) weeklySetRewardClaimed = true;
            else dailySetRewardClaimed = true;
            RaiseNotification(weekly ? "주간 공동 목표 완료!" : "오늘의 공동 목표 완료!");
        }

        private void ApplyRewards(IReadOnlyList<ShopProgressReward> rewards)
        {
            if (rewards == null) return;
            for (int i = 0; i < rewards.Count; i++)
            {
                ShopProgressReward reward = rewards[i];
                if (reward == null) continue;
                switch (reward.Type)
                {
                    case ShopProgressRewardType.Reputation:
                        reputation += reward.Amount;
                        break;
                    case ShopProgressRewardType.TeamFunds:
                        teamFunds += reward.Amount;
                        break;
                    case ShopProgressRewardType.RandomBox:
                        randomBoxes += Mathf.Max(1, reward.Amount);
                        break;
                    case ShopProgressRewardType.UnlockDistrict:
                        if (!string.IsNullOrWhiteSpace(reward.TargetId))
                            unlockedDistrictIds.Add(reward.TargetId);
                        break;
                    case ShopProgressRewardType.ExpansionVoucher:
                        expansionVouchers += Mathf.Max(1, reward.Amount);
                        break;
                    case ShopProgressRewardType.RareItem:
                        rareItemsAcquired += Mathf.Max(1, reward.Amount);
                        break;
                }
            }
            ApplyStateToBoundGame();
        }

        private void EnsureGoalCycles()
        {
            int day = Mathf.Max(1, currentDay);
            if (dailyGoalCycle <= 0 || dailyGoals.Count == 0) GenerateGoals(false, day);
            int week = (day - 1) / 7 + 1;
            if (weeklyGoalCycle != week || weeklyGoals.Count == 0) GenerateGoals(true, week);
        }

        private void BeginGoalCyclesForDay(int day)
        {
            GenerateGoals(false, day);
            int week = (Mathf.Max(1, day) - 1) / 7 + 1;
            if (weeklyGoalCycle != week) GenerateGoals(true, week);
        }

        private void GenerateGoals(bool weekly, int cycle)
        {
            List<ShopGoalDefinition> pool = catalog.GoalPool
                .Where(definition => definition != null && definition.Weekly == weekly)
                .ToList();
            List<ShopProgressGoalSave> target = weekly ? weeklyGoals : dailyGoals;
            target.Clear();
            int count = Mathf.Min(weekly ? catalog.WeeklyGoalCount : catalog.DailyGoalCount, pool.Count);
            if (pool.Count > 0)
            {
                int start = Mathf.Abs(cycle * 31) % pool.Count;
                for (int i = 0; i < count; i++)
                {
                    ShopGoalDefinition definition = pool[(start + i) % pool.Count];
                    int targetValue = ShopProgressionRules.DeterministicGoalTarget(definition, cycle);
                    target.Add(new ShopProgressGoalSave
                    {
                        definitionId = definition.GoalId,
                        displayName = definition.DisplayName,
                        conditionType = (int)definition.ConditionType,
                        target = targetValue,
                        categoryId = definition.CategoryId,
                        baseline = GetConditionValue(definition.ConditionType, definition.CategoryId),
                        completed = false
                    });
                }
            }
            if (weekly)
            {
                weeklyGoalCycle = cycle;
                weeklySetRewardClaimed = false;
            }
            else
            {
                dailyGoalCycle = cycle;
                dailySetRewardClaimed = false;
            }
        }

        private ShopProgressionSaveData CreateSaveData()
        {
            bool liveServer = HasLiveServerGame();
            ShopProgressionSaveData previous = loadedSaveData;
            return new ShopProgressionSaveData
            {
                currentDay = currentDay,
                teamFunds = teamFunds,
                playerShopName = PlayerShopName,
                rivalShopName = RivalShopName,
                reputation = reputation,
                lifetimeRevenue = lifetimeRevenue,
                unitsSold = unitsSold,
                rareItemsAcquired = rareItemsAcquired,
                rareItemsSold = rareItemsSold,
                onlineOrdersCompleted = onlineOrdersCompleted,
                clawSuccesses = clawSuccesses,
                currentStageIndex = currentStageIndex,
                expansionLevel = expansionLevel,
                expansionVouchers = expansionVouchers,
                randomBoxes = randomBoxes,
                dailyGoalCycle = dailyGoalCycle,
                weeklyGoalCycle = weeklyGoalCycle,
                dailySetRewardClaimed = dailySetRewardClaimed,
                weeklySetRewardClaimed = weeklySetRewardClaimed,
                unlockedDistrictIds = unlockedDistrictIds.ToList(),
                ownedCollectionItemIds = ownedCollectionItemIds.ToList(),
                grantedCollectionMilestones = grantedCollectionMilestones.ToList(),
                grantedCollectionSets = grantedCollectionSets.ToList(),
                completedCollectionCategories = completedCollectionCategories.ToList(),
                dailyGoals = CloneGoals(dailyGoals),
                weeklyGoals = CloneGoals(weeklyGoals),
                containerItems = liveServer ? CaptureContainerItems() :
                    CloneContainerItems(previous != null ? previous.containerItems : pendingContainerItems),
                clawMachines = liveServer ? CaptureClawMachines() :
                    CloneClawMachines(previous != null ? previous.clawMachines : pendingClawMachines),
                kujiStations = liveServer ? CaptureKujiStations() :
                    CloneKujiStations(previous != null ? previous.kujiStations : pendingKujiStations),
                playerUpgradeLevel = liveServer ? boundGame.PlayerUpgradeLevel.Value : previous?.playerUpgradeLevel ?? 0,
                operationsUpgradeLevel = liveServer ? boundGame.ShopUpgradeLevel.Value : previous?.operationsUpgradeLevel ?? 0,
                facilityUpgradeLevel = liveServer ? boundGame.FacilityUpgradeLevel.Value : previous?.facilityUpgradeLevel ?? 0,
                clawUpgradeLevel = liveServer ? boundGame.ClawUpgradeLevel.Value : previous?.clawUpgradeLevel ?? 0,
                gachaUpgradeLevel = liveServer ? boundGame.GachaUpgradeLevel.Value : previous?.gachaUpgradeLevel ?? 0,
                kujiUpgradeLevel = liveServer ? boundGame.KujiUpgradeLevel.Value : previous?.kujiUpgradeLevel ?? 0,
                staffHiredMask = liveServer ? boundGame.StaffHiredMask.Value : previous?.staffHiredMask ?? 0,
                staffAttendanceMask = liveServer ? boundGame.StaffAttendanceMask.Value : previous?.staffAttendanceMask ?? 0,
                staffAssignmentSlot2 = liveServer ? boundGame.StaffAssignmentSlot2.Value : previous?.staffAssignmentSlot2 ?? 0,
                staffAssignmentSlot3 = liveServer ? boundGame.StaffAssignmentSlot3.Value : previous?.staffAssignmentSlot3 ?? 0,
                tutorialStep = tutorialStep,
                tutorialCompleted = tutorialCompleted,
                upcycleDecorMask = liveServer ? boundGame.UpcycleDecorMask.Value : previous?.upcycleDecorMask ?? 0,
                lastOneAwards = liveServer ? boundGame.LastOneAwards.Value : previous?.lastOneAwards ?? 0,
                recentLastOneRecords = liveServer ? boundGame.RecentLastOneRecords.Value.ToString() : previous?.recentLastOneRecords ?? string.Empty,
                reviewHistory = liveServer ? boundGame.ReviewHistory.Value.ToString() : previous?.reviewHistory ?? string.Empty,
                latestReviewDay = liveServer ? boundGame.LatestReviewDay.Value : previous?.latestReviewDay ?? 0,
                appraisalSequence = liveServer ? boundGame.AppraisalSequence.Value : previous?.appraisalSequence ?? 0,
                consignmentOfferCount = liveServer ? boundGame.ConsignmentOfferCount.Value : previous?.consignmentOfferCount ?? 0,
                consignmentOfferProduct0 = liveServer ? boundGame.ConsignmentOfferProduct0.Value : previous?.consignmentOfferProduct0 ?? -1,
                consignmentOfferProduct1 = liveServer ? boundGame.ConsignmentOfferProduct1.Value : previous?.consignmentOfferProduct1 ?? -1,
                consignmentOfferProduct2 = liveServer ? boundGame.ConsignmentOfferProduct2.Value : previous?.consignmentOfferProduct2 ?? -1,
                consignmentOfferPrice0 = liveServer ? boundGame.ConsignmentOfferPrice0.Value : previous?.consignmentOfferPrice0 ?? 0,
                consignmentOfferPrice1 = liveServer ? boundGame.ConsignmentOfferPrice1.Value : previous?.consignmentOfferPrice1 ?? 0,
                consignmentOfferPrice2 = liveServer ? boundGame.ConsignmentOfferPrice2.Value : previous?.consignmentOfferPrice2 ?? 0,
                consignmentSecondsRemaining = liveServer ? boundGame.ConsignmentSecondsRemaining.Value : previous?.consignmentSecondsRemaining ?? 0f,
                consignmentOfferSerial = liveServer ? boundGame.ConsignmentOfferSerial.Value : previous?.consignmentOfferSerial ?? 0,
                hotbarProduct0 = hotbarProductIds[0],
                hotbarProduct1 = hotbarProductIds[1],
                hotbarProduct2 = hotbarProductIds[2],
                hotbarProduct3 = hotbarProductIds[3],
                hotbarProduct4 = hotbarProductIds[4],
                selectedHotbarSlot = selectedHotbarSlot,
                sideContentDay = liveServer ? boundGame.SideContentDay.Value : previous?.sideContentDay ?? 0,
                trashIncomeToday = liveServer ? boundGame.TrashIncomeToday.Value : previous?.trashIncomeToday ?? 0
            };
        }

        private bool HasLiveServerGame() => boundGame != null && boundGame.IsSpawned && boundGame.IsServer;

        private static List<ShopContainerItemSave> CloneContainerItems(IEnumerable<ShopContainerItemSave> source)
        {
            List<ShopContainerItemSave> result = new();
            if (source == null) return result;
            foreach (ShopContainerItemSave item in source)
            {
                if (item == null) continue;
                result.Add(new ShopContainerItemSave
                {
                    ownerClientId = item.ownerClientId,
                    container = item.container,
                    slotIndex = item.slotIndex,
                    productId = item.productId,
                    visualPrefabIndex = item.visualPrefabIndex,
                    quantity = item.quantity,
                    maxStack = item.maxStack,
                    unitPrice = item.unitPrice,
                    rarity = item.rarity,
                    displayName = item.displayName,
                    instanceId = item.instanceId,
                    appraisalGrade = item.appraisalGrade
                });
            }
            return result;
        }

        private static List<ShopClawMachineSave> CloneClawMachines(IEnumerable<ShopClawMachineSave> source)
        {
            List<ShopClawMachineSave> result = new();
            if (source == null) return result;
            foreach (ShopClawMachineSave machine in source)
            {
                if (machine == null) continue;
                ShopClawMachineSave clone = new()
                {
                    machineId = machine.machineId,
                    remainingCapsules = machine.remainingCapsules,
                    prizes = new List<ShopClawPrizeSave>()
                };
                if (machine.prizes != null)
                    foreach (ShopClawPrizeSave prize in machine.prizes)
                        if (prize != null)
                            clone.prizes.Add(new ShopClawPrizeSave
                            {
                                productId = prize.productId,
                                rarity = prize.rarity,
                                visualPrefabIndex = prize.visualPrefabIndex,
                                localPosition = prize.localPosition,
                                localRotation = prize.localRotation
                            });
                result.Add(clone);
            }
            return result;
        }

        private static List<ShopKujiStationSave> CloneKujiStations(IEnumerable<ShopKujiStationSave> source)
        {
            List<ShopKujiStationSave> result = new();
            if (source == null) return result;
            foreach (ShopKujiStationSave station in source)
            {
                if (station == null) continue;
                result.Add(new ShopKujiStationSave
                {
                    poolId = station.poolId,
                    setNumber = station.setNumber,
                    stockS = station.stockS,
                    stockA = station.stockA,
                    stockB = station.stockB,
                    stockC = station.stockC,
                    stockD = station.stockD,
                    drawsSinceCeiling = station.drawsSinceCeiling,
                    lastPrizeAwarded = station.lastPrizeAwarded,
                    refilling = station.refilling,
                    refillSecondsRemaining = station.refillSecondsRemaining
                });
            }
            return result;
        }

        private void ApplySaveData(ShopProgressionSaveData save)
        {
            currentDay = Mathf.Max(1, save.currentDay);
            teamFunds = Mathf.Max(0, save.teamFunds);
            reputation = Mathf.Max(0, save.reputation);
            playerShopName = NormalizeStoreName(save.playerShopName, true);
            rivalShopName = NormalizeStoreName(save.rivalShopName, false);
            ShopStoreNamingSystem.Instance.RestoreSavedNames(playerShopName, rivalShopName);
            lifetimeRevenue = Mathf.Max(0, save.lifetimeRevenue);
            unitsSold = Mathf.Max(0, save.unitsSold);
            rareItemsAcquired = Mathf.Max(0, save.rareItemsAcquired);
            rareItemsSold = Mathf.Max(0, save.rareItemsSold);
            onlineOrdersCompleted = Mathf.Max(0, save.onlineOrdersCompleted);
            clawSuccesses = Mathf.Max(0, save.clawSuccesses);
            currentStageIndex = catalog != null
                ? Mathf.Clamp(save.currentStageIndex, 0, Mathf.Max(0, catalog.Stages.Count - 1))
                : 0;
            expansionLevel = Mathf.Clamp(save.expansionLevel, 1, 6);
            expansionVouchers = Mathf.Max(0, save.expansionVouchers);
            randomBoxes = Mathf.Max(0, save.randomBoxes);
            dailyGoalCycle = Mathf.Max(0, save.dailyGoalCycle);
            weeklyGoalCycle = Mathf.Max(0, save.weeklyGoalCycle);
            dailySetRewardClaimed = save.dailySetRewardClaimed;
            weeklySetRewardClaimed = save.weeklySetRewardClaimed;
            ReplaceSet(unlockedDistrictIds, save.unlockedDistrictIds);
            ReplaceSet(ownedCollectionItemIds, save.ownedCollectionItemIds);
            ReplaceSet(grantedCollectionSets, save.grantedCollectionSets);
            ReplaceSet(completedCollectionCategories, save.completedCollectionCategories);
            grantedCollectionMilestones.Clear();
            if (save.grantedCollectionMilestones != null)
                for (int i = 0; i < save.grantedCollectionMilestones.Count; i++)
                    grantedCollectionMilestones.Add(save.grantedCollectionMilestones[i]);
            ReplaceGoals(dailyGoals, save.dailyGoals);
            ReplaceGoals(weeklyGoals, save.weeklyGoals);
            pendingContainerItems.Clear();
            if (save.containerItems != null) pendingContainerItems.AddRange(save.containerItems);
            pendingClawMachines.Clear();
            if (save.clawMachines != null) pendingClawMachines.AddRange(save.clawMachines);
            pendingKujiStations.Clear();
            if (save.kujiStations != null) pendingKujiStations.AddRange(save.kujiStations);
            tutorialStep = Mathf.Clamp(save.tutorialStep, 0, ShopTutorialRuntime.StepCount);
            tutorialCompleted = save.tutorialCompleted;
            hotbarProductIds[0] = save.hotbarProduct0;
            hotbarProductIds[1] = save.hotbarProduct1;
            hotbarProductIds[2] = save.hotbarProduct2;
            hotbarProductIds[3] = save.hotbarProduct3;
            hotbarProductIds[4] = save.hotbarProduct4;
            selectedHotbarSlot = Mathf.Clamp(save.selectedHotbarSlot, -1, hotbarProductIds.Length - 1);
            if (boundGame != null && boundGame.IsServer)
            {
                boundGame.UpcycleDecorMask.Value = Mathf.Max(0, save.upcycleDecorMask);
                boundGame.LastOneAwards.Value = Mathf.Max(0, save.lastOneAwards);
                boundGame.RecentLastOneRecords.Value = new Unity.Collections.FixedString512Bytes(
                    string.IsNullOrWhiteSpace(save.recentLastOneRecords) ? "기록 없음" : save.recentLastOneRecords);
                boundGame.ReviewHistory.Value = new Unity.Collections.FixedString4096Bytes(
                    string.IsNullOrWhiteSpace(save.reviewHistory)
                        ? "아직 등록된 리뷰가 없습니다." : save.reviewHistory);
                boundGame.LatestReviewDay.Value = Mathf.Max(0, save.latestReviewDay);
                boundGame.AppraisalSequence.Value = Mathf.Max(0, save.appraisalSequence);
                RestoreSideContent(boundGame, save);
                RestoreConsignment(boundGame, save);
            }
        }

        private static string NormalizeStoreName(string value, bool player)
        {
            ShopStoreNamingConfig namingConfig = ShopStoreNamingConfig.Load();
            string fallback = player
                ? namingConfig != null ? namingConfig.DefaultPlayerShopName : "PickAndPlace"
                : namingConfig != null ? namingConfig.DefaultRivalShopName : "Rival Shop";
            string normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            int maximum = namingConfig != null ? namingConfig.MaximumNameLength : 10;
            return normalized.Length <= maximum ? normalized : normalized.Substring(0, maximum);
        }

        private static string ResolveStoreName(string value, bool player) =>
            NormalizeStoreName(value, player);

        private static void RestoreConsignment(ShopNetworkGame game, ShopProgressionSaveData save)
        {
            if (game == null || save == null) return;
            game.ConsignmentOfferCount.Value = Mathf.Clamp(save.consignmentOfferCount, 0, 3);
            game.ConsignmentOfferProduct0.Value = save.consignmentOfferProduct0;
            game.ConsignmentOfferProduct1.Value = save.consignmentOfferProduct1;
            game.ConsignmentOfferProduct2.Value = save.consignmentOfferProduct2;
            game.ConsignmentOfferPrice0.Value = Mathf.Max(0, save.consignmentOfferPrice0);
            game.ConsignmentOfferPrice1.Value = Mathf.Max(0, save.consignmentOfferPrice1);
            game.ConsignmentOfferPrice2.Value = Mathf.Max(0, save.consignmentOfferPrice2);
            game.ConsignmentSecondsRemaining.Value = Mathf.Max(0f, save.consignmentSecondsRemaining);
            game.ConsignmentOfferSerial.Value = Mathf.Max(0, save.consignmentOfferSerial);
        }

        private static void RestoreSideContent(ShopNetworkGame game, ShopProgressionSaveData save)
        {
            if (game == null || save == null || !game.IsServer) return;
            int savedDay = Mathf.Max(0, save.sideContentDay);
            game.SideContentDay.Value = savedDay;
            game.TrashIncomeToday.Value = savedDay == game.Day.Value
                ? Mathf.Clamp(save.trashIncomeToday, 0,
                    game.SideContentConfig != null ? game.SideContentConfig.TrashDailyCap : 500)
                : 0;
            game.ServerEnsureSideContentDay();
        }

        public bool TryConsumeClawMachineSave(int machineId, out ShopClawMachineSave saved)
        {
            for (int i = 0; i < pendingClawMachines.Count; i++)
            {
                if (pendingClawMachines[i] == null || pendingClawMachines[i].machineId != machineId) continue;
                saved = pendingClawMachines[i];
                pendingClawMachines.RemoveAt(i);
                return true;
            }
            saved = null;
            return false;
        }

        public bool TryConsumeKujiStationSave(string poolId, out ShopKujiStationSave saved)
        {
            for (int i = 0; i < pendingKujiStations.Count; i++)
            {
                if (pendingKujiStations[i] == null || pendingKujiStations[i].poolId != poolId) continue;
                saved = pendingKujiStations[i];
                pendingKujiStations.RemoveAt(i);
                return true;
            }
            saved = null;
            return false;
        }

        public void RestoreContainersTo(ShopNetworkGame game)
        {
            TryRestoreContainersTo(game, "network-spawn", false);
        }

        private bool TryRestoreContainersTo(ShopNetworkGame game, string reason, bool force)
        {
            if (game == null || !game.IsSpawned || !game.IsServer)
            {
                Debug.LogWarning("[Containers] RESTORE_DEFERRED reason=" + reason +
                                 " game=" + (game != null) +
                                 " spawned=" + (game != null && game.IsSpawned) +
                                 " server=" + (game != null && game.IsServer), this);
                return false;
            }
            if (!force && containersRestoredGame == game) return true;

            ulong localOwner = game.NetworkManager != null ? game.NetworkManager.LocalClientId : 0;
            int expectedEntries = 0;
            int expectedQuantity = 0;
            game.ItemContainers.Clear();
            foreach (ShopContainerItemSave saved in pendingContainerItems)
            {
                if (saved == null || saved.quantity <= 0) continue;
                ShopContainerKind container = (ShopContainerKind)Mathf.Clamp(saved.container, 0, 5);
                ulong owner = container == ShopContainerKind.PersonalInventory
                    ? localOwner
                    : ShopContainerRules.SharedOwner;
                game.ItemContainers.Add(new ShopContainerItem
                {
                    OwnerClientId = owner,
                    Container = container,
                    SlotIndex = Mathf.Max(0, saved.slotIndex),
                    ProductId = saved.productId,
                    VisualPrefabIndex = saved.visualPrefabIndex,
                    Quantity = Mathf.Max(1, saved.quantity),
                    MaxStack = Mathf.Max(1, saved.maxStack),
                    UnitPrice = Mathf.Max(0, saved.unitPrice),
                    Rarity = (ShopProductRarity)Mathf.Clamp(saved.rarity, 0, 3),
                    DisplayName = new Unity.Collections.FixedString64Bytes(
                        string.IsNullOrWhiteSpace(saved.displayName) ? "상품" : saved.displayName),
                    InstanceId = saved.instanceId,
                    AppraisalGrade = (ShopAppraisalGrade)Mathf.Clamp(saved.appraisalGrade, 0, 4)
                });
                expectedEntries++;
                expectedQuantity += Mathf.Max(1, saved.quantity);
            }
            game.SyncLegacyContainerCounts();
            containersRestoredGame = game;

            int restoredQuantity = 0;
            for (int i = 0; i < game.ItemContainers.Count; i++)
                restoredQuantity += Mathf.Max(0, game.ItemContainers[i].Quantity);
            if (game.ItemContainers.Count != expectedEntries || restoredQuantity != expectedQuantity)
            {
                Debug.LogError("[Containers] RESTORE_FAILED reason=" + reason +
                               " expectedEntries=" + expectedEntries +
                               " actualEntries=" + game.ItemContainers.Count +
                               " expectedQuantity=" + expectedQuantity +
                               " actualQuantity=" + restoredQuantity, game);
                return false;
            }

            Debug.Log("[Containers] RESTORE_COMPLETE reason=" + reason +
                      " entries=" + game.ItemContainers.Count +
                      " quantity=" + restoredQuantity +
                      " localOwner=" + localOwner, game);
            return true;
        }

        private static List<ShopContainerItemSave> CaptureContainerItems()
        {
            List<ShopContainerItemSave> result = new();
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsServer) return result;
            for (int i = 0; i < game.ItemContainers.Count; i++)
            {
                ShopContainerItem item = game.ItemContainers[i];
                result.Add(new ShopContainerItemSave
                {
                    ownerClientId = item.OwnerClientId,
                    container = (int)item.Container,
                    slotIndex = item.SlotIndex,
                    productId = item.ProductId,
                    visualPrefabIndex = item.VisualPrefabIndex,
                    quantity = item.Quantity,
                    maxStack = item.MaxStack,
                    unitPrice = item.UnitPrice,
                    rarity = (int)item.Rarity,
                    displayName = item.DisplayName.ToString(),
                    instanceId = item.InstanceId,
                    appraisalGrade = (int)item.AppraisalGrade
                });
            }
            return result;
        }

        private static List<ShopClawMachineSave> CaptureClawMachines()
        {
            var result = new List<ShopClawMachineSave>();
            foreach (ShopClawMachineNetwork machine in
                     FindObjectsByType<ShopClawMachineNetwork>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (machine == null || !machine.IsSpawned || !machine.IsServer) continue;
                result.Add(machine.CaptureSaveState());
            }
            return result;
        }

        private static List<ShopKujiStationSave> CaptureKujiStations()
        {
            var result = new List<ShopKujiStationSave>();
            foreach (ShopKujiStationNetwork station in
                     FindObjectsByType<ShopKujiStationNetwork>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (station == null || !station.IsSpawned || !station.IsServer) continue;
                result.Add(station.CaptureSaveState());
            }
            return result;
        }

        private static void ReplaceSet(HashSet<string> target, List<string> source)
        {
            target.Clear();
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
                if (!string.IsNullOrWhiteSpace(source[i])) target.Add(source[i]);
        }

        private static void ReplaceGoals(List<ShopProgressGoalSave> target,
            List<ShopProgressGoalSave> source)
        {
            target.Clear();
            if (source == null) return;
            target.AddRange(CloneGoals(source));
        }

        private static List<ShopProgressGoalSave> CloneGoals(IReadOnlyList<ShopProgressGoalSave> source)
        {
            List<ShopProgressGoalSave> result = new();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                ShopProgressGoalSave goal = source[i];
                if (goal == null) continue;
                result.Add(new ShopProgressGoalSave
                {
                    definitionId = goal.definitionId,
                    displayName = goal.displayName,
                    conditionType = goal.conditionType,
                    target = goal.target,
                    categoryId = goal.categoryId,
                    baseline = goal.baseline,
                    completed = goal.completed
                });
            }
            return result;
        }

        private ShopExpansionTier FindExpansionTier(int level)
        {
            if (catalog == null) return null;
            for (int i = 0; i < catalog.ExpansionTiers.Count; i++)
                if (catalog.ExpansionTiers[i] != null && catalog.ExpansionTiers[i].Level == level)
                    return catalog.ExpansionTiers[i];
            return null;
        }

        private ShopGoalDefinition FindGoalDefinition(string goalId)
        {
            if (catalog == null) return null;
            for (int i = 0; i < catalog.GoalPool.Count; i++)
                if (catalog.GoalPool[i] != null &&
                    string.Equals(catalog.GoalPool[i].GoalId, goalId, StringComparison.Ordinal))
                    return catalog.GoalPool[i];
            return null;
        }

        private string ResolveCollectionItemId(string requestedId, string displayName, string categoryId)
        {
            if (catalog == null) return string.Empty;
            for (int i = 0; i < catalog.CollectionItems.Count; i++)
            {
                ShopCollectionItem item = catalog.CollectionItems[i];
                if (item == null) continue;
                if (!string.IsNullOrWhiteSpace(requestedId) &&
                    string.Equals(item.ItemId, requestedId, StringComparison.Ordinal)) return item.ItemId;
            }
            for (int i = 0; i < catalog.CollectionItems.Count; i++)
            {
                ShopCollectionItem item = catalog.CollectionItems[i];
                if (item != null && string.Equals(item.DisplayName, displayName, StringComparison.Ordinal))
                    return item.ItemId;
            }
            for (int i = 0; i < catalog.CollectionItems.Count; i++)
            {
                ShopCollectionItem item = catalog.CollectionItems[i];
                if (item != null && string.Equals(item.CategoryId, categoryId, StringComparison.Ordinal) &&
                    !ownedCollectionItemIds.Contains(item.ItemId)) return item.ItemId;
            }
            return string.Empty;
        }

        private void RaiseNotification(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            NotificationRaised?.Invoke(message);
            if (boundGame != null && boundGame.IsServer) boundGame.ServerSetEvent(message);
        }
    }
}
