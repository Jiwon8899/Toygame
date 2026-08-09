using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(700)]
    public sealed class ShopNegotiationPresenter : MonoBehaviour
    {
        private static ShopNegotiationPresenter instance;
        private ShopOperationsConfig config;
        private Canvas canvas;
        private Text priceText;
        private Text attemptsText;
        private Text titleText;
        private readonly Button[] offerButtons = new Button[3];
        private readonly Text[] offerLabels = new Text[3];
        private int openedFrame;
        private int lastAttempts = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[UI] Checkout Negotiation");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopNegotiationPresenter>();
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
            ShopNightSalesSystem sales = ShopNightSalesSystem.Instance;
            NetworkManager network = NetworkManager.Singleton;
            bool checkoutActive = sales != null && network != null && network.IsClient &&
                                  sales.CheckoutPromptActive.Value &&
                                  sales.CheckoutPromptOwner.Value == network.LocalClientId;
            bool discountActive = sales != null && network != null && network.IsClient &&
                                  sales.DiscountRequestActive.Value &&
                                  sales.DiscountRequestOwner.Value == network.LocalClientId;
            bool localActive = checkoutActive || discountActive || (sales != null && network != null && network.IsClient &&
                               sales.NegotiationActive.Value &&
                               sales.NegotiationOwner.Value == network.LocalClientId);
            if (!localActive)
            {
                if (canvas.gameObject.activeSelf)
                {
                    canvas.gameObject.SetActive(false);
                    ShopInputModeManager.Pop(this);
                    lastAttempts = -1;
                }
                return;
            }

            if (!canvas.gameObject.activeSelf)
            {
                canvas.gameObject.SetActive(true);
                ShopInputModeManager.Push(this, ShopInputMode.UI);
                openedFrame = Time.frameCount;
                lastAttempts = sales.NegotiationAttemptsRemaining.Value;
            }

            titleText.text = checkoutActive ? "계산 확인" : discountActive ? "손님의 할인 요청" : "계산대 흥정";
            int basePrice = checkoutActive ? sales.CheckoutPromptBasePrice.Value :
                discountActive ? sales.DiscountRequestBasePrice.Value : sales.NegotiationBasePrice.Value;
            priceText.text = "기준 판매가  " + basePrice.ToString("N0") + "원";
            attemptsText.text = checkoutActive
                ? "E 한 번으로 열린 계산 창입니다 · Shift+E 또는 2번으로 흥정"
                : discountActive
                ? "거절해도 큰 불이익이 없습니다  ·  마우스 클릭 또는 숫자 1 / 2 / 3"
                : "남은 기회 " + sales.NegotiationAttemptsRemaining.Value +
                  "회  ·  마우스 클릭 또는 숫자 1 / 2 / 3";
            for (int i = 0; i < offerLabels.Length; i++)
            {
                offerButtons[i].gameObject.SetActive(!checkoutActive || i < 2);
                if (checkoutActive)
                {
                    string[] choices = { "바로 계산\n정가로 판매", "흥정 시작\nShift+E" };
                    if (i < choices.Length) offerLabels[i].text = (i + 1) + "  " + choices[i];
                }
                else if (discountActive)
                {
                    string[] choices = { "요청 수락\n요청 할인 적용", "일부만 할인\n부담 없는 절충", "정중히 거절\n정가 구매" };
                    offerLabels[i].text = (i + 1) + "  " + choices[i];
                }
                else
                {
                    ShopNegotiationOffer offer = config != null ? config.NegotiationOfferAt(i) :
                        new ShopNegotiationOffer("흥정", 0.1f, 0.8f, "성공 높음");
                    offerLabels[i].text = (i + 1) + "  " + offer.Label + "\n+" +
                                          Mathf.RoundToInt(offer.PriceBonus * 100f) + "%  ·  " + offer.Difficulty;
                }
            }
            if (lastAttempts != sales.NegotiationAttemptsRemaining.Value)
                lastAttempts = sales.NegotiationAttemptsRemaining.Value;

            if (Time.frameCount <= openedFrame || Keyboard.current == null) return;
            bool shiftHeld = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
            if (checkoutActive && shiftHeld && Keyboard.current.eKey.wasPressedThisFrame) ChooseOffer(1);
            else if (Keyboard.current.digit1Key.wasPressedThisFrame) ChooseOffer(0);
            else if (Keyboard.current.digit2Key.wasPressedThisFrame) ChooseOffer(1);
            else if (!checkoutActive && Keyboard.current.digit3Key.wasPressedThisFrame) ChooseOffer(2);
        }

        private void OnDestroy()
        {
            ShopInputModeManager.Pop(this);
            if (instance == this) instance = null;
        }

        private void BuildUi()
        {
            GameObject canvasObject = new("NegotiationCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 31050;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject panel = Ui("Panel", canvas.transform, new Color(0.025f, 0.055f, 0.075f, 0.96f));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1040f, 500f);

            titleText = Label("Title", panel.transform, "계산대 흥정", 48, FontStyle.Bold);
            SetRect(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(920f, 70f), new Vector2(0f, -55f));
            priceText = Label("Price", panel.transform, string.Empty, 32, FontStyle.Normal);
            SetRect(priceText.rectTransform, new Vector2(0.5f, 1f), new Vector2(920f, 52f), new Vector2(0f, -120f));

            Color[] colors =
            {
                new Color(0.10f, 0.42f, 0.28f, 1f),
                new Color(0.52f, 0.35f, 0.08f, 1f),
                new Color(0.52f, 0.16f, 0.12f, 1f)
            };
            for (int i = 0; i < offerButtons.Length; i++)
            {
                int index = i;
                ShopNegotiationOffer offer = config != null
                    ? config.NegotiationOfferAt(i)
                    : new ShopNegotiationOffer("흥정", 0.1f, 0.8f, "성공 높음");
                GameObject buttonObject = Ui("Offer" + (i + 1), panel.transform, colors[i]);
                Button button = buttonObject.AddComponent<Button>();
                button.targetGraphic = buttonObject.GetComponent<Image>();
                button.onClick.AddListener(() => ChooseOffer(index));
                RectTransform rect = buttonObject.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(286f, 164f);
                rect.anchoredPosition = new Vector2((i - 1) * 310f, -12f);
                Text label = Label("Label", buttonObject.transform,
                    (i + 1) + "  " + offer.Label + "\n" +
                    "+" + Mathf.RoundToInt(offer.PriceBonus * 100f) + "%  ·  " + offer.Difficulty,
                    24, FontStyle.Bold);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(14f, 12f);
                label.rectTransform.offsetMax = new Vector2(-14f, -12f);
                offerButtons[i] = button;
                offerLabels[i] = label;
            }

            attemptsText = Label("Attempts", panel.transform, string.Empty, 28, FontStyle.Bold);
            SetRect(attemptsText.rectTransform, new Vector2(0.5f, 0f), new Vector2(920f, 60f), new Vector2(0f, 45f));
            canvas.gameObject.SetActive(false);
        }

        private void ChooseOffer(int offerIndex)
        {
            if (Time.frameCount <= openedFrame) return;
            ShopNightSalesSystem sales = ShopNightSalesSystem.Instance;
            if (sales != null && sales.CheckoutPromptActive.Value)
                ShopNetworkGame.Instance?.RequestCheckoutPromptChoice(offerIndex);
            else if (sales != null && sales.DiscountRequestActive.Value)
                ShopNetworkGame.Instance?.RequestDiscountChoice(offerIndex);
            else
                ShopNetworkGame.Instance?.RequestNegotiationOffer(offerIndex);
        }

        private static GameObject Ui(string name, Transform parent, Color color)
        {
            GameObject item = new(name, typeof(RectTransform), typeof(Image));
            item.transform.SetParent(parent, false);
            item.GetComponent<Image>().color = color;
            return item;
        }

        private static Text Label(string name, Transform parent, string value, int size, FontStyle style)
        {
            GameObject item = new(name, typeof(RectTransform), typeof(Text));
            item.transform.SetParent(parent, false);
            Text text = item.GetComponent<Text>();
            text.font = ShopUiFonts.Resolve(style);
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyle.Normal;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            return text;
        }

        private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }
    }
}
