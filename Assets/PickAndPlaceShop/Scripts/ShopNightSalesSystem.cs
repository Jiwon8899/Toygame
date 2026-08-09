using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShopNightSalesSystem : NetworkBehaviour
    {
        public static ShopNightSalesSystem Instance { get; private set; }

        [Header("Network customer")]
        [SerializeField] private GameObject customerPrefab;
        [SerializeField] private ShopCustomerArchetypeDefinition[] customerArchetypes;
        [SerializeField] private ShopProductDefinition[] products;

        [Header("Shop route")]
        [SerializeField] private Transform entrancePoint;
        [SerializeField] private Transform exitPoint;
        [SerializeField] private Transform roadsideEntryPoint;
        [SerializeField] private Transform[] roadsideSpawnPoints;
        [SerializeField] private Transform[] browsePoints;
        [SerializeField] private Transform[] inspectPoints;
        [SerializeField] private Transform[] queuePoints;
        [SerializeField] private Transform checkoutPoint;

        [Header("Balance")]
        [Min(20f)] [SerializeField] private float operatingDurationSeconds = 90f;
        [Min(1f)] [SerializeField] private float baseSpawnIntervalSeconds = 7f;
        [Range(1, 5)] [SerializeField] private int absoluteMaximumCustomers = 5;
        [Range(1f, 2f)] [SerializeField] private float checkoutDurationSeconds = 1.5f;
        [SerializeField] private bool debugLabelsVisible;

        public NetworkVariable<int> CustomersInStore = new(0);
        public NetworkVariable<int> QueueCount = new(0);
        public NetworkVariable<int> EmptyShelfCount = new(0);
        public NetworkVariable<int> CurrentRevenue = new(0);
        public NetworkVariable<int> RemainingSeconds = new(0);
        public NetworkVariable<int> VisitCount = new(0);
        public NetworkVariable<int> PurchaseCustomerCount = new(0);
        public NetworkVariable<int> GiveUpCount = new(0);
        public NetworkVariable<int> TotalSaleQuantity = new(0);
        public NetworkVariable<bool> NegotiationActive = new(false);
        public NetworkVariable<ulong> NegotiationOwner = new(ShopClawRules.NoOccupant);
        public NetworkVariable<int> NegotiationBasePrice = new(0);
        public NetworkVariable<int> NegotiationAttemptsRemaining = new(0);
        public NetworkVariable<bool> DiscountRequestActive = new(false);
        public NetworkVariable<ulong> DiscountRequestOwner = new(ShopClawRules.NoOccupant);
        public NetworkVariable<int> DiscountRequestBasePrice = new(0);
        public NetworkVariable<int> DiscountRequestPercent = new(0);
        public NetworkVariable<bool> CheckoutPromptActive = new(false);
        public NetworkVariable<ulong> CheckoutPromptOwner = new(ShopClawRules.NoOccupant);
        public NetworkVariable<int> CheckoutPromptBasePrice = new(0);
        public NetworkVariable<int> TotalRevenue = new(0);
        public NetworkVariable<int> ReputationDelta = new(0);
        public NetworkVariable<FixedString64Bytes> TopProductName = new(new FixedString64Bytes("없음"));
        public NetworkVariable<bool> SpawnEnabled = new(false);
        public NetworkVariable<bool> DebugLabelsEnabled = new(false);
        public NetworkVariable<int> PlushStock = new(0);
        public NetworkVariable<int> CapsuleStock = new(0);
        public NetworkVariable<int> RareStock = new(0);

        private readonly ShopStockLedger ledger = new();
        private readonly Dictionary<ulong, ShopCustomerNetwork> activeCustomers = new();
        private readonly List<ShopCustomerNetwork> queue = new();
        private readonly Dictionary<int, int> productSales = new();
        private readonly HashSet<ulong> giveUpsProcessed = new();
        private readonly Dictionary<ulong, int> browsePointReservations = new();
        private readonly Dictionary<ulong, int> inspectPointReservations = new();
        private readonly Dictionary<ulong, ShopContainerItem> heldCustomerItems = new();
        private float operatingRemaining;
        private float spawnElapsed;
        private int restockCursor;
        private bool sessionActive;
        private bool checkoutBusy;
        private ShopCustomerNetwork negotiationCustomer;
        private ShopCustomerNetwork discountCustomer;
        private ShopCustomerNetwork checkoutPromptCustomer;

        public Vector3 ExitPosition => exitPoint != null ? exitPoint.position : transform.position;
        public Vector3 EntrancePosition => entrancePoint != null ? entrancePoint.position : transform.position;
        public Vector3 RoadsidePosition => roadsideEntryPoint != null ? roadsideEntryPoint.position : EntrancePosition;
        public Vector3 CheckoutPosition => checkoutPoint != null ? checkoutPoint.position : transform.position;
        public Vector3 DisplayWorkPosition => browsePoints != null && browsePoints.Length > 0 && browsePoints[0] != null
            ? browsePoints[0].position : transform.position;
        public bool CheckoutBusy => checkoutBusy;
        public int CurrentMaximumCustomers => GetScaledMaximumCustomers();
        public float CurrentSpawnInterval => GetScaledSpawnInterval();
        public float CurrentCheckoutDuration => GetScaledCheckoutDuration();

#if UNITY_EDITOR
        public void EditorConfigure(GameObject networkCustomerPrefab,
            ShopCustomerArchetypeDefinition[] archetypes, ShopProductDefinition[] productDefinitions,
            Transform entrance, Transform exit, Transform[] browsing, Transform[] inspecting,
            Transform[] queues, Transform checkout)
        {
            customerPrefab = networkCustomerPrefab;
            customerArchetypes = archetypes;
            products = productDefinitions;
            entrancePoint = entrance;
            exitPoint = exit;
            browsePoints = browsing;
            inspectPoints = inspecting;
            queuePoints = queues;
            checkoutPoint = checkout;
        }

        public void EditorConfigureRoadsideRoute(
            Transform entry, Transform[] spawns, Transform exit)
        {
            roadsideEntryPoint = entry;
            roadsideSpawnPoints = spawns;
            exitPoint = exit;
        }
#endif

        private void Awake()
        {
            Instance = this;
            ShopProductDefinition[] catalog = Resources.LoadAll<ShopProductDefinition>("Products");
            if (catalog != null && catalog.Length > 0) products = catalog;
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;

            if (NetworkManager.NetworkConfig.NetworkTopology == NetworkTopologyTypes.DistributedAuthority)
                NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.SessionOwner, true);

            // World-space state labels are diagnostics, not player UI. They previously piled
            // up over moving customers and looked like duplicated gameplay text.
            DebugLabelsEnabled.Value = false;
            RebuildLedgerFromNetworkStock();
            SyncStockVariables();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || ShopNetworkGame.Instance == null) return;

            if (CheckoutPromptActive.Value &&
                (checkoutPromptCustomer == null || !checkoutPromptCustomer.IsSpawned))
                ClearCheckoutPrompt(true);

            if (ShopNetworkGame.Instance.Phase.Value == ShopPhase.Open && !sessionActive)
            {
                ServerBeginOpenSession();
            }

            if (!sessionActive) return;

            if (SpawnEnabled.Value)
            {
                operatingRemaining = Mathf.Max(0f, operatingRemaining - Time.deltaTime);
                RemainingSeconds.Value = Mathf.CeilToInt(operatingRemaining);
                if (operatingRemaining <= 0f)
                {
                    SpawnEnabled.Value = false;
                    ShopNetworkGame.Instance.ServerSetEvent("영업 시간이 끝났습니다. 남은 손님의 계산을 마쳐 주세요.");
                }
                else
                {
                    spawnElapsed += Time.deltaTime;
                    int maximum = GetScaledMaximumCustomers();
                    if (spawnElapsed >= GetScaledSpawnInterval() &&
                        ledger.TotalStock() > 0 &&
                        ShopProductScoring.CanSpawn(ShopNetworkGame.Instance.Phase.Value, SpawnEnabled.Value,
                            operatingRemaining, activeCustomers.Count, maximum))
                    {
                        spawnElapsed = 0f;
                        ServerSpawnCustomer();
                    }
                }
            }

            CustomersInStore.Value = activeCustomers.Count;
            QueueCount.Value = queue.Count;
            if (!SpawnEnabled.Value && activeCustomers.Count == 0 && !checkoutBusy)
            {
                ServerFinishOpenSession();
            }
        }

        public void ServerHandleRegister(ulong requester = 0)
        {
            if (!IsServer || ShopNetworkGame.Instance == null) return;
            ShopNetworkGame game = ShopNetworkGame.Instance;

            if (game.Phase.Value == ShopPhase.Setup)
            {
                // The previous close returns every display item to storage and clears the
                // sales ledger. Rebuild before evaluating the new day's register interaction.
                RebuildLedgerFromNetworkStock();
                SyncStockVariables();
                if (ledger.TotalStock() <= 0)
                {
                    game.ServerSetEvent("진열대에 상품을 하나 이상 보충해야 영업을 시작할 수 있습니다.");
                    return;
                }

                game.ServerSetPhase(ShopPhase.Open);
                ServerBeginOpenSession();
                game.ServerSetEvent("밤 영업을 시작했습니다. 손님이 오면 계산대에서 E를 눌러 주세요.");
                return;
            }

            if (game.Phase.Value != ShopPhase.Open)
            {
                game.ServerSetEvent("계산대는 준비 단계에서 영업을 열거나 손님을 계산할 때 사용합니다.");
                return;
            }

            if (checkoutBusy)
            {
                game.ServerSetEvent("앞 손님의 계산이 진행 중입니다.");
                return;
            }

            while (queue.Count > 0 && (queue[0] == null || !queue[0].IsSpawned)) queue.RemoveAt(0);
            if (queue.Count == 0)
            {
                game.ServerSetEvent("계산을 기다리는 손님이 없습니다.");
                QueueCount.Value = 0;
                return;
            }

            ShopSideContentConfig sideContent = ShopSideContentConfig.Load();
            if (sideContent != null && Random.value < sideContent.DiscountRequestChance)
            {
                discountCustomer = queue[0];
                queue.RemoveAt(0);
                QueueCount.Value = queue.Count;
                checkoutBusy = true;
                discountCustomer.ServerBeginCheckout(CheckoutPosition);
                ShopProductDefinition product = FindProduct(discountCustomer.DesiredProductId.Value);
                DiscountRequestOwner.Value = requester;
                DiscountRequestBasePrice.Value = product != null ? GetSalePrice(product) : 0;
                DiscountRequestPercent.Value = Mathf.RoundToInt(sideContent.FullDiscount * 100f);
                DiscountRequestActive.Value = true;
                discountCustomer.ServerSetDialogue("저기요, 이 상품 조금만 할인해 주실 수 있나요?");
                game.ServerSetEvent("할인 요청 손님 · 가볍게 선택해 주세요. 거절해도 큰 불이익은 없습니다.");
                Debug.Log("[SideContent:Discount] opened customer=" + discountCustomer.NetworkObjectId +
                          " requested=" + DiscountRequestPercent.Value + "%", this);
                return;
            }

            game.ServerSetEvent("계산 확인 · 바로 결제를 진행합니다. [Shift+E]를 누르면 흥정을 시도할 수 있습니다.");
            checkoutPromptCustomer = queue[0];
            queue.RemoveAt(0);
            QueueCount.Value = queue.Count;
            checkoutBusy = true;
            checkoutPromptCustomer.ServerBeginCheckout(CheckoutPosition);
            ShopProductDefinition checkoutProduct = FindProduct(checkoutPromptCustomer.DesiredProductId.Value);
            CheckoutPromptOwner.Value = requester;
            CheckoutPromptBasePrice.Value = checkoutProduct != null ? GetSalePrice(checkoutProduct) : 0;
            CheckoutPromptActive.Value = true;
            game.ServerSetEvent("계산 확인 창을 열었습니다. 바로 계산하거나 Shift+E로 흥정할 수 있습니다.");
        }

        public void ServerResolveCheckoutPrompt(ulong requester, int choiceIndex)
        {
            if (!IsServer || !CheckoutPromptActive.Value || requester != CheckoutPromptOwner.Value ||
                checkoutPromptCustomer == null || !checkoutPromptCustomer.IsSpawned) return;

            ShopCustomerNetwork customer = checkoutPromptCustomer;
            ClearCheckoutPrompt(false);
            if (choiceIndex == 1)
            {
                negotiationCustomer = customer;
                ShopProductDefinition product = FindProduct(customer.DesiredProductId.Value);
                NegotiationBasePrice.Value = product != null ? GetSalePrice(product) : 0;
                NegotiationOwner.Value = requester;
                NegotiationAttemptsRemaining.Value = ShopOperationsConfig.Load()?.NegotiationAttemptsPerSale ?? 3;
                NegotiationActive.Value = true;
                ShopNetworkGame.Instance.ServerSetEvent("흥정을 시작했습니다. 원하는 제안을 선택해 주세요.");
                return;
            }

            // The confirmation itself is the checkout action.  Do not put the
            // customer back behind the normal E-driven checkout cadence.
            StartCoroutine(ServerCheckoutRoutine(customer, 1f, 1f, true));
        }

        private void ClearCheckoutPrompt(bool releaseCheckout)
        {
            CheckoutPromptActive.Value = false;
            CheckoutPromptOwner.Value = ShopClawRules.NoOccupant;
            CheckoutPromptBasePrice.Value = 0;
            checkoutPromptCustomer = null;
            if (releaseCheckout) checkoutBusy = false;
        }

        public void ServerResolveDiscountChoice(ulong requester, int choiceIndex)
        {
            if (!IsServer || !DiscountRequestActive.Value || requester != DiscountRequestOwner.Value ||
                discountCustomer == null || !discountCustomer.IsSpawned) return;
            ShopSideContentConfig settings = ShopSideContentConfig.Load();
            float discount = choiceIndex == 0
                ? (settings != null ? settings.FullDiscount : 0.15f)
                : choiceIndex == 1 ? (settings != null ? settings.PartialDiscount : 0.07f) : 0f;
            ShopCustomerNetwork customer = discountCustomer;
            DiscountRequestActive.Value = false;
            DiscountRequestOwner.Value = ShopClawRules.NoOccupant;
            DiscountRequestBasePrice.Value = 0;
            DiscountRequestPercent.Value = 0;
            discountCustomer = null;
            customer.ServerSetDialogue(choiceIndex == 2 ? "알겠습니다. 정가로 살게요!" : "고맙습니다!");
            ShopNetworkGame.Instance.ServerSetEvent(choiceIndex == 2
                ? "할인을 정중히 거절했습니다. 손님은 정가로 구매합니다."
                : "할인 요청을 조정해 결제를 진행합니다.");
            Debug.Log("[SideContent:Discount] choice=" + choiceIndex + " discount=" + discount, this);
            StartCoroutine(ServerCheckoutRoutine(customer, 1f, Mathf.Max(0.5f, 1f - discount)));
        }

        public void ServerBeginNegotiation(ulong requester)
        {
            if (!IsServer || ShopNetworkGame.Instance == null) return;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game.Phase.Value != ShopPhase.Open)
            {
                game.ServerSetEvent("흥정은 영업 중 계산을 기다리는 손님에게만 시도할 수 있습니다.");
                return;
            }
            if (checkoutBusy || NegotiationActive.Value)
            {
                game.ServerSetEvent("앞 손님의 계산이 진행 중입니다.");
                return;
            }
            while (queue.Count > 0 && (queue[0] == null || !queue[0].IsSpawned)) queue.RemoveAt(0);
            if (queue.Count == 0)
            {
                game.ServerSetEvent("흥정할 손님이 계산대에 없습니다.");
                QueueCount.Value = 0;
                return;
            }

            negotiationCustomer = queue[0];
            queue.RemoveAt(0);
            QueueCount.Value = queue.Count;
            checkoutBusy = true;
            negotiationCustomer.ServerBeginCheckout(CheckoutPosition);
            ShopProductDefinition product = FindProduct(negotiationCustomer.DesiredProductId.Value);
            NegotiationBasePrice.Value = product != null ? GetSalePrice(product) : 0;
            NegotiationOwner.Value = requester;
            NegotiationAttemptsRemaining.Value = ShopOperationsConfig.Load()?.NegotiationAttemptsPerSale ?? 3;
            NegotiationActive.Value = true;
            game.ServerSetEvent("흥정 시작 · 원하는 가격 제안을 선택해 주세요.");
        }

        public void ServerResolveNegotiationOffer(ulong requester, int offerIndex)
        {
            if (!IsServer || !NegotiationActive.Value || requester != NegotiationOwner.Value ||
                negotiationCustomer == null || !negotiationCustomer.IsSpawned) return;
            ShopOperationsConfig settings = ShopOperationsConfig.Load();
            ShopNegotiationOffer offer = settings != null
                ? settings.NegotiationOfferAt(offerIndex)
                : new ShopNegotiationOffer("조금 더 얹어서", 0.10f, 0.80f, "성공 높음");
            if (ShopNegotiationRules.Succeeds(Random.value, offer.SuccessChance))
            {
                ShopCustomerNetwork customer = negotiationCustomer;
                ClearNegotiation(false);
                StartCoroutine(ServerCheckoutRoutine(customer, 1f, 1f + offer.PriceBonus));
                ShopNetworkGame.Instance.ServerSetEvent(offer.Label + " 성공! 판매가가 " +
                    Mathf.RoundToInt(offer.PriceBonus * 100f) + "% 올랐습니다.");
                return;
            }

            NegotiationAttemptsRemaining.Value = Mathf.Max(0, NegotiationAttemptsRemaining.Value - 1);
            if (NegotiationAttemptsRemaining.Value > 0)
            {
                ShopNetworkGame.Instance.ServerSetEvent("흥정 실패 · 남은 기회 " +
                    NegotiationAttemptsRemaining.Value + "회");
                return;
            }

            ShopCustomerNetwork failedCustomer = negotiationCustomer;
            ClearNegotiation(true);
            failedCustomer.ServerGiveUp("흥정 결렬");
            ShopNetworkGame.Instance.ServerSetEvent("흥정 기회를 모두 사용해 거래가 성사되지 않았습니다.");
        }

        private void ClearNegotiation(bool releaseCheckout)
        {
            NegotiationActive.Value = false;
            NegotiationOwner.Value = ShopClawRules.NoOccupant;
            NegotiationBasePrice.Value = 0;
            NegotiationAttemptsRemaining.Value = 0;
            negotiationCustomer = null;
            if (releaseCheckout) checkoutBusy = false;
        }

        public bool ServerTryStaffCheckout(float durationMultiplier)
        {
            if (!IsServer || checkoutBusy || ShopNetworkGame.Instance == null ||
                ShopNetworkGame.Instance.Phase.Value != ShopPhase.Open) return false;
            while (queue.Count > 0 && (queue[0] == null || !queue[0].IsSpawned)) queue.RemoveAt(0);
            if (queue.Count == 0) return false;
            if (NetworkManager != null)
            {
                float range = ShopOperationsConfig.Load()?.InteractionDistance ?? 2.5f;
                foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
                    if (client.PlayerObject != null &&
                        Vector3.Distance(client.PlayerObject.transform.position, CheckoutPosition) <= range)
                        return false;
            }
            StartCoroutine(ServerCheckoutRoutine(queue[0], Mathf.Max(1f, durationMultiplier)));
            return true;
        }

        public bool ServerTryStaffRestockDisplay()
        {
            if (!IsServer || ShopNetworkGame.Instance == null ||
                (ShopNetworkGame.Instance.Phase.Value != ShopPhase.Setup &&
                 ShopNetworkGame.Instance.Phase.Value != ShopPhase.Open)) return false;

            ShopProductDefinition selected = null;
            int selectedQuantity = -1;
            ShopProductCategory trend = ShopLiveOperationsNetwork.Instance != null
                ? ShopLiveOperationsNetwork.Instance.CurrentTrendCategory : ShopProductCategory.CatPlush;
            foreach (ShopProductDefinition product in products)
            {
                if (product == null) continue;
                int quantity = ShopNetworkGame.Instance.GetSharedProductQuantity(product.ProductId, false);
                if (quantity <= 0) continue;
                bool trendPreferred = product.Category == trend;
                bool currentTrend = selected != null && selected.Category == trend;
                if (selected == null || (trendPreferred && !currentTrend) ||
                    trendPreferred == currentTrend && quantity > selectedQuantity)
                {
                    selected = product;
                    selectedQuantity = quantity;
                }
            }
            if (selected == null || !ShopNetworkGame.Instance.ServerTryMoveItem(
                    ShopContainerRules.SharedOwner, ShopContainerKind.SharedStorage,
                    ShopContainerKind.SharedDisplay, out ShopContainerItem moved, selected.ProductId)) return false;
            RebuildLedgerFromNetworkStock();
            SyncStockVariables();
            ShopNetworkGame.Instance.ServerSetEvent("진열 알바가 " + moved.DisplayName + " 1개를 진열했습니다.");
            return true;
        }

        public void ServerTryRestockDisplay(ulong ownerClientId, int productId = -1)
        {
            if (!IsServer || ShopNetworkGame.Instance == null) return;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game.Phase.Value != ShopPhase.Setup && game.Phase.Value != ShopPhase.Open)
            {
                game.ServerSetEvent("진열대 보충은 준비 또는 영업 중에만 가능합니다.");
                return;
            }

            if (!game.ServerTryMoveItem(ownerClientId, ShopContainerKind.PersonalInventory,
                    ShopContainerKind.SharedDisplay, out ShopContainerItem moved, productId) &&
                !game.ServerTryMoveItem(ShopContainerRules.SharedOwner, ShopContainerKind.SharedStorage,
                    ShopContainerKind.SharedDisplay, out moved, productId))
            {
                game.ServerSetEvent("개인 인벤토리와 공용 창고에 진열할 상품이 없습니다.");
                return;
            }

            RebuildLedgerFromNetworkStock();
            SyncStockVariables();
            game.ServerSetEvent(moved.DisplayName + " 상품 1개를 진열했습니다.");
            ShopTutorialRuntime.Report(ShopTutorialAction.ProductDisplayed);
        }

        public bool ServerTrySelectAndReserve(ShopCustomerNetwork customer, out int productId,
            out string productName, out float bestScore)
        {
            productId = -1;
            productName = "상품 없음";
            bestScore = float.NegativeInfinity;
            if (!IsServer || customer == null || products == null) return false;

            foreach (ShopProductDefinition product in products)
            {
                if (product == null) continue;
                int price = GetSalePrice(product);
                int available = ledger.GetAvailable(product.ProductId);
                var offer = new ShopProductOffer(product.ProductId, product.Category, price,
                    product.Rarity, product.Condition, product.GiftWrappable,
                    available > 0);
                if (ShopProductScoring.TryScore(offer, customer.Preference, out float score) && score > bestScore)
                {
                    bestScore = score;
                    productId = product.ProductId;
                    productName = product.DisplayName;
                }

                if (Debug.isDebugBuild && available > 0)
                {
                    Debug.Log($"[SalesFlow:Offer] customer={customer.NetworkObjectId} " +
                              $"product={product.ProductId}:{product.DisplayName} category={product.Category} " +
                              $"price={price} budget={customer.Budget.Value} available={available}", this);
                }
            }

            bool reserved = productId >= 0 && ledger.TryReserve(customer.NetworkObjectId, productId);
            if (reserved && !customer.IsRobber.Value)
            {
                ShopNetworkGame game = ShopNetworkGame.Instance;
                ShopContainerItem pickedUp = default;
                bool removed = game != null &&
                               game.ServerTryConsumeDisplayedProduct(productId, out pickedUp);
                bool pickedUpInLedger = removed &&
                                        ledger.TryPickupReservation(customer.NetworkObjectId,
                                            out int pickedUpProduct) &&
                                        pickedUpProduct == productId;
                if (!pickedUpInLedger)
                {
                    if (removed) game.ServerTryRestoreDisplayedProduct(pickedUp);
                    ledger.CancelReservation(customer.NetworkObjectId, false);
                    reserved = false;
                }
                else
                {
                    heldCustomerItems[customer.NetworkObjectId] = pickedUp;
                    customer.ServerSetHeldProduct(pickedUp.ProductId, pickedUp.VisualPrefabIndex);
                    SyncStockVariables();
                    if (Debug.isDebugBuild)
                        Debug.Log($"[SalesFlow:PickupStock] customer={customer.NetworkObjectId} " +
                                  $"product={pickedUp.ProductId} display={game.Displayed.Value}", this);
                }
            }
            if (Debug.isDebugBuild)
            {
                Debug.Log($"[CustomerMatch] customer={customer.NetworkObjectId} " +
                          $"preference={customer.Preference.PreferredCategory} product={productId} " +
                          $"score={bestScore:0.00} reserved={reserved} displayed={ledger.TotalStock()}");
                Debug.Log($"[SalesFlow:Match] customer={customer.NetworkObjectId} " +
                          $"preferred={customer.Preference.PreferredCategory} selected={productId} " +
                          $"reserved={reserved} budget={customer.Budget.Value}", this);
            }

            return reserved;
        }

        public bool ServerIsCategoryDisplayed(ShopProductCategory category)
        {
            if (products == null) return false;
            for (int i = 0; i < products.Length; i++)
            {
                ShopProductDefinition product = products[i];
                if (product != null && product.Category == category && ledger.GetStock(product.ProductId) > 0)
                    return true;
            }
            return false;
        }

        public int ServerDisplayedCategoryCount()
        {
            if (products == null) return 0;
            var categories = new HashSet<ShopProductCategory>();
            for (int i = 0; i < products.Length; i++)
            {
                ShopProductDefinition product = products[i];
                if (product != null && ledger.GetStock(product.ProductId) > 0)
                    categories.Add(product.Category);
            }
            return categories.Count;
        }

        public void ServerRefreshDisplayLedger()
        {
            if (!IsServer) return;
            RebuildLedgerFromNetworkStock();
            SyncStockVariables();
        }

        public void ServerJoinQueue(ShopCustomerNetwork customer)
        {
            if (!IsServer || customer == null || queue.Contains(customer)) return;
            ReleaseCustomerPoints(customer.NetworkObjectId);
            queue.Add(customer);
            QueueCount.Value = queue.Count;
            if (Debug.isDebugBuild)
                Debug.Log($"[SalesFlow:Queue] customer={customer.NetworkObjectId} position={queue.Count - 1}", this);
            ShopNetworkGame.Instance.ServerSetEvent(CustomerTypeLabel(customer.CustomerType.Value) + " 손님이 계산 줄에 섰습니다.");
        }

        public Vector3 ServerGetBrowsePoint(ulong customerId)
        {
            int baseCount = browsePoints != null ? browsePoints.Length : 0;
            int extensionCount = ShopExpansionVisualController.CustomerBrowsePointCount;
            int total = baseCount + extensionCount;
            if (total <= 0) return entrancePoint != null ? entrancePoint.position : transform.position;
            int index = ReservePoint(browsePointReservations, customerId, total);
            if (index < baseCount && browsePoints[index] != null) return browsePoints[index].position;
            if (ShopExpansionVisualController.TryGetCustomerBrowsePoint(index - baseCount, out Vector3 extension))
                return extension;
            return PointFromArray(browsePoints, customerId, entrancePoint);
        }

        public Vector3 ServerGetInspectPoint(ulong customerId)
        {
            browsePointReservations.Remove(customerId);
            int count = inspectPoints != null ? inspectPoints.Length : 0;
            if (count <= 0) return entrancePoint != null ? entrancePoint.position : transform.position;
            int index = ReservePoint(inspectPointReservations, customerId, count);
            return inspectPoints[index] != null ? inspectPoints[index].position :
                PointFromArray(inspectPoints, customerId, entrancePoint);
        }

        public Vector3 ServerGetQueuePosition(ShopCustomerNetwork customer)
        {
            int index = Mathf.Max(0, queue.IndexOf(customer));
            if (queuePoints == null || queuePoints.Length == 0)
            {
                return checkoutPoint != null ? checkoutPoint.position + Vector3.back * (index + 1) : transform.position;
            }

            if (index < queuePoints.Length) return queuePoints[index].position;
            return queuePoints[queuePoints.Length - 1].position + Vector3.back * (index - queuePoints.Length + 1) * 1.1f;
        }

        public void ServerRegisterGiveUp(ShopCustomerNetwork customer, string reason)
        {
            if (!IsServer || customer == null || !giveUpsProcessed.Add(customer.NetworkObjectId)) return;
            queue.Remove(customer);
            ServerReleaseCustomerProduct(customer);
            ReleaseCustomerPoints(customer.NetworkObjectId);
            QueueCount.Value = queue.Count;
            GiveUpCount.Value++;
            int penalty = ShopLiveOperationsNetwork.Instance != null
                ? ShopLiveOperationsNetwork.Instance.Config.NoPurchaseReputationPenalty : 1;
            ReputationDelta.Value -= penalty;
            ShopNetworkGame.Instance.Reputation.Value -= penalty;
            ShopNetworkGame.Instance.ServerSetEvent("손님이 구매를 포기했습니다: " + reason);
        }

        public void ServerRegisterAttackExit(ShopCustomerNetwork customer)
        {
            if (!IsServer || customer == null || !giveUpsProcessed.Add(customer.NetworkObjectId)) return;
            queue.Remove(customer);
            ServerReleaseCustomerProduct(customer);
            ReleaseCustomerPoints(customer.NetworkObjectId);
            QueueCount.Value = queue.Count;
            GiveUpCount.Value++;
            ShopNetworkGame.Instance.ServerSetEvent("공격받은 손님이 구매하지 않고 떠납니다.");
        }

        public void ServerCustomerReachedExit(ShopCustomerNetwork customer)
        {
            if (!IsServer || customer == null) return;
            queue.Remove(customer);
            ServerReleaseCustomerProduct(customer);
            ReleaseCustomerPoints(customer.NetworkObjectId);
            activeCustomers.Remove(customer.NetworkObjectId);
            QueueCount.Value = queue.Count;
            CustomersInStore.Value = activeCustomers.Count;
            if (customer.NetworkObject != null && customer.NetworkObject.IsSpawned)
            {
                customer.NetworkObject.Despawn(true);
            }
        }

        public void ServerRequestClose()
        {
            if (!IsServer || !sessionActive) return;
            SpawnEnabled.Value = false;
            RemainingSeconds.Value = 0;
            ShopNetworkGame.Instance.ServerSetEvent("입장을 마감했습니다. 매장 안 손님을 모두 응대해 주세요.");
            if (activeCustomers.Count == 0 && !checkoutBusy) ServerFinishOpenSession();
        }

        public void ServerPrepareForNextDay()
        {
            if (!IsServer) return;
            sessionActive = false;
            checkoutBusy = false;
            SpawnEnabled.Value = false;
            RemainingSeconds.Value = 0;
            if (NegotiationActive.Value) ClearNegotiation(true);
            queue.Clear();
            QueueCount.Value = 0;
            RebuildLedgerFromNetworkStock();
            SyncStockVariables();
        }

        public void ServerTakeUnsoldStock(out int total, out int rare)
        {
            total = 0;
            rare = 0;
            if (!IsServer || products == null) return;

            foreach (ShopProductDefinition product in products)
            {
                if (product == null) continue;
                int amount = ledger.GetStock(product.ProductId);
                total += amount;
                if (product.Rarity == ShopProductRarity.Rare) rare += amount;
                ledger.SetStock(product.ProductId, 0);
            }

            ledger.ResetTransactions();
            SyncStockVariables();
        }

        private void ServerBeginOpenSession()
        {
            if (!IsServer || sessionActive) return;
            // Containers are the authoritative inventory. Rebuild immediately before opening
            // so restored saves and drag/drop changes can never diverge from customer offers.
            RebuildLedgerFromNetworkStock();
            sessionActive = true;
            checkoutBusy = false;
            operatingRemaining = operatingDurationSeconds;
            spawnElapsed = GetScaledSpawnInterval() - 1f;
            SpawnEnabled.Value = true;
            RemainingSeconds.Value = Mathf.CeilToInt(operatingRemaining);
            VisitCount.Value = 0;
            PurchaseCustomerCount.Value = 0;
            GiveUpCount.Value = 0;
            TotalSaleQuantity.Value = 0;
            TotalRevenue.Value = 0;
            CurrentRevenue.Value = 0;
            ReputationDelta.Value = 0;
            TopProductName.Value = new FixedString64Bytes("없음");
            productSales.Clear();
            giveUpsProcessed.Clear();
            browsePointReservations.Clear();
            inspectPointReservations.Clear();
            ledger.ResetTransactions();
        }

        private void ServerFinishOpenSession()
        {
            if (!IsServer || !sessionActive) return;
            sessionActive = false;
            SpawnEnabled.Value = false;
            RemainingSeconds.Value = 0;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game != null)
            {
                game.ServerRecordNightSummary(TotalRevenue.Value, TotalSaleQuantity.Value, GiveUpCount.Value);
                ShopDifferentiationController.Instance?.ServerGenerateDailyReview(
                    game.Day.Value,
                    TotalSaleQuantity.Value,
                    ShopLiveOperationsNetwork.Instance != null
                        ? ShopLiveOperationsNetwork.Instance.DailySalesGoal.Value : 1,
                    ShopLiveOperationsNetwork.Instance != null
                        ? ShopLiveOperationsNetwork.Instance.CurrentTrendCategory
                        : ShopProductCategory.Other);
                foreach (KeyValuePair<int, int> sale in productSales)
                {
                    ShopProductDefinition product = FindProduct(sale.Key);
                    if (product != null) game.ServerRecordProductSale(product.DisplayName, sale.Value);
                }
            }
            ShopNetworkGame.Instance.ServerSetPhase(ShopPhase.Summary);
            ShopNetworkGame.Instance.ServerSetEvent("영업을 마쳤습니다. 정산을 확인하고 마감 종을 울리세요.");
        }

        private void ServerSpawnCustomer()
        {
            if (customerPrefab == null || customerArchetypes == null || customerArchetypes.Length == 0) return;
            ShopCustomerArchetypeDefinition archetype = ChooseArchetype();
            if (archetype == null) return;

            Vector3 position = ChooseRoadsideSpawnPosition();
            GameObject instance = Instantiate(customerPrefab, position, Quaternion.identity);
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            ShopCustomerNetwork customer = instance.GetComponent<ShopCustomerNetwork>();
            if (networkObject == null || customer == null)
            {
                Destroy(instance);
                return;
            }

            networkObject.Spawn(true);
            int budget = Random.Range(archetype.BudgetMin, archetype.BudgetMax + 1);
            Vector3 entryTarget = roadsideEntryPoint != null
                ? roadsideEntryPoint.position
                : ServerGetBrowsePoint(networkObject.NetworkObjectId);
            customer.ServerInitialize(archetype, budget, position, entryTarget);
            customer.ServerConfigureRobber(ShopNetworkGame.Instance != null &&
                                           ShopNetworkGame.Instance.ServerTryClaimRobberSpawn());
            activeCustomers[networkObject.NetworkObjectId] = customer;
            VisitCount.Value++;
            CustomersInStore.Value = activeCustomers.Count;
            if (Debug.isDebugBuild)
            {
                Debug.Log($"[SalesFlow:Spawn] customer={networkObject.NetworkObjectId} " +
                          $"budget={budget} preferred={archetype.PreferredCategory} spawn={position}", this);
            }
        }

        public bool ServerRobberSteal(ShopCustomerNetwork customer, int productId,
            out ShopContainerItem stolen)
        {
            stolen = default;
            if (!IsServer || customer == null || ShopNetworkGame.Instance == null) return false;
            ledger.CancelReservation(customer.NetworkObjectId);
            bool removed = ShopNetworkGame.Instance.ServerTryConsumeDisplayedProduct(productId, out stolen);
            RebuildLedgerFromNetworkStock();
            SyncStockVariables();
            return removed;
        }

        private void ServerReleaseCustomerProduct(ShopCustomerNetwork customer)
        {
            if (customer == null) return;
            ulong customerId = customer.NetworkObjectId;
            bool restoredToDisplay = false;
            if (heldCustomerItems.Remove(customerId, out ShopContainerItem held))
            {
                bool preserved = ShopNetworkGame.Instance != null &&
                                 ShopNetworkGame.Instance.ServerTryRestoreDisplayedProduct(held,
                                     out restoredToDisplay);
                if (!preserved)
                    Debug.LogError("[SalesFlow:RestoreFailed] customer=" + customerId +
                                   " product=" + held.ProductId, this);
                customer.ServerSetHeldProduct(-1, -1);
            }
            ledger.CancelReservation(customerId, restoredToDisplay);
            SyncStockVariables();
        }

        private IEnumerator ServerCheckoutRoutine(ShopCustomerNetwork customer, float durationMultiplier,
            float salePriceMultiplier = 1f, bool skipCheckoutDelay = false)
        {
            checkoutBusy = true;
            queue.Remove(customer);
            QueueCount.Value = queue.Count;
            customer.ServerBeginCheckout(checkoutPoint != null ? checkoutPoint.position : transform.position);
            if (Debug.isDebugBuild)
                Debug.Log($"[SalesFlow:CheckoutBegin] customer={customer.NetworkObjectId} " +
                          $"product={customer.DesiredProductId.Value}", this);
            ShopNetworkGame.Instance.ServerSetEvent("계산 중입니다...");
            if (!skipCheckoutDelay)
                yield return new WaitForSeconds(GetScaledCheckoutDuration() * Mathf.Max(1f, durationMultiplier));

            if (customer == null || !customer.IsSpawned)
            {
                checkoutBusy = false;
                yield break;
            }

            ShopProductDefinition product = FindProduct(customer.DesiredProductId.Value);
            int price = product != null ? GetSalePrice(product) : 0;
            bool hasPickedUpItem = heldCustomerItems.TryGetValue(customer.NetworkObjectId,
                out ShopContainerItem displayedItem);
            if (!hasPickedUpItem)
                hasPickedUpItem = ShopNetworkGame.Instance.ServerTryPeekDisplayedProduct(
                    customer.DesiredProductId.Value, out displayedItem);
            if (product != null && hasPickedUpItem && displayedItem.IsAppraised)
            {
                float trendFactor = product.SalePrice > 0
                    ? price / (float)product.SalePrice : 1f;
                price = Mathf.RoundToInt(displayedItem.UnitPrice * trendFactor);
            }
            price = ShopSideContentRules.ApplySaleMultiplier(price, salePriceMultiplier);
            int coins = ShopNetworkGame.Instance.Coins.Value;
            int sold = ShopNetworkGame.Instance.SoldToday.Value;
            bool completed = ShopSaleProcessor.TryComplete(ledger, customer.NetworkObjectId, price,
                ref coins, ref sold, out int productId);
            if (completed)
            {
                heldCustomerItems.Remove(customer.NetworkObjectId);
                int reputation = ShopLiveOperationsNetwork.Instance != null
                    ? ShopLiveOperationsNetwork.Instance.Config.SuccessfulSaleReputationReward : 1;
                ShopNetworkGame.Instance.Coins.Value = coins;
                ShopNetworkGame.Instance.SoldToday.Value = sold;
                ShopNetworkGame.Instance.Reputation.Value += reputation;
                if (product != null && product.Rarity >= ShopProductRarity.Rare)
                {
                    ShopNetworkGame.Instance.RareSoldToday.Value++;
                }

                CurrentRevenue.Value += price;
                TotalRevenue.Value += price;
                TotalSaleQuantity.Value++;
                PurchaseCustomerCount.Value++;
                ReputationDelta.Value += reputation;
                ShopProgressionManager progression = ShopProgressionManager.Instance;
                if (progression == null)
                    Debug.LogError("[Progression] 손님 판매 기록 관리자를 찾지 못했습니다.", this);
                else
                {
                    progression.RecordSale(product != null ? "product:" + product.ProductId : "sale:unknown",
                        product != null ? product.DisplayName : "상품",
                        product != null ? ShopProductLocalization.CategoryId(product.Category) : "cat_goods",
                        price, product != null && product.Rarity >= ShopProductRarity.Rare);
                }
                productSales[productId] = productSales.TryGetValue(productId, out int count) ? count + 1 : 1;
                UpdateTopProduct();
                SyncStockVariables();
                customer.ServerCompleteCheckout();
                if (Debug.isDebugBuild)
                    Debug.Log($"[SalesFlow:Purchase] customer={customer.NetworkObjectId} product={productId} " +
                              $"price={price} total={PurchaseCustomerCount.Value}", this);
                ShopNetworkGame.Instance.ServerSetEvent((product != null ? product.DisplayName : "상품") +
                    " 판매 완료! 가게 자금 +" + price);
            }
            else
            {
                customer.ServerGiveUp("결제 시 재고 검증 실패");
            }

            checkoutBusy = false;
        }

        private ShopCustomerArchetypeDefinition ChooseArchetype()
        {
            float total = 0f;
            foreach (ShopCustomerArchetypeDefinition item in customerArchetypes)
                if (item != null) total += item.SpawnWeight;
            float roll = Random.value * Mathf.Max(0.01f, total);
            foreach (ShopCustomerArchetypeDefinition item in customerArchetypes)
            {
                if (item == null) continue;
                roll -= item.SpawnWeight;
                if (roll <= 0f) return item;
            }
            return customerArchetypes[0];
        }

        private ShopProductDefinition ChooseRestockProduct(bool hasRare)
        {
            if (products == null || products.Length == 0) return null;
            if (hasRare)
            {
                foreach (ShopProductDefinition item in products)
                    if (item != null && item.Rarity == ShopProductRarity.Rare) return item;
            }

            for (int i = 0; i < products.Length; i++)
            {
                restockCursor = (restockCursor + 1) % products.Length;
                ShopProductDefinition candidate = products[restockCursor];
                if (candidate != null && candidate.Rarity != ShopProductRarity.Rare) return candidate;
            }
            return products[0];
        }

        private ShopProductDefinition FindProduct(int productId)
        {
            if (products == null) return null;
            foreach (ShopProductDefinition product in products)
                if (product != null && product.ProductId == productId) return product;
            return null;
        }

        private int GetSalePrice(ShopProductDefinition product)
        {
            int trend = ShopNetworkGame.Instance != null ? ShopNetworkGame.Instance.TrendPercent.Value : 0;
            int legacyPrice = Mathf.Max(1, Mathf.RoundToInt(product.SalePrice * (1f + trend / 100f)));
            int basePrice = ShopLiveOperationsNetwork.Instance != null
                ? ShopLiveOperationsNetwork.Instance.ApplyTrendPrice(product, product.SalePrice)
                : legacyPrice;
            float collectionMultiplier = ShopProgressionManager.Instance != null
                ? ShopProgressionManager.Instance.CollectionSaleMultiplier(
                    ShopProductLocalization.CategoryId(product.Category))
                : 1f;
            return Mathf.Max(1, Mathf.RoundToInt(basePrice * collectionMultiplier));
        }

        private int GetScaledMaximumCustomers()
        {
            int players = NetworkManager != null ? Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count) : 1;
            int upgradeBonus = ShopNetworkGame.Instance != null
                ? ShopNetworkGame.Instance.CustomerCapacityBonus
                : 0;
            int configuredMaximum = ShopLiveOperationsNetwork.Instance != null
                ? ShopLiveOperationsNetwork.Instance.Config.MaximumConcurrentCustomers
                : absoluteMaximumCustomers;
            return Mathf.Min(configuredMaximum, players + 2 + upgradeBonus);
        }

        private float GetScaledSpawnInterval()
        {
            int players = NetworkManager != null ? Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count) : 1;
            float upgradeReduction = ShopNetworkGame.Instance != null
                ? ShopNetworkGame.Instance.CustomerSpawnIntervalReduction
                : 0f;
            int reputation = ShopNetworkGame.Instance != null ? ShopNetworkGame.Instance.Reputation.Value : 0;
            float reputationMultiplier = Mathf.Lerp(1f, 0.62f, Mathf.Clamp01(reputation / 100f));
            return Mathf.Max(2.5f,
                (baseSpawnIntervalSeconds - (players - 1) * 1.1f - upgradeReduction) *
                reputationMultiplier);
        }

        private float GetScaledCheckoutDuration()
        {
            float upgradeReduction = ShopNetworkGame.Instance != null
                ? ShopNetworkGame.Instance.CheckoutDurationReduction
                : 0f;
            int checkoutCount = ShopNetworkGame.Instance != null
                ? ShopNetworkGame.Instance.SharedCheckoutCount
                : 1;
            return Mathf.Max(0.75f,
                (checkoutDurationSeconds - upgradeReduction) / Mathf.Sqrt(checkoutCount));
        }

        private Vector3 ChooseRoadsideSpawnPosition()
        {
            if (roadsideSpawnPoints != null && roadsideSpawnPoints.Length > 0)
            {
                int startIndex = Random.Range(0, roadsideSpawnPoints.Length);
                for (int offset = 0; offset < roadsideSpawnPoints.Length; offset++)
                {
                    Transform point = roadsideSpawnPoints[(startIndex + offset) % roadsideSpawnPoints.Length];
                    if (point != null) return point.position;
                }
            }

            return entrancePoint != null ? entrancePoint.position : transform.position;
        }

        private static int ReservePoint(Dictionary<ulong, int> reservations, ulong customerId, int count)
        {
            if (reservations.TryGetValue(customerId, out int reserved) && reserved >= 0 && reserved < count)
                return reserved;
            int start = (int)(customerId % (ulong)count);
            for (int offset = 0; offset < count; offset++)
            {
                int candidate = (start + offset) % count;
                if (reservations.ContainsValue(candidate)) continue;
                reservations[customerId] = candidate;
                return candidate;
            }
            reservations[customerId] = start;
            return start;
        }

        private void ReleaseCustomerPoints(ulong customerId)
        {
            browsePointReservations.Remove(customerId);
            inspectPointReservations.Remove(customerId);
        }

        private void RebuildLedgerFromNetworkStock()
        {
            if (products == null) return;
            foreach (ShopProductDefinition product in products)
            {
                if (product == null) continue;
                ledger.SetStock(product.ProductId, 0);
            }
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null) return;
            for (int i = 0; i < game.ItemContainers.Count; i++)
            {
                ShopContainerItem item = game.ItemContainers[i];
                if (item.Container != ShopContainerKind.SharedDisplay || item.Quantity <= 0) continue;
                ledger.AddStock(item.ProductId, item.Quantity);
                if (Debug.isDebugBuild)
                {
                    ShopProductDefinition product = FindProduct(item.ProductId);
                    Debug.Log($"[SalesFlow:Display] product={item.ProductId}:" +
                              $"{(product != null ? product.DisplayName : item.DisplayName.ToString())} " +
                              $"category={(product != null ? product.Category.ToString() : "missing-definition")} " +
                              $"quantity={item.Quantity}", this);
                }
            }
        }

        private void SyncStockVariables()
        {
            PlushStock.Value = ledger.GetStock(0);
            CapsuleStock.Value = ledger.GetStock(1);
            RareStock.Value = ledger.GetStock(2);
            int total = ledger.TotalStock();
            if (ShopNetworkGame.Instance != null) ShopNetworkGame.Instance.Displayed.Value = total;
            int empty = 0;
            if (products != null)
                foreach (ShopProductDefinition product in products)
                    if (product != null && ledger.GetStock(product.ProductId) == 0) empty++;
            EmptyShelfCount.Value = empty;
        }

        private void UpdateTopProduct()
        {
            int bestProduct = -1;
            int bestCount = 0;
            foreach (KeyValuePair<int, int> pair in productSales)
            {
                if (pair.Value > bestCount)
                {
                    bestProduct = pair.Key;
                    bestCount = pair.Value;
                }
            }
            ShopProductDefinition product = FindProduct(bestProduct);
            TopProductName.Value = new FixedString64Bytes(product != null ? product.DisplayName : "없음");
        }

        private static Vector3 PointFromArray(Transform[] points, ulong id, Transform fallback)
        {
            if (points != null && points.Length > 0)
            {
                Transform point = points[(int)(id % (ulong)points.Length)];
                if (point != null) return point.position;
            }
            return fallback != null ? fallback.position : Vector3.zero;
        }

        private static string CustomerTypeLabel(ShopCustomerType type)
        {
            return type switch
            {
                ShopCustomerType.Student => "학생",
                ShopCustomerType.GiftShopper => "선물 구매자",
                ShopCustomerType.Collector => "수집가",
                _ => "일반"
            };
        }
    }
}
