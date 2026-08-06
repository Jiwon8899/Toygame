using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    public sealed class ShopNetworkHUD : MonoBehaviour
    {
        [SerializeField] private Text statusText;
        [SerializeField] private Text objectiveText;
        [SerializeField] private Text eventText;
        [SerializeField] private Text promptText;
        [SerializeField] private Text networkText;
        [SerializeField] private Text nightSalesText;
        [SerializeField] private Text summaryText;

        private GameObject statusPanel;
        private Text dayChipText;
        private Text moneyChipText;
        private Text reputationChipText;
        private GameObject networkPanel;
        private Text networkLabel;
        private GameObject nightPanel;
        private GameObject nightOwner;
        private Text nightLabel;

        private void Awake() => PrepareWarmLayout();

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
            NetworkManager net = NetworkManager.Singleton;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            ShopNightSalesSystem night = ShopNightSalesSystem.Instance;
            bool connected = net != null && (net.IsHost || net.IsClient);
            bool modalOpen = ShopProgressionHUD.IsOpen || ShopUpgradeUI.IsOpen;

            HideLegacyStatusObjects();
            if (statusPanel != null) statusPanel.SetActive(!modalOpen);
            if (networkPanel != null) networkPanel.SetActive(false);

            if (!connected || game == null || !game.IsSpawned)
            {
                SetChip(dayChipText, "준비 중");
                SetChip(moneyChipText, "0원");
                SetChip(reputationChipText, "평판 0");
                if (networkLabel != null) networkLabel.text = "서버에 연결하는 중입니다";
                if (nightPanel != null) nightPanel.SetActive(false);
                if (promptText != null) promptText.gameObject.SetActive(false);
                return;
            }

            SetChip(dayChipText, $"{game.Day.Value}일차 · {TimePeriod(game.Phase.Value)}");
            SetChip(moneyChipText, game.Coins.Value.ToString("N0") + "원");
            SetChip(reputationChipText, "평판 " + game.Reputation.Value.ToString("N0"));
            if (networkLabel != null)
                networkLabel.text = net.IsHost ? "호스트 연결됨" : "서버 연결됨";

            bool showNight = !modalOpen && night != null && game.Phase.Value == ShopPhase.Open;
            if (nightPanel != null) nightPanel.SetActive(showNight);
            if (showNight && nightLabel != null)
                nightLabel.text = $"오늘 매출  {night.CurrentRevenue.Value:N0}원\n계산 대기  {night.QueueCount.Value}명";

            if (promptText != null)
            {
                string prompt = ShopPlayerInteractor.LocalPrompt;
                promptText.text = prompt;
                promptText.gameObject.SetActive(!modalOpen && !string.IsNullOrWhiteSpace(prompt));
            }

            if (summaryText != null) summaryText.gameObject.SetActive(false);
        }

        private void PrepareWarmLayout()
        {
            HideLegacyStatusObjects();
            if (statusText != null)
            {
                statusPanel = statusText.transform.parent != null ? statusText.transform.parent.gameObject : statusText.gameObject;
                statusText.gameObject.SetActive(false);
                RectTransform rect = statusPanel.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(24f, -24f);
                rect.sizeDelta = new Vector2(760f, 64f);
                Image oldBackground = statusPanel.GetComponent<Image>();
                if (oldBackground != null) oldBackground.color = Color.clear;
                dayChipText = CreateChip("DayChip", statusPanel.transform, ShopUiIcon.Moon, 0f, 228f);
                moneyChipText = CreateChip("MoneyChip", statusPanel.transform, ShopUiIcon.Coin, 240f, 236f);
                reputationChipText = CreateChip("ReputationChip", statusPanel.transform, ShopUiIcon.Star, 488f, 248f);
            }

            if (promptText != null)
            {
                RectTransform rect = promptText.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 38f);
                rect.sizeDelta = new Vector2(920f, 64f);
                promptText.font = ShopUiFonts.Bold;
                promptText.fontSize = 22;
                promptText.fontStyle = FontStyle.Normal;
                promptText.alignment = TextAnchor.MiddleCenter;
                promptText.color = Color.white;
                Image background = promptText.GetComponent<Image>();
                if (background == null)
                {
                    Transform oldParent = promptText.transform.parent;
                    GameObject backdrop = new("InteractionPromptBackground", typeof(RectTransform), typeof(Image));
                    backdrop.transform.SetParent(oldParent, false);
                    RectTransform backdropRect = backdrop.GetComponent<RectTransform>();
                    backdropRect.anchorMin = backdropRect.anchorMax = new Vector2(0.5f, 0f);
                    backdropRect.pivot = new Vector2(0.5f, 0f);
                    backdropRect.anchoredPosition = new Vector2(0f, 38f);
                    backdropRect.sizeDelta = new Vector2(920f, 64f);
                    promptText.transform.SetParent(backdrop.transform, false);
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = new Vector2(18f, 4f);
                    rect.offsetMax = new Vector2(-18f, -4f);
                    background = backdrop.GetComponent<Image>();
                }
                Color brown = ShopUiSkin.BrownDeep;
                background.color = new Color(brown.r, brown.g, brown.b, 0.94f);
                background.raycastTarget = false;
                ShopUiSkin.Pill(background);
            }

            Transform controls = transform.Find("Controls");
            if (controls != null) controls.gameObject.SetActive(false);
            BuildStackItems();
        }

        private void BuildStackItems()
        {
            networkPanel = ShopHudStack.Instance.CreateItem(this, ShopHudStackSlot.Network, "NetworkStatus", 72f);
            ShopUiSkin.AddIcon("Network", networkPanel.transform, ShopUiIcon.People, ShopUiSkin.Teal,
                new Vector2(48f, 48f), new Vector2(14f, -12f), new Vector2(0f, 1f));
            networkLabel = CreateStackText("NetworkLabel", networkPanel.transform, 74f, 16f, 18);
            networkPanel.SetActive(false);

            nightOwner = new GameObject("NightSalesStackOwner");
            nightOwner.transform.SetParent(transform, false);
            nightPanel = ShopHudStack.Instance.CreateItem(nightOwner, ShopHudStackSlot.NightSales, "NightSales", 96f);
            ShopUiSkin.AddIcon("Night", nightPanel.transform, ShopUiIcon.Store, ShopUiSkin.Orange,
                new Vector2(52f, 52f), new Vector2(14f, -14f), new Vector2(0f, 1f));
            nightLabel = CreateStackText("NightLabel", nightPanel.transform, 80f, 14f, 18);
            nightPanel.SetActive(false);
        }

        private static Text CreateChip(string name, Transform parent, ShopUiIcon icon, float x, float width)
        {
            GameObject chip = new(name, typeof(RectTransform), typeof(Image));
            chip.transform.SetParent(parent, false);
            RectTransform rect = chip.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, 60f);
            Image image = chip.GetComponent<Image>();
            image.color = ShopUiSkin.CreamCard;
            ShopUiSkin.Pill(image);
            ShopUiSkin.AddIcon(name, chip.transform, icon, ShopUiSkin.Teal, new Vector2(42f, 42f),
                new Vector2(9f, -9f), new Vector2(0f, 1f));
            Text text = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(chip.transform, false);
            text.font = ShopUiFonts.Bold;
            text.fontSize = 18;
            text.fontStyle = FontStyle.Normal;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = ShopUiSkin.TextBody;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(60f, 4f);
            text.rectTransform.offsetMax = new Vector2(-12f, -4f);
            return text;
        }

        private static Text CreateStackText(string name, Transform parent, float left, float right, int fontSize)
        {
            Text text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = ShopUiFonts.Bold;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Normal;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = ShopUiSkin.TextBody;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(left, 8f);
            text.rectTransform.offsetMax = new Vector2(-right, -8f);
            return text;
        }

        private void HideLegacyStatusObjects()
        {
            if (networkText != null) networkText.gameObject.SetActive(false);
            if (objectiveText != null) objectiveText.gameObject.SetActive(false);
            if (eventText != null) eventText.gameObject.SetActive(false);
            if (nightSalesText != null && nightSalesText.transform.parent != null)
                nightSalesText.transform.parent.gameObject.SetActive(false);
        }

        private static void SetChip(Text target, string value)
        {
            if (target != null) target.text = value;
        }

        private void OnDestroy()
        {
            ShopHudStack.Instance.RemoveItem(this);
            if (nightOwner != null) ShopHudStack.Instance.RemoveItem(nightOwner);
        }

        private static string TimePeriod(ShopPhase phase)
        {
            return phase switch
            {
                ShopPhase.PrizeHunt => "낮",
                ShopPhase.Setup => "준비",
                ShopPhase.Open => "영업",
                ShopPhase.Summary => "마감",
                ShopPhase.Complete => "영업 완료",
                _ => "준비"
            };
        }
    }
}
