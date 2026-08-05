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
        private RectTransform marker;
        private Image successBand;
        private Text priceText;
        private Text attemptsText;
        private int openedFrame;
        private float markerPosition;
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
            bool localActive = sales != null && network != null && network.IsClient &&
                               sales.NegotiationActive.Value &&
                               sales.NegotiationOwner.Value == network.LocalClientId;
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

            float cycles = config != null ? config.NegotiationMarkerCyclesPerSecond : 0.75f;
            markerPosition = Mathf.PingPong(Time.unscaledTime * cycles, 1f);
            marker.anchorMin = marker.anchorMax = new Vector2(markerPosition, 0.5f);
            priceText.text = "기준 판매가  " + sales.NegotiationBasePrice.Value.ToString("N0") + "원";
            attemptsText.text = "남은 기회 " + sales.NegotiationAttemptsRemaining.Value + "회  ·  [E] 멈추기";
            if (lastAttempts != sales.NegotiationAttemptsRemaining.Value)
                lastAttempts = sales.NegotiationAttemptsRemaining.Value;

            if (Time.frameCount > openedFrame && Keyboard.current != null &&
                Keyboard.current.eKey.wasPressedThisFrame)
                ShopNetworkGame.Instance?.RequestNegotiationResolve(markerPosition);
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
            canvas.sortingOrder = 850;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject panel = Ui("Panel", canvas.transform, new Color(0.025f, 0.055f, 0.075f, 0.96f));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(860f, 360f);

            Text title = Label("Title", panel.transform, "계산대 흥정", 48, FontStyle.Bold);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(760f, 70f), new Vector2(0f, -55f));
            priceText = Label("Price", panel.transform, string.Empty, 32, FontStyle.Normal);
            SetRect(priceText.rectTransform, new Vector2(0.5f, 1f), new Vector2(760f, 52f), new Vector2(0f, -120f));

            GameObject bar = Ui("PriceRange", panel.transform, new Color(0.12f, 0.17f, 0.23f, 1f));
            RectTransform barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0.5f);
            barRect.sizeDelta = new Vector2(700f, 72f);
            barRect.anchoredPosition = new Vector2(0f, -5f);
            GameObject success = Ui("SuccessBand", bar.transform, new Color(0.2f, 0.85f, 0.45f, 0.85f));
            successBand = success.GetComponent<Image>();
            RectTransform successRect = success.GetComponent<RectTransform>();
            float half = config != null ? config.NegotiationSuccessHalfWidth : 0.22f;
            successRect.anchorMin = new Vector2(0.5f - half, 0f);
            successRect.anchorMax = new Vector2(0.5f + half, 1f);
            successRect.offsetMin = successRect.offsetMax = Vector2.zero;
            GameObject markerObject = Ui("Marker", bar.transform, new Color(1f, 0.8f, 0.18f, 1f));
            marker = markerObject.GetComponent<RectTransform>();
            marker.sizeDelta = new Vector2(14f, 100f);

            attemptsText = Label("Attempts", panel.transform, string.Empty, 28, FontStyle.Bold);
            SetRect(attemptsText.rectTransform, new Vector2(0.5f, 0f), new Vector2(760f, 60f), new Vector2(0f, 55f));
            canvas.gameObject.SetActive(false);
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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
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
