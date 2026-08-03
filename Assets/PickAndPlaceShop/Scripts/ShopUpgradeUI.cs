using System.Collections;
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
            public Button Button;
        }

        private static readonly ShopUpgradeCategory[] Categories =
        {
            ShopUpgradeCategory.Player, ShopUpgradeCategory.Operations,
            ShopUpgradeCategory.Facility, ShopUpgradeCategory.Claw,
            ShopUpgradeCategory.Gacha, ShopUpgradeCategory.Kuji,
            ShopUpgradeCategory.StoreExpansion, ShopUpgradeCategory.Staff
        };

        private static ShopUpgradeUI instance;
        private readonly List<Card> cards = new();
        private GameObject overlay;
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
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 30);
            BuildUi();
            SetOpen(false);
        }

        private void Update()
        {
            if (!open) return;
            if (ShopNetworkGame.Instance == null) { SetOpen(false); return; }
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.xKey.wasPressedThisFrame) { SetOpen(false); return; }
                if (keyboard.digit1Key.wasPressedThisFrame) Purchase(0);
                if (keyboard.digit2Key.wasPressedThisFrame) Purchase(1);
                if (keyboard.digit3Key.wasPressedThisFrame) Purchase(2);
                if (keyboard.digit4Key.wasPressedThisFrame) Purchase(3);
                if (keyboard.digit5Key.wasPressedThisFrame) Purchase(4);
                if (keyboard.digit6Key.wasPressedThisFrame) Purchase(5);
                if (keyboard.digit7Key.wasPressedThisFrame) Purchase(6);
                if (keyboard.digit8Key.wasPressedThisFrame) Purchase(7);
            }
            Refresh();
        }

        private void Purchase(int index)
        {
            if (index < 0 || index >= Categories.Length || ShopNetworkGame.Instance == null) return;
            StartCoroutine(Punch(cards[index].Background.rectTransform));
            ShopNetworkGame.Instance.RequestUpgradePurchase(Categories[index]);
        }

        private static IEnumerator Punch(RectTransform rect)
        {
            rect.localScale = Vector3.one * 0.97f;
            yield return new WaitForSecondsRealtime(0.08f);
            if (rect != null) rect.localScale = Vector3.one;
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

            overlay = CreatePanel("Dim", canvasObject.transform, Vector2.zero, new Color(0.01f, 0.018f, 0.035f, 0.88f));
            RectTransform dimRect = overlay.GetComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero; dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;

            GameObject panel = CreatePanel("UpgradePanel", overlay.transform, new Vector2(1540f, 1000f),
                new Color(0.035f, 0.055f, 0.085f, 0.98f));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);

            Text title = CreateText("Title", panel.transform, "상점 업그레이드", 44, FontStyle.Bold,
                TextAnchor.MiddleLeft, new Color(0.3f, 1f, 0.78f));
            SetRect(title.rectTransform, new Vector2(50f, -12f), new Vector2(900f, 76f), new Vector2(0f, 1f));
            Text subtitle = CreateText("Subtitle", panel.transform,
                "숫자 키 또는 카드를 클릭하세요. 현재 게임에 연결된 기능만 표시됩니다.", 22,
                FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.7f, 0.8f, 0.9f));
            SetRect(subtitle.rectTransform, new Vector2(52f, -82f), new Vector2(1100f, 40f), new Vector2(0f, 1f));
            moneyText = CreateText("Money", panel.transform, string.Empty, 30, FontStyle.Bold,
                TextAnchor.MiddleRight, new Color(1f, 0.78f, 0.25f));
            SetRect(moneyText.rectTransform, new Vector2(-95f, -38f), new Vector2(420f, 52f), new Vector2(1f, 1f));

            GameObject close = CreatePanel("Close", panel.transform, new Vector2(58f, 58f), new Color(0.6f, 0.16f, 0.2f));
            SetRect(close.GetComponent<RectTransform>(), new Vector2(-18f, -18f), new Vector2(58f, 58f), new Vector2(1f, 1f));
            Button closeButton = close.AddComponent<Button>();
            closeButton.targetGraphic = close.GetComponent<Image>();
            closeButton.onClick.AddListener(() => SetOpen(false));
            Text closeLabel = CreateText("Label", close.transform, "X", 30, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            SetRect(closeLabel.rectTransform, Vector2.zero, new Vector2(58f, 58f), new Vector2(0.5f, 0.5f));

            Color[] accents =
            {
                new(0.25f,0.75f,1f), new(0.3f,1f,0.72f), new(1f,0.68f,0.25f), new(0.85f,0.45f,1f),
                new(1f,0.45f,0.58f), new(0.45f,0.7f,1f), new(0.3f,0.9f,0.55f), new(1f,0.72f,0.2f)
            };
            for (int i = 0; i < Categories.Length; i++)
            {
                Card card = CreateCard(panel.transform, Categories[i], i + 1, accents[i]);
                SetRect(card.Background.rectTransform,
                    new Vector2(50f + (i % 2) * 750f, -132f - (i / 2) * 205f),
                    new Vector2(700f, 185f), new Vector2(0f, 1f));
                cards.Add(card);
            }

            Text footer = CreateText("Footer", panel.transform,
                "1~8 구매 · X 닫기 · 고용된 알바는 직접 걸어 다니며 공용 자원만 사용합니다.", 20,
                FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.74f, 0.8f, 0.9f));
            SetRect(footer.rectTransform, new Vector2(50f, 16f), new Vector2(1440f, 42f), Vector2.zero);
        }

        private Card CreateCard(Transform parent, ShopUpgradeCategory category, int key, Color accent)
        {
            GameObject root = CreatePanel("Card_" + category, parent, new Vector2(700f, 185f), new Color(0.075f, 0.105f, 0.15f));
            Image background = root.GetComponent<Image>();
            Button button = root.AddComponent<Button>();
            button.targetGraphic = background;
            int index = key - 1;
            button.onClick.AddListener(() => Purchase(index));
            GameObject bar = CreatePanel("Accent", root.transform, new Vector2(8f, 185f), accent);
            SetRect(bar.GetComponent<RectTransform>(), Vector2.zero, new Vector2(8f, 185f), new Vector2(0f, 0.5f));
            Text title = CreateText("Title", root.transform, string.Empty, 27, FontStyle.Bold, TextAnchor.MiddleLeft, accent);
            SetRect(title.rectTransform, new Vector2(28f, -10f), new Vector2(640f, 42f), new Vector2(0f, 1f));
            Text history = CreateText("History", root.transform, string.Empty, 18, FontStyle.Normal,
                TextAnchor.UpperLeft, new Color(0.76f, 0.82f, 0.9f));
            SetRect(history.rectTransform, new Vector2(30f, -55f), new Vector2(300f, 112f), new Vector2(0f, 1f));
            Text next = CreateText("Next", root.transform, string.Empty, 19, FontStyle.Bold, TextAnchor.UpperLeft, Color.white);
            SetRect(next.rectTransform, new Vector2(340f, -55f), new Vector2(330f, 112f), new Vector2(0f, 1f));
            return new Card { Category = category, Background = background, Title = title, History = history, Next = next, Button = button };
        }

        private void Refresh()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null) return;
            moneyText.text = "가게 자금 " + game.Coins.Value + "원\n전체 " + game.TotalUpgradeLevel + "/" + ShopNetworkGame.TotalSupportedUpgradeLevels;
            for (int i = 0; i < cards.Count; i++)
            {
                Card card = cards[i];
                int level = game.GetUpgradeLevel(card.Category);
                int max = ShopNetworkGame.GetUpgradeMaxLevel(card.Category);
                int cost = game.GetNextUpgradeCost(card.Category);
                card.Title.text = "[" + (i + 1) + "] " + ShopNetworkGame.UpgradeTitle(card.Category) + "  " + level + "/" + max;
                card.History.text = HistoryFor(card.Category, level);
                card.Next.text = level >= max ? "완료\n현재 효과 적용 중" :
                    "다음 효과\n" + game.UpgradeNextEffect(card.Category) + "\n비용 " + cost + "원";
                card.Button.interactable = level < max && cost > 0;
                card.Background.color = level >= max ? new Color(0.075f, 0.18f, 0.15f) :
                    game.Coins.Value >= cost ? new Color(0.075f, 0.15f, 0.13f) : new Color(0.14f, 0.075f, 0.085f);
            }
        }

        private static string HistoryFor(ShopUpgradeCategory category, int level)
        {
            if (category == ShopUpgradeCategory.StoreExpansion)
                return "구매 내역\n" +
                       "진열 확장 " + Mathf.Min(level, 3) + "/3\n" +
                       "매장 확장 " + Mathf.Max(0, level - 3) + "/2";
            string[] steps = category switch
            {
                ShopUpgradeCategory.Player => new[] { "빠른 걸음", "빠른 달리기" },
                ShopUpgradeCategory.Operations => new[] { "도로변 홍보", "대기공간 확장", "빠른 계산" },
                ShopUpgradeCategory.Facility => new[] { "따뜻한 조명", "매장 리뉴얼" },
                ShopUpgradeCategory.Claw => new[] { "정밀 레일", "강화 팬" },
                ShopUpgradeCategory.Gacha => new[] { "가챠 20% 할인" },
                ShopUpgradeCategory.Kuji => new[] { "상세 재고", "쿠지 20% 할인" },
                ShopUpgradeCategory.StoreExpansion => new[] { "진열대 A", "진열대 B", "진열대 C", "매장 확장 I", "매장 확장 II" },
                ShopUpgradeCategory.Staff => new[] { "계산 알바", "진열 알바", "수거 알바" },
                _ => System.Array.Empty<string>()
            };
            string result = "구매 내역\n";
            for (int i = 0; i < steps.Length; i++) result += (i < level ? "✓ " : "□ ") + steps[i] + (i + 1 < steps.Length ? "\n" : string.Empty);
            return result;
        }

        private static GameObject CreatePanel(string name, Transform parent, Vector2 size, Color color)
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
            Text target = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
            target.transform.SetParent(parent, false);
            target.font = uiFont; target.fontSize = size; target.fontStyle = style;
            target.alignment = alignment; target.color = color; target.text = content;
            target.horizontalOverflow = HorizontalWrapMode.Wrap;
            target.verticalOverflow = VerticalWrapMode.Truncate;
            target.raycastTarget = false;
            return target;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size, Vector2 anchor)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
