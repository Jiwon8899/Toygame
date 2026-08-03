using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    public sealed class ShopNetworkHUD : MonoBehaviour
    {
        private static readonly Color NormalTextColor = new(0.92f, 0.97f, 1f, 1f);
        private static readonly Color HighlightTextColor = new(1f, 0.82f, 0.3f, 1f);

        [SerializeField] private Text statusText;
        [SerializeField] private Text objectiveText;
        [SerializeField] private Text eventText;
        [SerializeField] private Text promptText;
        [SerializeField] private Text networkText;
        [SerializeField] private Text nightSalesText;
        [SerializeField] private Text summaryText;

        private GameObject statusPanel;
        private int previousCoins = int.MinValue;
        private int previousReputation = int.MinValue;
        private float moneyHighlightUntil;
        private float reputationHighlightUntil;

        private void Awake()
        {
            PrepareCompactLayout();
        }

        public void Configure(Text status, Text objective, Text activity, Text prompt, Text network)
        {
            statusText = status;
            objectiveText = objective;
            eventText = activity;
            promptText = prompt;
            networkText = network;
        }

        public void ConfigureNightSales(Text nightSales, Text summary)
        {
            nightSalesText = nightSales;
            summaryText = summary;
        }

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            ShopNightSalesSystem night = ShopNightSalesSystem.Instance;
            bool connected = manager != null && manager.IsHost;
            bool modalOpen = ShopProgressionHUD.IsOpen || ShopUpgradeUI.IsOpen;

            if (networkText != null) networkText.gameObject.SetActive(false);
            if (objectiveText != null) objectiveText.gameObject.SetActive(false);
            if (eventText != null) eventText.gameObject.SetActive(false);
            if (nightSalesText != null && nightSalesText.transform.parent != null)
                nightSalesText.transform.parent.gameObject.SetActive(false);
            if (statusPanel != null) statusPanel.SetActive(!modalOpen);

            if (!connected || game == null || !game.IsSpawned)
            {
                if (statusText != null) statusText.text = "가게 운영을 준비하고 있습니다.";
                if (promptText != null) promptText.gameObject.SetActive(false);
                return;
            }

            if (statusText != null)
            {
                if (previousCoins != int.MinValue && previousCoins != game.Coins.Value)
                    moneyHighlightUntil = Time.unscaledTime + 0.8f;
                if (previousReputation != int.MinValue && previousReputation != game.Reputation.Value)
                    reputationHighlightUntil = Time.unscaledTime + 0.8f;
                previousCoins = game.Coins.Value;
                previousReputation = game.Reputation.Value;

                string moneyColor = ColorUtility.ToHtmlStringRGB(
                    Time.unscaledTime < moneyHighlightUntil ? HighlightTextColor : NormalTextColor);
                string reputationColor = ColorUtility.ToHtmlStringRGB(
                    Time.unscaledTime < reputationHighlightUntil ? HighlightTextColor : NormalTextColor);
                string line = game.Day.Value + "일차 · " + TimePeriod(game.Phase.Value) +
                              "  |  <color=#" + moneyColor + ">" + game.Coins.Value.ToString("N0") +
                              "원</color>  |  <color=#" + reputationColor + ">평판 " +
                              game.Reputation.Value.ToString("N0") + "</color>";
                if (night != null && game.Phase.Value == ShopPhase.Open)
                    line += "  |  오늘 매출 " + night.CurrentRevenue.Value.ToString("N0") +
                            "원  |  대기 " + night.QueueCount.Value + "명";
                statusText.text = line;
            }

            if (promptText != null)
            {
                string prompt = ShopPlayerInteractor.LocalPrompt;
                promptText.text = prompt;
                promptText.gameObject.SetActive(!modalOpen && !string.IsNullOrWhiteSpace(prompt));
            }

            if (summaryText != null)
            {
                bool show = night != null && game.Phase.Value == ShopPhase.Summary;
                summaryText.gameObject.SetActive(show);
                if (show)
                {
                    summaryText.text = "오늘의 영업 정산\n" +
                        "방문 손님 " + night.VisitCount.Value + "명  |  구매 손님 " + night.PurchaseCustomerCount.Value + "명  |  구매 포기 " + night.GiveUpCount.Value + "명\n" +
                        "총판매 " + night.TotalSaleQuantity.Value + "개  |  총매출 " + night.TotalRevenue.Value.ToString("N0") + "원  |  평균 만족도 " + night.AverageSatisfaction + "점\n" +
                        "평판 변화 " + Signed(night.ReputationDelta.Value) + "  |  최다 판매 상품 " + night.TopProductName.Value;
                }
            }
        }

        private void PrepareCompactLayout()
        {
            if (statusText != null)
            {
                statusPanel = statusText.transform.parent != null
                    ? statusText.transform.parent.gameObject
                    : statusText.gameObject;
                RectTransform panelRect = statusPanel.GetComponent<RectTransform>();
                if (panelRect != null)
                {
                    panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 1f);
                    panelRect.pivot = new Vector2(0f, 1f);
                    panelRect.anchoredPosition = new Vector2(24f, -24f);
                    panelRect.sizeDelta = new Vector2(900f, 68f);
                }
                RectTransform textRect = statusText.rectTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(20f, 8f);
                textRect.offsetMax = new Vector2(-20f, -8f);
                statusText.fontSize = 25;
                statusText.alignment = TextAnchor.MiddleLeft;
                statusText.horizontalOverflow = HorizontalWrapMode.Overflow;
                statusText.verticalOverflow = VerticalWrapMode.Truncate;
                statusText.supportRichText = true;
            }

            if (promptText != null)
            {
                RectTransform rect = promptText.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 52f);
                rect.sizeDelta = new Vector2(900f, 56f);
                promptText.fontSize = 27;
                promptText.alignment = TextAnchor.MiddleCenter;
            }

            Transform controls = transform.Find("Controls");
            if (controls != null) controls.gameObject.SetActive(false);
        }

        private static string TimePeriod(ShopPhase phase)
        {
            return phase switch
            {
                ShopPhase.PrizeHunt => "낮",
                ShopPhase.Setup => "저녁",
                ShopPhase.Open => "밤",
                ShopPhase.Summary => "마감",
                ShopPhase.Complete => "운영 완료",
                _ => "준비"
            };
        }

        private static string FormatTime(int totalSeconds) => Mathf.Max(0, totalSeconds / 60).ToString("00") + ":" + Mathf.Max(0, totalSeconds % 60).ToString("00");

        private static string Signed(int value)
        {
            return value >= 0 ? "+" + value : value.ToString();
        }
    }
}
