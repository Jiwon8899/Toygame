#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [AddComponentMenu("")]
    public sealed class ShopUiBuildVerifier : MonoBehaviour
    {
        private const string Argument = "-shop-ui-verify";
        private string outputDirectory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Environment.GetCommandLineArgs().Contains(Argument)) return;
            GameObject host = new("[QA] UI Redesign Verifier");
            DontDestroyOnLoad(host);
            host.AddComponent<ShopUiBuildVerifier>();
        }

        private IEnumerator Start()
        {
            outputDirectory = Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ??
                                           Application.persistentDataPath, "UIRedesignVerification");
            Directory.CreateDirectory(outputDirectory);

            yield return null;
            if (!InvokeButton("BtnMainStart") || !InvokeButton("BtnSolo"))
                yield return Fail("solo start buttons missing");
            Button continueButton = Find<Button>("BtnContinueGame");
            if (continueButton != null && continueButton.gameObject.activeInHierarchy)
                continueButton.onClick.Invoke();

            float deadline = Time.realtimeSinceStartup + 25f;
            while (SceneManager.GetActiveScene().name == ShopLaunchContext.MainMenuScene &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            while ((ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsSpawned) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            if (ShopNetworkGame.Instance == null) yield return Fail("gameplay host missing");

            ShopNetworkGame.Instance.ServerSetPhase(ShopPhase.Setup);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Capture("01_HUD.png");

            ShopOnlineOrderIconWidget widget = FindFirstObjectByType<ShopOnlineOrderIconWidget>(FindObjectsInactive.Include);
            GameObject orderPanel = GetField<GameObject>(widget, "panel");
            Text orderLabel = GetField<Text>(widget, "label");
            if (orderPanel != null && orderLabel != null)
            {
                orderLabel.text = "온라인 주문\n치즈 고양이 ×2";
                orderPanel.SetActive(true);
                yield return null;
                yield return Capture("02_HUD_OnlineOrder.png");
            }

            ShopPauseMenuController pause = FindFirstObjectByType<ShopPauseMenuController>(FindObjectsInactive.Include);
            InvokePrivate(pause, "Open");
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Capture("03_Pause.png");
            InvokePrivate(pause, "Close");

            ShopUpgradeUI.Open();
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Capture("04_Upgrade.png");
            InvokePrivate(FindFirstObjectByType<ShopUpgradeUI>(FindObjectsInactive.Include), "SetOpen", false);

            ShopProgressionHUD progression = FindFirstObjectByType<ShopProgressionHUD>(FindObjectsInactive.Include);
            InvokePrivate(progression, "OpenTutorialSkipConfirmation");
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Capture("05_TutorialConfirm.png");
            InvokePrivate(progression, "CloseTutorialSkipConfirmation");

            ShopCurationSystem curation = ShopCurationSystem.Instance;
            Canvas scoreCanvas = GetField<Canvas>(curation, "scoreCanvas");
            if (scoreCanvas != null) scoreCanvas.enabled = true;
            InvokePrivate(curation, "UpdateScorePanel");
            yield return null;
            yield return Capture("06_Hotbar_Shelf.png");
            if (scoreCanvas != null) scoreCanvas.enabled = false;

            ShopKujiScratchView kuji = FindFirstObjectByType<ShopKujiScratchView>(FindObjectsInactive.Include);
            if (kuji != null)
            {
                InvokePrivate(kuji, "EnsureBuilt");
                InvokePrivate(kuji, "SetVisible", true);
                kuji.enabled = false;
                yield return null;
                yield return Capture("07_Kuji.png");
                kuji.enabled = true;
                InvokePrivate(kuji, "SetVisible", false);
            }

            ShopNetworkGame.Instance.ServerSetPhase(ShopPhase.Summary);
            yield return new WaitForSecondsRealtime(4.5f);
            yield return Capture("08_Closing.png");

            Debug.Log("[UiBuildVerify] COMPLETE output=" + outputDirectory);
            Application.Quit(0);
        }

        private IEnumerator Capture(string fileName)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(outputDirectory, fileName);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForEndOfFrame();
            Debug.Log("[UiBuildVerify] SCREENSHOT " + path);
        }

        private static IEnumerator Fail(string reason)
        {
            Debug.LogError("[UiBuildVerify] FAILED " + reason);
            Application.Quit(2);
            yield break;
        }

        private static bool InvokeButton(string name)
        {
            Button button = Find<Button>(name);
            if (button == null || !button.gameObject.activeInHierarchy) return false;
            button.onClick.Invoke();
            return true;
        }

        private static T Find<T>(string name) where T : Component
        {
            return Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(value =>
                value.name == name && value.gameObject.scene.IsValid());
        }

        private static T GetField<T>(object target, string name) where T : class
        {
            if (target == null) return null;
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(target) as T;
        }

        private static void InvokePrivate(object target, string name, params object[] parameters)
        {
            if (target == null) return;
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(target, parameters);
        }
    }
}
#endif
