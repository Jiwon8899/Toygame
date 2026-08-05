using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    public enum ShopPhase
    {
        PrizeHunt,
        Setup,
        Open,
        Summary,
        Complete
    }

    public enum ShopAction
    {
        ClawMachine,
        CapsuleMachine,
        PlanningBoard,
        DisplayShelf,
        Register,
        EndDay,
        UpgradeShop,
        OnlineOrder
    }

    public enum ShopUpgradeCategory
    {
        Player,
        Operations,
        Facility,
        Claw,
        Gacha,
        Kuji,
        StoreExpansion,
        Staff
    }

    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShopNetworkGame : NetworkBehaviour
    {
        public const int CampaignDays = 7;
        public const int MaxClawInventorySlots = 10;
        public const int MaxShopUpgradeLevel = 3;
        public const int TotalSupportedUpgradeLevels = 20;

        private static readonly int[] ShopUpgradeCosts = { 250, 450, 700 };
        private static readonly int[] PlayerUpgradeCosts = { 200, 400 };
        private static readonly int[] FacilityUpgradeCosts = { 300, 550 };
        private static readonly int[] ClawUpgradeCosts = { 350, 600 };
        private static readonly int[] GachaUpgradeCosts = { 300 };
        private static readonly int[] KujiUpgradeCosts = { 250, 500 };

        public static ShopNetworkGame Instance { get; private set; }

        public NetworkVariable<int> Day = new(1);
        public NetworkVariable<int> Coins = new(1200);
        public NetworkVariable<int> Inventory = new(0);
        public NetworkVariable<int> RareInventory = new(0);
        public NetworkList<int> ClawInventoryVisuals = new();
        public NetworkList<ShopContainerItem> ItemContainers = new();
        public NetworkVariable<int> Displayed = new(0);
        public NetworkVariable<int> SoldToday = new(0);
        public NetworkVariable<int> RareSoldToday = new(0);
        public NetworkVariable<int> Reputation = new(0);
        public NetworkVariable<int> ShopUpgradeLevel = new(0);
        public NetworkVariable<int> PlayerUpgradeLevel = new(0);
        public NetworkVariable<int> FacilityUpgradeLevel = new(0);
        public NetworkVariable<int> ClawUpgradeLevel = new(0);
        public NetworkVariable<int> GachaUpgradeLevel = new(0);
        public NetworkVariable<int> KujiUpgradeLevel = new(0);
        public NetworkVariable<int> StaffHiredMask = new(0);
        public NetworkVariable<int> StaffAttendanceMask = new(0);
        public NetworkVariable<int> TrendPercent = new(15);
        public NetworkVariable<int> FailStreak = new(0);
        public NetworkVariable<ShopPhase> Phase = new(ShopPhase.PrizeHunt);
        public NetworkVariable<FixedString128Bytes> LastEvent =
            new(new FixedString128Bytes("Open a claw machine and collect stock together."));

        public NetworkVariable<int> CampaignRevenue = new(0);
        public NetworkVariable<int> CampaignSold = new(0);
        public NetworkVariable<int> CampaignAcquired = new(0);
        public NetworkVariable<int> CampaignGiveUps = new(0);
        public NetworkVariable<int> CampaignSatisfactionTotal = new(0);
        public NetworkVariable<int> CampaignSatisfactionSamples = new(0);
        public NetworkVariable<int> CampaignClawSuccesses = new(0);
        public NetworkVariable<int> CampaignClawFailures = new(0);
        public NetworkVariable<int> CampaignTopProductSales = new(0);
        public NetworkVariable<FixedString64Bytes> CampaignTopProductName = new(new FixedString64Bytes("없음"));

        private readonly Dictionary<string, int> campaignProductSales = new();

        private bool KoreanMode => ShopNightSalesSystem.Instance != null;

        public int NextShopUpgradeCost =>
            ShopUpgradeLevel.Value >= MaxShopUpgradeLevel
                ? 0
                : ShopUpgradeCosts[ShopUpgradeLevel.Value];

        public int CustomerCapacityBonus => (ShopUpgradeLevel.Value >= 2 ? 1 : 0) +
            Mathf.Max(0, (ShopProgressionManager.Instance?.ExpansionLevel ?? 1) - 4);
        public int SharedStorageCapacity =>
            ShopProgressionManager.Instance != null
                ? ShopProgressionManager.Instance.CurrentStorageSlots
                : 30;
        public int SharedDisplayCapacity =>
            ShopProgressionManager.Instance != null
                ? ShopProgressionManager.Instance.CurrentDisplaySlots
                : 4;
        public int SharedCheckoutCount =>
            ShopProgressionManager.Instance != null
                ? ShopProgressionManager.Instance.CurrentCheckoutCount
                : 1;
        public float CustomerSpawnIntervalReduction => ShopUpgradeLevel.Value >= 1 ? 1.5f : 0f;
        public float CheckoutDurationReduction => ShopUpgradeLevel.Value >= 3 ? 0.5f : 0f;
        public float PlayerMoveSpeedMultiplier => PlayerUpgradeLevel.Value >= 1 ? 1.15f : 1f;
        public float PlayerSprintSpeedMultiplier => PlayerUpgradeLevel.Value >= 2 ? 1.2f : 1f;
        public float ClawMoveSpeedMultiplier => ClawUpgradeLevel.Value >= 1 ? 1.25f : 1f;
        public float ClawAimTimeBonus => ClawUpgradeLevel.Value >= 1 ? 5f : 0f;
        public float ClawStrengthMultiplier => ClawUpgradeLevel.Value >= 2 ? 1.35f : 1f;
        public float ClawGripThresholdReduction => ClawUpgradeLevel.Value >= 2 ? 5f : 0f;
        public float GachaCostMultiplier => GachaUpgradeLevel.Value >= 1 ? 0.8f : 1f;
        public bool KujiDetailedInformation => KujiUpgradeLevel.Value >= 1;
        public float KujiCostMultiplier => KujiUpgradeLevel.Value >= 2 ? 0.8f : 1f;

        public int TotalUpgradeLevel =>
            PlayerUpgradeLevel.Value + ShopUpgradeLevel.Value + FacilityUpgradeLevel.Value +
            ClawUpgradeLevel.Value + GachaUpgradeLevel.Value + KujiUpgradeLevel.Value +
            GetUpgradeLevel(ShopUpgradeCategory.StoreExpansion) + GetUpgradeLevel(ShopUpgradeCategory.Staff);

        public string ShopUpgradePrompt =>
            "업그레이드 내역 열기 (" + TotalUpgradeLevel + "/" + TotalSupportedUpgradeLevels + ")";

        public string PhaseLabel => KoreanMode
            ? Phase.Value switch
            {
                ShopPhase.PrizeHunt => "낮 - 상품 획득",
                ShopPhase.Setup => "저녁 - 매장 준비",
                ShopPhase.Open => "밤 - 영업 중",
                ShopPhase.Summary => "마감 - 오늘의 정산",
                ShopPhase.Complete => "7일 운영 완료",
                _ => "알 수 없음"
            }
            : Phase.Value switch
            {
                ShopPhase.PrizeHunt => "DAY - PRIZE HUNT",
                ShopPhase.Setup => "EVENING - SHOP SETUP",
                ShopPhase.Open => "NIGHT - SHOP OPEN",
                ShopPhase.Summary => "CLOSING - DAY SUMMARY",
                ShopPhase.Complete => "PROTOTYPE COMPLETE",
                _ => "UNKNOWN"
            };

        public string Objective => KoreanMode
            ? Phase.Value switch
            {
                ShopPhase.PrizeHunt => "인형뽑기 또는 캡슐 기계로 상품을 얻은 뒤 오늘의 계획표를 확인하세요.",
                ShopPhase.Setup => "가게 창고의 상품을 진열한 뒤 계산대에서 영업을 시작하세요.",
                ShopPhase.Open => "손님을 계산하고 비어 가는 진열대를 함께 보충하세요.",
                ShopPhase.Summary => "정산을 확인한 뒤 마감 종을 울려 다음 날로 넘어가세요.",
                ShopPhase.Complete => "7일 운영을 완료했습니다.",
                _ => string.Empty
            }
            : Phase.Value switch
            {
                ShopPhase.PrizeHunt => "Use claw or capsule machines, then check the planning board.",
                ShopPhase.Setup => "Place prizes on a display shelf, then use the register.",
                ShopPhase.Open => "Serve customers at the register. Restock shelves when needed.",
                ShopPhase.Summary => "Ring the closing bell to pay rent and begin the next day.",
                ShopPhase.Complete => "Seven days complete. Keep playing or rebuild with more content.",
                _ => string.Empty
            };

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer && NetworkManager.NetworkConfig.NetworkTopology == NetworkTopologyTypes.DistributedAuthority)
                NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.SessionOwner, true);
            if (IsServer && Day.Value < 1)
            {
                ResetCampaign();
            }
            else if (IsServer && KoreanMode && LastEvent.Value.ToString().StartsWith("Open a claw"))
            {
                SetEvent("인형뽑기 기계를 사용해 함께 판매 상품을 모으세요.");
            }
            if (IsServer)
                ShopProgressionManager.Instance?.RestoreContainersTo(this);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void RequestInteraction(ShopAction action)
        {
            if (!IsSpawned)
            {
                return;
            }

            InteractRpc(action);
        }

        public void RequestUpgradePurchase(ShopUpgradeCategory category)
        {
            if (!IsSpawned) return;
            PurchaseUpgradeRpc(category);
        }

        public void RequestContainerMove(ShopContainerKind sourceContainer, int sourceSlot,
            ShopContainerKind destinationContainer, int destinationSlot)
        {
            if (!IsSpawned) return;
            MoveContainerSlotRpc(sourceContainer, sourceSlot, destinationContainer, destinationSlot);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void MoveContainerSlotRpc(ShopContainerKind sourceContainer, int sourceSlot,
            ShopContainerKind destinationContainer, int destinationSlot, RpcParams rpcParams = default)
        {
            ulong requester = rpcParams.Receive.SenderClientId;
            if (!ServerTryMoveSlot(requester, sourceContainer, sourceSlot,
                    destinationContainer, destinationSlot, out string message))
            {
                ServerSetEvent(string.IsNullOrWhiteSpace(message)
                    ? "상품을 옮길 수 없습니다." : message);
            }
            else if (sourceContainer == ShopContainerKind.SharedDisplay ||
                     destinationContainer == ShopContainerKind.SharedDisplay)
            {
                ShopNightSalesSystem.Instance?.ServerRefreshDisplayLedger();
            }
        }

        public int GetUpgradeLevel(ShopUpgradeCategory category) => category switch
        {
            ShopUpgradeCategory.Player => PlayerUpgradeLevel.Value,
            ShopUpgradeCategory.Operations => ShopUpgradeLevel.Value,
            ShopUpgradeCategory.Facility => FacilityUpgradeLevel.Value,
            ShopUpgradeCategory.Claw => ClawUpgradeLevel.Value,
            ShopUpgradeCategory.Gacha => GachaUpgradeLevel.Value,
            ShopUpgradeCategory.Kuji => KujiUpgradeLevel.Value,
            ShopUpgradeCategory.StoreExpansion => Mathf.Max(0,
                (ShopProgressionManager.Instance?.ExpansionLevel ?? 1) - 1),
            ShopUpgradeCategory.Staff => CountBits(StaffHiredMask.Value),
            _ => 0
        };

        public static int GetUpgradeMaxLevel(ShopUpgradeCategory category) => category switch
        {
            ShopUpgradeCategory.Player => 2,
            ShopUpgradeCategory.Operations => 3,
            ShopUpgradeCategory.Facility => 2,
            ShopUpgradeCategory.Claw => 2,
            ShopUpgradeCategory.Gacha => 1,
            ShopUpgradeCategory.Kuji => 2,
            ShopUpgradeCategory.StoreExpansion => 5,
            ShopUpgradeCategory.Staff => 3,
            _ => 0
        };

        public int GetNextUpgradeCost(ShopUpgradeCategory category)
        {
            int level = GetUpgradeLevel(category);
            if (category == ShopUpgradeCategory.StoreExpansion)
            {
                ShopExpansionTier next = ShopProgressionManager.Instance?.NextExpansion;
                return next != null ? next.RequiredFunds : 0;
            }
            if (category == ShopUpgradeCategory.Staff)
            {
                ShopWorkforceConfig workforce = ShopWorkforceConfig.Load();
                return workforce != null && level < 3 ? workforce.HireCost((ShopStaffRole)level) : 0;
            }
            int[] costs = CostsFor(category);
            return level >= costs.Length ? 0 : costs[level];
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void InteractRpc(ShopAction action, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            switch (action)
            {
                case ShopAction.ClawMachine:
                    UseClawMachine(sender);
                    break;
                case ShopAction.CapsuleMachine:
                    UseCapsuleMachine(sender);
                    break;
                case ShopAction.PlanningBoard:
                    UsePlanningBoard();
                    break;
                case ShopAction.DisplayShelf:
                    UseDisplayShelf(sender);
                    break;
                case ShopAction.Register:
                    UseRegister();
                    break;
                case ShopAction.EndDay:
                    UseClosingBell();
                    break;
                case ShopAction.UpgradeShop:
                    UseShopUpgrade();
                    break;
                case ShopAction.OnlineOrder:
                    ShopLiveOperationsNetwork.Instance?.ServerHandlePackingStation(sender);
                    break;
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void PurchaseUpgradeRpc(ShopUpgradeCategory category)
        {
            ServerPurchaseUpgrade(category);
        }

        private void UseShopUpgrade()
        {
            ServerPurchaseUpgrade(ShopUpgradeCategory.Operations);
        }

        private void ServerPurchaseUpgrade(ShopUpgradeCategory category)
        {
            int level = GetUpgradeLevel(category);
            int maxLevel = GetUpgradeMaxLevel(category);
            if (level >= maxLevel)
            {
                SetEvent(UpgradeTitle(category) + " 업그레이드를 모두 완료했습니다.");
                return;
            }

            if (category == ShopUpgradeCategory.StoreExpansion)
            {
                ShopProgressionManager progression = ShopProgressionManager.Instance;
                if (progression == null)
                {
                    SetEvent("가게 확장 데이터를 찾지 못했습니다.");
                    return;
                }
                if (!progression.TryExpandShop(out string expansionMessage))
                {
                    SetEvent(string.IsNullOrWhiteSpace(expansionMessage)
                        ? "가게 확장을 진행할 수 없습니다." : expansionMessage);
                    return;
                }
                SetEvent(UpgradePurchasedMessage(category, level + 1));
                return;
            }

            int cost = GetNextUpgradeCost(category);
            int balance = Coins.Value;
            if (!ShopEconomy.TrySpend(ref balance, cost))
            {
                SetEvent("업그레이드 비용이 부족합니다. 필요한 금액: " + cost + "원");
                return;
            }

            Coins.Value = balance;
            SetUpgradeLevel(category, level + 1);
            SetEvent(UpgradePurchasedMessage(category, level + 1));
            ShopProgressionManager.Instance?.SaveNow();
        }

        private void SetUpgradeLevel(ShopUpgradeCategory category, int level)
        {
            switch (category)
            {
                case ShopUpgradeCategory.Player: PlayerUpgradeLevel.Value = level; break;
                case ShopUpgradeCategory.Operations: ShopUpgradeLevel.Value = level; break;
                case ShopUpgradeCategory.Facility: FacilityUpgradeLevel.Value = level; break;
                case ShopUpgradeCategory.Claw: ClawUpgradeLevel.Value = level; break;
                case ShopUpgradeCategory.Gacha: GachaUpgradeLevel.Value = level; break;
                case ShopUpgradeCategory.Kuji: KujiUpgradeLevel.Value = level; break;
                case ShopUpgradeCategory.Staff:
                    StaffHiredMask.Value = (1 << Mathf.Clamp(level, 0, 3)) - 1;
                    StaffAttendanceMask.Value |= StaffHiredMask.Value;
                    break;
            }
        }

        private static int[] CostsFor(ShopUpgradeCategory category) => category switch
        {
            ShopUpgradeCategory.Player => PlayerUpgradeCosts,
            ShopUpgradeCategory.Operations => ShopUpgradeCosts,
            ShopUpgradeCategory.Facility => FacilityUpgradeCosts,
            ShopUpgradeCategory.Claw => ClawUpgradeCosts,
            ShopUpgradeCategory.Gacha => GachaUpgradeCosts,
            ShopUpgradeCategory.Kuji => KujiUpgradeCosts,
            _ => System.Array.Empty<int>()
        };

        public static string UpgradeTitle(ShopUpgradeCategory category) => category switch
        {
            ShopUpgradeCategory.Player => "플레이어 이동",
            ShopUpgradeCategory.Operations => "상점 운영",
            ShopUpgradeCategory.Facility => "가게 화장",
            ShopUpgradeCategory.Claw => "인형뽑기 장비",
            ShopUpgradeCategory.Gacha => "가챠 운영",
            ShopUpgradeCategory.Kuji => "쿠지 정보",
            ShopUpgradeCategory.StoreExpansion => "매장 확장",
            ShopUpgradeCategory.Staff => "알바 고용",
            _ => "업그레이드"
        };

        public string UpgradeNextEffect(ShopUpgradeCategory category)
        {
            int level = GetUpgradeLevel(category);
            return category switch
            {
                ShopUpgradeCategory.Player => level switch
                {
                    0 => "이동속도 +15%",
                    1 => "달리기속도 +20%",
                    _ => "모든 이동 강화 완료"
                },
                ShopUpgradeCategory.Operations => level switch
                {
                    0 => "손님 방문 간격 7초 → 5.5초",
                    1 => "동시 손님 3명 → 4명",
                    2 => "계산시간 1.5초 → 1초",
                    _ => "모든 운영 강화 완료"
                },
                ShopUpgradeCategory.Facility => level switch
                {
                    0 => "따뜻한 천장 조명 설치",
                    1 => "카운터·매장 장식 리뉴얼",
                    _ => "가게 화장 완료"
                },
                ShopUpgradeCategory.Claw => level switch
                {
                    0 => "팬 이동 +25% / 조작시간 +5초",
                    1 => "팬 안정성 +35% / 정밀 퍼올리기",
                    _ => "뽑기 장비 강화 완료"
                },
                ShopUpgradeCategory.Gacha => level == 0
                    ? "가챠 비용 20% 할인"
                    : "가챠 운영 강화 완료",
                ShopUpgradeCategory.Kuji => level switch
                {
                    0 => "C·D 재고와 마지막상 정보 표시",
                    1 => "쿠지 티켓 비용 20% 할인",
                    _ => "쿠지 정보 강화 완료"
                },
                ShopUpgradeCategory.StoreExpansion => level switch
                {
                    0 => "진열대 1개 · 진열 2칸 추가",
                    1 => "진열대 1개 · 진열 2칸 추가",
                    2 => "진열대 1개 · 진열 2칸 추가",
                    3 => "매장 면적 · 창고 용량 확장",
                    4 => "매장 면적 · 창고 · 계산대 확장",
                    _ => "모든 매장 확장 완료"
                },
                ShopUpgradeCategory.Staff => level switch
                {
                    0 => "계산 알바 고용 (일급 80원)",
                    1 => "진열 알바 고용 (일급 100원)",
                    2 => "수거 알바 고용 (일급 120원)",
                    _ => "모든 알바 고용 완료"
                },
                _ => string.Empty
            };
        }

        private static string UpgradePurchasedMessage(ShopUpgradeCategory category, int level)
        {
            return category switch
            {
                ShopUpgradeCategory.Player when level == 1 => "플레이어 이동속도가 15% 빨라졌습니다.",
                ShopUpgradeCategory.Player => "플레이어 달리기속도가 20% 빨라졌습니다.",
                ShopUpgradeCategory.Operations when level == 1 => "도로변 홍보를 강화했습니다. 손님이 더 자주 방문합니다.",
                ShopUpgradeCategory.Operations when level == 2 => "손님 대기공간을 넓혔습니다. 한 명 더 입장할 수 있습니다.",
                ShopUpgradeCategory.Operations => "계산대를 개선했습니다. 계산 시간이 짧아집니다.",
                ShopUpgradeCategory.Facility when level == 1 => "매장 천장에 따뜻한 조명을 설치했습니다.",
                ShopUpgradeCategory.Facility => "카운터와 매장 장식을 새롭게 꾸몄습니다.",
                ShopUpgradeCategory.Claw when level == 1 => "정밀 레일을 설치해 팬이 빠르고 오래 움직입니다.",
                ShopUpgradeCategory.Claw => "강화 팬으로 교체해 퍼올리기 안정성이 좋아졌습니다.",
                ShopUpgradeCategory.Gacha => "가챠 이용 비용이 20% 할인됩니다.",
                ShopUpgradeCategory.Kuji when level == 1 => "쿠지의 상세 재고와 마지막상 정보를 확인할 수 있습니다.",
                ShopUpgradeCategory.Kuji => "쿠지 티켓 비용이 20% 할인됩니다.",
                ShopUpgradeCategory.StoreExpansion when level <= 3 => "새 진열대와 진열 공간 2칸을 설치했습니다.",
                ShopUpgradeCategory.StoreExpansion => "매장과 창고 공간을 확장했습니다.",
                ShopUpgradeCategory.Staff when level == 1 => "계산 알바를 고용했습니다.",
                ShopUpgradeCategory.Staff when level == 2 => "진열 알바를 고용했습니다.",
                ShopUpgradeCategory.Staff => "수거 알바를 고용했습니다.",
                _ => "업그레이드를 완료했습니다."
            };
        }

        private void UseClawMachine(ulong ownerClientId)
        {
            if (!ShopClawRules.CanOperateDuring(Phase.Value))
            {
                SetEvent(KoreanMode ? "인형뽑기는 상품 획득 단계에서만 사용할 수 있습니다." : "Claw machines are only available during the prize hunt.");
                return;
            }

            int balance = Coins.Value;
            if (!ShopEconomy.TrySpend(ref balance, ShopEconomy.ClawCost))
            {
                SetEvent(KoreanMode ? "인형뽑기에 사용할 가게 자금이 부족합니다." : "Not enough shop funds for a claw attempt.");
                return;
            }

            Coins.Value = balance;
            float successChance = Mathf.Clamp01(0.68f + FailStreak.Value * 0.08f);
            if (Random.value <= successChance)
            {
                bool rare = Random.value <= 0.18f;
                ShopProductDefinition fallbackProduct = FindProductByRarity(rare);
                int visualIndex = fallbackProduct != null
                    ? ShopClawPrizeNetwork.FindCatalogIndex(fallbackProduct.PrizePrefab)
                    : -1;
                if (!ServerTryAcquireItem(ownerClientId, fallbackProduct, visualIndex, out _))
                {
                    Coins.Value += ShopEconomy.ClawCost;
                    SetEvent("개인 인벤토리와 공용 창고가 모두 가득 차 뽑기가 취소되었습니다.");
                    return;
                }
                ServerRecordAcquired(1);
                ServerRecordClawResult(true);

                FailStreak.Value = 0;
                SetEvent(KoreanMode
                    ? rare ? "뽑기 성공! 희귀 별 인형이 가게 창고에 들어왔습니다." : "뽑기 성공! 포근한 인형이 가게 창고에 들어왔습니다."
                    : rare ? "Claw success! A rare star plush joined the shared stock." : "Claw success! A cozy plush joined the shared stock.");
            }
            else
            {
                FailStreak.Value++;
                ServerRecordClawResult(false);
                SetEvent(KoreanMode ? "상품이 팬에서 미끄러졌습니다. 다음 시도의 보정 확률이 증가합니다." : "The prize slipped from the scoop. Pity increased for the next attempt.");
            }
        }

        private void UseCapsuleMachine(ulong ownerClientId)
        {
            if (!ShopClawRules.CanOperateDuring(Phase.Value))
            {
                SetEvent(KoreanMode ? "캡슐 기계는 상품 획득 단계에서만 사용할 수 있습니다." : "Capsule machines are only available during the prize hunt.");
                return;
            }

            int balance = Coins.Value;
            if (!ShopEconomy.TrySpend(ref balance, ShopEconomy.CapsuleCost))
            {
                SetEvent(KoreanMode ? "캡슐을 구매할 가게 자금이 부족합니다." : "Not enough shop funds for a capsule.");
                return;
            }

            Coins.Value = balance;
            ShopProductDefinition capsule = FindProductByCategory(ShopProductCategory.CapsuleToy);
            int visualIndex = capsule != null
                ? ShopClawPrizeNetwork.FindCatalogIndex(capsule.PrizePrefab)
                : -1;
            if (!ServerTryAcquireItem(ownerClientId, capsule, visualIndex, out _))
            {
                Coins.Value += ShopEconomy.CapsuleCost;
                SetEvent("개인 인벤토리와 공용 창고가 모두 가득 차 캡슐 구매가 취소되었습니다.");
                return;
            }
            ServerRecordAcquired(1);
            SetEvent(KoreanMode ? "캡슐 개봉! 작은 수집품이 가게 창고에 들어왔습니다." : "Capsule opened: a small collectible joined the shop stock.");
        }

        private void UsePlanningBoard()
        {
            if (Phase.Value == ShopPhase.PrizeHunt)
            {
                Phase.Value = ShopPhase.Setup;
                SetEvent(KoreanMode ? "상품 획득을 마쳤습니다. 함께 진열대를 채우세요." : "Prize hunt finished. Work together to stock the shop.");
                return;
            }

            SetEvent(KoreanMode
                ? "오늘의 유행은 " + TrendPercent.Value + "%입니다. 가게 창고: " + Inventory.Value + "개."
                : "Today's trend is " + TrendPercent.Value + "%. Shared stock: " + Inventory.Value + ".");
        }

        private void UseDisplayShelf(ulong ownerClientId)
        {
            if (ShopNightSalesSystem.Instance != null)
            {
                ShopNightSalesSystem.Instance.ServerTryRestockDisplay(ownerClientId);
                return;
            }

            if (Phase.Value != ShopPhase.Setup && Phase.Value != ShopPhase.Open)
            {
                SetEvent("Shelves can be stocked during setup or while the shop is open.");
                return;
            }

            if (Inventory.Value <= 0)
            {
                SetEvent("Shared storage is empty. Acquire more prizes tomorrow.");
                return;
            }
            if (Displayed.Value >= SharedDisplayCapacity)
            {
                SetEvent("현재 확장 단계의 진열 한도(" + SharedDisplayCapacity + ")에 도달했습니다.");
                return;
            }

            if (!ServerTryMoveItem(ownerClientId, ShopContainerKind.PersonalInventory,
                    ShopContainerKind.SharedDisplay, out ShopContainerItem moved) &&
                !ServerTryMoveItem(ShopContainerRules.SharedOwner, ShopContainerKind.SharedStorage,
                    ShopContainerKind.SharedDisplay, out moved))
            {
                SetEvent("진열할 개인 인벤토리 또는 공용 창고 상품이 없습니다.");
                return;
            }
            string displayedName = moved.DisplayName.ToString();
            SetEvent(displayedName + " 상품 1개를 진열했습니다.");
            ShopTutorialRuntime.Report(ShopTutorialAction.ProductDisplayed);
        }

        private void UseRegister()
        {
            if (ShopNightSalesSystem.Instance != null)
            {
                ShopNightSalesSystem.Instance.ServerHandleRegister();
                return;
            }

            if (Phase.Value == ShopPhase.Setup)
            {
                if (Displayed.Value <= 0)
                {
                    SetEvent("Stock at least one display shelf before opening.");
                    return;
                }

                Phase.Value = ShopPhase.Open;
                SetEvent("The shop is open! Use the register again to serve customers.");
                return;
            }

            if (Phase.Value != ShopPhase.Open)
            {
                SetEvent("The register is used after setup to open and serve the shop.");
                return;
            }

            if (Displayed.Value <= 0)
            {
                SetEvent("No displayed stock. Restock a shelf or close for the day.");
                return;
            }

            if (!ServerTryConsumeDisplayedProduct(int.MinValue, out ShopContainerItem soldItem))
            {
                SetEvent("진열 컨테이너에서 판매할 상품을 찾지 못했습니다.");
                return;
            }
            bool rareSale = soldItem.Rarity == ShopProductRarity.Rare;
            int price = soldItem.UnitPrice > 0
                ? soldItem.UnitPrice
                : ShopEconomy.CalculateSalePrice(TrendPercent.Value, rareSale);
            SoldToday.Value++;
            Coins.Value += price;
            CampaignRevenue.Value += price;
            CampaignSold.Value++;
            ServerRecordProductSale(rareSale ? "희귀 상품" : "일반 상품", 1);
            if (rareSale)
            {
                RareSoldToday.Value++;
            }

            Reputation.Value += rareSale ? 5 : 2;
            ShopProgressionManager progression = ShopProgressionManager.Instance;
            if (progression == null)
                Debug.LogError("[Progression] 판매 기록 관리자를 찾지 못했습니다.", this);
            else
                progression.RecordSale(rareSale ? "sale:rare" : "sale:common",
                    rareSale ? "희귀 상품" : "일반 상품", rareSale ? "rare" : "general",
                    price, rareSale, rareSale ? 95 : 80);
            SetEvent((rareSale ? "Rare customer sale! +" : "Customer served! +") + price + " coins.");

            if (Displayed.Value == 0)
            {
                Phase.Value = ShopPhase.Summary;
                SetEvent("All displayed stock sold. Ring the closing bell when ready.");
            }
        }

        private void UseClosingBell()
        {
            ShopNightSalesSystem nightSales = ShopNightSalesSystem.Instance;
            if (nightSales != null && Phase.Value == ShopPhase.Open)
            {
                nightSales.ServerRequestClose();
                return;
            }

            if (Phase.Value != ShopPhase.Open && Phase.Value != ShopPhase.Summary)
            {
                SetEvent(KoreanMode
                    ? "영업을 시작하고 손님을 응대한 뒤 마감할 수 있습니다."
                    : "Open the shop and serve at least one customer before closing.");
                return;
            }

            int rent = ShopEconomy.CalculateRent(Day.Value);
            int wages = ServerPayStaffWages();
            if (nightSales != null)
            {
                nightSales.ServerTakeUnsoldStock(out _, out _);
                ServerReturnAllDisplayedToStorage();
            }
            else
            {
                ServerReturnAllDisplayedToStorage();
            }
            Coins.Value = Mathf.Max(0, Coins.Value - rent);

            int completedDay = Day.Value;
            if (completedDay >= CampaignDays)
            {
                Phase.Value = ShopPhase.Complete;
                SetEvent(KoreanMode
                    ? "7일 운영 완료! 최종 점수: " + ShopEconomy.CalculateDayScore(Coins.Value, SoldToday.Value, Reputation.Value)
                    : "Seven-day prototype complete! Final score: " +
                      ShopEconomy.CalculateDayScore(Coins.Value, SoldToday.Value, Reputation.Value) + ".");
                ShopTutorialRuntime.Report(ShopTutorialAction.DayClosed);
                ShopProgressionManager.Instance?.SaveNowWithFeedback();
                return;
            }

            Day.Value++;
            SoldToday.Value = 0;
            RareSoldToday.Value = 0;
            TrendPercent.Value = Random.Range(-10, 36);
            Phase.Value = ShopPhase.PrizeHunt;
            SetEvent(KoreanMode
                ? completedDay + "일 차 마감. 임대료 " + rent + "원 · 알바 급여 " + wages +
                  "원을 지불했고 새 유행이 공개되었습니다."
                : "Day " + completedDay + " closed. Rent " + rent + " and wages " + wages +
                  " paid. New trend revealed.");
            ShopTutorialRuntime.Report(ShopTutorialAction.DayClosed);
            ShopProgressionManager.Instance?.SaveNowWithFeedback();
        }

        private void ResetCampaign()
        {
            Day.Value = 1;
            Coins.Value = 1200;
            Inventory.Value = 0;
            RareInventory.Value = 0;
            ClawInventoryVisuals.Clear();
            ItemContainers.Clear();
            Displayed.Value = 0;
            SoldToday.Value = 0;
            RareSoldToday.Value = 0;
            Reputation.Value = 0;
            ShopUpgradeLevel.Value = 0;
            PlayerUpgradeLevel.Value = 0;
            FacilityUpgradeLevel.Value = 0;
            ClawUpgradeLevel.Value = 0;
            GachaUpgradeLevel.Value = 0;
            KujiUpgradeLevel.Value = 0;
            StaffHiredMask.Value = 0;
            StaffAttendanceMask.Value = 0;
            TrendPercent.Value = 15;
            FailStreak.Value = 0;
            Phase.Value = ShopPhase.PrizeHunt;
            CampaignRevenue.Value = 0;
            CampaignSold.Value = 0;
            CampaignAcquired.Value = 0;
            CampaignGiveUps.Value = 0;
            CampaignSatisfactionTotal.Value = 0;
            CampaignSatisfactionSamples.Value = 0;
            CampaignClawSuccesses.Value = 0;
            CampaignClawFailures.Value = 0;
            CampaignTopProductSales.Value = 0;
            CampaignTopProductName.Value = new FixedString64Bytes("없음");
            campaignProductSales.Clear();
            SetEvent(KoreanMode ? "인형뽑기 기계를 사용해 함께 판매 상품을 모으세요." : "Open a claw machine and collect stock together.");
        }

        public void ServerResetCampaign()
        {
            if (!IsServer) return;
            ResetCampaign();
        }

        public void ServerRecordAcquired(int amount)
        {
            if (!IsServer || amount <= 0) return;
            CampaignAcquired.Value += amount;
        }

        public void ServerRecordClawResult(bool success)
        {
            if (!IsServer) return;
            if (success) CampaignClawSuccesses.Value++;
            else CampaignClawFailures.Value++;
            ShopProgressionManager progression = ShopProgressionManager.Instance;
            if (progression == null)
                Debug.LogError("[Progression] 인형뽑기 기록 관리자를 찾지 못했습니다.", this);
            else
                progression.RecordClawResult(success);
            if (success) ShopTutorialRuntime.Report(ShopTutorialAction.PrizeAcquired);
        }

        public void ServerRecordClawPrize(int visualPrefabIndex)
        {
            if (!IsServer || visualPrefabIndex < 0) return;
            if (ClawInventoryVisuals.Count < MaxClawInventorySlots)
                ClawInventoryVisuals.Add(visualPrefabIndex);
        }

        public bool ServerTryConsumeClawInventoryVisual(out int visualPrefabIndex)
        {
            visualPrefabIndex = -1;
            if (!IsServer || ClawInventoryVisuals.Count <= 0) return false;
            visualPrefabIndex = ClawInventoryVisuals[0];
            ClawInventoryVisuals.RemoveAt(0);
            return true;
        }

        public ShopContainerSnapshot GetContainerSnapshot(ulong ownerClientId, ShopContainerKind container)
        {
            int capacity = container switch
            {
                ShopContainerKind.PersonalInventory => ShopContainerRules.PersonalCapacity,
                ShopContainerKind.SharedStorage => SharedStorageCapacity,
                ShopContainerKind.SharedDisplay => SharedDisplayCapacity,
                ShopContainerKind.AutomationBuffer => ShopOperationsConfig.Load()?.AutomationBufferSlots ?? 10,
                _ => 0
            };
            ulong owner = container == ShopContainerKind.PersonalInventory ||
                          container == ShopContainerKind.AutomationBuffer
                ? ownerClientId
                : ShopContainerRules.SharedOwner;
            return new ShopContainerSnapshot(
                ShopContainerRules.UsedCount(ItemContainers, owner, container), capacity);
        }

        public bool ServerTryAcquireItem(ulong ownerClientId, ShopProductDefinition product,
            int visualPrefabIndex, out ShopContainerKind destination)
        {
            return ServerTryAcquireItem(ownerClientId, product, visualPrefabIndex,
                ShopAcquisitionSource.Manual, 0, out destination);
        }

        public bool ServerTryAcquireItem(ulong ownerClientId, ShopProductDefinition product,
            int visualPrefabIndex, ShopAcquisitionSource source, ulong automationOwner,
            out ShopContainerKind destination)
        {
            destination = source == ShopAcquisitionSource.Automation
                ? ShopContainerKind.AutomationBuffer
                : ShopContainerKind.PersonalInventory;
            if (!IsServer) return false;
            if (product == null)
            {
                Debug.LogError("[Acquisition] 상품 데이터 참조가 없어 획득을 거부했습니다. " +
                               "문자열 또는 등급 fallback은 사용하지 않습니다.", this);
                return false;
            }

            if (source == ShopAcquisitionSource.Automation)
            {
                int bufferCapacity = ShopOperationsConfig.Load()?.AutomationBufferSlots ?? 10;
                if (TryAddToContainer(automationOwner, ShopContainerKind.AutomationBuffer, product,
                        visualPrefabIndex, bufferCapacity))
                {
                    SyncLegacyContainerCounts();
                    return true;
                }

                destination = ShopContainerKind.SharedStorage;
                if (!TryAddToContainer(ShopContainerRules.SharedOwner, ShopContainerKind.SharedStorage,
                        product, visualPrefabIndex))
                    return false;
                SyncLegacyContainerCounts();
                return true;
            }

            if (TryAddToContainer(ownerClientId, ShopContainerKind.PersonalInventory, product,
                    visualPrefabIndex))
            {
                ServerRecordClawPrize(visualPrefabIndex);
                SyncLegacyContainerCounts();
                return true;
            }

            destination = ShopContainerKind.SharedStorage;
            if (!TryAddToContainer(ShopContainerRules.SharedOwner, ShopContainerKind.SharedStorage,
                    product, visualPrefabIndex))
                return false;
            SyncLegacyContainerCounts();
            return true;
        }

        public bool ServerCanAcquireItem(ulong ownerClientId)
        {
            if (!IsServer) return false;
            return !GetContainerSnapshot(ownerClientId, ShopContainerKind.PersonalInventory).IsFull ||
                   !GetContainerSnapshot(ShopContainerRules.SharedOwner, ShopContainerKind.SharedStorage).IsFull;
        }

        public bool ServerCanAcquireItem(ulong ownerClientId, ShopProductDefinition product)
        {
            if (!IsServer || product == null) return false;
            return ShopContainerRules.CanAcceptProduct(ItemContainers, ownerClientId,
                       ShopContainerKind.PersonalInventory, product.ProductId,
                       ShopContainerRules.PersonalCapacity) ||
                   ShopContainerRules.CanAcceptProduct(ItemContainers, ShopContainerRules.SharedOwner,
                       ShopContainerKind.SharedStorage, product.ProductId, SharedStorageCapacity);
        }

        public bool ServerTryMoveItem(ulong sourceOwner, ShopContainerKind source,
            ShopContainerKind destination, out ShopContainerItem moved, int requiredProductId = -1)
        {
            moved = default;
            if (!IsServer || source == destination) return false;
            int sourceIndex = -1;
            for (int i = 0; i < ItemContainers.Count; i++)
            {
                ShopContainerItem candidate = ItemContainers[i];
                if (!ShopContainerRules.BelongsTo(candidate, sourceOwner, source) ||
                    (requiredProductId >= 0 && candidate.ProductId != requiredProductId)) continue;
                sourceIndex = i;
                break;
            }
            if (sourceIndex < 0) return false;
            ShopContainerItem sourceItem = ItemContainers[sourceIndex];
            ulong destinationOwner = destination == ShopContainerKind.PersonalInventory
                ? sourceOwner
                : ShopContainerRules.SharedOwner;
            int capacity = destination switch
            {
                ShopContainerKind.PersonalInventory => ShopContainerRules.PersonalCapacity,
                ShopContainerKind.SharedStorage => SharedStorageCapacity,
                ShopContainerKind.SharedDisplay => SharedDisplayCapacity,
                _ => 0
            };
            if (!TryAddExistingToContainer(destinationOwner, destination, sourceItem, capacity, out moved))
                return false;

            sourceItem.Quantity--;
            if (sourceItem.Quantity <= 0) ItemContainers.RemoveAt(sourceIndex);
            else ItemContainers[sourceIndex] = sourceItem;
            SyncLegacyContainerCounts();
            return true;
        }

        public bool ServerTryMoveSlot(ulong requester, ShopContainerKind sourceContainer,
            int sourceSlot, ShopContainerKind destinationContainer, int destinationSlot,
            out string message)
        {
            message = string.Empty;
            if (!IsServer)
            {
                message = "호스트에서만 상품을 이동할 수 있습니다.";
                return false;
            }
            if (!IsPlayerManagedContainer(sourceContainer) ||
                !IsPlayerManagedContainer(destinationContainer))
            {
                message = "자동화 수집함은 장치 관리 화면에서 옮겨 주세요.";
                return false;
            }

            ulong sourceOwner = sourceContainer == ShopContainerKind.PersonalInventory
                ? requester : ShopContainerRules.SharedOwner;
            ulong destinationOwner = destinationContainer == ShopContainerKind.PersonalInventory
                ? requester : ShopContainerRules.SharedOwner;
            int sourceCapacity = CapacityFor(sourceContainer);
            int destinationCapacity = CapacityFor(destinationContainer);
            if (sourceSlot < 0 || sourceSlot >= sourceCapacity || destinationSlot < 0 ||
                destinationSlot >= destinationCapacity ||
                sourceContainer == destinationContainer && sourceSlot == destinationSlot)
            {
                message = "올바르지 않은 슬롯입니다.";
                return false;
            }

            int sourceIndex = FindContainerSlot(sourceOwner, sourceContainer, sourceSlot);
            if (sourceIndex < 0)
            {
                message = "원본 슬롯이 비어 있습니다.";
                return false;
            }
            int destinationIndex = FindContainerSlot(destinationOwner, destinationContainer, destinationSlot);
            ShopContainerItem source = ItemContainers[sourceIndex];

            if (destinationIndex < 0)
            {
                source.OwnerClientId = destinationOwner;
                source.Container = destinationContainer;
                source.SlotIndex = destinationSlot;
                ItemContainers[sourceIndex] = source;
                SyncLegacyContainerCounts();
                message = "상품을 옮겼습니다.";
                return true;
            }

            ShopContainerItem destination = ItemContainers[destinationIndex];
            if (source.ProductId == destination.ProductId)
            {
                int available = Mathf.Max(0, destination.MaxStack - destination.Quantity);
                int moved = Mathf.Min(source.Quantity, available);
                if (moved <= 0)
                {
                    message = "이 스택은 이미 가득 찼습니다.";
                    return false;
                }
                source.Quantity -= moved;
                destination.Quantity += moved;
                ItemContainers[destinationIndex] = destination;
                if (source.Quantity <= 0) ItemContainers.RemoveAt(sourceIndex);
                else ItemContainers[sourceIndex] = source;
                SyncLegacyContainerCounts();
                message = moved + "개를 합쳤습니다.";
                return true;
            }

            // Different products swap atomically. No list mutation is made until every
            // permission and capacity check above has succeeded, so a rejected drag rolls back.
            ShopContainerItem swappedSource = source;
            swappedSource.OwnerClientId = destinationOwner;
            swappedSource.Container = destinationContainer;
            swappedSource.SlotIndex = destinationSlot;
            ShopContainerItem swappedDestination = destination;
            swappedDestination.OwnerClientId = sourceOwner;
            swappedDestination.Container = sourceContainer;
            swappedDestination.SlotIndex = sourceSlot;
            ItemContainers[sourceIndex] = swappedDestination;
            ItemContainers[destinationIndex] = swappedSource;
            SyncLegacyContainerCounts();
            message = "두 상품의 위치를 바꿨습니다.";
            return true;
        }

        private int FindContainerSlot(ulong owner, ShopContainerKind container, int slot)
        {
            for (int i = 0; i < ItemContainers.Count; i++)
            {
                ShopContainerItem item = ItemContainers[i];
                if (ShopContainerRules.BelongsTo(item, owner, container) && item.SlotIndex == slot)
                    return i;
            }
            return -1;
        }

        private static bool IsPlayerManagedContainer(ShopContainerKind container) =>
            container == ShopContainerKind.PersonalInventory ||
            container == ShopContainerKind.SharedStorage ||
            container == ShopContainerKind.SharedDisplay;

        public bool ServerTrySplitStack(ulong owner, ShopContainerKind container, int slotIndex,
            int amount, out ShopContainerItem split)
        {
            split = default;
            if (!IsServer || amount <= 0) return false;
            int sourceIndex = -1;
            for (int i = 0; i < ItemContainers.Count; i++)
            {
                ShopContainerItem candidate = ItemContainers[i];
                if (ShopContainerRules.BelongsTo(candidate, owner, container) &&
                    candidate.SlotIndex == slotIndex && candidate.Quantity > amount)
                {
                    sourceIndex = i;
                    break;
                }
            }
            if (sourceIndex < 0) return false;

            int capacity = CapacityFor(container);
            int freeSlot = ShopContainerRules.FindFreeSlot(ItemContainers, owner, container, capacity);
            if (freeSlot < 0) return false;
            ShopContainerItem source = ItemContainers[sourceIndex];
            source.Quantity -= amount;
            ItemContainers[sourceIndex] = source;
            split = source;
            split.SlotIndex = freeSlot;
            split.Quantity = amount;
            ItemContainers.Add(split);
            SyncLegacyContainerCounts();
            return true;
        }

        public bool ServerTryMergeStacks(ulong owner, ShopContainerKind container,
            int sourceSlot, int destinationSlot)
        {
            if (!IsServer || sourceSlot == destinationSlot) return false;
            int sourceIndex = -1;
            int destinationIndex = -1;
            for (int i = 0; i < ItemContainers.Count; i++)
            {
                ShopContainerItem item = ItemContainers[i];
                if (!ShopContainerRules.BelongsTo(item, owner, container)) continue;
                if (item.SlotIndex == sourceSlot) sourceIndex = i;
                if (item.SlotIndex == destinationSlot) destinationIndex = i;
            }
            if (sourceIndex < 0 || destinationIndex < 0) return false;
            ShopContainerItem source = ItemContainers[sourceIndex];
            ShopContainerItem destination = ItemContainers[destinationIndex];
            if (source.ProductId != destination.ProductId || destination.Quantity >= destination.MaxStack)
                return false;
            int moved = Mathf.Min(source.Quantity, destination.MaxStack - destination.Quantity);
            if (moved <= 0) return false;
            source.Quantity -= moved;
            destination.Quantity += moved;
            ItemContainers[destinationIndex] = destination;
            if (source.Quantity <= 0) ItemContainers.RemoveAt(sourceIndex);
            else ItemContainers[sourceIndex] = source;
            SyncLegacyContainerCounts();
            return true;
        }

        public int ServerMoveAutomationBuffer(ulong automationOwner, ulong playerOwner,
            ShopContainerKind destination)
        {
            if (!IsServer || (destination != ShopContainerKind.SharedStorage &&
                              destination != ShopContainerKind.PersonalInventory)) return 0;
            int moved = 0;
            while (ShopContainerRules.FindFirst(ItemContainers, automationOwner,
                       ShopContainerKind.AutomationBuffer) >= 0)
            {
                int sourceIndex = ShopContainerRules.FindFirst(ItemContainers, automationOwner,
                    ShopContainerKind.AutomationBuffer);
                if (sourceIndex < 0) break;
                ShopContainerItem source = ItemContainers[sourceIndex];
                ulong destinationOwner = destination == ShopContainerKind.PersonalInventory
                    ? playerOwner
                    : ShopContainerRules.SharedOwner;
                if (!TryAddExistingToContainer(destinationOwner, destination, source,
                        CapacityFor(destination), out _)) break;
                source.Quantity--;
                if (source.Quantity <= 0) ItemContainers.RemoveAt(sourceIndex);
                else ItemContainers[sourceIndex] = source;
                moved++;
            }
            SyncLegacyContainerCounts();
            return moved;
        }

        public int GetSharedProductQuantity(int productId, bool includeDisplay)
        {
            int total = 0;
            for (int i = 0; i < ItemContainers.Count; i++)
            {
                ShopContainerItem item = ItemContainers[i];
                if (item.OwnerClientId != ShopContainerRules.SharedOwner || item.ProductId != productId)
                    continue;
                if (item.Container == ShopContainerKind.SharedStorage ||
                    (includeDisplay && item.Container == ShopContainerKind.SharedDisplay))
                    total += Mathf.Max(0, item.Quantity);
            }
            return total;
        }

        public bool ServerTryConsumeContainerProduct(int productId, int amount,
            bool allowDisplay, out int consumed)
        {
            consumed = 0;
            if (!IsServer || amount <= 0) return false;
            if (GetSharedProductQuantity(productId, allowDisplay) < amount) return false;
            ShopContainerKind[] order = allowDisplay
                ? new[] { ShopContainerKind.SharedStorage, ShopContainerKind.SharedDisplay }
                : new[] { ShopContainerKind.SharedStorage };
            foreach (ShopContainerKind container in order)
            {
                while (consumed < amount)
                {
                    int index = ShopContainerRules.FindFirst(ItemContainers,
                        ShopContainerRules.SharedOwner, container, productId);
                    if (index < 0) break;
                    ShopContainerItem item = ItemContainers[index];
                    int take = Mathf.Min(amount - consumed, item.Quantity);
                    item.Quantity -= take;
                    consumed += take;
                    if (item.Quantity <= 0) ItemContainers.RemoveAt(index);
                    else ItemContainers[index] = item;
                }
            }
            SyncLegacyContainerCounts();
            return consumed == amount;
        }

        public bool ServerTryConsumeDisplayedProduct(int productId, out ShopContainerItem consumed)
        {
            consumed = default;
            if (!IsServer) return false;
            int index = ShopContainerRules.FindFirst(ItemContainers, ShopContainerRules.SharedOwner,
                ShopContainerKind.SharedDisplay, productId);
            if (index < 0 && productId != int.MinValue)
                index = ShopContainerRules.FindFirst(ItemContainers, ShopContainerRules.SharedOwner,
                    ShopContainerKind.SharedDisplay);
            if (index < 0) return false;
            consumed = ItemContainers[index];
            ShopContainerItem remaining = consumed;
            consumed.Quantity = 1;
            remaining.Quantity--;
            if (remaining.Quantity <= 0) ItemContainers.RemoveAt(index);
            else ItemContainers[index] = remaining;
            SyncLegacyContainerCounts();
            return true;
        }

        public int ServerReturnAllDisplayedToStorage()
        {
            if (!IsServer) return 0;
            int movedCount = 0;
            while (ShopContainerRules.FindFirst(ItemContainers, ShopContainerRules.SharedOwner,
                       ShopContainerKind.SharedDisplay) >= 0)
            {
                if (!ServerTryMoveItem(ShopContainerRules.SharedOwner, ShopContainerKind.SharedDisplay,
                        ShopContainerKind.SharedStorage, out _))
                {
                    ServerSetEvent("공용 창고가 가득 차 일부 진열 상품은 진열대에 남았습니다.");
                    break;
                }
                movedCount++;
            }
            SyncLegacyContainerCounts();
            return movedCount;
        }

        private bool TryAddToContainer(ulong owner, ShopContainerKind container,
            ShopProductDefinition product, int visualPrefabIndex)
        {
            return TryAddToContainer(owner, container, product, visualPrefabIndex,
                CapacityFor(container));
        }

        private bool TryAddToContainer(ulong owner, ShopContainerKind container,
            ShopProductDefinition product, int visualPrefabIndex, int capacity)
        {
            ShopContainerItem item = new(owner, container, -1, product, visualPrefabIndex);
            return TryAddExistingToContainer(owner, container, item, capacity, out _);
        }

        private int CapacityFor(ShopContainerKind container) => container switch
        {
            ShopContainerKind.PersonalInventory => ShopContainerRules.PersonalCapacity,
            ShopContainerKind.SharedStorage => SharedStorageCapacity,
            ShopContainerKind.SharedDisplay => SharedDisplayCapacity,
            ShopContainerKind.AutomationBuffer => ShopOperationsConfig.Load()?.AutomationBufferSlots ?? 10,
            _ => 0
        };

        private bool TryAddExistingToContainer(ulong owner, ShopContainerKind container,
            ShopContainerItem source, int capacity, out ShopContainerItem added)
        {
            added = default;
            for (int i = 0; i < ItemContainers.Count; i++)
            {
                ShopContainerItem existing = ItemContainers[i];
                if (!ShopContainerRules.BelongsTo(existing, owner, container) ||
                    existing.ProductId != source.ProductId || existing.Quantity >= existing.MaxStack) continue;
                existing.Quantity++;
                ItemContainers[i] = existing;
                added = existing;
                added.Quantity = 1;
                return true;
            }

            if (ShopContainerRules.UsedCount(ItemContainers, owner, container) >= capacity) return false;

            int slot = ShopContainerRules.FindFreeSlot(ItemContainers, owner, container, capacity);
            if (slot < 0) return false;
            source.OwnerClientId = owner;
            source.Container = container;
            source.SlotIndex = slot;
            source.Quantity = 1;
            ItemContainers.Add(source);
            added = source;
            return true;
        }

        public void SyncLegacyContainerCounts()
        {
            if (!IsServer) return;
            Inventory.Value = ShopContainerRules.TotalQuantity(ItemContainers, ShopContainerRules.SharedOwner,
                ShopContainerKind.SharedStorage);
            Displayed.Value = ShopContainerRules.TotalQuantity(ItemContainers, ShopContainerRules.SharedOwner,
                ShopContainerKind.SharedDisplay);
            int rare = 0;
            for (int i = 0; i < ItemContainers.Count; i++)
            {
                ShopContainerItem item = ItemContainers[i];
                if (item.Container == ShopContainerKind.SharedStorage &&
                    item.Rarity >= ShopProductRarity.Rare)
                    rare += item.Quantity;
            }
            RareInventory.Value = rare;
        }

        private static ShopProductDefinition FindProductByCategory(ShopProductCategory category)
        {
            foreach (ShopProductDefinition product in Resources.LoadAll<ShopProductDefinition>("Products"))
                if (product != null && product.Category == category) return product;
            return null;
        }

        private static ShopProductDefinition FindProductByRarity(bool rare)
        {
            ShopProductDefinition[] products = Resources.LoadAll<ShopProductDefinition>("Products");
            ShopProductRarity target = rare ? ShopProductRarity.Rare : ShopProductRarity.Common;
            foreach (ShopProductDefinition product in products)
                if (product != null && product.Rarity == target) return product;
            return products.Length > 0 ? products[0] : null;
        }

        public ShopProductDefinition ResolveAcquisitionProduct(string displayName, bool rare)
        {
            ShopProductDefinition[] products = Resources.LoadAll<ShopProductDefinition>("Products");
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                foreach (ShopProductDefinition product in products)
                    if (product != null && string.Equals(product.DisplayName, displayName,
                            System.StringComparison.OrdinalIgnoreCase))
                        return product;
            }
            Debug.LogWarning("[Acquisition] 표시 이름과 일치하는 상품 데이터가 없습니다: " +
                             displayName + ". 등급 fallback은 사용하지 않습니다.", this);
            return null;
        }

        public void ServerRecordNightSummary(int revenue, int sold, int giveUps,
            int satisfactionTotal, int satisfactionSamples)
        {
            if (!IsServer) return;
            CampaignRevenue.Value += Mathf.Max(0, revenue);
            CampaignSold.Value += Mathf.Max(0, sold);
            CampaignGiveUps.Value += Mathf.Max(0, giveUps);
            CampaignSatisfactionTotal.Value += Mathf.Max(0, satisfactionTotal);
            CampaignSatisfactionSamples.Value += Mathf.Max(0, satisfactionSamples);
        }

        public void ServerRecordProductSale(string productName, int amount)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(productName) || amount <= 0) return;
            campaignProductSales[productName] = campaignProductSales.TryGetValue(productName, out int count)
                ? count + amount
                : amount;
            if (campaignProductSales[productName] <= CampaignTopProductSales.Value) return;
            CampaignTopProductSales.Value = campaignProductSales[productName];
            CampaignTopProductName.Value = new FixedString64Bytes(productName);
        }

        public ShopCampaignResultData ServerCreateCampaignResult(ShopCampaignGradeConfig config)
        {
            ShopCampaignResultData result = new ShopCampaignResultData
            {
                FinalCoins = Coins.Value,
                TotalRevenue = CampaignRevenue.Value,
                TotalSold = CampaignSold.Value,
                TotalAcquired = CampaignAcquired.Value,
                FinalReputation = Reputation.Value,
                AverageSatisfaction = CampaignSatisfactionSamples.Value <= 0
                    ? 0
                    : Mathf.RoundToInt(CampaignSatisfactionTotal.Value / (float)CampaignSatisfactionSamples.Value),
                GiveUpCustomers = CampaignGiveUps.Value,
                ClawSuccesses = CampaignClawSuccesses.Value,
                ClawFailures = CampaignClawFailures.Value,
                TopProductName = CampaignTopProductName.Value
            };
            result.Score = ShopCampaignGradeRules.CalculateScore(result, config);
            result.Grade = new FixedString32Bytes(ShopCampaignGradeRules.CalculateGrade(result, config));
            return result;
        }

        public void ServerSetPhase(ShopPhase phase)
        {
            if (IsServer) Phase.Value = phase;
        }

        public void ServerFinishDayFromTimer()
        {
            if (!IsServer || Phase.Value != ShopPhase.Summary) return;
            UseClosingBell();
        }

        public void ServerSetEvent(string message)
        {
            if (IsServer) SetEvent(message);
        }

        private void SetEvent(string message)
        {
            LastEvent.Value = new FixedString128Bytes(message);
        }

        public bool IsStaffHired(ShopStaffRole role) => (StaffHiredMask.Value & (1 << (int)role)) != 0;
        public bool IsStaffAttending(ShopStaffRole role) => (StaffAttendanceMask.Value & (1 << (int)role)) != 0;

        public void ServerSetStaffAttendance(ShopStaffRole role, bool attending)
        {
            if (!IsServer || !IsStaffHired(role)) return;
            int bit = 1 << (int)role;
            StaffAttendanceMask.Value = attending ? StaffAttendanceMask.Value | bit : StaffAttendanceMask.Value & ~bit;
        }

        public int ServerPayStaffWages()
        {
            if (!IsServer) return 0;
            ShopWorkforceConfig workforce = ShopWorkforceConfig.Load();
            if (workforce == null) return 0;
            int paid = 0;
            int attendance = 0;
            for (int i = 0; i < 3; i++)
            {
                int bit = 1 << i;
                if ((StaffHiredMask.Value & bit) == 0) continue;
                int wage = workforce.DailyWage((ShopStaffRole)i);
                if (Coins.Value < wage) continue;
                Coins.Value -= wage;
                paid += wage;
                attendance |= bit;
            }
            StaffAttendanceMask.Value = attendance;
            return paid;
        }

        public string StaffStatusSummary()
        {
            string[] names = { "계산", "진열", "수거" };
            string result = "알바 ";
            for (int i = 0; i < names.Length; i++)
            {
                int bit = 1 << i;
                string state = (StaffHiredMask.Value & bit) == 0 ? "미고용" :
                    (StaffAttendanceMask.Value & bit) != 0 ? "근무" : "급여 대기";
                result += names[i] + " " + state + (i + 1 < names.Length ? " · " : string.Empty);
            }
            return result;
        }

        public void ServerRestoreUpgradeState(int player, int operations, int facility, int claw,
            int gacha, int kuji, int hiredMask, int attendanceMask)
        {
            if (!IsServer) return;
            PlayerUpgradeLevel.Value = Mathf.Clamp(player, 0, 2);
            ShopUpgradeLevel.Value = Mathf.Clamp(operations, 0, 3);
            FacilityUpgradeLevel.Value = Mathf.Clamp(facility, 0, 2);
            ClawUpgradeLevel.Value = Mathf.Clamp(claw, 0, 2);
            GachaUpgradeLevel.Value = Mathf.Clamp(gacha, 0, 1);
            KujiUpgradeLevel.Value = Mathf.Clamp(kuji, 0, 2);
            StaffHiredMask.Value = hiredMask & 7;
            StaffAttendanceMask.Value = attendanceMask & StaffHiredMask.Value;
        }

        private static int CountBits(int value)
        {
            value &= 7;
            int count = 0;
            while (value != 0) { count += value & 1; value >>= 1; }
            return count;
        }
    }
}
