using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    public sealed class ShopMainMenuController : MonoBehaviour
    {
        [SerializeField] private string gameTitle = ShopGameIdentity.KoreanFormalName;

        private readonly string[] panelNames =
        {
            "MainPanel", "StartPanel", "HelpPanel", "SettingsPanel",
            "CreditsPanel", "QuitPanel", "ErrorPanel", "LoadingPanel",
            "ContinueChoicePanel", "NewGameConfirmPanel"
        };

        private readonly string[] helpTitles =
        {
            "1 / 5  게임의 목표", "2 / 5  기본 조작", "3 / 5  인형뽑기", "4 / 5  소품샵 운영", "5 / 5  혼자 플레이"
        };

        private readonly string[] helpBodies =
        {
            "고양이 붐이 도시를 휩쓸었습니다. 우리는 고양이 굿즈 전문 소품샵을 열기로 했습니다.\n\n낮 상품 확보  →  상품 진열  →  밤 영업  →  하루 정산  →  다음 날\n\n낮에는 냥이 뽑기와 캡슐 기계에서 판매할 상품을 확보하고, 밤에는 상품을 진열하고 손님을 계산합니다.",
            "WASD          이동\n마우스         시점 이동\nE              상호작용\nEsc            일시정지\n\n게임패드를 사용하면 방향키와 확인/취소 버튼으로 메뉴를 조작할 수 있습니다.",
            "기계 앞에서 E를 눌러 조작을 시작합니다.\n\nWASD          팬 이동\nSpace         팬 내리기\nEsc            하강 전 취소\n\n팬을 상품 더미 앞에 맞춘 뒤 내려 퍼올리세요. 담긴 상품은 물리적으로 흔들리고 미끄러질 수 있습니다.",
            "진열대       가게 창고의 상품을 진열합니다.\n계산대       줄을 선 손님을 계산합니다.\n마감 종       영업을 시작하거나 종료합니다.\n\n손님의 예산과 취향에 맞는 상품이 없으면 구매를 포기할 수 있습니다.",
            "모든 자금과 상품은 내 가게에 저장됩니다.\n\n낮에는 상품을 충분히 확보하고, 밤에는 계산과 진열, 재고 관리를 순서대로 진행하세요."
        };

        private readonly List<Resolution> resolutions = new();
        private ShopTitlePresentationConfig presentation;
        private RectTransform logoRect;
        private CanvasGroup logoGroup;
        private Vector2 logoRestPosition;
        private GameObject currentPanel;
        private bool loading;
        private int helpPage;

        private void Awake()
        {
            presentation = ShopTitlePresentationConfig.Load();
            NormalizeTitlePresentation();
            BuildTitleArtwork();
            ShopInputModeManager.Push(this, ShopInputMode.Menu);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Text title = Find<Text>("TitleText");
            Text subtitle = Find<Text>("TitleSubtitle");
            Text version = Find<Text>("VersionText");
            if (title != null && (presentation == null || presentation.Logo == null))
            {
                string formalTitle = string.IsNullOrWhiteSpace(gameTitle)
                    ? ShopGameIdentity.KoreanFormalName
                    : gameTitle;
                title.text = formalTitle.Replace(" : ", " : \n");
                title.resizeTextForBestFit = true;
                title.resizeTextMinSize = 28;
                title.resizeTextMaxSize = 44;
                title.lineSpacing = 0.9f;
            }
            if (subtitle != null) subtitle.text = ShopGameIdentity.Subtitle;
            if (version != null) version.text = ShopGameIdentity.VersionLabel;
            CreateSaveChoicePanels();
            RegisterButtons();
            RegisterSaveChoiceButtons();
            PopulateResolutionDropdown();
            Show("MainPanel", "BtnMainStart");
        }

        private void NormalizeTitlePresentation()
        {
            Canvas canvas = GetComponentInChildren<Canvas>(true);
            if (canvas != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 100;
                canvas.targetDisplay = 0;
                canvas.transform.localScale = Vector3.one;

                GameObject veil = FindObject("MenuVeil");
                if (veil != null && veil.transform.parent != canvas.transform)
                {
                    veil.transform.SetParent(canvas.transform, false);
                    veil.transform.SetAsFirstSibling();
                }
                if (veil != null && veil.transform is RectTransform veilRect)
                {
                    veilRect.anchorMin = Vector2.zero;
                    veilRect.anchorMax = Vector2.one;
                    veilRect.offsetMin = Vector2.zero;
                    veilRect.offsetMax = Vector2.zero;
                }
            }

            Camera menuCamera = GetComponentInChildren<Camera>(true);
            if (menuCamera != null)
            {
                menuCamera.clearFlags = CameraClearFlags.SolidColor;
                menuCamera.backgroundColor = new Color(0.035f, 0.055f, 0.08f, 1f);
            }
        }

        private void OnDestroy()
        {
            ShopInputModeManager.Pop(this);
        }

        private void Start()
        {
            ShopUserSettings.Apply(ShopUserSettings.Current, false);
            if (logoRect != null) StartCoroutine(AnimateLogo());
            string error = ShopLaunchContext.ConsumeError();
            if (!string.IsNullOrEmpty(error)) ShowError(error);
            else if (ShopLaunchContext.TryCreateQaRequest(out ShopLaunchRequest request)) StartCoroutine(LoadGame(request));
        }

        private void BuildTitleArtwork()
        {
            if (presentation == null) return;
            Canvas canvas = GetComponentInChildren<Canvas>(true);
            if (canvas == null) return;

            if (presentation.Background != null)
            {
                GameObject backgroundObject = new("TitleBackground", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image), typeof(AspectRatioFitter));
                backgroundObject.transform.SetParent(canvas.transform, false);
                backgroundObject.transform.SetAsFirstSibling();
                Image background = backgroundObject.GetComponent<Image>();
                background.sprite = presentation.Background;
                background.color = Color.white;
                background.raycastTarget = false;
                RectTransform rect = background.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(presentation.Background.rect.width,
                    presentation.Background.rect.height);
                AspectRatioFitter fitter = backgroundObject.GetComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = presentation.Background.rect.width /
                                     Mathf.Max(1f, presentation.Background.rect.height);
            }

            GameObject veilObject = FindObject("MenuVeil");
            Image veil = veilObject != null ? veilObject.GetComponent<Image>() : null;
            if (veil != null)
            {
                Color color = veil.color;
                color.a = presentation.BackgroundVeilAlpha;
                veil.color = color;
            }

            Text title = Find<Text>("TitleText");
            if (presentation.Logo == null || title == null) return;
            GameObject logoObject = new("TitleLogo", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(CanvasGroup));
            logoObject.transform.SetParent(title.transform.parent, false);
            logoObject.transform.SetSiblingIndex(title.transform.GetSiblingIndex());
            logoRect = logoObject.GetComponent<RectTransform>();
            RectTransform titleRect = title.rectTransform;
            logoRect.anchorMin = titleRect.anchorMin;
            logoRect.anchorMax = titleRect.anchorMax;
            logoRect.pivot = titleRect.pivot;
            logoRect.anchoredPosition = titleRect.anchoredPosition + presentation.LogoOffset;
            Vector2 configuredSize = presentation.LogoSize;
            logoRect.sizeDelta = configuredSize.x > 0f && configuredSize.y > 0f
                ? configuredSize
                : titleRect.sizeDelta;
            logoRestPosition = logoRect.anchoredPosition;
            Image logo = logoObject.GetComponent<Image>();
            logo.sprite = presentation.Logo;
            logo.preserveAspect = true;
            logo.raycastTarget = false;
            logoGroup = logoObject.GetComponent<CanvasGroup>();
            title.gameObject.SetActive(false);
        }

        private IEnumerator AnimateLogo()
        {
            float duration = presentation != null ? presentation.EntranceSeconds : 0.65f;
            float startScale = presentation != null ? presentation.EntranceStartScale : 0.85f;
            logoGroup.alpha = 0f;
            logoRect.localScale = Vector3.one * startScale;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                logoGroup.alpha = eased;
                logoRect.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, eased);
                yield return null;
            }
            logoGroup.alpha = 1f;
            logoRect.localScale = Vector3.one;

            float amplitude = presentation != null ? presentation.IdleAmplitudePixels : 4f;
            float period = presentation != null ? presentation.IdlePeriodSeconds : 2.6f;
            float phase = 0f;
            while (logoRect != null)
            {
                phase += Time.unscaledDeltaTime * Mathf.PI * 2f / Mathf.Max(0.1f, period);
                logoRect.anchoredPosition = logoRestPosition + Vector2.up * (Mathf.Sin(phase) * amplitude);
                yield return null;
            }
        }

        private void Update()
        {
            if (loading) return;
            bool cancel = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            cancel |= Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
            if (cancel) NavigateBack();
        }

        private void RegisterButtons()
        {
            OnClick("BtnMainStart", () => Show("StartPanel", "BtnSolo"));
            OnClick("BtnMainHelp", () => OpenHelp(0));
            OnClick("BtnMainSettings", OpenSettings);
            OnClick("BtnMainCredits", () => Show("CreditsPanel", "BtnCreditsBack"));
            OnClick("BtnMainQuit", () => Show("QuitPanel", "BtnQuitCancel"));

            OnClick("BtnSolo", StartSolo);
            OnClick("BtnStartBack", () => Show("MainPanel", "BtnMainStart"));

            OnClick("BtnHelpPrevious", () => SetHelpPage(helpPage - 1));
            OnClick("BtnHelpNext", () => SetHelpPage(helpPage + 1));
            OnClick("BtnHelpBack", () => Show("MainPanel", "BtnMainHelp"));
            OnClick("BtnSettingsApply", SaveSettings);
            OnClick("BtnSettingsDefaults", RestoreDefaults);
            OnClick("BtnSettingsBack", () => Show("MainPanel", "BtnMainSettings"));
            OnClick("BtnCreditsBack", () => Show("MainPanel", "BtnMainCredits"));
            OnClick("BtnQuitConfirm", QuitApplication);
            OnClick("BtnQuitCancel", () => Show("MainPanel", "BtnMainQuit"));
            OnClick("BtnErrorClose", () => Show("MainPanel", "BtnMainStart"));
        }

        private void StartSolo()
        {
            if (ShopProgressionSaveStore.HasUsableSave)
            {
                Show("ContinueChoicePanel", "BtnContinueGame");
                return;
            }
            LaunchSolo(true);
        }

        private void LaunchSolo(bool resetCampaign)
        {
            StartCoroutine(LoadGame(new ShopLaunchRequest
            {
                Mode = ShopLaunchMode.Solo,
                MaximumPlayers = 1,
                Address = "127.0.0.1",
                Port = ShopFlowRules.DefaultPort,
                PlayerName = "점장",
                ResetCampaign = resetCampaign
            }));
        }

        private void RegisterSaveChoiceButtons()
        {
            OnClick("BtnContinueGame", () => LaunchSolo(false));
            OnClick("BtnChooseNewGame", () => Show("NewGameConfirmPanel", "BtnCancelNewGame"));
            OnClick("BtnSaveChoiceBack", () => Show("StartPanel", "BtnSolo"));
            OnClick("BtnConfirmNewGame", () => LaunchSolo(true));
            OnClick("BtnCancelNewGame", () => Show("ContinueChoicePanel", "BtnContinueGame"));
        }

        private void CreateSaveChoicePanels()
        {
            Canvas canvas = GetComponentInChildren<Canvas>(true);
            Button template = Find<Button>("BtnSolo");
            if (canvas == null || template == null || FindObject("ContinueChoicePanel") != null) return;
            CreateChoicePanel(canvas.transform, template, "ContinueChoicePanel", "저장된 게임이 있습니다",
                "이어서 하시겠습니까?", new[]
                {
                    ("BtnContinueGame", "이어하기"),
                    ("BtnChooseNewGame", "새로 시작"),
                    ("BtnSaveChoiceBack", "뒤로")
                });
            CreateChoicePanel(canvas.transform, template, "NewGameConfirmPanel", "새 게임 확인",
                "새로 시작하면 기존 저장을 덮어씁니다.\n정말 새 게임을 시작하시겠습니까?", new[]
                {
                    ("BtnConfirmNewGame", "덮어쓰고 시작"),
                    ("BtnCancelNewGame", "취소")
                });
        }

        private static void CreateChoicePanel(Transform parent, Button template, string panelName,
            string title, string message, (string name, string label)[] buttons)
        {
            GameObject panel = new(panelName, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.015f, 0.035f, 0.055f, 0.96f);

            Text sourceText = template.GetComponentInChildren<Text>(true);
            Text titleText = CreatePromptText(panel.transform, "PromptTitle", sourceText, 30,
                new Vector2(0f, 112f), new Vector2(700f, 52f));
            titleText.text = title;
            titleText.color = new Color(1f, 0.82f, 0.28f);
            Text bodyText = CreatePromptText(panel.transform, "PromptMessage", sourceText, 22,
                new Vector2(0f, 40f), new Vector2(780f, 90f));
            bodyText.text = message;
            bodyText.color = Color.white;

            float totalWidth = buttons.Length * 210f + (buttons.Length - 1) * 18f;
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = Instantiate(template, panel.transform);
                button.name = buttons[i].name;
                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(210f, 62f);
                rect.anchoredPosition = new Vector2(-totalWidth * 0.5f + 105f + i * 228f, -80f);
                Text label = button.GetComponentInChildren<Text>(true);
                if (label != null) label.text = buttons[i].label;
            }
            panel.SetActive(false);
        }

        private static Text CreatePromptText(Transform parent, string name, Text template, int size,
            Vector2 position, Vector2 dimensions)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = dimensions;
            rect.anchoredPosition = position;
            Text text = go.GetComponent<Text>();
            ShopUiFonts.Apply(text, ShopUiFontWeight.Bold);
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 16;
            text.resizeTextMaxSize = size;
            return text;
        }

        private IEnumerator LoadGame(ShopLaunchRequest request)
        {
            if (loading) yield break;
            loading = true;
            ShopLaunchContext.SetRequest(request);
            Show("LoadingPanel", null);
            Text message = Find<Text>("LoadingMessageText");
            Text progressText = Find<Text>("LoadingProgressText");
            Slider progressBar = Find<Slider>("LoadingProgressBar");
            string[] messages = { "가게 문을 여는 중...", "상품을 정리하는 중...", "오늘 영업을 준비하는 중..." };
            float nextMessage = Time.unscaledTime;
            int messageIndex = 0;
            AsyncOperation operation = SceneManager.LoadSceneAsync(ShopLaunchContext.ResolveGameplayScene());
            if (operation == null)
            {
                loading = false;
                ShowError("게임 씬을 불러오지 못했습니다.");
                yield break;
            }
            while (!operation.isDone)
            {
                float normalized = Mathf.Clamp01(operation.progress / 0.9f);
                if (progressBar != null) progressBar.value = normalized;
                if (progressText != null) progressText.text = Mathf.RoundToInt(normalized * 100f) + "%";
                if (message != null && Time.unscaledTime >= nextMessage)
                {
                    message.text = messages[messageIndex++ % messages.Length];
                    nextMessage = Time.unscaledTime + 1.35f;
                }
                yield return null;
            }
        }

        private void OpenHelp(int page)
        {
            Show("HelpPanel", "BtnHelpNext");
            SetHelpPage(page);
        }

        private void SetHelpPage(int page)
        {
            helpPage = Mathf.Clamp(page, 0, helpBodies.Length - 1);
            Text title = Find<Text>("HelpTitleText");
            Text body = Find<Text>("HelpBodyText");
            Text dots = Find<Text>("HelpDotsText");
            if (title != null) title.text = helpTitles[helpPage];
            if (body != null) body.text = helpBodies[helpPage];
            if (dots != null) dots.text = string.Join("  ", Enumerable.Range(0, helpBodies.Length).Select(i => i == helpPage ? "●" : "○"));
            Button previous = Find<Button>("BtnHelpPrevious");
            Button next = Find<Button>("BtnHelpNext");
            if (previous != null) previous.interactable = helpPage > 0;
            if (next != null) next.interactable = helpPage < helpBodies.Length - 1;
        }

        private void OpenSettings()
        {
            LoadSettingsControls(ShopUserSettings.Current);
            Show("SettingsPanel", "SettingsMasterSlider");
        }

        private void SaveSettings()
        {
            ShopUserSettingsData data = ReadSettingsControls();
            ShopUserSettings.Save(data);
            Show("MainPanel", "BtnMainSettings");
        }

        private void RestoreDefaults()
        {
            ShopUserSettingsData data = ShopUserSettings.Defaults();
            LoadSettingsControls(data);
            ShopUserSettings.Save(data);
        }

        private void LoadSettingsControls(ShopUserSettingsData data)
        {
            SetSlider("SettingsMasterSlider", data.MasterVolume);
            SetSlider("SettingsMusicSlider", data.MusicVolume);
            SetSlider("SettingsEffectsSlider", data.EffectsVolume);
            SetSlider("SettingsSensitivitySlider", data.MouseSensitivity);
            SetSlider("SettingsUiScaleSlider", data.UiScale);
            SetToggle("SettingsInvertYToggle", data.InvertY);
            SetToggle("SettingsCameraShakeToggle", data.CameraShake);
            SetToggle("SettingsVibrationToggle", data.GamepadVibration);
            SetToggle("SettingsFullscreenToggle", data.Fullscreen);
            SetToggle("SettingsVSyncToggle", data.VSync);
            Dropdown resolution = Find<Dropdown>("SettingsResolutionDropdown");
            if (resolution != null)
            {
                int match = resolutions.FindIndex(x => x.width == data.Width && x.height == data.Height);
                resolution.value = Mathf.Max(0, match);
            }
        }

        private ShopUserSettingsData ReadSettingsControls()
        {
            ShopUserSettingsData data = new ShopUserSettingsData
            {
                MasterVolume = SliderValue("SettingsMasterSlider", 0.85f),
                MusicVolume = SliderValue("SettingsMusicSlider", 0.7f),
                EffectsVolume = SliderValue("SettingsEffectsSlider", 0.85f),
                MouseSensitivity = SliderValue("SettingsSensitivitySlider", 1f),
                UiScale = SliderValue("SettingsUiScaleSlider", 1f),
                InvertY = ToggleValue("SettingsInvertYToggle"),
                CameraShake = ToggleValue("SettingsCameraShakeToggle"),
                GamepadVibration = ToggleValue("SettingsVibrationToggle"),
                Fullscreen = ToggleValue("SettingsFullscreenToggle"),
                VSync = ToggleValue("SettingsVSyncToggle")
            };
            Dropdown resolution = Find<Dropdown>("SettingsResolutionDropdown");
            int index = resolution != null ? Mathf.Clamp(resolution.value, 0, resolutions.Count - 1) : 0;
            if (resolutions.Count > 0)
            {
                data.Width = resolutions[index].width;
                data.Height = resolutions[index].height;
            }
            return data;
        }

        private void PopulateResolutionDropdown()
        {
            Dropdown dropdown = Find<Dropdown>("SettingsResolutionDropdown");
            if (dropdown == null) return;
            resolutions.Clear();
            foreach (Resolution resolution in Screen.resolutions)
            {
                if (resolutions.Any(x => x.width == resolution.width && x.height == resolution.height)) continue;
                resolutions.Add(resolution);
            }
            if (resolutions.Count == 0) resolutions.Add(Screen.currentResolution);
            dropdown.ClearOptions();
            dropdown.AddOptions(resolutions.Select(x => x.width + " × " + x.height).ToList());
        }

        private void NavigateBack()
        {
            if (currentPanel == null || currentPanel.name == "MainPanel") return;
            Show("MainPanel", "BtnMainStart");
        }

        private void ShowError(string message)
        {
            Text errorText = Find<Text>("ErrorMessageText");
            if (errorText != null) errorText.text = message;
            Show("ErrorPanel", "BtnErrorClose");
        }

        private void Show(string panelName, string selectionName)
        {
            foreach (string name in panelNames)
            {
                GameObject panel = FindObject(name);
                if (panel != null) panel.SetActive(name == panelName);
            }
            currentPanel = FindObject(panelName);
            if (!string.IsNullOrEmpty(selectionName))
            {
                Selectable selectable = Find<Selectable>(selectionName);
                if (selectable != null) StartCoroutine(SelectNextFrame(selectable.gameObject));
            }
        }

        private static IEnumerator SelectNextFrame(GameObject target)
        {
            yield return null;
            if (EventSystem.current != null && target != null) EventSystem.current.SetSelectedGameObject(target);
        }

        private void QuitApplication()
        {
            Debug.Log("[ShopFlow] QUIT_REQUESTED");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnClick(string name, UnityEngine.Events.UnityAction action)
        {
            Button button = Find<Button>(name);
            if (button != null) button.onClick.AddListener(action);
        }

        private void SetSlider(string name, float value) { Slider slider = Find<Slider>(name); if (slider != null) slider.value = value; }
        private void SetToggle(string name, bool value) { Toggle toggle = Find<Toggle>(name); if (toggle != null) toggle.isOn = value; }
        private float SliderValue(string name, float fallback) { Slider slider = Find<Slider>(name); return slider != null ? slider.value : fallback; }
        private bool ToggleValue(string name) { Toggle toggle = Find<Toggle>(name); return toggle != null && toggle.isOn; }

        private GameObject FindObject(string name)
        {
            Transform target = GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            return target != null ? target.gameObject : null;
        }

        private T Find<T>(string name) where T : Component
        {
            return GetComponentsInChildren<T>(true).FirstOrDefault(x => x.gameObject.name == name);
        }
    }
}
