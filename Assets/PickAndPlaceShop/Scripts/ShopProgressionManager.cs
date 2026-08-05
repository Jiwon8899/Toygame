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
        private readonly HashSet<string> regularCustomerIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> unlockedDistrictIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> ownedCollectionItemIds = new(StringComparer.Ordinal);
        private readonly HashSet<int> grantedCollectionMilestones = new();
        private readonly List<ShopProgressGoalSave> dailyGoals = new();
        private readonly List<ShopProgressGoalSave> weeklyGoals = new();
        private readonly List<ShopContainerItemSave> pendingContainerItems = new();
        private readonly List<ShopClawMachineSave> pendingClawMachines = new();
        private readonly List<ShopKujiStationSave> pendingKujiStations = new();

        private int currentDay = 1;
        private int teamFunds;
        private int reputation;
        private int lifetimeRevenue;
        private int unitsSold;
        private int rareItemsAcquired;
        private int rareItemsSold;
        private int satisfactionTotal;
        private int satisfactionSamples;
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
        private float bindAt;
        private int observedGameFunds;
        private int observedGameReputation;
        private int observedGameDay;
        private int tutorialStep;
        private bool tutorialCompleted;

        public ShopProgressionCatalog Catalog => catalog;
        public int CurrentDay => currentDay;
        public int TeamFunds => teamFunds;
        public int Reputation => reputation;
        public int LifetimeRevenue => lifetimeRevenue;
        public int UnitsSold => unitsSold;
        public int RareItemsAcquired => rareItemsAcquired;
        public int RareItemsSold => rareItemsSold;
        public int RegularCustomerCount => regularCustomerIds.Count;
        public int AverageSatisfaction => satisfactionSamples <= 0
            ? 0
            : Mathf.RoundToInt(satisfactionTotal / (float)satisfactionSamples);
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
                ShopProgressConditionType.RegularCustomers => regularCustomerIds.Count,
                ShopProgressConditionType.AverageSatisfaction => AverageSatisfaction,
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
            teamFunds = Mathf.Max(0, teamFunds + amount);
            if (boundGame != null && boundGame.IsServer)
            {
                boundGame.Coins.Value = teamFunds;
                observedGameFunds = teamFunds;
            }
            MarkChanged();
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

        public void RecordSale(string itemId, string displayName, string categoryId,
            int revenue, bool rare, int satisfaction)
        {
            lifetimeRevenue += Mathf.Max(0, revenue);
            unitsSold++;
            if (rare) rareItemsSold++;
            if (satisfaction >= 0)
            {
                satisfactionTotal += Mathf.Clamp(satisfaction, 0, 100);
                satisfactionSamples++;
            }
            MarkChanged();
            ShopTutorialRuntime.Report(ShopTutorialAction.ProductSold);
        }

        public void RecordAcquisition(string itemId, string displayName, string categoryId,
            bool rare, int amount = 1)
        {
            int positiveAmount = Mathf.Max(1, amount);
            if (rare) rareItemsAcquired += positiveAmount;
            string registeredId = ResolveCollectionItemId(itemId, displayName, categoryId);
            if (!string.IsNullOrWhiteSpace(registeredId)) ownedCollectionItemIds.Add(registeredId);
            MarkChanged();
        }

        public void RecordRegularCustomer(string customerId)
        {
            if (string.IsNullOrWhiteSpace(customerId) || !regularCustomerIds.Add(customerId)) return;
            MarkChanged();
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
            ShopLiveOperationsNetwork.Instance?.WriteSave(data);
            bool saved = ShopProgressionSaveStore.Save(data);
            if (saved)
            {
                dirty = false;
                loadedSaveData = data;
            }
            return saved;
        }

        public bool SaveNowWithFeedback()
        {
            NotificationRaised?.Invoke("저장 중...");
            bool saved = SaveNow();
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
            ApplyStateToBoundGame();
            RestoreContainersTo(boundGame);
            EvaluateProgress();
            StateChanged?.Invoke();
            return true;
        }

        public void ResetProgressionForNewProfile(bool deleteSave)
        {
            if (deleteSave) ShopProgressionSaveStore.Delete();
            currentDay = 1;
            teamFunds = boundGame != null ? boundGame.Coins.Value : 0;
            reputation = 0;
            lifetimeRevenue = 0;
            unitsSold = 0;
            rareItemsAcquired = 0;
            rareItemsSold = 0;
            satisfactionTotal = 0;
            satisfactionSamples = 0;
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
            regularCustomerIds.Clear();
            unlockedDistrictIds.Clear();
            ownedCollectionItemIds.Clear();
            grantedCollectionMilestones.Clear();
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
                return;
            }
            if (boundGame != game)
            {
                boundGame = game;
                bindAt = Time.unscaledTime + GameBindingDelay;
                return;
            }
            if (bindAt > 0f)
            {
                if (Time.unscaledTime < bindAt) return;
                bindAt = 0f;
                if (loadedFromSave || loadedSaveData != null) ApplyStateToBoundGame();
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
                boundGame.UpcycleDecorMask.Value = Mathf.Max(0, loadedSaveData.upcycleDecorMask);
                boundGame.LastOneAwards.Value = Mathf.Max(0, loadedSaveData.lastOneAwards);
                boundGame.RecentLastOneRecords.Value = new Unity.Collections.FixedString512Bytes(
                    string.IsNullOrWhiteSpace(loadedSaveData.recentLastOneRecords)
                        ? "기록 없음" : loadedSaveData.recentLastOneRecords);
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
                        baseline = definition.ConditionType == ShopProgressConditionType.AverageSatisfaction
                            ? 0
                            : GetConditionValue(definition.ConditionType, definition.CategoryId),
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
            return new ShopProgressionSaveData
            {
                currentDay = currentDay,
                teamFunds = teamFunds,
                reputation = reputation,
                lifetimeRevenue = lifetimeRevenue,
                unitsSold = unitsSold,
                rareItemsAcquired = rareItemsAcquired,
                rareItemsSold = rareItemsSold,
                satisfactionTotal = satisfactionTotal,
                satisfactionSamples = satisfactionSamples,
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
                regularCustomerIds = regularCustomerIds.ToList(),
                unlockedDistrictIds = unlockedDistrictIds.ToList(),
                ownedCollectionItemIds = ownedCollectionItemIds.ToList(),
                grantedCollectionMilestones = grantedCollectionMilestones.ToList(),
                dailyGoals = CloneGoals(dailyGoals),
                weeklyGoals = CloneGoals(weeklyGoals),
                containerItems = CaptureContainerItems(),
                clawMachines = CaptureClawMachines(),
                kujiStations = CaptureKujiStations(),
                playerUpgradeLevel = boundGame != null ? boundGame.PlayerUpgradeLevel.Value : 0,
                operationsUpgradeLevel = boundGame != null ? boundGame.ShopUpgradeLevel.Value : 0,
                facilityUpgradeLevel = boundGame != null ? boundGame.FacilityUpgradeLevel.Value : 0,
                clawUpgradeLevel = boundGame != null ? boundGame.ClawUpgradeLevel.Value : 0,
                gachaUpgradeLevel = boundGame != null ? boundGame.GachaUpgradeLevel.Value : 0,
                kujiUpgradeLevel = boundGame != null ? boundGame.KujiUpgradeLevel.Value : 0,
                staffHiredMask = boundGame != null ? boundGame.StaffHiredMask.Value : 0,
                staffAttendanceMask = boundGame != null ? boundGame.StaffAttendanceMask.Value : 0,
                tutorialStep = tutorialStep,
                tutorialCompleted = tutorialCompleted,
                upcycleDecorMask = boundGame != null ? boundGame.UpcycleDecorMask.Value : 0,
                lastOneAwards = boundGame != null ? boundGame.LastOneAwards.Value : 0,
                recentLastOneRecords = boundGame != null ? boundGame.RecentLastOneRecords.Value.ToString() : string.Empty
            };
        }

        private void ApplySaveData(ShopProgressionSaveData save)
        {
            currentDay = Mathf.Max(1, save.currentDay);
            teamFunds = Mathf.Max(0, save.teamFunds);
            reputation = Mathf.Max(0, save.reputation);
            lifetimeRevenue = Mathf.Max(0, save.lifetimeRevenue);
            unitsSold = Mathf.Max(0, save.unitsSold);
            rareItemsAcquired = Mathf.Max(0, save.rareItemsAcquired);
            rareItemsSold = Mathf.Max(0, save.rareItemsSold);
            satisfactionTotal = Mathf.Max(0, save.satisfactionTotal);
            satisfactionSamples = Mathf.Max(0, save.satisfactionSamples);
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
            ReplaceSet(regularCustomerIds, save.regularCustomerIds);
            ReplaceSet(unlockedDistrictIds, save.unlockedDistrictIds);
            ReplaceSet(ownedCollectionItemIds, save.ownedCollectionItemIds);
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
            if (boundGame != null && boundGame.IsServer)
            {
                boundGame.UpcycleDecorMask.Value = Mathf.Max(0, save.upcycleDecorMask);
                boundGame.LastOneAwards.Value = Mathf.Max(0, save.lastOneAwards);
                boundGame.RecentLastOneRecords.Value = new Unity.Collections.FixedString512Bytes(
                    string.IsNullOrWhiteSpace(save.recentLastOneRecords) ? "기록 없음" : save.recentLastOneRecords);
            }
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
            if (game == null || !game.IsServer) return;
            game.ItemContainers.Clear();
            foreach (ShopContainerItemSave saved in pendingContainerItems)
            {
                if (saved == null || saved.quantity <= 0) continue;
                game.ItemContainers.Add(new ShopContainerItem
                {
                    OwnerClientId = saved.ownerClientId,
                    Container = (ShopContainerKind)Mathf.Clamp(saved.container, 0, 5),
                    SlotIndex = Mathf.Max(0, saved.slotIndex),
                    ProductId = saved.productId,
                    VisualPrefabIndex = saved.visualPrefabIndex,
                    Quantity = Mathf.Max(1, saved.quantity),
                    MaxStack = Mathf.Max(1, saved.maxStack),
                    UnitPrice = Mathf.Max(0, saved.unitPrice),
                    Rarity = (ShopProductRarity)Mathf.Clamp(saved.rarity, 0, 3),
                    DisplayName = new Unity.Collections.FixedString64Bytes(
                        string.IsNullOrWhiteSpace(saved.displayName) ? "상품" : saved.displayName)
                });
            }
            game.SyncLegacyContainerCounts();
            Debug.Log("[Containers] SAVE_RESTORED count=" + game.ItemContainers.Count, game);
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
                    displayName = item.DisplayName.ToString()
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
