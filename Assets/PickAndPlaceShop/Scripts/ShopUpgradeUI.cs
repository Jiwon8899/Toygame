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
            public Text Dots;
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
        private GameObject staffAssignmentPanel;
        private Transform staffAssignmentRows;
        private Text staffAssignmentStatus;
        private Button staffHireButton;
        private Text staffHireLabel;
        private int observedStaffMask = -1;
        private int observedSlot2 = -1;
        private int observedSlot3 = -1;

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
            uiFont = ShopUiFonts.Regular;
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
            if (staffAssignmentPanel != null && staffAssignmentPanel.activeSelf)
            {
                ShopNetworkGame game = ShopNetworkGame.Instance;
                if (game != null && (observedStaffMask != game.StaffHiredMask.Value ||
                                     observedSlot2 != game.StaffAssignmentSlot2.Value ||
                                     observedSlot3 != game.StaffAssignmentSlot3.Value))
                    RefreshStaffAssignments();
            }
        }

        private void Purchase(int index)
        {
            if (index < 0 || index >= Categories.Length || ShopNetworkGame.Instance == null) return;
            ShopUpgradeCategory category = Categories[index];
            if (category == ShopUpgradeCategory.Staff &&
                ShopNetworkGame.Instance.GetUpgradeLevel(ShopUpgradeCategory.Staff) >= 2)
            {
                OpenStaffAssignments();
                return;
            }
            StartCoroutine(Punch(cards[index].Background.rectTransform));
            ShopNetworkGame.Instance.RequestUpgradePurchase(category);
            if (category == ShopUpgradeCategory.Staff) StartCoroutine(OpenStaffAssignmentsAfterPurchase());
        }

        private IEnumerator OpenStaffAssignmentsAfterPurchase()
        {
            yield return new WaitForSecondsRealtime(0.3f);
            if (ShopNetworkGame.Instance != null &&
                ShopNetworkGame.Instance.GetUpgradeLevel(ShopUpgradeCategory.Staff) >= 2)
                OpenStaffAssignments();
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

            overlay = CreatePanel("Dim", canvasObject.transform, Vector2.zero, new Color(0.26f, 0.16f, 0.1f, 0.84f));
            RectTransform dimRect = overlay.GetComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero; dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;

            GameObject panel = CreatePanel("UpgradePanel", overlay.transform, new Vector2(1620f, 980f),
                ShopUiSkin.CreamCard);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
            ShopUiSkin.Round(panel.GetComponent<Image>(), 28);

            Text title = CreateText("Title", panel.transform, "상점 업그레이드", 44, FontStyle.Bold,
                TextAnchor.MiddleLeft, ShopUiSkin.BrownDeep);
            SetRect(title.rectTransform, new Vector2(50f, -12f), new Vector2(900f, 76f), new Vector2(0f, 1f));
            Text subtitle = CreateText("Subtitle", panel.transform,
                "숫자 키 또는 카드를 클릭하세요. 현재 게임에 연결된 기능만 표시됩니다.", 22,
                FontStyle.Normal, TextAnchor.MiddleLeft, ShopUiSkin.TextMuted);
            SetRect(subtitle.rectTransform, new Vector2(52f, -82f), new Vector2(1100f, 40f), new Vector2(0f, 1f));
            GameObject moneyChip = CreatePanel("MoneyChip", panel.transform, new Vector2(340f, 62f), ShopUiSkin.Orange);
            SetRect(moneyChip.GetComponent<RectTransform>(), new Vector2(-104f, -24f), new Vector2(340f, 62f), Vector2.one);
            ShopUiSkin.Pill(moneyChip.GetComponent<Image>());
            ShopUiSkin.AddIcon("Coin", moneyChip.transform, ShopUiIcon.Coin, ShopUiSkin.BrownMid,
                new Vector2(44f, 44f), new Vector2(10f, -9f), new Vector2(0f, 1f));
            moneyText = CreateText("Money", moneyChip.transform, string.Empty, 20, FontStyle.Bold,
                TextAnchor.MiddleLeft, Color.white);
            moneyText.rectTransform.anchorMin = Vector2.zero;
            moneyText.rectTransform.anchorMax = Vector2.one;
            moneyText.rectTransform.offsetMin = new Vector2(66f, 4f);
            moneyText.rectTransform.offsetMax = new Vector2(-12f, -4f);

            GameObject close = CreatePanel("Close", panel.transform, new Vector2(58f, 58f), ShopUiSkin.BrownMid);
            SetRect(close.GetComponent<RectTransform>(), new Vector2(-24f, -26f), new Vector2(58f, 58f), new Vector2(1f, 1f));
            ShopUiSkin.Round(close.GetComponent<Image>(), 28);
            Button closeButton = close.AddComponent<Button>();
            closeButton.targetGraphic = close.GetComponent<Image>();
            closeButton.onClick.AddListener(() => SetOpen(false));
            Text closeLabel = CreateText("Label", close.transform, "X", 30, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            SetRect(closeLabel.rectTransform, Vector2.zero, new Vector2(58f, 58f), new Vector2(0.5f, 0.5f));

            Color[] accents =
            {
                ShopUiSkin.Teal, ShopUiSkin.Orange, ShopUiSkin.Pink, ShopUiSkin.Teal,
                ShopUiSkin.Pink, ShopUiSkin.Orange, ShopUiSkin.BrownMid, ShopUiSkin.Teal
            };
            for (int i = 0; i < Categories.Length; i++)
            {
                Card card = CreateCard(panel.transform, Categories[i], i + 1, accents[i]);
                SetRect(card.Background.rectTransform,
                    new Vector2(50f + (i % 2) * 785f, -132f - (i / 2) * 202f),
                    new Vector2(735f, 182f), new Vector2(0f, 1f));
                cards.Add(card);
            }

            Text footer = CreateText("Footer", panel.transform,
                "1~8 구매 · X 닫기 · 고용된 알바는 직접 걸어 다니며 공용 자원만 사용합니다.", 20,
                FontStyle.Bold, TextAnchor.MiddleCenter, ShopUiSkin.TextMuted);
            SetRect(footer.rectTransform, new Vector2(50f, 16f), new Vector2(1440f, 42f), Vector2.zero);
            BuildStaffAssignmentPanel(panel.transform);
        }

        private void BuildStaffAssignmentPanel(Transform parent)
        {
            staffAssignmentPanel = CreatePanel("StaffAssignmentPanel", parent, new Vector2(1480f, 720f),
                ShopUiSkin.CreamCard);
            SetRect(staffAssignmentPanel.GetComponent<RectTransform>(), Vector2.zero,
                new Vector2(1480f, 720f), new Vector2(0.5f, 0.5f));
            ShopUiSkin.Round(staffAssignmentPanel.GetComponent<Image>(), 26);
            Text title = CreateText("Title", staffAssignmentPanel.transform, "알바 기계 배치", 38,
                FontStyle.Bold, TextAnchor.MiddleLeft, ShopUiSkin.BrownDeep);
            SetRect(title.rectTransform, new Vector2(42f, -26f), new Vector2(850f, 60f), new Vector2(0f, 1f));
            Text help = CreateText("Help", staffAssignmentPanel.transform,
                "2·3번 알바를 쿠지 또는 뽑기 기계에 배치합니다. 다시 선택하면 즉시 재배치됩니다.", 20,
                FontStyle.Normal, TextAnchor.MiddleLeft, ShopUiSkin.TextMuted);
            SetRect(help.rectTransform, new Vector2(44f, -88f), new Vector2(1250f, 42f), new Vector2(0f, 1f));

            GameObject close = CreatePanel("Close", staffAssignmentPanel.transform, new Vector2(58f, 58f),
                ShopUiSkin.BrownMid);
            SetRect(close.GetComponent<RectTransform>(), new Vector2(-24f, -24f), new Vector2(58f, 58f), Vector2.one);
            ShopUiSkin.Round(close.GetComponent<Image>(), 29);
            Button closeButton = close.AddComponent<Button>();
            closeButton.targetGraphic = close.GetComponent<Image>();
            closeButton.onClick.AddListener(() => staffAssignmentPanel.SetActive(false));
            Text closeLabel = CreateText("Label", close.transform, "X", 26, FontStyle.Bold,
                TextAnchor.MiddleCenter, Color.white);
            SetRect(closeLabel.rectTransform, Vector2.zero, new Vector2(58f, 58f), new Vector2(0.5f, 0.5f));

            GameObject rows = new("AssignmentRows", typeof(RectTransform));
            rows.transform.SetParent(staffAssignmentPanel.transform, false);
            SetRect(rows.GetComponent<RectTransform>(), new Vector2(44f, -150f), new Vector2(1390f, 420f),
                new Vector2(0f, 1f));
            staffAssignmentRows = rows.transform;

            GameObject hire = CreatePanel("HireNext", staffAssignmentPanel.transform, new Vector2(420f, 64f),
                ShopUiSkin.Orange);
            SetRect(hire.GetComponent<RectTransform>(), new Vector2(44f, 34f), new Vector2(420f, 64f), Vector2.zero);
            ShopUiSkin.Pill(hire.GetComponent<Image>());
            staffHireButton = hire.AddComponent<Button>();
            staffHireButton.targetGraphic = hire.GetComponent<Image>();
            staffHireButton.onClick.AddListener(() =>
            {
                ShopNetworkGame.Instance?.RequestUpgradePurchase(ShopUpgradeCategory.Staff);
                StartCoroutine(RefreshStaffAssignmentsSoon());
            });
            staffHireLabel = CreateText("Label", hire.transform, "3번 알바 추가 고용", 21, FontStyle.Bold,
                TextAnchor.MiddleCenter, Color.white);
            SetRect(staffHireLabel.rectTransform, Vector2.zero, new Vector2(420f, 64f), new Vector2(0.5f, 0.5f));
            staffAssignmentStatus = CreateText("Status", staffAssignmentPanel.transform, string.Empty, 20,
                FontStyle.Bold, TextAnchor.MiddleRight, ShopUiSkin.Teal);
            SetRect(staffAssignmentStatus.rectTransform, new Vector2(-44f, 38f), new Vector2(850f, 54f), Vector2.one);
            staffAssignmentPanel.SetActive(false);
        }

        private IEnumerator RefreshStaffAssignmentsSoon()
        {
            yield return new WaitForSecondsRealtime(0.3f);
            RefreshStaffAssignments();
        }

        private void OpenStaffAssignments()
        {
            if (staffAssignmentPanel == null) return;
            staffAssignmentPanel.SetActive(true);
            staffAssignmentPanel.transform.SetAsLastSibling();
            RefreshStaffAssignments();
        }

        private void RefreshStaffAssignments()
        {
            if (staffAssignmentRows == null || ShopNetworkGame.Instance == null) return;
            for (int i = staffAssignmentRows.childCount - 1; i >= 0; i--)
                Destroy(staffAssignmentRows.GetChild(i).gameObject);
            ShopNetworkGame game = ShopNetworkGame.Instance;
            observedStaffMask = game.StaffHiredMask.Value;
            observedSlot2 = game.StaffAssignmentSlot2.Value;
            observedSlot3 = game.StaffAssignmentSlot3.Value;
            List<ShopStaffMachineOption> options = ShopStaffMachineAssignment.Options();
            for (int slot = 2; slot <= 3; slot++)
            {
                bool hired = game.IsStaffHired((ShopStaffRole)(slot - 1));
                Text rowTitle = CreateText("Slot" + slot + "Title", staffAssignmentRows,
                    slot + "번 알바 " + (hired ? "배치 선택" : "(아직 고용되지 않음)"), 24,
                    FontStyle.Bold, TextAnchor.MiddleLeft, hired ? ShopUiSkin.BrownDeep : ShopUiSkin.TextMuted);
                SetRect(rowTitle.rectTransform, new Vector2(0f, -(slot - 2) * 190f), new Vector2(420f, 44f),
                    new Vector2(0f, 1f));
                int current = game.GetStaffMachineAssignment(slot);
                for (int i = 0; i < options.Count && i < 7; i++)
                {
                    ShopStaffMachineOption option = options[i];
                    Color color = option.Assignment == current ? ShopUiSkin.Teal : ShopUiSkin.CreamBackground;
                    GameObject buttonObject = CreatePanel("Slot" + slot + "_" + option.Assignment,
                        staffAssignmentRows, new Vector2(185f, 66f), color);
                    SetRect(buttonObject.GetComponent<RectTransform>(),
                        new Vector2(i * 195f, -52f - (slot - 2) * 190f), new Vector2(185f, 66f),
                        new Vector2(0f, 1f));
                    ShopUiSkin.Round(buttonObject.GetComponent<Image>(), 14);
                    Button button = buttonObject.AddComponent<Button>();
                    button.targetGraphic = buttonObject.GetComponent<Image>();
                    button.interactable = hired;
                    int capturedSlot = slot;
                    int capturedAssignment = option.Assignment;
                    button.onClick.AddListener(() =>
                    {
                        ShopNetworkGame.Instance?.RequestStaffMachineAssignment(capturedSlot, capturedAssignment);
                        StartCoroutine(RefreshStaffAssignmentsSoon());
                    });
                    Text label = CreateText("Label", buttonObject.transform, option.Label, 16, FontStyle.Bold,
                        TextAnchor.MiddleCenter, option.Assignment == current ? Color.white : ShopUiSkin.TextBody);
                    SetRect(label.rectTransform, Vector2.zero, new Vector2(175f, 60f), new Vector2(0.5f, 0.5f));
                }
            }
            staffAssignmentStatus.text = "2번: " + ShopStaffMachineAssignment.Label(game.StaffAssignmentSlot2.Value) +
                                         "  ·  3번: " + ShopStaffMachineAssignment.Label(game.StaffAssignmentSlot3.Value);
            bool canHireThird = !game.IsStaffHired(ShopStaffRole.Collector);
            if (staffHireButton != null) staffHireButton.interactable = canHireThird;
            if (staffHireLabel != null)
                staffHireLabel.text = canHireThird ? "3번 알바 추가 고용" : "알바 3명 고용 완료";
        }

        private Card CreateCard(Transform parent, ShopUpgradeCategory category, int key, Color accent)
        {
            GameObject root = CreatePanel("Card_" + category, parent, new Vector2(735f, 182f), ShopUiSkin.CreamBackground);
            Image background = root.GetComponent<Image>();
            ShopUiSkin.Round(background, 20);
            Button button = root.AddComponent<Button>();
            button.targetGraphic = background;
            int index = key - 1;
            button.onClick.AddListener(() => Purchase(index));
            GameObject number = CreatePanel("NumberBadge", root.transform, new Vector2(40f, 40f), ShopUiSkin.BrownMid);
            SetRect(number.GetComponent<RectTransform>(), new Vector2(14f, -14f), new Vector2(40f, 40f), new Vector2(0f, 1f));
            ShopUiSkin.Round(number.GetComponent<Image>(), 20);
            Text numberText = CreateText("Number", number.transform, key.ToString(), 16, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            SetRect(numberText.rectTransform, Vector2.zero, new Vector2(40f, 40f), new Vector2(0.5f, 0.5f));
            ShopUiSkin.AddIcon("Category", root.transform, IconFor(category), accent,
                new Vector2(56f, 56f), new Vector2(68f, -14f), new Vector2(0f, 1f));
            Text title = CreateText("Title", root.transform, string.Empty, 25, FontStyle.Bold, TextAnchor.MiddleLeft, ShopUiSkin.BrownDeep);
            SetRect(title.rectTransform, new Vector2(140f, -12f), new Vector2(420f, 42f), new Vector2(0f, 1f));
            Text history = CreateText("History", root.transform, string.Empty, 19, FontStyle.Bold,
                TextAnchor.MiddleLeft, ShopUiSkin.Teal);
            SetRect(history.rectTransform, new Vector2(140f, -54f), new Vector2(260f, 36f), new Vector2(0f, 1f));
            Text next = CreateText("Next", root.transform, string.Empty, 17, FontStyle.Bold, TextAnchor.MiddleLeft, ShopUiSkin.TextBody);
            SetRect(next.rectTransform, new Vector2(18f, -106f), new Vector2(699f, 60f), new Vector2(0f, 1f));
            GameObject nextBar = CreatePanel("NextBar", root.transform, new Vector2(699f, 60f), ShopUiSkin.CreamCard);
            nextBar.transform.SetAsFirstSibling();
            SetRect(nextBar.GetComponent<RectTransform>(), new Vector2(18f, -106f), new Vector2(699f, 60f), new Vector2(0f, 1f));
            ShopUiSkin.Round(nextBar.GetComponent<Image>(), 12);
            next.transform.SetAsLastSibling();
            return new Card { Category = category, Background = background, Title = title, History = history, Next = next, Button = button, Dots = history };
        }

        private void Refresh()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null) return;
            moneyText.text = game.Coins.Value.ToString("N0") + "원  ·  " + game.TotalUpgradeLevel + "/" + ShopNetworkGame.TotalSupportedUpgradeLevels;
            for (int i = 0; i < cards.Count; i++)
            {
                Card card = cards[i];
                int level = game.GetUpgradeLevel(card.Category);
                int max = ShopNetworkGame.GetUpgradeMaxLevel(card.Category);
                int cost = game.GetNextUpgradeCost(card.Category);
                card.Title.text = ShopNetworkGame.UpgradeTitle(card.Category);
                card.History.text = ProgressDots(level, max);
                card.Next.text = level >= max ? "최대 레벨 · 현재 효과 적용 중" :
                    "다음  " + game.UpgradeNextEffect(card.Category) + "     " + cost.ToString("N0") + "원";
                card.Button.interactable = level < max && cost > 0;
                card.Background.color = level >= max ? new Color32(0xE7, 0xDD, 0xCB, 0xFF) :
                    game.Coins.Value >= cost ? ShopUiSkin.CreamBackground : new Color32(0xF0, 0xE1, 0xDC, 0xFF);
            }
        }

        private static string ProgressDots(int level, int max)
        {
            string result = string.Empty;
            for (int i = 0; i < max; i++) result += i < level ? "● " : "○ ";
            return result.TrimEnd();
        }

        private static ShopUiIcon IconFor(ShopUpgradeCategory category)
        {
            return category switch
            {
                ShopUpgradeCategory.Player => ShopUiIcon.Shoe,
                ShopUpgradeCategory.Operations => ShopUiIcon.Store,
                ShopUpgradeCategory.Facility => ShopUiIcon.Idea,
                ShopUpgradeCategory.Claw => ShopUiIcon.Target,
                ShopUpgradeCategory.Gacha => ShopUiIcon.Capsule,
                ShopUpgradeCategory.Kuji => ShopUiIcon.Ticket,
                ShopUpgradeCategory.StoreExpansion => ShopUiIcon.Expand,
                ShopUpgradeCategory.Staff => ShopUiIcon.People,
                _ => ShopUiIcon.Star
            };
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
            target.font = ShopUiFonts.Resolve(style); target.fontSize = size; target.fontStyle = FontStyle.Normal;
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
