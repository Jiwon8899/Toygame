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
        public NetworkVariable<int> TotalRevenue = new(0);
        public NetworkVariable<int> SatisfactionTotal = new(0);
        public NetworkVariable<int> SatisfactionSamples = new(0);
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
        private float operatingRemaining;
        private float spawnElapsed;
        private int restockCursor;
        private bool sessionActive;
        private bool checkoutBusy;

        public Vector3 ExitPosition => exitPoint != null ? exitPoint.position : transform.position;
        public Vector3 EntrancePosition => entrancePoint != null ? entrancePoint.position : transform.position;
        public Vector3 RoadsidePosition => roadsideEntryPoint != null ? roadsideEntryPoint.position : EntrancePosition;
        public Vector3 CheckoutPosition => checkoutPoint != null ? checkoutPoint.position : transform.position;
        public Vector3 DisplayWorkPosition => browsePoints != null && browsePoints.Length > 0 && browsePoints[0] != null
            ? browsePoints[0].position : transform.position;
        public bool CheckoutBusy => checkoutBusy;
        public int AverageSatisfaction => SatisfactionSamples.Value <= 0 ? 0 :
            Mathf.RoundToInt(SatisfactionTotal.Value / (float)SatisfactionSamples.Value);
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

        public void ServerHandleRegister()
        {
            if (!IsServer || ShopNetworkGame.Instance == null) return;
            ShopNetworkGame game = ShopNetworkGame.Instance;

            if (game.Phase.Value == ShopPhase.Setup)
            {
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

            StartCoroutine(ServerCheckoutRoutine(queue[0], 1f));
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

        public void ServerTryRestockDisplay(ulong ownerClientId)
        {
            if (!IsServer || ShopNetworkGame.Instance == null) return;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game.Phase.Value != ShopPhase.Setup && game.Phase.Value != ShopPhase.Open)
            {
                game.ServerSetEvent("진열대 보충은 준비 또는 영업 중에만 가능합니다.");
                return;
            }

            if (!game.ServerTryMoveItem(ownerClientId, ShopContainerKind.PersonalInventory,
                    ShopContainerKind.SharedDisplay, out ShopContainerItem moved) &&
                !game.ServerTryMoveItem(ShopContainerRules.SharedOwner, ShopContainerKind.SharedStorage,
                    ShopContainerKind.SharedDisplay, out moved))
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
                var offer = new ShopProductOffer(product.ProductId, product.Category, price,
                    product.Rarity, product.Condition, product.GiftWrappable,
                    ledger.GetAvailable(product.ProductId) > 0);
                if (ShopProductScoring.TryScore(offer, customer.Preference, out float score) && score > bestScore)
                {
                    bestScore = score;
                    productId = product.ProductId;
                    productName = product.DisplayName;
                }
            }

            return productId >= 0 && ledger.TryReserve(customer.NetworkObjectId, productId);
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

        public void ServerRefreshDisplayLedger()
        {
            if (!IsServer) return;
            RebuildLedgerFromNetworkStock();
            SyncStockVariables();
        }

        public void ServerJoinQueue(ShopCustomerNetwork customer)
        {
            if (!IsServer || customer == null || queue.Contains(customer)) return;
            queue.Add(customer);
            QueueCount.Value = queue.Count;
            ShopNetworkGame.Instance.ServerSetEvent(CustomerTypeLabel(customer.CustomerType.Value) + " 손님이 계산 줄에 섰습니다.");
        }

        public Vector3 ServerGetBrowsePoint(ulong customerId)
        {
            int baseCount = browsePoints != null ? browsePoints.Length : 0;
            int extensionCount = ShopExpansionVisualController.CustomerBrowsePointCount;
            int total = baseCount + extensionCount;
            if (total <= 0) return entrancePoint != null ? entrancePoint.position : transform.position;
            int index = (int)(customerId % (ulong)total);
            if (index < baseCount && browsePoints[index] != null) return browsePoints[index].position;
            if (ShopExpansionVisualController.TryGetCustomerBrowsePoint(index - baseCount, out Vector3 extension))
                return extension;
            return PointFromArray(browsePoints, customerId, entrancePoint);
        }

        public Vector3 ServerGetInspectPoint(ulong customerId)
        {
            return PointFromArray(inspectPoints, customerId, entrancePoint);
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
            ledger.CancelReservation(customer.NetworkObjectId);
            QueueCount.Value = queue.Count;
            GiveUpCount.Value++;
            SatisfactionTotal.Value += 25;
            SatisfactionSamples.Value++;
            ReputationDelta.Value--;
            ShopNetworkGame.Instance.Reputation.Value--;
            ShopNetworkGame.Instance.ServerSetEvent("손님이 구매를 포기했습니다: " + reason);
        }

        public void ServerCustomerReachedExit(ShopCustomerNetwork customer)
        {
            if (!IsServer || customer == null) return;
            queue.Remove(customer);
            ledger.CancelReservation(customer.NetworkObjectId);
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
            SatisfactionTotal.Value = 0;
            SatisfactionSamples.Value = 0;
            ReputationDelta.Value = 0;
            TopProductName.Value = new FixedString64Bytes("없음");
            productSales.Clear();
            giveUpsProcessed.Clear();
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
                game.ServerRecordNightSummary(TotalRevenue.Value, TotalSaleQuantity.Value, GiveUpCount.Value,
                    SatisfactionTotal.Value, SatisfactionSamples.Value);
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
            ShopCustomerProfileSelection profile = ShopLiveOperationsNetwork.Instance != null
                ? ShopLiveOperationsNetwork.Instance.ServerSelectCustomerProfile()
                : new ShopCustomerProfileSelection("customer:" + networkObject.NetworkObjectId,
                    archetype.PreferredCategory, 0, 70);
            customer.ServerInitialize(archetype, budget, position, entryTarget, profile);
            activeCustomers[networkObject.NetworkObjectId] = customer;
            VisitCount.Value++;
            CustomersInStore.Value = activeCustomers.Count;
        }

        private IEnumerator ServerCheckoutRoutine(ShopCustomerNetwork customer, float durationMultiplier)
        {
            checkoutBusy = true;
            queue.Remove(customer);
            QueueCount.Value = queue.Count;
            customer.ServerBeginCheckout(checkoutPoint != null ? checkoutPoint.position : transform.position);
            ShopNetworkGame.Instance.ServerSetEvent("계산 중입니다...");
            yield return new WaitForSeconds(GetScaledCheckoutDuration() * Mathf.Max(1f, durationMultiplier));

            if (customer == null || !customer.IsSpawned)
            {
                checkoutBusy = false;
                yield break;
            }

            ShopProductDefinition product = FindProduct(customer.DesiredProductId.Value);
            int price = product != null ? GetSalePrice(product) : 0;
            if (product != null && ShopLiveOperationsNetwork.Instance != null)
                price = Mathf.RoundToInt(price * ShopLiveOperationsNetwork.Instance
                    .RegularPriceMultiplier(customer.CustomerId));
            int coins = ShopNetworkGame.Instance.Coins.Value;
            int sold = ShopNetworkGame.Instance.SoldToday.Value;
            bool completed = ShopSaleProcessor.TryComplete(ledger, customer.NetworkObjectId, price,
                ref coins, ref sold, out int productId);
            if (completed)
            {
                if (!ShopNetworkGame.Instance.ServerTryConsumeDisplayedProduct(productId, out _))
                    Debug.LogError("[Containers] 판매 완료 상품이 공용 진열 컨테이너에 없습니다. product=" +
                                   productId, this);
                int displayedVariety = 0;
                bool rareDisplayed = false;
                if (products != null)
                {
                    foreach (ShopProductDefinition definition in products)
                    {
                        if (definition == null || ledger.GetStock(definition.ProductId) <= 0) continue;
                        displayedVariety++;
                        if (definition.Rarity >= ShopProductRarity.Rare) rareDisplayed = true;
                    }
                }
                int satisfaction = ShopLiveOperationsNetwork.Instance != null
                    ? ShopLiveOperationsNetwork.Instance.CalculateSatisfaction(customer,
                        displayedVariety, rareDisplayed)
                    : ShopProductScoring.CalculateSatisfaction(customer.MatchScore, price,
                        customer.Budget.Value, customer.QueueWaitSeconds, customer.PatienceSeconds, true);
                int reputation = ShopProductScoring.ReputationDelta(satisfaction);
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
                SatisfactionTotal.Value += satisfaction;
                SatisfactionSamples.Value++;
                ReputationDelta.Value += reputation;
                ShopProgressionManager progression = ShopProgressionManager.Instance;
                if (progression == null)
                    Debug.LogError("[Progression] 손님 판매 기록 관리자를 찾지 못했습니다.", this);
                else
                {
                    progression.RecordSale(product != null ? "product:" + product.ProductId : "sale:unknown",
                        product != null ? product.DisplayName : "상품",
                        product != null ? ShopProductLocalization.CategoryId(product.Category) : "cat_goods",
                        price, product != null && product.Rarity >= ShopProductRarity.Rare, satisfaction);
                }
                ShopLiveOperationsNetwork.Instance?.ServerRecordCustomerPurchase(customer.CustomerId,
                    customer.Preference.PreferredCategory, satisfaction);
                productSales[productId] = productSales.TryGetValue(productId, out int count) ? count + 1 : 1;
                UpdateTopProduct();
                SyncStockVariables();
                customer.ServerCompleteCheckout(satisfaction);
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
            return ShopLiveOperationsNetwork.Instance != null
                ? ShopLiveOperationsNetwork.Instance.ApplyTrendPrice(product, product.SalePrice)
                : legacyPrice;
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
