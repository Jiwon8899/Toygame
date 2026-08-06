using System.Collections.Generic;
using System.Text;
using Blocks.Gameplay.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(400)]
    public sealed class ShopProgressionHUD : MonoBehaviour
    {
        private const string OnboardingSeenKey = "PickAndPlaceShop.HudOnboardingSeen";
        private static readonly Color PanelColor = new(0.025f, 0.045f, 0.075f, 0.94f);
        private static readonly Color Mint = new(0.25f, 1f, 0.72f, 1f);
        private static readonly Color Gold = new(1f, 0.72f, 0.22f, 1f);
        private static readonly Color Muted = new(0.66f, 0.75f, 0.84f, 1f);
        private static ShopProgressionHUD instance;

        private readonly Queue<string> notificationQueue = new();
        private readonly Button[] tabButtons = new Button[4];
        private ShopProgressionManager manager;
        private GameObject objectivePanel;
        private CanvasGroup objectiveGroup;
        private Text objectiveText;
        private Text objectiveStepText;
        private Image objectiveFill;
        private GameObject overlay;
        private Text contentText;
        private ScrollRect contentScroll;
        private Button expansionButton;
        private Button tutorialSkipButton;
        private GameObject tutorialSkipConfirmation;
        private Text tabHint;
        private GameObject notificationPanel;
        private Text notificationText;
        private InputAction toggleStatusAction;
        private GameObject canvasRoot;
        private string visibleSceneName = string.Empty;
        private string objectiveKey = string.Empty;
        private float nextRefresh;
        private float hideNotificationAt;
        private int activeTab;
        private bool open;
        private bool resetScroll;

        public static bool IsOpen => instance != null && instance.open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[Progression] HUD");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopProgressionHUD>();
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
            BuildUi();
            SetOpen(false);
            RefreshSceneVisibility();
            toggleStatusAction = new InputAction(
                "가게 현황", InputActionType.PassThrough, "<Keyboard>/tab");
            toggleStatusAction.performed += OnStatusTogglePerformed;
            toggleStatusAction.Enable();
        }

        private void OnDestroy()
        {
            CloseTutorialSkipConfirmation();
            ShopHudStack.Instance.RemoveItem(this);
            Detach();
            if (toggleStatusAction != null)
            {
                toggleStatusAction.performed -= OnStatusTogglePerformed;
                toggleStatusAction.Disable();
                toggleStatusAction.Dispose();
            }
            ShopInputModeManager.Pop(this);
            if (instance == this) instance = null;
        }

        private void Update()
        {
            RefreshSceneVisibility();
            if (canvasRoot != null && !canvasRoot.activeSelf) return;
            if (manager == null && ShopProgressionManager.Instance != null)
                Attach(ShopProgressionManager.Instance);

            HandleInput();
            UpdateNotification();
            if (objectiveGroup != null && objectivePanel.activeSelf)
                objectiveGroup.alpha = Mathf.MoveTowards(
                    objectiveGroup.alpha, 1f, Time.unscaledDeltaTime * 4.5f);

            if (manager == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        private void HandleInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (tutorialSkipConfirmation != null && tutorialSkipConfirmation.activeSelf)
            {
                if (keyboard.escapeKey.wasPressedThisFrame || keyboard.nKey.wasPressedThisFrame)
                    CloseTutorialSkipConfirmation();
                else if (keyboard.enterKey.wasPressedThisFrame || keyboard.yKey.wasPressedThisFrame)
                    ConfirmTutorialSkip();
                return;
            }

            if (keyboard.f1Key.wasPressedThisFrame)
                EnqueueNotification("WASD 이동 · Shift 달리기 · 마우스 시점 · E 상호작용");

            if (manager != null && keyboard.f6Key.wasPressedThisFrame)
                manager.SaveNowWithFeedback();

            if (!open) return;
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                SetOpen(false);
                return;
            }
            if (keyboard.digit1Key.wasPressedThisFrame) SelectTab(0);
            if (keyboard.digit2Key.wasPressedThisFrame) SelectTab(1);
            if (keyboard.digit3Key.wasPressedThisFrame) SelectTab(2);
            if (keyboard.digit4Key.wasPressedThisFrame) SelectTab(3);
            if (activeTab == 3 && keyboard.f4Key.wasPressedThisFrame) TryExpand();
        }

        private void OnStatusTogglePerformed(InputAction.CallbackContext context)
        {
            if (!context.ReadValueAsButton()) return;
            if (!ShopUpgradeUI.IsOpen) SetOpen(!open);
        }

        private void Attach(ShopProgressionManager target)
        {
            Detach();
            manager = target;
            manager.StateChanged += Refresh;
            manager.NotificationRaised += EnqueueNotification;
            if (PlayerPrefs.GetInt(OnboardingSeenKey, 0) == 0)
            {
                PlayerPrefs.SetInt(OnboardingSeenKey, 1);
                PlayerPrefs.Save();
                EnqueueNotification("WASD 이동 · Shift 달리기 · 마우스 시점 · E 상호작용 · F1 다시 보기");
            }
            Refresh();
        }

        private void Detach()
        {
            if (manager == null) return;
            manager.StateChanged -= Refresh;
            manager.NotificationRaised -= EnqueueNotification;
            manager = null;
        }

        private void SetOpen(bool value)
        {
            open = value;
            if (overlay != null) overlay.SetActive(value);
            if (objectivePanel != null) objectivePanel.SetActive(!value);
            if (tabHint != null) tabHint.gameObject.SetActive(false);
            if (value) ShopInputModeManager.Push(this, ShopInputMode.UI);
            else ShopInputModeManager.Pop(this);
            if (value)
            {
                resetScroll = true;
                Refresh();
            }
        }

        private void BuildUi()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasRoot = canvasObject;
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15000;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject safe = new("SafeArea", typeof(RectTransform), typeof(ShopSafeArea));
            safe.transform.SetParent(canvasObject.transform, false);
            BuildObjective(safe.transform);
            BuildStatusScreen(safe.transform);
            BuildNotification(safe.transform);
            BuildTutorialSkipConfirmation(safe.transform);

            tabHint = CreateText("StatusHint", safe.transform, "Tab · 가게 현황   |   I · 상품 보관함",
                18, FontStyle.Normal, TextAnchor.MiddleCenter, ShopUiSkin.TextMuted);
            SetRect(tabHint.rectTransform, new Vector2(0f, 12f), new Vector2(680f, 34f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            GlobalGameFontApplier.ApplyTo(gameObject);
        }

        private void RefreshSceneVisibility()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName == visibleSceneName) return;
            visibleSceneName = sceneName;
            bool visible = !string.Equals(sceneName, ShopLaunchContext.MainMenuScene,
                System.StringComparison.Ordinal);
            if (canvasRoot != null) canvasRoot.SetActive(visible);
            if (!visible && open) SetOpen(false);
        }

        private void BuildObjective(Transform parent)
        {
            objectivePanel = ShopHudStack.Instance.CreateItem(this, ShopHudStackSlot.Objective,
                "CurrentObjective", 124f);
            objectiveGroup = objectivePanel.AddComponent<CanvasGroup>();

            GameObject stepBadge = CreatePanel("StepBadge", objectivePanel.transform,
                new Vector2(60f, 60f), ShopUiSkin.Pink);
            SetRect(stepBadge.GetComponent<RectTransform>(), new Vector2(16f, -16f),
                new Vector2(60f, 60f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            ShopUiSkin.Round(stepBadge.GetComponent<Image>(), 20);
            objectiveStepText = CreateText("Step", stepBadge.transform, "1/7", 17,
                FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            objectiveStepText.rectTransform.anchorMin = Vector2.zero;
            objectiveStepText.rectTransform.anchorMax = Vector2.one;
            objectiveStepText.rectTransform.offsetMin = objectiveStepText.rectTransform.offsetMax = Vector2.zero;

            objectiveText = CreateText("ObjectiveText", objectivePanel.transform, "목표를 불러오는 중",
                19, FontStyle.Bold, TextAnchor.UpperLeft, ShopUiSkin.TextBody);
            SetRect(objectiveText.rectTransform, new Vector2(88f, -16f), new Vector2(248f, 70f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));

            tutorialSkipButton = CreateButton("TutorialSkip", objectivePanel.transform, "건너뛰기",
                new Vector2(96f, 36f), OpenTutorialSkipConfirmation);
            SetRect(tutorialSkipButton.GetComponent<RectTransform>(), new Vector2(-16f, -16f),
                new Vector2(96f, 36f), Vector2.one, Vector2.one, Vector2.one);
            Image skipImage = tutorialSkipButton.GetComponent<Image>();
            skipImage.color = ShopUiSkin.CreamBackground;
            ShopUiSkin.Pill(skipImage);
            Text skipLabel = tutorialSkipButton.GetComponentInChildren<Text>();
            skipLabel.color = ShopUiSkin.BrownMid;
            skipLabel.fontSize = 15;
            tutorialSkipButton.gameObject.SetActive(false);

            GameObject track = CreatePanel("ProgressTrack", objectivePanel.transform,
                new Vector2(420f, 10f), ShopUiSkin.Divider);
            SetRect(track.GetComponent<RectTransform>(), new Vector2(16f, 14f), new Vector2(420f, 10f),
                Vector2.zero, Vector2.zero, Vector2.zero);
            ShopUiSkin.Pill(track.GetComponent<Image>());
            GameObject fill = CreatePanel("ProgressFill", track.transform, Vector2.zero, Mint);
            objectiveFill = fill.GetComponent<Image>();
            objectiveFill.color = ShopUiSkin.Teal;
            ShopUiSkin.Pill(objectiveFill);
            RectTransform fillRect = objectiveFill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        }

        private void BuildStatusScreen(Transform parent)
        {
            overlay = CreatePanel("ShopStatusOverlay", parent, Vector2.zero,
                new Color(0.008f, 0.015f, 0.028f, 0.9f));
            RectTransform overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;

            GameObject panel = CreatePanel("ShopStatusPanel", overlay.transform,
                new Vector2(1540f, 880f), new Color(0.025f, 0.045f, 0.075f, 0.99f));
            SetRect(panel.GetComponent<RectTransform>(), Vector2.zero, new Vector2(1540f, 880f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));

            Text title = CreateText("Title", panel.transform, "가게 현황",
                46, FontStyle.Bold, TextAnchor.MiddleLeft, Mint);
            SetRect(title.rectTransform, new Vector2(48f, -22f), new Vector2(440f, 72f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));

            string[] names = { "1  등급 진행", "2  오늘·주간 목표", "3  컬렉션", "4  가게 확장" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                tabButtons[i] = CreateButton("Tab_" + i, panel.transform, names[i],
                    new Vector2(340f, 62f), () => SelectTab(index));
                SetRect(tabButtons[i].GetComponent<RectTransform>(),
                    new Vector2(48f + i * 364f, -98f), new Vector2(340f, 62f),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            }

            GameObject viewport = CreatePanel("Viewport", panel.transform, new Vector2(1444f, 596f),
                new Color(0.04f, 0.065f, 0.1f, 0.98f));
            SetRect(viewport.GetComponent<RectTransform>(), new Vector2(48f, -178f),
                new Vector2(1444f, 596f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f));
            Mask mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            GameObject scrollObject = new("Scroll", typeof(RectTransform), typeof(ScrollRect));
            scrollObject.transform.SetParent(viewport.transform, false);
            RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.offsetMin = scrollRectTransform.offsetMax = Vector2.zero;

            contentText = CreateText("Content", scrollObject.transform, string.Empty,
                23, FontStyle.Normal, TextAnchor.UpperLeft, Color.white);
            RectTransform contentRect = contentText.rectTransform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = new Vector2(0f, -18f);
            contentRect.sizeDelta = new Vector2(-44f, 596f);
            contentText.verticalOverflow = VerticalWrapMode.Overflow;

            contentScroll = scrollObject.GetComponent<ScrollRect>();
            contentScroll.viewport = viewport.GetComponent<RectTransform>();
            contentScroll.content = contentRect;
            contentScroll.horizontal = false;
            contentScroll.vertical = true;
            contentScroll.scrollSensitivity = 34f;
            contentScroll.movementType = ScrollRect.MovementType.Clamped;

            expansionButton = CreateButton("ExpandButton", panel.transform, "확장 실행",
                new Vector2(300f, 60f), TryExpand);
            SetRect(expansionButton.GetComponent<RectTransform>(), new Vector2(-48f, 30f),
                new Vector2(300f, 60f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 0f));

            Text footer = CreateText("Footer", panel.transform,
                "Tab 또는 Esc · 닫기   |   마우스 휠 · 내용 보기   |   F1 · 조작 안내",
                19, FontStyle.Normal, TextAnchor.MiddleLeft, Muted);
            SetRect(footer.rectTransform, new Vector2(48f, 30f), new Vector2(1000f, 44f),
                Vector2.zero, Vector2.zero, Vector2.zero);
        }

        private void BuildNotification(Transform parent)
        {
            notificationPanel = CreatePanel("ProgressionNotification", parent,
                new Vector2(980f, 92f), new Color(0.02f, 0.08f, 0.12f, 0.96f));
            SetRect(notificationPanel.GetComponent<RectTransform>(), new Vector2(0f, -76f),
                new Vector2(980f, 92f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f));
            notificationText = CreateText("Content", notificationPanel.transform, string.Empty,
                32, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            RectTransform textRect = notificationText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20f, 8f);
            textRect.offsetMax = new Vector2(-20f, -8f);
            notificationPanel.SetActive(false);
        }

        private void BuildTutorialSkipConfirmation(Transform parent)
        {
            tutorialSkipConfirmation = CreatePanel("TutorialSkipConfirmation", parent, Vector2.zero,
                new Color(0.26f, 0.16f, 0.1f, 0.82f));
            RectTransform overlayRect = tutorialSkipConfirmation.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;

            GameObject card = CreatePanel("Card", tutorialSkipConfirmation.transform,
                new Vector2(680f, 390f), ShopUiSkin.CreamCard);
            SetRect(card.GetComponent<RectTransform>(), Vector2.zero, new Vector2(680f, 390f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ShopUiSkin.Round(card.GetComponent<Image>(), 28);
            ShopUiSkin.AddIcon("Question", card.transform, ShopUiIcon.Idea, ShopUiSkin.Pink,
                new Vector2(72f, 72f), new Vector2(0f, -36f), new Vector2(0.5f, 1f));
            Text title = CreateText("Title", card.transform, "튜토리얼을 건너뛸까요?",
                30, FontStyle.Bold, TextAnchor.MiddleCenter, ShopUiSkin.BrownDeep);
            SetRect(title.rectTransform, new Vector2(0f, 60f), new Vector2(610f, 62f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            Text body = CreateText("Body", card.transform,
                "나중에 설정에서 다시 시작할 수 있어요.", 19, FontStyle.Normal,
                TextAnchor.MiddleCenter, ShopUiSkin.TextMuted);
            SetRect(body.rectTransform, new Vector2(0f, 8f), new Vector2(610f, 48f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            Button no = CreateButton("ContinueTutorial", card.transform, "계속할게요",
                new Vector2(260f, 60f), CloseTutorialSkipConfirmation);
            SetRect(no.GetComponent<RectTransform>(), new Vector2(-142f, -92f), new Vector2(260f, 60f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            Image noImage = no.GetComponent<Image>();
            noImage.color = ShopUiSkin.CreamBackground;
            ShopUiSkin.Pill(noImage);
            no.GetComponentInChildren<Text>().color = ShopUiSkin.BrownDeep;
            Button yes = CreateButton("ConfirmSkip", card.transform, "건너뛸게요",
                new Vector2(260f, 60f), ConfirmTutorialSkip);
            SetRect(yes.GetComponent<RectTransform>(), new Vector2(142f, -92f), new Vector2(260f, 60f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            Image yesImage = yes.GetComponent<Image>();
            yesImage.color = ShopUiSkin.Teal;
            ShopUiSkin.Pill(yesImage);
            tutorialSkipConfirmation.SetActive(false);
        }

        private void OpenTutorialSkipConfirmation()
        {
            if (manager == null || manager.TutorialCompleted || tutorialSkipConfirmation == null) return;
            tutorialSkipConfirmation.SetActive(true);
            ShopInputModeManager.Push(tutorialSkipConfirmation, ShopInputMode.UI);
        }

        private void ConfirmTutorialSkip()
        {
            manager?.SkipTutorial();
            CloseTutorialSkipConfirmation();
            Refresh();
        }

        private void CloseTutorialSkipConfirmation()
        {
            if (tutorialSkipConfirmation != null) tutorialSkipConfirmation.SetActive(false);
            if (tutorialSkipConfirmation != null) ShopInputModeManager.Pop(tutorialSkipConfirmation);
        }

        private void SelectTab(int index)
        {
            activeTab = Mathf.Clamp(index, 0, tabButtons.Length - 1);
            resetScroll = true;
            Refresh();
        }

        private void Refresh()
        {
            if (manager == null) return;
            RefreshObjective();
            if (!open || contentText == null) return;

            contentText.text = activeTab switch
            {
                0 => BuildGradeText(),
                1 => BuildGoalsText(),
                2 => BuildCollectionText(),
                3 => BuildExpansionText(),
                _ => string.Empty
            };
            for (int i = 0; i < tabButtons.Length; i++)
            {
                Image image = tabButtons[i].GetComponent<Image>();
                image.color = i == activeTab
                    ? new Color(0.12f, 0.36f, 0.34f, 1f)
                    : new Color(0.075f, 0.105f, 0.15f, 1f);
            }

            RectTransform contentRect = contentText.rectTransform;
            contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x,
                Mathf.Max(596f, contentText.preferredHeight + 44f));
            if (resetScroll)
            {
                Canvas.ForceUpdateCanvases();
                contentScroll.verticalNormalizedPosition = 1f;
                resetScroll = false;
            }
            RefreshExpansionButton();
        }

        private void RefreshObjective()
        {
            if (ShopTutorialRuntime.TryGetDisplay(out string tutorialLabel, out int tutorialCurrent,
                    out int tutorialTarget))
            {
                if (tutorialSkipButton != null) tutorialSkipButton.gameObject.SetActive(true);
                if (objectiveStepText != null) objectiveStepText.text = (manager.TutorialStep + 1) + "/7";
                string tutorialKey = "tutorial:" + manager.TutorialStep;
                if (tutorialKey != objectiveKey)
                {
                    objectiveKey = tutorialKey;
                    objectiveGroup.alpha = 0.25f;
                }
                objectiveText.text = tutorialLabel + "\n" +
                                     (manager.TutorialStep == 0
                                         ? (tutorialCurrent / 10f).ToString("0.0") + "m / " +
                                           (tutorialTarget / 10f).ToString("0.0") + "m"
                                         : "실제 행동을 완료하면 다음 단계로 넘어갑니다.");
                objectiveFill.rectTransform.anchorMax = new Vector2(
                    tutorialTarget <= 0 ? 0f : Mathf.Clamp01(tutorialCurrent / (float)tutorialTarget), 1f);
                return;
            }
            if (tutorialSkipButton != null) tutorialSkipButton.gameObject.SetActive(false);
            if (objectiveStepText != null) objectiveStepText.text = "목표";
            string key;
            string label;
            ShopProgressConditionType type;
            int current;
            int target;
            if (TryGetCurrentGoal(out ShopProgressGoalSave goal))
            {
                key = "goal:" + goal.definitionId;
                label = goal.displayName;
                type = (ShopProgressConditionType)goal.conditionType;
                current = manager.GetGoalProgress(goal);
                target = goal.target;
            }
            else if (TryGetCurrentStageCondition(out ShopProgressCondition condition))
            {
                key = "stage:" + manager.CurrentStageIndex + ":" + condition.DisplayName;
                label = condition.DisplayName;
                type = condition.Type;
                current = manager.GetConditionValue(condition);
                target = condition.Target;
            }
            else
            {
                key = "complete";
                label = "모든 등급 목표 달성";
                type = ShopProgressConditionType.UnitsSold;
                current = target = 1;
            }

            if (key != objectiveKey)
            {
                objectiveKey = key;
                objectiveGroup.alpha = 0.25f;
            }
            float progress = target <= 0 ? 1f : Mathf.Clamp01(current / (float)target);
            objectiveText.text = "현재 목표 · " + label + "\n" +
                                 FormatCurrent(type, current) + " / " + FormatDisplayTarget(type, target);
            objectiveFill.rectTransform.anchorMax = new Vector2(progress, 1f);
        }

        private bool TryGetCurrentGoal(out ShopProgressGoalSave result)
        {
            for (int i = 0; i < manager.DailyGoals.Count; i++)
            {
                ShopProgressGoalSave goal = manager.DailyGoals[i];
                if (goal == null || goal.completed) continue;
                result = goal;
                return true;
            }
            result = null;
            return false;
        }

        private bool TryGetCurrentStageCondition(out ShopProgressCondition result)
        {
            ShopProgressStage next = manager.NextStage;
            if (next != null)
            {
                for (int i = 0; i < next.Conditions.Count; i++)
                {
                    ShopProgressCondition condition = next.Conditions[i];
                    if (manager.GetConditionValue(condition) >= condition.Target) continue;
                    result = condition;
                    return true;
                }
            }
            result = null;
            return false;
        }

        private string BuildGradeText()
        {
            StringBuilder text = new();
            text.Append("<size=34><color=#46FFBF><b>등급 진행</b></color></size>\n\n");
            for (int i = 0; i < manager.Catalog.Stages.Count; i++)
            {
                ShopProgressStage stage = manager.Catalog.Stages[i];
                bool reached = i <= manager.CurrentStageIndex;
                text.Append(reached ? "<color=#46FFBF>● " : "<color=#70859A>○ ")
                    .Append(i + 1).Append(". ").Append(stage.DisplayName).Append("</color>");
                if (i == manager.CurrentStageIndex) text.Append("  <b>현재</b>");
                text.Append('\n');
            }

            ShopProgressStage next = manager.NextStage;
            text.Append('\n');
            if (next == null)
                return text.Append("<b>최종 등급을 달성했습니다.</b>").ToString();

            text.Append("<size=28><b>다음 등급 · ").Append(next.DisplayName).Append("</b></size>\n");
            for (int i = 0; i < next.Conditions.Count; i++)
            {
                ShopProgressCondition condition = next.Conditions[i];
                int current = manager.GetConditionValue(condition);
                text.Append(current >= condition.Target ? "✓ " : "○ ")
                    .Append(condition.DisplayName).Append("    ")
                    .Append(FormatCurrent(condition.Type, current)).Append(" / ")
                    .Append(FormatDisplayTarget(condition.Type, condition.Target)).Append('\n');
            }
            return text.ToString();
        }

        private string BuildGoalsText()
        {
            StringBuilder text = new();
            text.Append("<size=34><color=#FFBE38><b>오늘 목표</b></color></size>\n\n");
            AppendGoals(text, manager.DailyGoals);
            text.Append("\n<size=34><color=#8FB9FF><b>주간 목표</b></color></size>\n\n");
            AppendGoals(text, manager.WeeklyGoals);
            ShopLiveOperationsNetwork.Instance?.AppendStatus(text);
            ShopLiveOperationsNetwork.Instance?.AppendStampCards(text);
            ShopDifferentiationController.Instance?.AppendStatus(text);
            return text.ToString();
        }

        private void AppendGoals(StringBuilder text, IReadOnlyList<ShopProgressGoalSave> goals)
        {
            for (int i = 0; i < goals.Count; i++)
            {
                ShopProgressGoalSave goal = goals[i];
                ShopProgressConditionType type = (ShopProgressConditionType)goal.conditionType;
                int current = manager.GetGoalProgress(goal);
                text.Append(goal.completed ? "<color=#46FFBF>✓ " : "○ ")
                    .Append(goal.displayName).Append("    ")
                    .Append(FormatCurrent(type, current)).Append(" / ")
                    .Append(FormatDisplayTarget(type, goal.target));
                if (goal.completed) text.Append("</color>");
                text.Append('\n');
            }
        }

        private string BuildCollectionText()
        {
            StringBuilder text = new();
            text.Append("<size=34><color=#74B6FF><b>컬렉션 ")
                .Append(manager.CollectionPercent).Append("%</b></color></size>\n")
                .Append("보유 ").Append(manager.CollectionOwnedCount.ToString("N0"))
                .Append(" / 전체 ").Append(manager.CollectionRegisteredCount.ToString("N0"))
                .Append("\n\n");

            Dictionary<string, Vector2Int> progress = manager.GetCategoryCollectionProgress();
            foreach (KeyValuePair<string, Vector2Int> category in progress)
            {
                string displayName = manager.Catalog.GetCategoryDisplayName(category.Key);
                text.Append("<size=27><b>").Append(displayName).Append("</b>  ")
                    .Append(category.Value.x).Append(" / ").Append(category.Value.y)
                    .Append("</size>\n");
                for (int i = 0; i < manager.Catalog.CollectionItems.Count; i++)
                {
                    ShopCollectionItem item = manager.Catalog.CollectionItems[i];
                    if (item == null || item.CategoryId != category.Key) continue;
                    if (manager.OwnsCollectionItem(item.ItemId))
                        text.Append("  <color=#DCEBFA>◆ ").Append(item.DisplayName).Append("</color>\n");
                    else
                        text.Append("  <color=#536579>◆ 미획득</color>\n");
                }
                text.Append('\n');
            }
            return text.ToString();
        }

        private string BuildExpansionText()
        {
            StringBuilder text = new();
            text.Append("<size=34><color=#E978FF><b>가게 확장 Lv.")
                .Append(manager.ExpansionLevel).Append("</b></color></size>\n\n")
                .Append("<b>현재 시설</b>\n")
                .Append("진열대 ").Append(manager.CurrentDisplaySlots).Append("칸\n")
                .Append("창고 ").Append(manager.CurrentStorageSlots).Append("칸\n")
                .Append("계산대 ").Append(manager.CurrentCheckoutCount).Append("대\n\n");

            ShopExpansionTier next = manager.NextExpansion;
            if (next == null)
                return text.Append("<color=#46FFBF><b>모든 확장을 완료했습니다.</b></color>").ToString();
            text.Append("<size=28><b>다음 확장 · Lv.").Append(next.Level).Append("</b></size>\n")
                .Append("필요 평판 ").Append(next.RequiredReputation.ToString("N0")).Append("\n")
                .Append("비용 ").Append(next.RequiredFunds.ToString("N0")).Append("원\n");
            if (manager.ExpansionVouchers > 0)
                text.Append("확장권 보유 · 비용 없이 실행 가능\n");
            text.Append("\n화면 아래의 <b>확장 실행</b> 버튼을 누르세요. F4는 보조 단축키입니다.");
            return text.ToString();
        }

        private void RefreshExpansionButton()
        {
            bool visible = activeTab == 3;
            expansionButton.gameObject.SetActive(visible);
            if (!visible) return;
            ShopExpansionTier next = manager.NextExpansion;
            Text label = expansionButton.GetComponentInChildren<Text>();
            if (next == null)
            {
                expansionButton.interactable = false;
                label.text = "확장 완료";
                return;
            }
            bool affordable = manager.ExpansionVouchers > 0 ||
                              manager.TeamFunds >= next.RequiredFunds;
            expansionButton.interactable = manager.Reputation >= next.RequiredReputation && affordable;
            label.text = "확장 실행 · " + next.RequiredFunds.ToString("N0") + "원";
        }

        private void TryExpand()
        {
            if (manager == null) return;
            manager.TryExpandShop(out string result);
            EnqueueNotification(result);
            Refresh();
        }

        private void EnqueueNotification(string message)
        {
            if (!string.IsNullOrWhiteSpace(message)) notificationQueue.Enqueue(message);
        }

        private void UpdateNotification()
        {
            if (notificationPanel == null) return;
            if (notificationPanel.activeSelf)
            {
                if (Time.unscaledTime < hideNotificationAt) return;
                notificationPanel.SetActive(false);
            }
            if (notificationQueue.Count == 0) return;
            notificationText.text = notificationQueue.Dequeue();
            notificationPanel.SetActive(true);
            hideNotificationAt = Time.unscaledTime + 3.2f;
        }

        private static string FormatCurrent(ShopProgressConditionType type, int value)
        {
            return type == ShopProgressConditionType.LifetimeRevenue
                ? value.ToString("N0") + "원"
                : type == ShopProgressConditionType.CollectionPercent
                    ? value.ToString("N0") + "%"
                    : value.ToString("N0");
        }

        private static string FormatDisplayTarget(ShopProgressConditionType type, int value)
        {
            int rounded = FriendlyTarget(value);
            string prefix = rounded == value ? string.Empty : "약 ";
            return prefix + FormatCurrent(type, rounded);
        }

        private static int FriendlyTarget(int value)
        {
            if (value <= 0) return 0;
            int step = value >= 1000 ? 500 : value >= 100 ? 50 : value >= 10 ? 5 : 1;
            return Mathf.Max(step, Mathf.RoundToInt(value / (float)step) * step);
        }

        private static GameObject CreatePanel(string name, Transform parent, Vector2 size, Color color)
        {
            GameObject panel = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<RectTransform>().sizeDelta = size;
            Image image = panel.GetComponent<Image>();
            image.color = color;
            return panel;
        }

        private static Text CreateText(string name, Transform parent, string content, int size,
            FontStyle style, TextAnchor alignment, Color color)
        {
            Text text = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = ShopUiFonts.Resolve(style);
            text.fontSize = size;
            text.fontStyle = FontStyle.Normal;
            text.alignment = alignment;
            text.color = color;
            text.text = content;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label,
            Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            GameObject root = CreatePanel(name, parent, size, new Color(0.075f, 0.105f, 0.15f, 1f));
            Button button = root.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.15f, 0.3f, 0.34f, 1f);
            colors.pressedColor = new Color(0.08f, 0.22f, 0.24f, 1f);
            colors.disabledColor = new Color(0.08f, 0.09f, 0.11f, 0.7f);
            button.colors = colors;
            button.onClick.AddListener(onClick);

            Text text = CreateText("Label", root.transform, label, 22, FontStyle.Bold,
                TextAnchor.MiddleCenter, Color.white);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 4f);
            textRect.offsetMax = new Vector2(-10f, -4f);
            return button;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
