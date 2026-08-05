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

            title.text = day + "일차 마감";
            yield return WaitOrSkip(interval);
            if (!skipRequested)
            {
                float countSeconds = config != null ? config.ClosingRevenueCountSeconds : 0.8f;
                elapsed = 0f;
                while (elapsed < countSeconds && !skipRequested)
                {
                    elapsed += Time.unscaledDeltaTime;
                    int shown = Mathf.RoundToInt(totalRevenue * Mathf.Clamp01(elapsed / countSeconds));
                    revenue.text = "오늘 매출   " + shown.ToString("N0") + "원";
                    yield return null;
                }
            }
            revenue.text = "오늘 매출   " + totalRevenue.ToString("N0") + "원";
            yield return WaitOrSkip(interval);
            sold.text = "판매 수량   " + totalSold + "개";
            yield return WaitOrSkip(interval);
            goal.text = (goalMet ? "✓  목표 달성" : "—  목표 미달성") +
                        "   " + totalSold + " / " + goalTarget;
            goal.color = goalMet ? new Color(0.35f, 1f, 0.58f) : new Color(1f, 0.62f, 0.35f);
            yield return WaitOrSkip(interval);
            reputation.text = "평판 변화   " + (reputationDelta >= 0 ? "+" : string.Empty) + reputationDelta;
            yield return WaitOrSkip(interval);
            nextDay.text = "다음 날 예고   준비 시간에 기계 재고가 리필됩니다";

            if (skipRequested)
            {
                title.text = day + "일차 마감";
                revenue.text = "오늘 매출   " + totalRevenue.ToString("N0") + "원";
                sold.text = "판매 수량   " + totalSold + "개";
                goal.text = (goalMet ? "✓  목표 달성" : "—  목표 미달성") +
                            "   " + totalSold + " / " + goalTarget;
                goal.color = goalMet ? new Color(0.35f, 1f, 0.58f) : new Color(1f, 0.62f, 0.35f);
                reputation.text = "평판 변화   " + (reputationDelta >= 0 ? "+" : string.Empty) + reputationDelta;
                nextDay.text = "다음 날 예고   준비 시간에 기계 재고가 리필됩니다";
            }
            footer.text = "[E / Space / Esc] 다음 날로";
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
            goal.color = Color.white;
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

            Image backdrop = CreateImage("Backdrop", canvas.transform, new Color(0.008f, 0.015f, 0.025f, 0.97f));
            RectTransform bg = backdrop.rectTransform;
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
            bg.offsetMin = bg.offsetMax = Vector2.zero;
            Image card = CreateImage("SummaryCard", backdrop.transform, new Color(0.035f, 0.075f, 0.095f, 1f));
            RectTransform cardRect = card.rectTransform;
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(980f, 760f);

            title = CreateText("Title", card.transform, 54, FontStyle.Bold, new Color(1f, 0.82f, 0.3f));
            revenue = CreateText("Revenue", card.transform, 40, FontStyle.Bold, Color.white);
            sold = CreateText("Sold", card.transform, 36, FontStyle.Normal, Color.white);
            goal = CreateText("Goal", card.transform, 36, FontStyle.Bold, Color.white);
            reputation = CreateText("Reputation", card.transform, 36, FontStyle.Normal, Color.white);
            nextDay = CreateText("NextDay", card.transform, 30, FontStyle.Normal, new Color(0.68f, 0.88f, 1f));
            footer = CreateText("Footer", card.transform, 25, FontStyle.Normal, new Color(0.7f, 0.76f, 0.82f));
            Place(title, 265f, 90f); Place(revenue, 145f, 70f); Place(sold, 65f, 64f);
            Place(goal, -20f, 64f); Place(reputation, -105f, 64f); Place(nextDay, -215f, 60f);
            Place(footer, -325f, 50f);
            canvasObject.SetActive(false);
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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size; text.fontStyle = style; text.color = color;
            text.alignment = TextAnchor.MiddleCenter; return text;
        }

        private static void Place(Text text, float y, float height)
        {
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(860f, height); rect.anchoredPosition = new Vector2(0f, y);
        }
    }
}
