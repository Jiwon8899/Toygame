using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(710)]
    public sealed class ShopClosingSummaryPresenter : MonoBehaviour
    {
        private static ShopClosingSummaryPresenter instance;
        private ShopOperationsConfig config;
        private Canvas canvas;
        private CanvasGroup group;
        private Text title;
        private Text revenue;
        private Text sold;
        private Text goal;
        private Text reputation;
        private Text nextDay;
        private Text footer;
        private Coroutine presentation;
        private bool skipRequested;
        private bool waitingForClose;
        private ShopPhase observedPhase = (ShopPhase)(-1);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[UI] Closing Summary Presenter");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopClosingSummaryPresenter>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            config = ShopOperationsConfig.Load();
            BuildUi();
        }

        private void Update()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            ShopPhase phase = game != null ? game.Phase.Value : (ShopPhase)(-1);
            if (phase != observedPhase)
            {
                observedPhase = phase;
                if (phase == ShopPhase.Summary) BeginPresentation();
                else Hide();
            }

            bool pressed = Keyboard.current != null &&
                           (Keyboard.current.eKey.wasPressedThisFrame ||
                            Keyboard.current.spaceKey.wasPressedThisFrame ||
                            Keyboard.current.escapeKey.wasPressedThisFrame);
            if (!pressed || !canvas.gameObject.activeSelf) return;
            if (waitingForClose)
            {
                game?.RequestInteraction(ShopAction.EndDay);
                Hide();
            }
            else skipRequested = true;
        }

        private void BeginPresentation()
        {
            if (presentation != null) StopCoroutine(presentation);
            presentation = StartCoroutine(PlayPresentation());
        }

        private IEnumerator PlayPresentation()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            ShopNightSalesSystem night = ShopNightSalesSystem.Instance;
            ShopLiveOperationsNetwork live = ShopLiveOperationsNetwork.Instance;
            if (game == null || night == null) yield break;

            skipRequested = false;
            waitingForClose = false;
            ClearRows();
            canvas.gameObject.SetActive(true);
            ShopInputModeManager.Push(this, ShopInputMode.UI);
            float fade = config != null ? config.ClosingFadeSeconds : 0.45f;
            float elapsed = 0f;
            while (elapsed < fade && !skipRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Clamp01(elapsed / fade);
                yield return null;
            }
            group.alpha = 1f;

            int day = game.Day.Value;
            int totalRevenue = night.TotalRevenue.Value;
            int totalSold = night.TotalSaleQuantity.Value;
            int goalTarget = live != null ? live.DailySalesGoal.Value : 1;
            bool goalMet = totalSold >= goalTarget;
            int reputationDelta = night.ReputationDelta.Value;
            float interval = config != null ? config.ClosingItemInterval : 0.4f;

            title.text = day + "일차 마감\n오늘도 수고했어요!";
            yield return WaitOrSkip(interval);
            if (!skipRequested)
            {
                float countSeconds = config != null ? config.ClosingRevenueCountSeconds : 0.8f;
                elapsed = 0f;
                while (elapsed < countSeconds && !skipRequested)
                {
                    elapsed += Time.unscaledDeltaTime;
                    int shown = Mathf.RoundToInt(totalRevenue * Mathf.Clamp01(elapsed / countSeconds));
                    revenue.text = shown.ToString("N0") + "원";
                    yield return null;
                }
            }
            revenue.text = totalRevenue.ToString("N0") + "원";
            yield return WaitOrSkip(interval);
            sold.text = totalSold + "개";
            yield return WaitOrSkip(interval);
            goal.text = (goalMet ? "달성" : "미달성") + "\n" + totalSold + " / " + goalTarget;
            goal.color = goalMet ? ShopUiSkin.Teal : ShopUiSkin.Orange;
            yield return WaitOrSkip(interval);
            reputation.text = (reputationDelta >= 0 ? "+" : string.Empty) + reputationDelta;
            reputation.color = reputationDelta >= 0 ? ShopUiSkin.Teal : ShopUiSkin.Orange;
            yield return WaitOrSkip(interval);
            nextDay.text = "다음 날에는 준비 시간에 기계 재고가 채워져요.";

            if (skipRequested)
            {
                title.text = day + "일차 마감\n오늘도 수고했어요!";
                revenue.text = totalRevenue.ToString("N0") + "원";
                sold.text = totalSold + "개";
                goal.text = (goalMet ? "달성" : "미달성") + "\n" + totalSold + " / " + goalTarget;
                goal.color = goalMet ? ShopUiSkin.Teal : ShopUiSkin.Orange;
                reputation.text = (reputationDelta >= 0 ? "+" : string.Empty) + reputationDelta;
                reputation.color = reputationDelta >= 0 ? ShopUiSkin.Teal : ShopUiSkin.Orange;
                nextDay.text = "다음 날에는 준비 시간에 기계 재고가 채워져요.";
            }
            footer.text = "다음 날로  →\nE · Space · Esc";
            skipRequested = false;
            waitingForClose = true;
            presentation = null;
        }

        private IEnumerator WaitOrSkip(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds && !skipRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void Hide()
        {
            if (presentation != null) { StopCoroutine(presentation); presentation = null; }
            skipRequested = false;
            waitingForClose = false;
            if (canvas != null) canvas.gameObject.SetActive(false);
            ShopInputModeManager.Pop(this);
        }

        private void OnDestroy()
        {
            ShopInputModeManager.Pop(this);
            if (instance == this) instance = null;
        }

        private void ClearRows()
        {
            group.alpha = 0f;
            title.text = revenue.text = sold.text = goal.text = reputation.text = nextDay.text = footer.text = string.Empty;
            goal.color = ShopUiSkin.TextBody;
            reputation.color = ShopUiSkin.TextBody;
        }

        private void BuildUi()
        {
            GameObject canvasObject = new("ClosingSummaryCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            group = canvasObject.GetComponent<CanvasGroup>();
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            Image backdrop = CreateImage("Backdrop", canvas.transform, ShopUiSkin.BrownDeep);
            RectTransform bg = backdrop.rectTransform;
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
            bg.offsetMin = bg.offsetMax = Vector2.zero;
            AddConfetti(backdrop.transform);
            Image card = CreateImage("SummaryCard", backdrop.transform, ShopUiSkin.CreamCard);
            RectTransform cardRect = card.rectTransform;
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(1060f, 860f);
            ShopUiSkin.Round(card, 28);

            title = CreateText("Title", card.transform, 38, FontStyle.Bold, ShopUiSkin.BrownDeep);
            Place(title, 332f, 110f);

            Image revenueCard = CreateImage("RevenueHero", card.transform, ShopUiSkin.Teal);
            Place(revenueCard.rectTransform, new Vector2(0f, 190f), new Vector2(900f, 150f));
            ShopUiSkin.Round(revenueCard, 20);
            Text revenueLabel = CreateText("RevenueLabel", revenueCard.transform, 18, FontStyle.Normal, Color.white);
            revenueLabel.text = "오늘 매출";
            Place(revenueLabel, 42f, 32f);
            revenue = CreateText("Revenue", revenueCard.transform, 46, FontStyle.Bold, Color.white);
            Place(revenue, -18f, 70f);

            sold = CreateStatCard("SoldCard", card.transform, "판매 수량", -310f);
            goal = CreateStatCard("GoalCard", card.transform, "목표 달성", 0f);
            reputation = CreateStatCard("ReputationCard", card.transform, "평판 변화", 310f);

            Image notice = CreateImage("NextDayBanner", card.transform, ShopUiSkin.CreamBackground);
            Place(notice.rectTransform, new Vector2(0f, -120f), new Vector2(900f, 92f));
            ShopUiSkin.Round(notice, 20);
            ShopUiSkin.AddIcon("Idea", notice.transform, ShopUiIcon.Idea, ShopUiSkin.Orange,
                new Vector2(58f, 58f), new Vector2(18f, -17f), new Vector2(0f, 1f));
            nextDay = CreateText("NextDay", notice.transform, 20, FontStyle.Bold, ShopUiSkin.TextBody);
            nextDay.alignment = TextAnchor.MiddleLeft;
            nextDay.rectTransform.anchorMin = Vector2.zero;
            nextDay.rectTransform.anchorMax = Vector2.one;
            nextDay.rectTransform.offsetMin = new Vector2(94f, 8f);
            nextDay.rectTransform.offsetMax = new Vector2(-20f, -8f);

            Image cta = CreateImage("NextDayButton", card.transform, ShopUiSkin.Teal);
            Place(cta.rectTransform, new Vector2(0f, -244f), new Vector2(620f, 78f));
            ShopUiSkin.Pill(cta);
            footer = CreateText("Footer", cta.transform, 23, FontStyle.Bold, Color.white);
            footer.rectTransform.anchorMin = Vector2.zero;
            footer.rectTransform.anchorMax = Vector2.one;
            footer.rectTransform.offsetMin = footer.rectTransform.offsetMax = Vector2.zero;
            canvasObject.SetActive(false);
        }

        private Text CreateStatCard(string name, Transform parent, string label, float x)
        {
            Image card = CreateImage(name, parent, ShopUiSkin.CreamBackground);
            Place(card.rectTransform, new Vector2(x, 34f), new Vector2(280f, 150f));
            ShopUiSkin.Round(card, 20);
            Text caption = CreateText("Label", card.transform, 17, FontStyle.Normal, ShopUiSkin.TextMuted);
            caption.text = label;
            Place(caption, 40f, 32f);
            Text value = CreateText("Value", card.transform, 30, FontStyle.Bold, ShopUiSkin.TextBody);
            Place(value, -20f, 72f);
            return value;
        }

        private static void AddConfetti(Transform parent)
        {
            Color[] colors = { ShopUiSkin.Pink, ShopUiSkin.Teal, ShopUiSkin.Orange, ShopUiSkin.Currency };
            for (int i = 0; i < 28; i++)
            {
                Image dot = CreateImage("Confetti", parent, new Color(colors[i % colors.Length].r,
                    colors[i % colors.Length].g, colors[i % colors.Length].b, 0.35f));
                RectTransform rect = dot.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2((i * 37 % 100) / 100f, (i * 61 % 100) / 100f);
                rect.sizeDelta = new Vector2(10f + i % 3 * 6f, 10f + i % 3 * 6f);
                rect.anchoredPosition = Vector2.zero;
                ShopUiSkin.Round(dot, 12);
            }
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject item = new(name, typeof(RectTransform), typeof(Image));
            item.transform.SetParent(parent, false);
            Image image = item.GetComponent<Image>(); image.color = color; return image;
        }

        private static Text CreateText(string name, Transform parent, int size, FontStyle style, Color color)
        {
            GameObject item = new(name, typeof(RectTransform), typeof(Text));
            item.transform.SetParent(parent, false);
            Text text = item.GetComponent<Text>();
            text.font = ShopUiFonts.Resolve(style);
            text.fontSize = size; text.fontStyle = FontStyle.Normal; text.color = color;
            text.alignment = TextAnchor.MiddleCenter; return text;
        }

        private static void Place(Text text, float y, float height)
        {
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(860f, height); rect.anchoredPosition = new Vector2(0f, y);
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
