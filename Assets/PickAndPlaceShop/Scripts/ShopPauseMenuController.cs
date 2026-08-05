using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    public static class ShopLocalPauseState
    {
        public static bool IsPaused { get; internal set; }
    }

    public sealed class ShopPauseMenuController : MonoBehaviour
    {
        private GameObject root;
        private bool open;
        private bool soloTimePaused;
        private string confirmAction;

        private void Awake()
        {
            root = FindObject("PauseRoot");
            if (root != null) root.SetActive(false);
            Text title = Find<Text>("PauseTitle");
            if (title != null) title.text = ShopGameIdentity.KoreanShortName;
            CreateSaveButton();
            CreateTutorialResetButton();
            RegisterButtons();
        }

        private void Update()
        {
            if (ShopUpgradeUI.IsOpen) return;
            bool menuPressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            menuPressed |= Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
            if (!menuPressed || ShopClawMachineNetwork.LocalOperatorActive) return;
            if (open && !FindObject("PauseMainPanel").activeSelf) ShowMain();
            else if (open) Close();
            else Open();
        }

        private void RegisterButtons()
        {
            OnClick("BtnPauseResume", Close);
            OnClick("BtnPauseSave", SaveGame);
            OnClick("BtnPauseHelp", () => ShowOnly("PauseHelpPanel", "BtnPauseHelpBack"));
            OnClick("BtnPauseSettings", OpenSettings);
            OnClick("BtnPauseMainMenu", () => OpenConfirm("menu"));
            OnClick("BtnPauseQuit", () => OpenConfirm("quit"));
            OnClick("BtnPauseHelpBack", ShowMain);
            OnClick("BtnPauseSettingsApply", SaveSettings);
            OnClick("BtnPauseSettingsBack", ShowMain);
            OnClick("BtnPauseTutorialReset", ResetTutorial);
            OnClick("BtnPauseConfirmYes", Confirm);
            OnClick("BtnPauseConfirmNo", ShowMain);
        }

        private void Open()
        {
            open = true;
            ShopLocalPauseState.IsPaused = true;
            root.SetActive(true);
            soloTimePaused = true;
            if (soloTimePaused) Time.timeScale = 0f;
            ShopInputModeManager.Push(this, ShopInputMode.UI);
            ShowMain();
        }

        private void Close()
        {
            if (!open) return;
            open = false;
            ShopLocalPauseState.IsPaused = false;
            if (soloTimePaused) Time.timeScale = 1f;
            soloTimePaused = false;
            ShopInputModeManager.Pop(this);
            if (root != null) root.SetActive(false);
        }

        private void ShowMain() => ShowOnly("PauseMainPanel", "BtnPauseResume");

        private void OpenSettings()
        {
            ShopUserSettingsData data = ShopUserSettings.Current;
            SetSlider("PauseMasterSlider", data.MasterVolume);
            SetSlider("PauseMusicSlider", data.MusicVolume);
            SetSlider("PauseEffectsSlider", data.EffectsVolume);
            SetSlider("PauseSensitivitySlider", data.MouseSensitivity);
            SetSlider("PauseUiScaleSlider", data.UiScale);
            SetToggle("PauseInvertYToggle", data.InvertY);
            SetToggle("PauseShakeToggle", data.CameraShake);
            SetToggle("PauseVibrationToggle", data.GamepadVibration);
            ShowOnly("PauseSettingsPanel", "PauseMasterSlider");
        }

        private void SaveSettings()
        {
            ShopUserSettingsData previous = ShopUserSettings.Current;
            ShopUserSettingsData data = new ShopUserSettingsData
            {
                MasterVolume = SliderValue("PauseMasterSlider", previous.MasterVolume),
                MusicVolume = SliderValue("PauseMusicSlider", previous.MusicVolume),
                EffectsVolume = SliderValue("PauseEffectsSlider", previous.EffectsVolume),
                MouseSensitivity = SliderValue("PauseSensitivitySlider", previous.MouseSensitivity),
                UiScale = SliderValue("PauseUiScaleSlider", previous.UiScale),
                InvertY = ToggleValue("PauseInvertYToggle"),
                CameraShake = ToggleValue("PauseShakeToggle"),
                GamepadVibration = ToggleValue("PauseVibrationToggle"),
                Fullscreen = previous.Fullscreen,
                VSync = previous.VSync,
                Width = previous.Width,
                Height = previous.Height
            };
            ShopUserSettings.Save(data);
            ShowMain();
        }

        private void SaveGame()
        {
            ShopProgressionManager manager = ShopProgressionManager.Instance;
            bool saved = manager != null && manager.SaveNowWithFeedback();
            Text status = Find<Text>("PauseSaveStatus");
            if (status != null)
            {
                status.text = saved ? "저장 완료" : "저장 실패";
                StartCoroutine(ClearSaveStatus(status));
            }
        }

        private void ResetTutorial()
        {
            ShopProgressionManager.Instance?.ResetTutorial();
            ShowMain();
        }

        private void CreateTutorialResetButton()
        {
            GameObject panel = FindObject("PauseSettingsPanel");
            Button template = Find<Button>("BtnPauseSettingsBack");
            if (panel == null || template == null || FindObject("BtnPauseTutorialReset") != null) return;
            Button button = Instantiate(template, panel.transform);
            button.name = "BtnPauseTutorialReset";
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.text = "튜토리얼 다시 보기";
            RectTransform rect = button.GetComponent<RectTransform>();
            RectTransform templateRect = template.GetComponent<RectTransform>();
            rect.anchoredPosition = templateRect.anchoredPosition + Vector2.up * 72f;
        }

        private IEnumerator ClearSaveStatus(Text status)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            if (status != null) status.text = string.Empty;
        }

        private void CreateSaveButton()
        {
            Button template = Find<Button>("BtnPauseResume");
            GameObject panel = FindObject("PauseMainPanel");
            if (template == null || panel == null) return;

            Button saveButton = Find<Button>("BtnPauseSave");
            if (saveButton == null)
            {
                saveButton = Instantiate(template, panel.transform);
                saveButton.name = "BtnPauseSave";
                Text saveLabel = saveButton.GetComponentInChildren<Text>(true);
                if (saveLabel != null) saveLabel.text = "저장";
            }

            BuildButtonLayout(panel.transform);

            if (FindObject("PauseSaveStatus") == null)
            {
                GameObject statusObject = new("PauseSaveStatus", typeof(RectTransform), typeof(Text));
                statusObject.transform.SetParent(panel.transform, false);
                RectTransform statusRect = statusObject.GetComponent<RectTransform>();
                statusRect.anchorMin = statusRect.anchorMax = new Vector2(0.5f, 0.5f);
                statusRect.sizeDelta = new Vector2(430f, 36f);
                statusRect.anchoredPosition = new Vector2(0f, -326f);
                Text status = statusObject.GetComponent<Text>();
                Text label = template.GetComponentInChildren<Text>(true);
                status.font = label != null ? label.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                status.fontSize = 20;
                status.alignment = TextAnchor.MiddleCenter;
                status.color = new Color(0.45f, 1f, 0.75f);
            }
        }

        private void BuildButtonLayout(Transform panel)
        {
            Transform existing = panel.Find("PauseButtonColumn");
            GameObject column = existing != null ? existing.gameObject :
                new GameObject("PauseButtonColumn", typeof(RectTransform), typeof(VerticalLayoutGroup));
            if (existing == null) column.transform.SetParent(panel, false);
            RectTransform columnRect = column.GetComponent<RectTransform>();
            columnRect.anchorMin = columnRect.anchorMax = columnRect.pivot = new Vector2(0.5f, 0.5f);
            columnRect.sizeDelta = new Vector2(430f, 444f);
            columnRect.anchoredPosition = new Vector2(0f, -70f);
            VerticalLayoutGroup layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            string[] orderedButtons =
            {
                "BtnPauseResume", "BtnPauseSave", "BtnPauseHelp", "BtnPauseSettings",
                "BtnPauseMainMenu", "BtnPauseQuit"
            };
            for (int index = 0; index < orderedButtons.Length; index++)
            {
                Button button = Find<Button>(orderedButtons[index]);
                if (button == null) continue;
                button.transform.SetParent(column.transform, false);
                button.transform.SetSiblingIndex(index);
                LayoutElement element = button.GetComponent<LayoutElement>() ??
                                        button.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = 430f;
                element.preferredHeight = 64f;
                element.flexibleWidth = 0f;
                element.flexibleHeight = 0f;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(columnRect);
        }

        private void OpenConfirm(string action)
        {
            confirmAction = action;
            Text message = Find<Text>("PauseConfirmMessage");
            if (message != null)
            {
                message.text = action == "quit"
                    ? "현재 게임을 종료하시겠습니까?"
                    : "메인 메뉴로 돌아가시겠습니까?";
            }
            ShowOnly("PauseConfirmPanel", "BtnPauseConfirmNo");
        }

        private void Confirm()
        {
            bool quit = confirmAction == "quit";
            StartCoroutine(ShutdownAndExit(quit));
        }

        private IEnumerator ShutdownAndExit(bool quit)
        {
            if (soloTimePaused) Time.timeScale = 1f;
            ShopLocalPauseState.IsPaused = false;
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null)
            {
                if (manager.IsListening || manager.ShutdownInProgress) manager.Shutdown();
                float deadline = Time.realtimeSinceStartup + 5f;
                while ((manager.IsListening || manager.ShutdownInProgress) && Time.realtimeSinceStartup < deadline) yield return null;
                Destroy(manager.gameObject);
            }
            yield return null;
            Debug.Log("[ShopFlow] SOLO_ENDED target=" + (quit ? "QUIT" : "MAIN_MENU"));
            if (quit)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
            else SceneManager.LoadScene(ShopLaunchContext.MainMenuScene);
        }

        private void ShowOnly(string panelName, string selection)
        {
            foreach (string name in new[] { "PauseMainPanel", "PauseHelpPanel", "PauseSettingsPanel", "PauseConfirmPanel" })
            {
                GameObject panel = FindObject(name);
                if (panel != null) panel.SetActive(name == panelName);
            }
            Selectable selectable = Find<Selectable>(selection);
            if (selectable != null) StartCoroutine(SelectNextFrame(selectable.gameObject));
        }

        private static IEnumerator SelectNextFrame(GameObject target)
        {
            yield return null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(target);
        }

        private void OnClick(string name, UnityEngine.Events.UnityAction action) { Button button = Find<Button>(name); if (button != null) button.onClick.AddListener(action); }
        private void SetSlider(string name, float value) { Slider slider = Find<Slider>(name); if (slider != null) slider.value = value; }
        private void SetToggle(string name, bool value) { Toggle toggle = Find<Toggle>(name); if (toggle != null) toggle.isOn = value; }
        private float SliderValue(string name, float fallback) { Slider slider = Find<Slider>(name); return slider != null ? slider.value : fallback; }
        private bool ToggleValue(string name) { Toggle toggle = Find<Toggle>(name); return toggle != null && toggle.isOn; }
        private GameObject FindObject(string name) { Transform value = GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name); return value != null ? value.gameObject : null; }
        private T Find<T>(string name) where T : Component => GetComponentsInChildren<T>(true).FirstOrDefault(x => x.gameObject.name == name);
    }
}
