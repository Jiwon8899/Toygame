using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(250)]
    public sealed class ShopUpgradeUI : MonoBehaviour
    {
        private sealed class Card
        {
            public ShopUpgradeCategory Category;
            public Image Background;
            public Text Title;
            public Text History;
            public Text Next;
        }

        private static readonly ShopUpgradeCategory[] Categories =
        {
            ShopUpgradeCategory.Player,
            ShopUpgradeCategory.Operations,
            ShopUpgradeCategory.Facility,
            ShopUpgradeCategory.Claw,
            ShopUpgradeCategory.Gacha,
            ShopUpgradeCategory.Kuji
        };

        private static ShopUpgradeUI instance;
        private readonly List<Card> cards = new();
        private GameObject overlay;
        private GameObject panel;
        private Text moneyText;
        private Font uiFont;
        private bool open;

        public static bool IsOpen => instance != null && instance.open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject root = new("ShopUpgradeUI");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<ShopUpgradeUI>();
        }

        public static void Open()
        {
            if (instance == null) Bootstrap();
            instance.SetOpen(true);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 30);
            BuildUi();
            SetOpen(false);
        }

        private void Update()
        {
            if (!open) return;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null)
            {
                SetOpen(false);
                return;
            }

            if (Keyboard.current != null)
            {
                if (Keyboard.current.xKey.wasPressedThisFrame)
                {
                    SetOpen(false);
                    return;
                }

                if (Keyboard.current.digit1Key.wasPressedThisFrame) Purchase(0);
                if (Keyboard.current.digit2Key.wasPressedThisFrame) Purchase(1);
                if (Keyboard.current.digit3Key.wasPressedThisFrame) Purchase(2);
                if (Keyboard.current.digit4Key.wasPressedThisFrame) Purchase(3);
                if (Keyboard.current.digit5Key.wasPressedThisFrame) Purchase(4);
                if (Keyboard.current.digit6Key.wasPressedThisFrame) Purchase(5);
            }
            Refresh();
        }

        private void Purchase(int index)
        {
            if (index < 0 || index >= Categories.Length || ShopNetworkGame.Instance == null) return;
            ShopNetworkGame.Instance.RequestUpgradePurchase(Categories[index]);
        }

        private void SetOpen(bool value)
        {
            open = value;
            if (overlay != null) overlay.SetActive(value);
            ShopLocalPauseState.IsPaused = value;
            if (value) ShopInputModeManager.Push(this, ShopInputMode.UI);
            else ShopInputModeManager.Pop(this);
            if (value) Refresh();
        }

        private void BuildUi()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject dim = CreatePanel("Dim", canvasObject.transform, Vector2.zero,
                new Color(0.01f, 0.018f, 0.035f, 0.88f));
            overlay = dim;
            RectTransform dimRect = dim.GetComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;

            panel = CreatePanel("UpgradePanel", dim.transform, new Vector2(1540f, 880f),
                new Color(0.035f, 0.055f, 0.085f, 0.98f));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;

            Text title = CreateText("Title", panel.transform, "상점 업그레이드 내역",
                44, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.3f, 1f, 0.78f));
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            SetRect(title.rectTransform, new Vector2(50f, -12f), new Vector2(900f, 76f),
                new Vector2(0f, 1f), new Vector2(0f, 1f));

            Text subtitle = CreateText("Subtitle", panel.transform,
                "현재 게임에 실제로 연결된 기능만 표시합니다 · 숫자키로 다음 단계를 구매하세요",
                22, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.7f, 0.8f, 0.9f));
            SetRect(subtitle.rectTransform, new Vector2(52f, -88f), new Vector2(1050f, 40f),
                new Vector2(0f, 1f), new Vector2(0f, 1f));

            moneyText = CreateText("Money", panel.transform, "가게 자금 0",
                30, FontStyle.Bold, TextAnchor.MiddleRight, new Color(1f, 0.78f, 0.25f));
            SetRect(moneyText.rectTransform, new Vector2(-50f, -38f), new Vector2(360f, 52f),
                new Vector2(1f, 1f), new Vector2(1f, 1f));

            Color[] colors =
            {
                new(0.25f, 0.75f, 1f),
                new(0.3f, 1f, 0.72f),
                new(1f, 0.68f, 0.25f),
                new(0.85f, 0.45f, 1f),
                new(1f, 0.45f, 0.58f),
                new(0.45f, 0.7f, 1f)
            };

            for (int i = 0; i < Categories.Length; i++)
            {
                int row = i / 2;
                int column = i % 2;
                Card card = CreateCard(panel.transform, Categories[i], i + 1, colors[i]);
                RectTransform rect = card.Background.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(50f + column * 750f, -145f - row * 215f);
                cards.Add(card);
            }

            Text footer = CreateText("Footer", panel.transform,
                "1~6 구매  |  X 닫기  |  미구현 시스템(알바·배달·온라인·경매 등)은 구매 목록에서 제외",
                21, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.74f, 0.8f, 0.9f));
            SetRect(footer.rectTransform, new Vector2(50f, 22f), new Vector2(1440f, 44f),
                new Vector2(0f, 0f), new Vector2(0f, 0f));
        }

        private Card CreateCard(Transform parent, ShopUpgradeCategory category, int key, Color accent)
        {
            GameObject root = CreatePanel("Card_" + category, parent, new Vector2(700f, 190f),
                new Color(0.075f, 0.105f, 0.15f, 1f));
            Image background = root.GetComponent<Image>();

            GameObject bar = CreatePanel("Accent", root.transform, new Vector2(8f, 190f), accent);
            RectTransform barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = new Vector2(0f, 0.5f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.anchoredPosition = Vector2.zero;

            Text title = CreateText("Title", root.transform,
                "[" + key + "] " + ShopNetworkGame.UpgradeTitle(category),
                28, FontStyle.Bold, TextAnchor.MiddleLeft, accent);
            SetRect(title.rectTransform, new Vector2(28f, -14f), new Vector2(630f, 42f),
                new Vector2(0f, 1f), new Vector2(0f, 1f));

            Text history = CreateText("History", root.transform, string.Empty,
                19, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.76f, 0.82f, 0.9f));
            SetRect(history.rectTransform, new Vector2(30f, -58f), new Vector2(300f, 110f),
                new Vector2(0f, 1f), new Vector2(0f, 1f));

            Text next = CreateText("Next", root.transform, string.Empty,
                20, FontStyle.Bold, TextAnchor.UpperLeft, Color.white);
            SetRect(next.rectTransform, new Vector2(340f, -58f), new Vector2(330f, 112f),
                new Vector2(0f, 1f), new Vector2(0f, 1f));

            return new Card
            {
                Category = category,
                Background = background,
                Title = title,
                History = history,
                Next = next
            };
        }

        private void Refresh()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null) return;
            moneyText.text = "가게 자금 " + game.Coins.Value + "원\n전체 " +
                             game.TotalUpgradeLevel + "/" + ShopNetworkGame.TotalSupportedUpgradeLevels;
            foreach (Card card in cards)
            {
                int level = game.GetUpgradeLevel(card.Category);
                int max = ShopNetworkGame.GetUpgradeMaxLevel(card.Category);
                int cost = game.GetNextUpgradeCost(card.Category);
                card.Title.text = card.Title.text.Substring(0, card.Title.text.IndexOf(']') + 1) + " " +
                                  ShopNetworkGame.UpgradeTitle(card.Category) + "  " + level + "/" + max;
                card.History.text = HistoryFor(card.Category, level);
                card.Next.text = level >= max
                    ? "완료\n현재 효과가 적용 중입니다."
                    : "다음 효과\n" + game.UpgradeNextEffect(card.Category) + "\n비용 " + cost + "원";
                card.Background.color = level >= max
                    ? new Color(0.075f, 0.18f, 0.15f, 1f)
                    : new Color(0.075f, 0.105f, 0.15f, 1f);
            }
        }

        private static string HistoryFor(ShopUpgradeCategory category, int level)
        {
            string[] steps = category switch
            {
                ShopUpgradeCategory.Player => new[] { "빠른 걸음", "빠른 달리기" },
                ShopUpgradeCategory.Operations => new[] { "도로변 홍보", "대기공간 확장", "빠른 계산" },
                ShopUpgradeCategory.Facility => new[] { "따뜻한 조명", "매장 리뉴얼" },
                ShopUpgradeCategory.Claw => new[] { "정밀 레일", "강화 팬" },
                ShopUpgradeCategory.Gacha => new[] { "가챠 20% 할인" },
                ShopUpgradeCategory.Kuji => new[] { "상세 재고 정보", "쿠지 20% 할인" },
                _ => System.Array.Empty<string>()
            };
            string result = "구매 내역\n";
            for (int i = 0; i < steps.Length; i++)
                result += (i < level ? "✓ " : "○ ") + steps[i] + (i + 1 < steps.Length ? "\n" : string.Empty);
            return result;
        }

        private GameObject CreatePanel(string name, Transform parent, Vector2 size, Color color)
        {
            GameObject target = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            target.transform.SetParent(parent, false);
            target.GetComponent<RectTransform>().sizeDelta = size;
            target.GetComponent<Image>().color = color;
            return target;
        }

        private Text CreateText(string name, Transform parent, string content, int size,
            FontStyle style, TextAnchor alignment, Color color)
        {
            Text target = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text))
                .GetComponent<Text>();
            target.transform.SetParent(parent, false);
            target.font = uiFont;
            target.fontSize = size;
            target.fontStyle = style;
            target.alignment = alignment;
            target.color = color;
            target.text = content;
            target.horizontalOverflow = HorizontalWrapMode.Wrap;
            target.verticalOverflow = VerticalWrapMode.Truncate;
            target.raycastTarget = false;
            return target;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(anchorMin.x, anchorMax.y);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
