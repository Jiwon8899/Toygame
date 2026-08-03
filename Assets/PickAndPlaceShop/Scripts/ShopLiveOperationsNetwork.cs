using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    [Serializable]
    public sealed class ShopCustomerProfileSave
    {
        public string customerId;
        public int preferredCategory;
        public int purchaseCount;
        public int lastSatisfaction = 70;
        public bool regular;
    }

    [Serializable]
    public sealed class ShopOnlineOrderSave
    {
        public int orderId;
        public int productId;
        public string productName;
        public int quantity;
        public int reward;
        public int deadlineDay;
        public bool active = true;
    }

    [Serializable]
    public sealed class ShopAutomationMachineSave
    {
        public int machineId;
        public bool installed;
        public bool enabled;
        public float elapsedSeconds;
        public int todayAcquired;
        public int todayCost;
    }

    public struct ShopOnlineOrderState : INetworkSerializable, IEquatable<ShopOnlineOrderState>
    {
        public int OrderId;
        public int ProductId;
        public FixedString64Bytes ProductName;
        public int Quantity;
        public int Reward;
        public int DeadlineDay;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref OrderId);
            serializer.SerializeValue(ref ProductId);
            serializer.SerializeValue(ref ProductName);
            serializer.SerializeValue(ref Quantity);
            serializer.SerializeValue(ref Reward);
            serializer.SerializeValue(ref DeadlineDay);
        }

        public bool Equals(ShopOnlineOrderState other) =>
            OrderId == other.OrderId && ProductId == other.ProductId &&
            Quantity == other.Quantity && Reward == other.Reward &&
            DeadlineDay == other.DeadlineDay && ProductName.Equals(other.ProductName);
    }

    public readonly struct ShopCustomerProfileSelection
    {
        public readonly string CustomerId;
        public readonly ShopProductCategory PreferredCategory;
        public readonly int PurchaseCount;
        public readonly int LastSatisfaction;

        public ShopCustomerProfileSelection(string id, ShopProductCategory category,
            int purchases, int satisfaction)
        {
            CustomerId = id;
            PreferredCategory = category;
            PurchaseCount = purchases;
            LastSatisfaction = satisfaction;
        }
    }

    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShopLiveOperationsNetwork : NetworkBehaviour
    {
        public static ShopLiveOperationsNetwork Instance { get; private set; }

        [SerializeField] private ShopOperationsConfig config;

        public NetworkVariable<int> PhaseSecondsRemaining = new(0);
        public NetworkVariable<int> TrendCategory = new((int)ShopProductCategory.CatPlush);
        public NetworkVariable<int> DailySalesGoal = new(5);
        public NetworkVariable<int> DailySalesProgress = new(0);
        public NetworkVariable<int> AutomatedAcquiredToday = new(0);
        public NetworkVariable<int> AutomatedCostToday = new(0);
        public NetworkVariable<FixedString128Bytes> DayAnnouncement =
            new(new FixedString128Bytes("오늘의 운영 정보를 준비 중입니다."));
        public NetworkVariable<FixedString128Bytes> DaySummary =
            new(new FixedString128Bytes(string.Empty));
        public NetworkList<ShopOnlineOrderState> OnlineOrders = new();

        private readonly List<ShopCustomerProfileSave> customerProfiles = new();
        private readonly List<ShopAutomationMachineSave> pendingAutomation = new();
        private float phaseRemaining;
        private int observedDay;
        private ShopPhase observedPhase;
        private int nextOrderId = 1;
        private bool openCloseRequested;

        public ShopOperationsConfig Config => config != null ? config : config = ShopOperationsConfig.Load();
        public ShopProductCategory CurrentTrendCategory =>
            ShopProductLocalization.IsCatTheme((ShopProductCategory)TrendCategory.Value)
                ? (ShopProductCategory)TrendCategory.Value
                : ShopProductCategory.CatPlush;
        public int RegularCustomerCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < customerProfiles.Count; i++)
                    if (customerProfiles[i].regular) count++;
                return count;
            }
        }

        private void Awake()
        {
            Instance = this;
            if (config == null) config = ShopOperationsConfig.Load();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;
            if (NetworkManager.NetworkConfig.NetworkTopology == NetworkTopologyTypes.DistributedAuthority)
                NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.SessionOwner, true);

            ShopProgressionSaveData save = ShopProgressionManager.Instance?.GetLoadedSaveData();
            if (save != null) Restore(save);
            EnsureCustomerPool();
            ShopNetworkGame game = ShopNetworkGame.Instance;
            observedDay = game != null ? game.Day.Value : 1;
            observedPhase = game != null ? game.Phase.Value : ShopPhase.Setup;

            if (save == null || save.livePhaseSecondsRemaining <= 0f)
                ServerBeginPreparation(true);
            else
                SyncRemaining();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || Config == null) return;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || game.Phase.Value == ShopPhase.Complete) return;

            DailySalesProgress.Value = game.SoldToday.Value;
            if (game.Day.Value != observedDay)
            {
                observedDay = game.Day.Value;
                ServerBeginPreparation(false);
                return;
            }

            if (game.Phase.Value != observedPhase)
            {
                observedPhase = game.Phase.Value;
                phaseRemaining = DurationFor(observedPhase);
                openCloseRequested = false;
                SyncRemaining();
            }

            if (game.Phase.Value == ShopPhase.PrizeHunt)
            {
                ServerBeginPreparation(false);
                return;
            }

            if (game.Phase.Value != ShopPhase.Setup && game.Phase.Value != ShopPhase.Open &&
                game.Phase.Value != ShopPhase.Summary) return;

            phaseRemaining = Mathf.Max(0f, phaseRemaining - Time.unscaledDeltaTime);
            SyncRemaining();
            if (phaseRemaining > 0f) return;

            switch (game.Phase.Value)
            {
                case ShopPhase.Setup:
                    game.ServerSetPhase(ShopPhase.Open);
                    game.ServerSetEvent("영업을 시작합니다. 진열대가 비어 있으면 손님은 입장하지 않습니다.");
                    break;
                case ShopPhase.Open:
                    if (!openCloseRequested)
                    {
                        openCloseRequested = true;
                        ShopNightSalesSystem.Instance?.ServerRequestClose();
                        if (ShopNightSalesSystem.Instance == null)
                            game.ServerSetPhase(ShopPhase.Summary);
                    }
                    break;
                case ShopPhase.Summary:
                    DaySummary.Value = new FixedString128Bytes(BuildSummary(game));
                    game.ServerFinishDayFromTimer();
                    break;
            }
        }

        private void ServerBeginPreparation(bool firstLoad)
        {
            if (!IsServer || Config == null) return;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null) return;
            observedPhase = ShopPhase.Setup;
            game.ServerSetPhase(ShopPhase.Setup);
            phaseRemaining = Config.PreparationSeconds;
            openCloseRequested = false;
            AutomatedAcquiredToday.Value = 0;
            AutomatedCostToday.Value = 0;
            DailySalesProgress.Value = 0;
            DailySalesGoal.Value = Config.SalesGoalForStage(
                ShopProgressionManager.Instance != null
                    ? ShopProgressionManager.Instance.CurrentStageIndex
                    : 0);
            PickTrend(game.Day.Value);
            ExpireOrders(game.Day.Value);
            FillOrderBoard(game.Day.Value);
            string announcement = "준비 2분 시작 · 오늘의 유행: " +
                                  ShopProductLocalization.CategoryLabel(CurrentTrendCategory) +
                                  " (+" + Mathf.RoundToInt(Config.TrendPriceBonus * 100f) + "%) · 판매 목표 " +
                                  DailySalesGoal.Value + "개";
            DayAnnouncement.Value = new FixedString128Bytes(announcement);
            game.ServerSetEvent(announcement);
            SyncRemaining();
            if (!firstLoad) ShopProgressionManager.Instance?.SaveNow();
        }

        private void PickTrend(int day)
        {
            ShopProductCategory[] categories =
            {
                ShopProductCategory.CatPlush,
                ShopProductCategory.CatFigure,
                ShopProductCategory.CatGoods,
                ShopProductCategory.CatSeasonal,
                ShopProductCategory.CatRetro
            };
            TrendCategory.Value = (int)categories[Mathf.Abs(day * 17 + 3) % categories.Length];
            if (ShopNetworkGame.Instance != null)
                ShopNetworkGame.Instance.TrendPercent.Value = Mathf.RoundToInt(Config.TrendPriceBonus * 100f);
        }

        private float DurationFor(ShopPhase phase) => phase switch
        {
            ShopPhase.Setup => Config.PreparationSeconds,
            ShopPhase.Open => Config.OpeningSeconds,
            ShopPhase.Summary => Config.ClosingSeconds,
            _ => 0f
        };

        private void SyncRemaining()
        {
            PhaseSecondsRemaining.Value = Mathf.CeilToInt(Mathf.Max(0f, phaseRemaining));
        }

        public bool IsTrend(ShopProductCategory category) => category == CurrentTrendCategory;

        public int ApplyTrendPrice(ShopProductDefinition product, int basePrice)
        {
            if (product == null || !IsTrend(product.Category)) return Mathf.Max(1, basePrice);
            return Mathf.Max(1, Mathf.RoundToInt(basePrice * (1f + Config.TrendPriceBonus)));
        }

        public int CalculateSatisfaction(ShopCustomerNetwork customer, int displayedVariety,
            bool rareDisplayed)
        {
            if (customer == null || Config == null) return 0;
            float waitScore = Mathf.Clamp01(1f - customer.QueueWaitSeconds /
                Mathf.Max(1f, customer.PatienceSeconds));
            float varietyScore = Mathf.Clamp01(displayedVariety / 5f);
            float rarityScore = rareDisplayed ? 1f : 0f;
            float totalWeight = Mathf.Max(0.01f, Config.SatisfactionWaitWeight +
                Config.SatisfactionVarietyWeight + Config.SatisfactionRarityWeight);
            float score = (waitScore * Config.SatisfactionWaitWeight +
                           varietyScore * Config.SatisfactionVarietyWeight +
                           rarityScore * Config.SatisfactionRarityWeight) / totalWeight;
            if (IsTrend(customer.Preference.PreferredCategory))
                score += Config.TrendSatisfactionBonus / 100f;
            return Mathf.Clamp(Mathf.RoundToInt(score * 100f), 0, 100);
        }

        public ShopCustomerProfileSelection ServerSelectCustomerProfile()
        {
            EnsureCustomerPool();
            if (customerProfiles.Count == 0)
                return new ShopCustomerProfileSelection("customer:001", ShopProductCategory.CatPlush, 0, 70);
            float total = 0f;
            for (int i = 0; i < customerProfiles.Count; i++) total += ProfileWeight(customerProfiles[i]);
            float roll = UnityEngine.Random.value * Mathf.Max(0.01f, total);
            ShopCustomerProfileSave selected = customerProfiles[0];
            for (int i = 0; i < customerProfiles.Count; i++)
            {
                selected = customerProfiles[i];
                roll -= ProfileWeight(selected);
                if (roll <= 0f) break;
            }
            return new ShopCustomerProfileSelection(selected.customerId,
                (ShopProductCategory)selected.preferredCategory,
                selected.purchaseCount, selected.lastSatisfaction);
        }

        private float ProfileWeight(ShopCustomerProfileSave profile)
        {
            float satisfactionWeight = Mathf.Lerp(0.5f, 1.8f,
                Mathf.Clamp01(profile.lastSatisfaction / 100f));
            float trendWeight = profile.preferredCategory == TrendCategory.Value
                ? Config.TrendCustomerWeight
                : 1f;
            float regularWeight = profile.regular ? 1.35f : 1f;
            return satisfactionWeight * trendWeight * regularWeight;
        }

        public void ServerRecordCustomerPurchase(string customerId,
            ShopProductCategory preferredCategory, int satisfaction)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(customerId)) return;
            ShopCustomerProfileSave profile = FindProfile(customerId);
            if (profile == null) return;
            profile.preferredCategory = (int)preferredCategory;
            profile.purchaseCount++;
            profile.lastSatisfaction = Mathf.Clamp(satisfaction, 0, 100);
            if (!profile.regular && profile.purchaseCount >= Config.RegularPurchaseThreshold)
            {
                profile.regular = true;
                ShopProgressionManager.Instance?.RecordRegularCustomer(profile.customerId);
            }
        }

        public float RegularPriceMultiplier(string customerId)
        {
            ShopCustomerProfileSave profile = FindProfile(customerId);
            return profile != null && profile.regular ? Config.RegularPriceMultiplier : 1f;
        }

        public bool ShouldRegularBuyExtra(string customerId, ShopProductCategory category)
        {
            ShopCustomerProfileSave profile = FindProfile(customerId);
            return profile != null && profile.regular && profile.preferredCategory == (int)category &&
                   UnityEngine.Random.value < Config.RegularExtraPurchaseChance;
        }

        private ShopCustomerProfileSave FindProfile(string id)
        {
            for (int i = 0; i < customerProfiles.Count; i++)
                if (string.Equals(customerProfiles[i].customerId, id, StringComparison.Ordinal))
                    return customerProfiles[i];
            return null;
        }

        private void EnsureCustomerPool()
        {
            if (Config == null) return;
            ShopProductCategory[] categories =
            {
                ShopProductCategory.CatPlush, ShopProductCategory.CatFigure,
                ShopProductCategory.CatGoods, ShopProductCategory.CatSeasonal,
                ShopProductCategory.CatRetro
            };
            for (int i = customerProfiles.Count; i < Config.PersistentCustomerCount; i++)
            {
                customerProfiles.Add(new ShopCustomerProfileSave
                {
                    customerId = "customer:" + (i + 1).ToString("D3"),
                    preferredCategory = (int)categories[i % categories.Length],
                    lastSatisfaction = 70
                });
            }
        }

        public void ServerHandlePackingStation(ulong senderClientId)
        {
            if (!IsServer) return;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            ShopProgressionManager progression = ShopProgressionManager.Instance;
            if (game == null || progression == null) return;
            if (progression.ExpansionLevel < Config.OrderUnlockExpansionLevel ||
                game.Reputation.Value < Config.OrderUnlockReputation)
            {
                game.ServerSetEvent("포장대는 가게 확장 Lv.2와 평판 10에서 해금됩니다.");
                return;
            }
            if (OnlineOrders.Count == 0)
            {
                game.ServerSetEvent("현재 접수 가능한 온라인 주문이 없습니다.");
                return;
            }

            ShopOnlineOrderState order = OnlineOrders[0];
            if (game.GetSharedProductQuantity(order.ProductId, true) < order.Quantity)
            {
                game.ServerSetEvent(order.ProductName + " " + order.Quantity +
                                    "개를 창고나 진열대에 준비하세요.");
                return;
            }
            if (!game.ServerTryConsumeContainerProduct(order.ProductId, order.Quantity, true, out _))
            {
                game.ServerSetEvent("공유 컨테이너가 다른 작업 중입니다. 잠시 후 다시 시도하세요.");
                return;
            }
            game.Coins.Value += order.Reward;
            game.Reputation.Value += Config.OrderReputationReward;
            progression.RecordOnlineOrder();
            OnlineOrders.RemoveAt(0);
            game.ServerSetEvent("온라인 주문 발송 완료 · +" + order.Reward + "원 · 평판 +" +
                                Config.OrderReputationReward);
            FillOrderBoard(game.Day.Value);
        }

        private void FillOrderBoard(int day)
        {
            ShopProgressionManager progression = ShopProgressionManager.Instance;
            if (progression == null || progression.ExpansionLevel < Config.OrderUnlockExpansionLevel ||
                ShopNetworkGame.Instance == null ||
                ShopNetworkGame.Instance.Reputation.Value < Config.OrderUnlockReputation) return;
            int capacity = progression.ExpansionLevel >= Config.OrderRoomExpansionLevel
                ? Config.OrderRoomConcurrentOrders
                : Config.BaseConcurrentOrders;
            ShopProductDefinition[] products = Resources.LoadAll<ShopProductDefinition>("Products")
                .Where(product => product != null &&
                                  ShopProductLocalization.IsCatTheme(product.Category)).ToArray();
            if (products == null || products.Length == 0) return;
            while (OnlineOrders.Count < capacity)
            {
                ShopProductDefinition product = products[Mathf.Abs(nextOrderId * 37 + day * 11) % products.Length];
                if (product == null) break;
                int quantity = 1 + Mathf.Abs(nextOrderId + day) % 3;
                int deadline = day + UnityEngine.Random.Range(Config.MinimumOrderDeadlineDays,
                    Config.MaximumOrderDeadlineDays + 1);
                OnlineOrders.Add(new ShopOnlineOrderState
                {
                    OrderId = nextOrderId++,
                    ProductId = product.ProductId,
                    ProductName = new FixedString64Bytes(product.DisplayName),
                    Quantity = quantity,
                    Reward = Mathf.RoundToInt(product.SalePrice * quantity * Config.OrderPriceMultiplier),
                    DeadlineDay = deadline
                });
            }
        }

        private void ExpireOrders(int day)
        {
            for (int i = OnlineOrders.Count - 1; i >= 0; i--)
            {
                if (OnlineOrders[i].DeadlineDay >= day) continue;
                OnlineOrders.RemoveAt(i);
                ShopNetworkGame game = ShopNetworkGame.Instance;
                if (game != null)
                    game.Reputation.Value = Mathf.Max(0,
                        game.Reputation.Value - Config.OrderFailureReputationPenalty);
            }
        }

        public void ServerRecordAutomation(int acquired, int cost)
        {
            if (!IsServer) return;
            AutomatedAcquiredToday.Value += Mathf.Max(0, acquired);
            AutomatedCostToday.Value += Mathf.Max(0, cost);
        }

        public bool TryConsumeAutomationSave(int machineId, out ShopAutomationMachineSave saved)
        {
            for (int i = 0; i < pendingAutomation.Count; i++)
            {
                if (pendingAutomation[i].machineId != machineId) continue;
                saved = pendingAutomation[i];
                pendingAutomation.RemoveAt(i);
                return true;
            }
            saved = null;
            return false;
        }

        public void AppendStatus(StringBuilder text)
        {
            if (text == null || Config == null) return;
            text.AppendLine();
            text.AppendLine("<color=#46FFBF><b>오늘의 운영</b></color>");
            text.AppendLine("구간: " + (ShopNetworkGame.Instance != null ? ShopNetworkGame.Instance.PhaseLabel : "-") +
                            " · 남은 시간 " + PhaseSecondsRemaining.Value + "초");
            text.AppendLine("유행: " + ShopProductLocalization.CategoryLabel(CurrentTrendCategory) +
                            " · 판매가 +" + Mathf.RoundToInt(Config.TrendPriceBonus * 100f) + "%");
            text.AppendLine("일일 판매 목표: " + DailySalesProgress.Value + " / " + DailySalesGoal.Value);
            text.AppendLine("단골: " + RegularCustomerCount + " / " + Config.PersistentCustomerCount);
            int shown = 0;
            for (int i = 0; i < customerProfiles.Count && shown < 8; i++)
            {
                if (!customerProfiles[i].regular) continue;
                text.AppendLine("  · " + customerProfiles[i].customerId + " · " +
                                ShopProductLocalization.CategoryLabel(
                                    (ShopProductCategory)customerProfiles[i].preferredCategory));
                shown++;
            }
            text.AppendLine("온라인 주문: " + OnlineOrders.Count + "건");
            text.AppendLine("자동화: 오늘 획득 " + AutomatedAcquiredToday.Value + "개 · 소모 " +
                            AutomatedCostToday.Value + "원");
            if (ShopNetworkGame.Instance != null)
                text.AppendLine(ShopNetworkGame.Instance.StaffStatusSummary());
        }

        public void WriteSave(ShopProgressionSaveData save)
        {
            if (save == null) return;
            save.livePhase = ShopNetworkGame.Instance != null ? (int)ShopNetworkGame.Instance.Phase.Value : 0;
            save.livePhaseSecondsRemaining = phaseRemaining;
            save.trendCategory = TrendCategory.Value;
            save.dailySalesGoal = DailySalesGoal.Value;
            save.dailySalesProgress = DailySalesProgress.Value;
            save.nextOrderId = nextOrderId;
            save.customerProfiles = CloneProfiles(customerProfiles);
            save.onlineOrders = new List<ShopOnlineOrderSave>();
            for (int i = 0; i < OnlineOrders.Count; i++)
            {
                ShopOnlineOrderState order = OnlineOrders[i];
                save.onlineOrders.Add(new ShopOnlineOrderSave
                {
                    orderId = order.OrderId,
                    productId = order.ProductId,
                    productName = order.ProductName.ToString(),
                    quantity = order.Quantity,
                    reward = order.Reward,
                    deadlineDay = order.DeadlineDay,
                    active = true
                });
            }
            save.automationMachines = new List<ShopAutomationMachineSave>();
            foreach (ShopClawAutomationDevice device in
                     FindObjectsByType<ShopClawAutomationDevice>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (device != null && device.IsSpawned && device.IsServer)
                    save.automationMachines.Add(device.CaptureSave());
            }
        }

        private void Restore(ShopProgressionSaveData save)
        {
            phaseRemaining = Mathf.Max(0f, save.livePhaseSecondsRemaining);
            TrendCategory.Value = ShopProductLocalization.IsCatTheme(
                    (ShopProductCategory)save.trendCategory)
                ? save.trendCategory
                : (int)ShopProductCategory.CatPlush;
            DailySalesGoal.Value = Mathf.Max(1, save.dailySalesGoal);
            DailySalesProgress.Value = Mathf.Max(0, save.dailySalesProgress);
            nextOrderId = Mathf.Max(1, save.nextOrderId);
            customerProfiles.Clear();
            if (save.customerProfiles != null) customerProfiles.AddRange(CloneProfiles(save.customerProfiles));
            OnlineOrders.Clear();
            if (save.onlineOrders != null)
            {
                for (int i = 0; i < save.onlineOrders.Count; i++)
                {
                    ShopOnlineOrderSave order = save.onlineOrders[i];
                    if (order == null || !order.active) continue;
                    OnlineOrders.Add(new ShopOnlineOrderState
                    {
                        OrderId = order.orderId,
                        ProductId = order.productId,
                        ProductName = new FixedString64Bytes(order.productName ?? "상품"),
                        Quantity = Mathf.Max(1, order.quantity),
                        Reward = Mathf.Max(1, order.reward),
                        DeadlineDay = Mathf.Max(1, order.deadlineDay)
                    });
                }
            }
            pendingAutomation.Clear();
            if (save.automationMachines != null) pendingAutomation.AddRange(save.automationMachines);
            if (ShopNetworkGame.Instance != null)
                ShopNetworkGame.Instance.ServerSetPhase((ShopPhase)Mathf.Clamp(save.livePhase,
                    (int)ShopPhase.PrizeHunt, (int)ShopPhase.Summary));
        }

        private static List<ShopCustomerProfileSave> CloneProfiles(IReadOnlyList<ShopCustomerProfileSave> source)
        {
            List<ShopCustomerProfileSave> result = new();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                ShopCustomerProfileSave profile = source[i];
                if (profile == null) continue;
                result.Add(new ShopCustomerProfileSave
                {
                    customerId = profile.customerId,
                    preferredCategory = profile.preferredCategory,
                    purchaseCount = profile.purchaseCount,
                    lastSatisfaction = profile.lastSatisfaction,
                    regular = profile.regular
                });
            }
            return result;
        }

        private static string BuildSummary(ShopNetworkGame game)
        {
            return "매출 " + game.CampaignRevenue.Value + "원 · 오늘 판매 " +
                   game.SoldToday.Value + "개 · 목표 " +
                   (game.SoldToday.Value >= (Instance != null ? Instance.DailySalesGoal.Value : 1)
                       ? "달성"
                       : "미달성");
        }
    }
}
