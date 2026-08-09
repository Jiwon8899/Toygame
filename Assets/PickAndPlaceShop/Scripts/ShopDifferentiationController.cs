using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(450)]
    public sealed class ShopDifferentiationController : MonoBehaviour
    {
        public static ShopDifferentiationController Instance { get; private set; }

        private ShopDifferentiationConfig config;
        private GameObject facilitiesRoot;
        private readonly List<GameObject> upcycleDecorations = new();
        private TextMesh reviewBoardText;
        private string lastReviewSnapshot;
        private GameObject consignmentNpc;
        private Transform consignmentVisualRoot;
        private TextMesh consignmentText;
        private readonly List<GameObject> consignmentVisuals = new();
        private string consignmentSnapshot;
        private GameObject capsuleRecyclerFacility;
        private GameObject appraisalFacility;
        private GameObject consignmentFacility;
        private GameObject consignmentRejectFacility;

        public int EmptyCapsuleCount
        {
            get
            {
                ShopNetworkGame game = ShopNetworkGame.Instance;
                ShopProductDefinition empty = config != null ? config.EmptyCapsuleProduct : null;
                if (game == null || empty == null) return 0;
                int total = 0;
                for (int i = 0; i < game.ItemContainers.Count; i++)
                {
                    ShopContainerItem item = game.ItemContainers[i];
                    if (item.Container == ShopContainerKind.CapsuleRecycler && item.ProductId == empty.ProductId)
                        total += Mathf.Max(0, item.Quantity);
                }
                return total;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject host = new("[Shop] Differentiation Systems");
            DontDestroyOnLoad(host);
            Instance = host.AddComponent<ShopDifferentiationController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            config = ShopDifferentiationConfig.Load();
        }

        private void Update()
        {
            if (config == null) config = ShopDifferentiationConfig.Load();
            if (config == null || ShopNetworkGame.Instance == null) return;
            if (facilitiesRoot == null) BuildFacilities();
            RefreshUpcycleDecorations();
            RefreshReviewBoard();
            RefreshFacilityUnlocks();
            ServerUpdateConsignment();
            RefreshConsignmentCorner();
        }

        public bool ServerCollectEmptyCapsule()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsServer || config == null || config.EmptyCapsuleProduct == null)
                return false;
            bool stored = game.ServerTryAcquireSharedContainer(config.EmptyCapsuleProduct, -1,
                ShopContainerKind.CapsuleRecycler, config.CapsuleRecyclerSlots);
            if (!stored)
                game.ServerSetEvent("빈 캡슐 회수함이 가득 찼습니다. 이번 캡슐 껍데기는 안전하게 폐기했습니다.");
            return stored;
        }

        public void ServerHandleInteraction(ShopAction action, ulong requester)
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsServer) return;
            int expansion = ShopProgressionManager.Instance?.ExpansionLevel ?? 1;
            if (action == ShopAction.CapsuleRecycler && expansion < 3 ||
                action == ShopAction.AppraisalDesk && expansion < 4 ||
                (action == ShopAction.ConsignmentCorner || action == ShopAction.ConsignmentReject) && expansion < 5)
            {
                game.ServerSetEvent("이 시설은 가게 확장 후 이용할 수 있습니다.");
                return;
            }
            if (action == ShopAction.CapsuleRecycler) ServerCraftNextDecoration(game);
            else if (action == ShopAction.ReviewBoard)
                game.ServerSetEvent(game.ReviewHistory.Value.ToString());
            else if (action == ShopAction.AppraisalDesk)
            {
                game.ServerTryAppraiseFirstOwned(requester, config, out _, out string message);
                game.ServerSetEvent(message);
                ShopProgressionManager.Instance?.SaveNow();
            }
            else if (action == ShopAction.ConsignmentCorner) ServerAcceptConsignment(game, requester);
            else if (action == ShopAction.ConsignmentReject) ServerRejectConsignment(game);
        }

        private void ServerUpdateConsignment()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsServer || config == null) return;
            if ((ShopProgressionManager.Instance?.ExpansionLevel ?? 1) < 5 ||
                game.Reputation.Value < config.ConsignmentUnlockReputation)
            {
                if (game.ConsignmentOfferCount.Value > 0) ClearConsignmentOffer(game, false);
                return;
            }
            game.ConsignmentSecondsRemaining.Value = Mathf.Max(0f,
                game.ConsignmentSecondsRemaining.Value - Time.deltaTime);
            if (game.ConsignmentSecondsRemaining.Value > 0f) return;
            if (game.ConsignmentOfferCount.Value > 0)
            {
                game.ServerSetEvent("위탁 수집가가 제안을 거두고 돌아갔습니다.");
                ClearConsignmentOffer(game, false);
                return;
            }
            ServerGenerateConsignmentOffer(game);
        }

        private void ServerGenerateConsignmentOffer(ShopNetworkGame game)
        {
            ShopProductDefinition[] catalog = Resources.LoadAll<ShopProductDefinition>("Products/CatCatalog");
            if (catalog == null || catalog.Length == 0)
            {
                game.ConsignmentSecondsRemaining.Value = config.ConsignmentVisitSeconds;
                return;
            }
            int serial = game.ConsignmentOfferSerial.Value + 1;
            Vector2Int range = config.ConsignmentOfferCount;
            int count = range.x + Mathf.Abs(serial * 1103515245 + game.Day.Value) %
                Mathf.Max(1, range.y - range.x + 1);
            count = Mathf.Min(count, config.ConsignmentSlots);
            var excluded = new HashSet<int>();
            for (int slot = 0; slot < count; slot++)
            {
                ShopProductDefinition selected = SelectConsignmentProduct(catalog,
                    serial * 31 + slot * 997 + game.Day.Value, excluded);
                if (selected == null) break;
                excluded.Add(selected.ProductId);
                SetConsignmentOffer(game, slot, selected.ProductId,
                    config.ConsignmentPrice(selected.Rarity));
            }
            game.ConsignmentOfferCount.Value = excluded.Count;
            game.ConsignmentOfferSerial.Value = serial;
            game.ConsignmentSecondsRemaining.Value = config.ConsignmentOfferDurationSeconds;
            game.ServerSetEvent("위탁 수집가가 " + excluded.Count + "개의 상품을 제안했습니다. " +
                                "위탁 코너에서 수락하거나 거절하세요.");
        }

        private ShopProductDefinition SelectConsignmentProduct(ShopProductDefinition[] catalog,
            int seed, HashSet<int> excluded)
        {
            ShopProgressionManager progression = ShopProgressionManager.Instance;
            float missingWeight = config.MissingCollectionWeight;
            var missingProducts = new List<ShopProductDefinition>();
            var ownedProducts = new List<ShopProductDefinition>();
            for (int i = 0; i < catalog.Length; i++)
            {
                ShopProductDefinition product = catalog[i];
                if (product == null || excluded.Contains(product.ProductId)) continue;
                bool missing = progression == null || !progression.OwnsCollectionItem(product.StableItemId);
                (missing ? missingProducts : ownedProducts).Add(product);
            }
            uint hash = unchecked((uint)seed * 747796405u + 2891336453u);
            float groupRoll = (hash & 0x00FFFFFFu) / 16777215f;
            List<ShopProductDefinition> selectedGroup = groupRoll < missingWeight && missingProducts.Count > 0
                ? missingProducts : ownedProducts.Count > 0 ? ownedProducts : missingProducts;
            if (selectedGroup.Count == 0) return null;
            uint itemHash = unchecked(hash * 1664525u + 1013904223u);
            return selectedGroup[(int)(itemHash % (uint)selectedGroup.Count)];
        }

        private void ServerAcceptConsignment(ShopNetworkGame game, ulong requester)
        {
            int count = game.ConsignmentOfferCount.Value;
            if (count <= 0)
            {
                game.ServerSetEvent(game.Reputation.Value < config.ConsignmentUnlockReputation
                    ? "위탁 코너는 평판 " + config.ConsignmentUnlockReputation + "에 열립니다."
                    : "현재 도착한 위탁 제안이 없습니다.");
                return;
            }
            int used = ShopContainerRules.UsedCount(game.ItemContainers, ShopContainerRules.SharedOwner,
                ShopContainerKind.ConsignmentDisplay);
            if (used + count > config.ConsignmentSlots)
            {
                game.ServerSetEvent("위탁 진열 슬롯을 먼저 비워 주세요.");
                return;
            }
            var products = new List<ShopProductDefinition>();
            int total = 0;
            for (int slot = 0; slot < count; slot++)
            {
                int id = GetConsignmentProduct(game, slot);
                ShopProductDefinition product = ShopProductVisuals.Find(id);
                if (product == null || product.ExclusiveReward) return;
                products.Add(product);
                total += GetConsignmentPrice(game, slot);
            }
            if (game.Coins.Value < total)
            {
                game.ServerSetEvent("위탁 제안을 수락하려면 " + total + "원이 필요합니다.");
                return;
            }
            for (int i = 0; i < products.Count; i++)
            {
                if (!game.ServerTryAcquireItem(requester, products[i], -1,
                        ShopAcquisitionSource.Consignment, 0, out _))
                {
                    game.ServerSetEvent("위탁 상품을 진열하지 못했습니다. 슬롯을 확인해 주세요.");
                    return;
                }
            }
            game.Coins.Value -= total;
            game.ServerSetEvent("위탁 상품 " + products.Count + "개를 " + total + "원에 들였습니다.");
            ClearConsignmentOffer(game, true);
            ShopProgressionManager.Instance?.SaveNow();
        }

        private void ServerRejectConsignment(ShopNetworkGame game)
        {
            if (game.ConsignmentOfferCount.Value <= 0)
            {
                game.ServerSetEvent("거절할 위탁 제안이 없습니다.");
                return;
            }
            game.ServerSetEvent("위탁 제안을 정중히 거절했습니다.");
            ClearConsignmentOffer(game, true);
        }

        private void ClearConsignmentOffer(ShopNetworkGame game, bool restartTimer)
        {
            game.ConsignmentOfferCount.Value = 0;
            for (int i = 0; i < 3; i++) SetConsignmentOffer(game, i, -1, 0);
            game.ConsignmentSecondsRemaining.Value = restartTimer || game.Reputation.Value >=
                config.ConsignmentUnlockReputation ? config.ConsignmentVisitSeconds : 0f;
        }

        private static int GetConsignmentProduct(ShopNetworkGame game, int index) => index switch
        {
            0 => game.ConsignmentOfferProduct0.Value,
            1 => game.ConsignmentOfferProduct1.Value,
            _ => game.ConsignmentOfferProduct2.Value
        };

        private static int GetConsignmentPrice(ShopNetworkGame game, int index) => index switch
        {
            0 => game.ConsignmentOfferPrice0.Value,
            1 => game.ConsignmentOfferPrice1.Value,
            _ => game.ConsignmentOfferPrice2.Value
        };

        private static void SetConsignmentOffer(ShopNetworkGame game, int index, int productId, int price)
        {
            if (index == 0) { game.ConsignmentOfferProduct0.Value = productId; game.ConsignmentOfferPrice0.Value = price; }
            else if (index == 1) { game.ConsignmentOfferProduct1.Value = productId; game.ConsignmentOfferPrice1.Value = price; }
            else { game.ConsignmentOfferProduct2.Value = productId; game.ConsignmentOfferPrice2.Value = price; }
        }

        public void ServerGenerateDailyReview(int day, int sold, int dailyGoal,
            ShopProductCategory trendCategory)
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsServer || day <= game.LatestReviewDay.Value) return;

            int stars = config != null ? config.ReviewStars(sold, dailyGoal) : 1;
            string fallback = BuildReviewFallback(sold, dailyGoal, trendCategory);
            CommitReview(day, stars, sold, dailyGoal, trendCategory, fallback);

            ShopNarrativeAIService service = ShopNarrativeAIService.Instance;
            if (service == null) return;
            string prompt = "고양이 소품샵의 하루 손님 리뷰를 한국어 한 문장으로 작성하세요. " +
                            "판매량=" + sold + "개, " +
                            "일일 목표=" + dailyGoal + "개, " +
                            "목표 달성=" + (sold >= dailyGoal ? "예" : "아니오") + ", " +
                            "오늘의 유행=" + ShopProductLocalization.CategoryLabel(trendCategory) +
                            ". 수치나 판정을 바꾸지 말고 실제로 두드러진 장점 또는 단점을 언급하세요.";
            service.Request("daily-review:" + day, prompt, result =>
            {
                if (ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsServer ||
                    ShopNetworkGame.Instance.LatestReviewDay.Value != day) return;
                ShopLiveOperationsNetwork live = ShopLiveOperationsNetwork.Instance;
                if (result.IsApiSuccess && result.HasText)
                {
                    CommitReview(day, stars, sold, dailyGoal, trendCategory, result.Text);
                    if (live != null)
                    {
                        if (result.Kind == ShopNarrativeResultKind.Api) live.NarrativeApiCallsToday.Value++;
                        else live.NarrativeCacheHitsToday.Value++;
                    }
                }
                else if (live != null)
                {
                    live.NarrativeFallbacksToday.Value++;
                    if (result.Kind == ShopNarrativeResultKind.Timeout ||
                        result.Kind == ShopNarrativeResultKind.RequestFailed ||
                        result.Kind == ShopNarrativeResultKind.InvalidResponse)
                        live.NarrativeFailuresToday.Value++;
                }
            });
        }

        private void CommitReview(int day, int stars, int sold, int dailyGoal,
            ShopProductCategory trendCategory, string sentence)
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsServer) return;
            string clean = (sentence ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (clean.Length > 100) clean = clean.Substring(0, 100);
            string line = "[" + day + "일차 " + new string('★', stars) + new string('☆', 5 - stars) +
                          " | 판매 " + sold + "/" + dailyGoal + " · 유행 " +
                          ShopProductLocalization.CategoryLabel(trendCategory) + "] " + clean;
            string current = game.ReviewHistory.Value.ToString();
            var reviews = new List<string>();
            if (!string.IsNullOrWhiteSpace(current) && current != "아직 등록된 리뷰가 없습니다.")
                reviews.AddRange(current.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries));
            string prefix = "[" + day + "일차 ";
            reviews.RemoveAll(value => value.StartsWith(prefix, System.StringComparison.Ordinal));
            reviews.Insert(0, line);
            int capacity = config != null ? config.ReviewHistoryCapacity : 5;
            if (reviews.Count > capacity) reviews.RemoveRange(capacity, reviews.Count - capacity);
            game.ReviewHistory.Value = new Unity.Collections.FixedString4096Bytes(string.Join("\n", reviews));
            game.LatestReviewDay.Value = day;
        }

        private static string BuildReviewFallback(int sold, int dailyGoal,
            ShopProductCategory trendCategory)
        {
            if (sold <= 0) return "오늘은 구매한 손님이 없어 다음 영업을 기대하고 있어요.";
            if (sold >= Mathf.Max(1, dailyGoal) * 1.5f)
                return "오늘의 목표를 훌쩍 넘긴 활기찬 소품샵이었어요.";
            if (sold >= Mathf.Max(1, dailyGoal))
                return "오늘의 판매 목표를 채운 믿음직한 소품샵이었어요.";
            return ShopProductLocalization.CategoryLabel(trendCategory) +
                   " 상품이 눈에 띄었지만 다음에는 목표 판매량도 기대할게요.";
        }

        public void ServerRecordLastOne(string poolId, int setNumber, ulong clientId,
            ShopProductDefinition reward)
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsServer) return;
            game.LastOneAwards.Value++;
            string playerName = "플레이어 " + clientId;
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                client.PlayerObject != null)
                playerName = client.PlayerObject.name;
            string line = playerName + "님이 [세트 #" + setNumber + "]의 " +
                          (reward != null ? reward.DisplayName : "라스트원상") + "을 뽑았습니다!";
            string previous = game.RecentLastOneRecords.Value.ToString();
            if (previous == "기록 없음") previous = string.Empty;
            string combined = string.IsNullOrEmpty(previous) ? line : line + "\n" + previous;
            if (combined.Length > 480) combined = combined.Substring(0, 480);
            game.RecentLastOneRecords.Value = new Unity.Collections.FixedString512Bytes(combined);
            game.ServerSetEvent(line);
        }

        public void AppendStatus(StringBuilder text)
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (text == null || game == null) return;
            text.AppendLine();
            text.AppendLine("<color=#FFD45B><b>쿠지 라스트원상</b></color>");
            text.AppendLine("획득 " + game.LastOneAwards.Value + "회 · 칭호 " + LastOneTitle(game.LastOneAwards.Value));
            text.AppendLine(game.RecentLastOneRecords.Value.ToString());
        }

        public string LastOneTitle(int count)
        {
            int[] thresholds = config != null ? config.LastOneTitleThresholds : System.Array.Empty<int>();
            string[] labels = { "입문 집사", "라스트원상 헌터", "세트 수호자", "전설의 집사" };
            string result = "도전자";
            for (int i = 0; i < thresholds.Length && i < labels.Length; i++)
                if (count >= thresholds[i]) result = labels[i];
            return result;
        }

        private void ServerCraftNextDecoration(ShopNetworkGame game)
        {
            int[] thresholds = config.UpcycleThresholds;
            int mask = game.UpcycleDecorMask.Value;
            for (int index = 0; index < thresholds.Length; index++)
            {
                if ((mask & (1 << index)) != 0) continue;
                int required = Mathf.Max(1, thresholds[index]);
                int productId = config.EmptyCapsuleProduct != null ? config.EmptyCapsuleProduct.ProductId : int.MinValue;
                if (!game.ServerTryConsumeContainerProductFrom(ShopContainerKind.CapsuleRecycler,
                        productId, required))
                {
                    game.ServerSetEvent("빈 캡슐 " + EmptyCapsuleCount + "개 보관 중 · 다음 업사이클 장식은 " +
                                        required + "개가 필요합니다.");
                    return;
                }
                game.UpcycleDecorMask.Value = mask | (1 << index);
                game.ServerSetEvent("빈 캡슐 " + required + "개로 매장 장식 " + (index + 1) + "을 제작했습니다!");
                ShopProgressionManager.Instance?.SaveNow();
                return;
            }
            game.ServerSetEvent("모든 빈 캡슐 업사이클 장식을 제작했습니다. 현재 보관량 " + EmptyCapsuleCount + "개");
        }

        private void BuildFacilities()
        {
            facilitiesRoot = new GameObject("Differentiation Facilities");
            capsuleRecyclerFacility = BuildFacility("빈 캡슐 회수함", config.CapsuleRecyclerPosition,
                new Color(0.12f, 0.62f, 0.58f), ShopAction.CapsuleRecycler,
                "빈 캡슐 회수함 / 업사이클 장식 제작");
            BuildReviewBoard();
            appraisalFacility = BuildFacility("굿즈 감정소", config.AppraisalPosition,
                new Color(0.34f, 0.23f, 0.52f), ShopAction.AppraisalDesk,
                "보유 상품 1개 감정하기");
            BuildConsignmentCorner();
            RefreshFacilityUnlocks();
        }

        private void BuildConsignmentCorner()
        {
            GameObject corner = BuildFacility("위탁 판매 코너", config.ConsignmentPosition,
                new Color(0.62f, 0.38f, 0.17f), ShopAction.ConsignmentCorner,
                "위탁 수집가의 제안 수락");
            corner.transform.localScale = new Vector3(2.4f, 1.45f, 0.9f);
            consignmentFacility = corner;
            Transform label = corner.transform.Find("위탁 판매 코너_Label");
            if (label != null)
            {
                consignmentText = label.GetComponent<TextMesh>();
                label.localPosition = new Vector3(0f, 0.75f, -0.55f);
                consignmentText.characterSize = 0.075f;
                consignmentText.fontSize = 42;
            }
            GameObject reject = BuildFacility("위탁 제안 거절", config.ConsignmentPosition +
                new Vector3(1.8f, 0f, 0f), new Color(0.48f, 0.16f, 0.16f),
                ShopAction.ConsignmentReject, "현재 위탁 제안 거절");
            reject.transform.localScale = new Vector3(0.65f, 0.65f, 0.65f);
            consignmentRejectFacility = reject;

            GameObject visualHost = new("Consignment Product Visuals");
            visualHost.transform.SetParent(facilitiesRoot.transform, false);
            visualHost.transform.position = config.ConsignmentPosition + new Vector3(0f, 1.15f, -0.65f);
            consignmentVisualRoot = visualHost.transform;

            ShopWorkforceConfig workforce = ShopWorkforceConfig.Load();
            GameObject[] appearances = workforce != null ? workforce.AppearancePrefabs : null;
            if (appearances != null && appearances.Length > 0 && appearances[0] != null)
            {
                consignmentNpc = new GameObject("Consignment Collector NPC");
                consignmentNpc.transform.SetParent(facilitiesRoot.transform, false);
                consignmentNpc.transform.position = config.ConsignmentPosition + new Vector3(-1.7f, 0f, 0.2f);
                consignmentNpc.AddComponent<ShopWorldSafetyAgent>();
                GameObject visual = Instantiate(appearances[0], consignmentNpc.transform);
                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 135f, 0f));
                foreach (Collider collider in consignmentNpc.GetComponentsInChildren<Collider>(true))
                    Destroy(collider);
                foreach (Rigidbody body in consignmentNpc.GetComponentsInChildren<Rigidbody>(true))
                    Destroy(body);
                Animator animator = consignmentNpc.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.applyRootMotion = false;
            }
        }

        private void RefreshConsignmentCorner()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || config == null) return;
            bool unlocked = (ShopProgressionManager.Instance?.ExpansionLevel ?? 1) >= 5 &&
                            game.Reputation.Value >= config.ConsignmentUnlockReputation;
            bool visiting = unlocked && game.ConsignmentOfferCount.Value > 0;
            if (consignmentNpc != null) consignmentNpc.SetActive(visiting);
            if (consignmentText != null)
            {
                var text = new StringBuilder("위탁 판매 코너\n");
                if (!unlocked) text.Append("평판 ").Append(config.ConsignmentUnlockReputation).Append("에 해금");
                else if (!visiting) text.Append("다음 방문 ").Append(Mathf.CeilToInt(
                    game.ConsignmentSecondsRemaining.Value)).Append("초");
                else
                {
                    for (int i = 0; i < game.ConsignmentOfferCount.Value; i++)
                    {
                        ShopProductDefinition product = ShopProductVisuals.Find(GetConsignmentProduct(game, i));
                        text.Append(product != null ? product.DisplayName : "상품").Append(" ")
                            .Append(GetConsignmentPrice(game, i)).Append("원\n");
                    }
                    text.Append("E 수락 · 옆 버튼 거절 · ")
                        .Append(Mathf.CeilToInt(game.ConsignmentSecondsRemaining.Value)).Append("초");
                }
                consignmentText.text = text.ToString();
            }

            var signature = new StringBuilder();
            for (int i = 0; i < game.ItemContainers.Count; i++)
            {
                ShopContainerItem item = game.ItemContainers[i];
                if (item.Container == ShopContainerKind.ConsignmentDisplay)
                    signature.Append(item.ProductId).Append(':').Append(item.Quantity).Append('|');
            }
            string next = signature.ToString();
            if (next == consignmentSnapshot || consignmentVisualRoot == null) return;
            consignmentSnapshot = next;
            foreach (GameObject visual in consignmentVisuals) if (visual != null) Destroy(visual);
            consignmentVisuals.Clear();
            int slot = 0;
            for (int i = 0; i < game.ItemContainers.Count && slot < config.ConsignmentSlots; i++)
            {
                ShopContainerItem item = game.ItemContainers[i];
                if (item.Container != ShopContainerKind.ConsignmentDisplay || item.Quantity <= 0) continue;
                ShopProductDefinition product = ShopProductVisuals.Find(item.ProductId);
                GameObject visual = ShopProductVisuals.Instantiate(product, consignmentVisualRoot);
                if (visual == null) continue;
                visual.name = "ConsignmentDisplayed_" + item.ProductId;
                visual.transform.localPosition = new Vector3((slot - 1) * 0.55f, 0f, 0f);
                visual.transform.localRotation = Quaternion.Euler(0f, slot * 30f - 30f, 0f);
                consignmentVisuals.Add(visual);
                slot++;
            }
        }

        private void RefreshFacilityUnlocks()
        {
            int level = ShopProgressionManager.Instance?.ExpansionLevel ?? 1;
            if (capsuleRecyclerFacility != null) capsuleRecyclerFacility.SetActive(level >= 3);
            if (appraisalFacility != null) appraisalFacility.SetActive(level >= 4);
            if (consignmentFacility != null) consignmentFacility.SetActive(level >= 5);
            if (consignmentRejectFacility != null) consignmentRejectFacility.SetActive(level >= 5);
            if (consignmentVisualRoot != null) consignmentVisualRoot.gameObject.SetActive(level >= 5);
            if (consignmentNpc != null && level < 5) consignmentNpc.SetActive(false);
        }

        private void BuildReviewBoard()
        {
            GameObject board = GameObject.Find("손님 리뷰 게시판");
            if (board == null || !board.scene.IsValid())
            {
                Debug.LogError("[Differentiation] MainStreet 씬에 손님 리뷰 게시판이 배치되지 않았습니다.", this);
                return;
            }
            ShopInteractable interactable = board.GetComponent<ShopInteractable>();
            if (interactable != null)
                interactable.Configure(ShopAction.ReviewBoard, "최근 손님 리뷰 보기");
            Transform label = board.transform.Find("손님 리뷰 게시판_Label");
            if (label == null)
            {
                Debug.LogError("[Differentiation] 손님 리뷰 게시판의 TextMesh가 없습니다.", board);
                return;
            }
            reviewBoardText = label.GetComponent<TextMesh>();
            ShopUiFonts.Apply(reviewBoardText, ShopUiFontWeight.Bold);
        }

        private GameObject BuildFacility(string objectName, Vector3 position, Color color,
            ShopAction action, string prompt)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = objectName;
            root.transform.SetParent(facilitiesRoot.transform, false);
            root.transform.position = position + Vector3.up * 0.65f;
            root.transform.localScale = new Vector3(1.35f, 1.3f, 0.75f);
            Renderer renderer = root.GetComponent<Renderer>();
            ShopBuildSafeMaterials.ApplyLitColor(renderer, color);
            root.AddComponent<ShopInteractable>().Configure(action, prompt);
            NavMeshObstacle obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            GameObject labelHost = new(objectName + "_Label");
            labelHost.transform.SetParent(root.transform, false);
            labelHost.transform.localPosition = new Vector3(0f, 0.8f, -0.52f);
            // TextMesh renders its readable face toward local -Z. These fixtures sit on the
            // public/front (-Z) side of the shop, so identity is the authored readable pose.
            labelHost.transform.localRotation = Quaternion.identity;
            TextMesh label = labelHost.AddComponent<TextMesh>();
            label.text = objectName;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.12f;
            label.fontSize = 48;
            label.color = Color.white;
            ShopUiFonts.Apply(label, ShopUiFontWeight.Bold);
            return root;
        }

        private void RefreshReviewBoard()
        {
            if (reviewBoardText == null || ShopNetworkGame.Instance == null) return;
            string reviews = ShopNetworkGame.Instance.ReviewHistory.Value.ToString();
            if (reviews == lastReviewSnapshot) return;
            lastReviewSnapshot = reviews;
            reviewBoardText.text = "손님 리뷰\n" + WrapBoardText(reviews, 14, 5);
        }

        private static string WrapBoardText(string value, int charactersPerLine, int maximumLines)
        {
            if (string.IsNullOrWhiteSpace(value)) return "아직 등록된 리뷰가 없습니다.";
            value = value.Replace("\r", string.Empty).Replace("\n", " ").Trim();
            var builder = new System.Text.StringBuilder();
            int line = 0;
            int column = 0;
            for (int index = 0; index < value.Length && line < maximumLines; index++)
            {
                if (column >= charactersPerLine)
                {
                    builder.Append('\n');
                    line++;
                    column = 0;
                    if (line >= maximumLines) break;
                }
                builder.Append(value[index]);
                column++;
            }
            if (builder.Length < value.Length) builder.Append('…');
            return builder.ToString();
        }

        private void RefreshUpcycleDecorations()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || facilitiesRoot == null) return;
            int mask = game.UpcycleDecorMask.Value;
            int count = config.UpcycleThresholds.Length;
            while (upcycleDecorations.Count < count)
            {
                int index = upcycleDecorations.Count;
                GameObject decor = GameObject.CreatePrimitive(index == 0 ? PrimitiveType.Quad :
                    index == 1 ? PrimitiveType.Sphere : PrimitiveType.Cube);
                decor.name = "UpcycleDecor_" + index;
                decor.transform.SetParent(facilitiesRoot.transform, false);
                decor.transform.position = index switch
                {
                    0 => new Vector3(4.2f, 2.2f, 7.6f),
                    1 => new Vector3(10.8f, 3.2f, 3.8f),
                    _ => new Vector3(1.4f, 2.1f, 1.8f)
                };
                decor.transform.localScale = index == 0 ? new Vector3(1.8f, 1.1f, 1f) : Vector3.one * 0.55f;
                Collider collider = decor.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                Renderer renderer = decor.GetComponent<Renderer>();
                ShopBuildSafeMaterials.ApplyLitColor(renderer,
                    index == 0 ? new Color(0.85f, 0.38f, 0.58f) :
                    index == 1 ? new Color(1f, 0.75f, 0.25f) : new Color(0.45f, 0.8f, 1f));
                upcycleDecorations.Add(decor);
            }
            for (int i = 0; i < upcycleDecorations.Count; i++)
                upcycleDecorations[i].SetActive((mask & (1 << i)) != 0);
        }
    }
}
