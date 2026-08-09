using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(-720)]
    public sealed class ShopStoreNamingSystem : MonoBehaviour
    {
        private static ShopStoreNamingSystem instance;

        private ShopStoreNamingConfig config;
        private GameObject namingCanvas;
        private InputField playerNameInput;
        private InputField rivalNameInput;
        private Button confirmButton;
        private Text playerCounter;
        private Text rivalCounter;

        public static ShopStoreNamingSystem Instance
        {
            get
            {
                if (instance == null) Bootstrap();
                return instance;
            }
        }

        public string PlayerShopName { get; private set; }
        public string RivalShopName { get; private set; }
        public bool HasConfirmedNames { get; private set; }
        public bool IsNaming => namingCanvas != null;
        public int MaximumNameLength => config != null ? config.MaximumNameLength : 10;

        public event Action NamesChanged;
        public event Action NamingCompleted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[Progression] Store Naming");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopStoreNamingSystem>();
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
            config = ShopStoreNamingConfig.Load();
            PlayerShopName = DefaultPlayerName;
            RivalShopName = DefaultRivalName;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            ShopInputModeManager.Pop(this);
        }

        public void BeginNewGameNaming()
        {
            HasConfirmedNames = false;
            PlayerShopName = string.Empty;
            RivalShopName = string.Empty;
            if (namingCanvas != null) Destroy(namingCanvas);
            BuildNamingUi();
            ShopInputModeManager.Push(this, ShopInputMode.Menu);
            playerNameInput.ActivateInputField();
        }

        public void SetDraftNames(string playerName, string rivalName)
        {
            if (playerNameInput == null || rivalNameInput == null) return;
            playerNameInput.text = Limit(playerName);
            rivalNameInput.text = Limit(rivalName);
            RefreshValidation();
        }

        public bool TryConfirmNaming()
        {
            if (playerNameInput == null || rivalNameInput == null) return false;
            string playerName = Normalize(playerNameInput.text);
            string rivalName = Normalize(rivalNameInput.text);
            if (!IsValid(playerName) || !IsValid(rivalName))
            {
                RefreshValidation();
                return false;
            }

            PlayerShopName = playerName;
            RivalShopName = rivalName;
            HasConfirmedNames = true;
            if (namingCanvas != null) Destroy(namingCanvas);
            namingCanvas = null;
            playerNameInput = null;
            rivalNameInput = null;
            confirmButton = null;
            ShopInputModeManager.Pop(this);
            ApplyNamesToSigns();
            ShopProgressionManager.Instance?.SetStoreNames(PlayerShopName, RivalShopName);
            NamesChanged?.Invoke();
            NamingCompleted?.Invoke();
            return true;
        }

        public void RestoreSavedNames(string playerName, string rivalName)
        {
            PlayerShopName = ResolveRestoredName(playerName, true);
            RivalShopName = ResolveRestoredName(rivalName, false);
            HasConfirmedNames = true;
            if (namingCanvas != null) Destroy(namingCanvas);
            namingCanvas = null;
            playerNameInput = null;
            rivalNameInput = null;
            confirmButton = null;
            ShopInputModeManager.Pop(this);
            ApplyNamesToSigns();
            NamesChanged?.Invoke();
        }

        public bool ApplyNamesToSigns()
        {
            Scene scene = SceneManager.GetActiveScene();
            Transform playerSign = FindScenePath(scene,
                "PickAndPlaceShop_Generated/Architecture/ShopSign");
            Transform rivalSign = FindScenePath(scene, "RivalShop/ShopSign (1)");
            if (playerSign == null || rivalSign == null) return false;

            bool playerApplied = ApplySignText(playerSign, ResolvedPlayerName);
            bool rivalApplied = ApplySignText(rivalSign, ResolvedRivalName);
            return playerApplied && rivalApplied;
        }

        private string ResolvedPlayerName => string.IsNullOrWhiteSpace(PlayerShopName)
            ? DefaultPlayerName
            : PlayerShopName;
        private string ResolvedRivalName => string.IsNullOrWhiteSpace(RivalShopName)
            ? DefaultRivalName
            : RivalShopName;

        private void HandleSceneLoaded(Scene scene, LoadSceneMode _)
        {
            if (scene.name == ShopLaunchContext.CompleteFlowScene ||
                scene.name == ShopLaunchContext.MainStreetSliceScene)
                ApplyNamesToSigns();
        }

        private bool ApplySignText(Transform sign, string value)
        {
            TextMesh text = sign.GetComponent<TextMesh>();
            if (text == null)
            {
                Debug.LogError("[StoreNaming] TextMesh가 없는 간판입니다: " + BuildPath(sign), sign);
                return false;
            }

            text.text = value;
            text.characterSize = config != null ? config.SignBaseCharacterSize : 0.12f;
            MeshRenderer renderer = text.GetComponent<MeshRenderer>();
            if (renderer == null) return true;

            float maximumWidth = config != null ? config.SignMaximumWorldWidth : 4.8f;
            float currentWidth = renderer.bounds.size.x;
            if (currentWidth > maximumWidth && currentWidth > 0.001f)
            {
                float minimum = config != null ? config.SignMinimumCharacterSize : 0.075f;
                text.characterSize = Mathf.Max(minimum,
                    text.characterSize * maximumWidth / currentWidth);
            }
            return true;
        }

        private static Transform FindScenePath(Scene scene, string path)
        {
            if (!scene.IsValid() || string.IsNullOrWhiteSpace(path)) return null;
            string[] segments = path.Split('/');
            GameObject[] roots = scene.GetRootGameObjects();
            Transform current = null;
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name != segments[0]) continue;
                current = roots[i].transform;
                break;
            }
            for (int i = 1; current != null && i < segments.Length; i++)
                current = current.Find(segments[i]);
            return current;
        }

        private static string BuildPath(Transform target)
        {
            if (target == null) return "<null>";
            string path = target.name;
            while (target.parent != null)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }
            return path;
        }

        private string DefaultPlayerName => config != null
            ? config.DefaultPlayerShopName
            : "픽앤플레이스";
        private string DefaultRivalName => config != null
            ? config.DefaultRivalShopName
            : "고양이 조달상점";

        private string Limit(string value)
        {
            value ??= string.Empty;
            return value.Length <= MaximumNameLength
                ? value
                : value.Substring(0, MaximumNameLength);
        }

        private static string Normalize(string value) => value?.Trim() ?? string.Empty;
        private bool IsValid(string value) => value.Length >= 1 && value.Length <= MaximumNameLength;
        private string ResolveRestoredName(string value, bool player)
        {
            string fallback = player ? DefaultPlayerName : DefaultRivalName;
            string normalized = string.IsNullOrWhiteSpace(value) ? fallback : Normalize(value);
            return Limit(normalized);
        }

        private void BuildNamingUi()
        {
            namingCanvas = new GameObject("StoreNamingCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(namingCanvas);
            Canvas canvas = namingCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 950;
            CanvasScaler scaler = namingCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject veil = CreateImage("Veil", namingCanvas.transform,
                new Color(0.05f, 0.025f, 0.015f, 0.82f));
            Stretch(veil.GetComponent<RectTransform>());

            GameObject panel = CreateImage("NamingPanel", veil.transform, ShopUiSkin.CreamCard);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(800f, 620f);
            ShopUiSkin.Round(panel.GetComponent<Image>(), 28);

            CreateText("Title", panel.transform, "우리 가게 이름을 정해요", 42,
                new Vector2(0f, 238f), new Vector2(700f, 64f), ShopUiSkin.BrownDeep,
                ShopUiFontWeight.Bold);
            CreateText("Description", panel.transform,
                "내 가게와 라이벌 가게의 간판에 표시될 이름입니다.", 22,
                new Vector2(0f, 188f), new Vector2(700f, 40f), ShopUiSkin.TextMuted,
                ShopUiFontWeight.Regular);

            CreateText("PlayerLabel", panel.transform, "내 가게", 24,
                new Vector2(-270f, 113f), new Vector2(180f, 38f), ShopUiSkin.TextBody,
                ShopUiFontWeight.Bold);
            playerNameInput = CreateInputField(panel.transform, "PlayerShopNameInput",
                "가게 이름을 입력하세요", new Vector2(0f, 54f));
            playerCounter = CreateText("PlayerCounter", panel.transform, string.Empty, 18,
                new Vector2(280f, 113f), new Vector2(130f, 34f), ShopUiSkin.TextMuted,
                ShopUiFontWeight.Regular);

            CreateText("RivalLabel", panel.transform, "라이벌 가게", 24,
                new Vector2(-250f, -23f), new Vector2(220f, 38f), ShopUiSkin.TextBody,
                ShopUiFontWeight.Bold);
            rivalNameInput = CreateInputField(panel.transform, "RivalShopNameInput",
                "라이벌 가게의 이름을 입력하세요", new Vector2(0f, -82f));
            rivalCounter = CreateText("RivalCounter", panel.transform, string.Empty, 18,
                new Vector2(280f, -23f), new Vector2(130f, 34f), ShopUiSkin.TextMuted,
                ShopUiFontWeight.Regular);

            confirmButton = CreateButton(panel.transform, "ConfirmStoreNamesButton", "이 이름으로 시작",
                new Vector2(0f, -210f));
            confirmButton.onClick.AddListener(() => TryConfirmNaming());
            playerNameInput.onValueChanged.AddListener(_ => RefreshValidation());
            rivalNameInput.onValueChanged.AddListener(_ => RefreshValidation());
            RefreshValidation();
        }

        private InputField CreateInputField(Transform parent, string name, string placeholder,
            Vector2 position)
        {
            GameObject fieldObject = CreateImage(name, parent, new Color(0.97f, 0.93f, 0.86f, 1f));
            RectTransform rect = fieldObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(650f, 76f);
            rect.anchoredPosition = position;
            ShopUiSkin.Round(fieldObject.GetComponent<Image>(), 20);

            Text inputText = CreateText("Text", fieldObject.transform, string.Empty, 28,
                Vector2.zero, new Vector2(590f, 62f), ShopUiSkin.TextBody,
                ShopUiFontWeight.Medium);
            inputText.alignment = TextAnchor.MiddleLeft;
            Text placeholderText = CreateText("Placeholder", fieldObject.transform, placeholder, 25,
                Vector2.zero, new Vector2(590f, 62f), new Color(0.55f, 0.47f, 0.4f, 0.72f),
                ShopUiFontWeight.Regular);
            placeholderText.alignment = TextAnchor.MiddleLeft;
            placeholderText.fontStyle = FontStyle.Italic;

            InputField input = fieldObject.AddComponent<InputField>();
            input.textComponent = inputText;
            input.placeholder = placeholderText;
            input.targetGraphic = fieldObject.GetComponent<Image>();
            input.characterLimit = MaximumNameLength;
            input.contentType = InputField.ContentType.Standard;
            input.lineType = InputField.LineType.SingleLine;
            input.shouldHideMobileInput = false;
            return input;
        }

        private void RefreshValidation()
        {
            string player = playerNameInput != null ? Normalize(playerNameInput.text) : string.Empty;
            string rival = rivalNameInput != null ? Normalize(rivalNameInput.text) : string.Empty;
            if (playerCounter != null) playerCounter.text = $"{player.Length} / {MaximumNameLength}";
            if (rivalCounter != null) rivalCounter.text = $"{rival.Length} / {MaximumNameLength}";
            if (confirmButton != null) confirmButton.interactable = IsValid(player) && IsValid(rival);
        }

        private static GameObject CreateImage(string name, Transform parent, Color color)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go;
        }

        private static Text CreateText(string name, Transform parent, string value, int fontSize,
            Vector2 position, Vector2 size, Color color, ShopUiFontWeight weight)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text text = go.GetComponent<Text>();
            ShopUiFonts.Apply(text, weight);
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 position)
        {
            GameObject go = CreateImage(name, parent, ShopUiSkin.Teal);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(430f, 78f);
            rect.anchoredPosition = position;
            ShopUiSkin.Pill(go.GetComponent<Image>());
            Button button = go.AddComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.95f, 0.82f, 1f);
            colors.pressedColor = new Color(0.75f, 0.9f, 0.84f, 1f);
            colors.disabledColor = new Color(0.58f, 0.58f, 0.58f, 0.6f);
            button.colors = colors;
            CreateText("Label", go.transform, label, 27, Vector2.zero,
                new Vector2(390f, 60f), Color.white, ShopUiFontWeight.Bold);
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
