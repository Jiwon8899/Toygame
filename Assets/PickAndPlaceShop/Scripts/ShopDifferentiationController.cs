using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

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
            if (action == ShopAction.CapsuleRecycler) ServerCraftNextDecoration(game);
            else if (action == ShopAction.ReviewBoard)
                game.ServerSetEvent(game.ReviewHistory.Value.ToString());
            else if (action == ShopAction.AppraisalDesk)
            {
                game.ServerTryAppraiseFirstOwned(requester, config, out _, out string message);
                game.ServerSetEvent(message);
                ShopProgressionManager.Instance?.SaveNow();
            }
        }

        public void ServerGenerateDailyReview(int day, float averageWaitSeconds,
            int displayedCategoryCount, int averageSatisfaction, ShopProductCategory trendCategory)
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsServer || day <= game.LatestReviewDay.Value) return;

            int stars = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp(averageSatisfaction, 0, 100) / 20f), 1, 5);
            string fallback = BuildReviewFallback(day, averageWaitSeconds, displayedCategoryCount,
                averageSatisfaction, trendCategory);
            CommitReview(day, stars, averageWaitSeconds, displayedCategoryCount,
                averageSatisfaction, trendCategory, fallback);

            ShopNarrativeAIService service = ShopNarrativeAIService.Instance;
            if (service == null) return;
            string prompt = "고양이 소품샵의 하루 손님 리뷰를 한국어 한 문장으로 작성하세요. " +
                            "평균 대기시간=" + averageWaitSeconds.ToString("0.0") + "초, " +
                            "진열 카테고리=" + displayedCategoryCount + "개, " +
                            "평균 만족도=" + averageSatisfaction + "점, " +
                            "오늘의 유행=" + ShopProductLocalization.CategoryLabel(trendCategory) +
                            ". 수치나 판정을 바꾸지 말고 실제로 두드러진 장점 또는 단점을 언급하세요.";
            service.Request("daily-review:" + day, prompt, result =>
            {
                if (ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsServer ||
                    ShopNetworkGame.Instance.LatestReviewDay.Value != day) return;
                ShopLiveOperationsNetwork live = ShopLiveOperationsNetwork.Instance;
                if (result.IsApiSuccess && result.HasText)
                {
                    CommitReview(day, stars, averageWaitSeconds, displayedCategoryCount,
                        averageSatisfaction, trendCategory, result.Text);
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

        private void CommitReview(int day, int stars, float waitSeconds, int categoryCount,
            int satisfaction, ShopProductCategory trendCategory, string sentence)
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsServer) return;
            string clean = (sentence ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (clean.Length > 100) clean = clean.Substring(0, 100);
            string line = "[" + day + "일차 " + new string('★', stars) + new string('☆', 5 - stars) +
                          " | 대기 " + waitSeconds.ToString("0.0") + "초 · 진열 " + categoryCount +
                          "종 · 만족 " + satisfaction + " · 유행 " +
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

        private static string BuildReviewFallback(int day, float waitSeconds, int categoryCount,
            int satisfaction, ShopProductCategory trendCategory)
        {
            if (waitSeconds >= 25f)
                return "계산 줄이 길어서 기다림이 아쉬웠지만 고양이 굿즈 구경은 즐거웠어요.";
            if (categoryCount <= 1)
                return "진열 종류가 조금 더 다양해지면 다시 오래 둘러보고 싶어요.";
            if (satisfaction >= 85)
                return "유행 상품과 다양한 진열을 편하게 둘러볼 수 있어 아주 만족스러웠어요.";
            if (satisfaction >= 60)
                return "분위기가 편안하고 상품을 고르는 재미가 있는 소품샵이에요.";
            return "귀여운 상품은 좋았지만 대기와 진열 구성이 조금 더 나아지면 좋겠어요.";
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
            BuildFacility("빈 캡슐 회수함", config.CapsuleRecyclerPosition,
                new Color(0.12f, 0.62f, 0.58f), ShopAction.CapsuleRecycler,
                "빈 캡슐 회수함 / 업사이클 장식 제작");
            BuildReviewBoard();
            BuildFacility("굿즈 감정소", config.AppraisalPosition,
                new Color(0.34f, 0.23f, 0.52f), ShopAction.AppraisalDesk,
                "보유 상품 1개 감정하기");
        }

        private void BuildReviewBoard()
        {
            GameObject board = BuildFacility("손님 리뷰 게시판", config.ReviewBoardPosition,
                new Color(0.28f, 0.16f, 0.10f), ShopAction.ReviewBoard,
                "최근 손님 리뷰 보기");
            board.transform.localScale = new Vector3(2.8f, 1.9f, 0.24f);
            Transform label = board.transform.Find("손님 리뷰 게시판_Label");
            if (label == null) return;
            reviewBoardText = label.GetComponent<TextMesh>();
            label.localPosition = new Vector3(0f, 0f, -0.65f);
            reviewBoardText.characterSize = 0.055f;
            reviewBoardText.fontSize = 42;
            reviewBoardText.anchor = TextAnchor.MiddleCenter;
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
            renderer.material.color = color;
            root.AddComponent<ShopInteractable>().Configure(action, prompt);

            GameObject labelHost = new(objectName + "_Label");
            labelHost.transform.SetParent(root.transform, false);
            labelHost.transform.localPosition = new Vector3(0f, 0.8f, -0.52f);
            labelHost.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            TextMesh label = labelHost.AddComponent<TextMesh>();
            label.text = objectName;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.12f;
            label.fontSize = 48;
            label.color = Color.white;
            return root;
        }

        private void RefreshReviewBoard()
        {
            if (reviewBoardText == null || ShopNetworkGame.Instance == null) return;
            string reviews = ShopNetworkGame.Instance.ReviewHistory.Value.ToString();
            if (reviews == lastReviewSnapshot) return;
            lastReviewSnapshot = reviews;
            reviewBoardText.text = "손님 리뷰\n\n" + reviews;
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
                renderer.material.color = index == 0 ? new Color(0.85f, 0.38f, 0.58f) :
                    index == 1 ? new Color(1f, 0.75f, 0.25f) : new Color(0.45f, 0.8f, 1f);
                upcycleDecorations.Add(decor);
            }
            for (int i = 0; i < upcycleDecorations.Count; i++)
                upcycleDecorations[i].SetActive((mask & (1 << i)) != 0);
        }
    }
}
