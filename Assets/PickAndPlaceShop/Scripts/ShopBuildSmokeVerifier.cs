#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [AddComponentMenu("")]
    public sealed class ShopBuildSmokeVerifier : MonoBehaviour
    {
        private const string Argument = "-shop-smoke";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Application.isEditor && Environment.GetCommandLineArgs().Contains(Argument))
            {
                GameObject host = new("[QA] Build Smoke Verifier");
                DontDestroyOnLoad(host);
                host.AddComponent<ShopBuildSmokeVerifier>();
            }
        }

        private IEnumerator Start()
        {
            yield return null;
            yield return null;
            Debug.Log("[BuildSmoke] TITLE_READY scene=" + SceneManager.GetActiveScene().name);
            Text title = FindComponent<Text>("TitleText");
            if (title == null || !title.text.Contains("냥냥"))
                yield return Fail("cat theme title missing");
            yield return Capture("CatTheme_Title.png");

            if (!InvokeButton("BtnMainStart")) yield return Fail("main start button missing");
            yield return null;
            if (!InvokeButton("BtnSolo")) yield return Fail("solo button missing");

            float deadline = Time.realtimeSinceStartup + 20f;
            while (SceneManager.GetActiveScene().name == ShopLaunchContext.MainMenuScene &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            if (SceneManager.GetActiveScene().name == ShopLaunchContext.MainMenuScene)
                yield return Fail("gameplay scene load timeout");

            deadline = Time.realtimeSinceStartup + 20f;
            while ((ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsSpawned) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            if (ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsSpawned)
                yield return Fail("network host startup timeout");
            Debug.Log("[BuildSmoke] MAIN_SCENE_READY scene=" + SceneManager.GetActiveScene().name);
            if (!ValidateCatTheme(out string catThemeResult))
                yield return Fail(catThemeResult);
            Debug.Log("[BuildSmoke] CAT_THEME_OK " + catThemeResult);
            yield return Capture("CatTheme_Gameplay.png");

            ShopPauseMenuController pause = FindFirstObjectByType<ShopPauseMenuController>();
            if (pause == null) yield return Fail("pause controller missing");
            pause.SendMessage("Open", SendMessageOptions.DontRequireReceiver);
            yield return new WaitForSecondsRealtime(0.15f);
            int eventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None).Length;
            Debug.Log("[BuildSmoke] PAUSE_OPEN paused=" + ShopLocalPauseState.IsPaused +
                      " scale=" + Time.timeScale.ToString("F1") +
                      " cursor=" + Cursor.lockState + "/" + Cursor.visible +
                      " eventSystems=" + eventSystems);
            if (!ShopLocalPauseState.IsPaused || Time.timeScale != 0f || eventSystems != 1)
                yield return Fail("pause state invalid");
            if (!InvokeButton("BtnPauseSettings")) yield return Fail("settings button missing");
            yield return null;
            GameObject settings = FindObject("PauseSettingsPanel");
            Slider slider = FindComponent<Slider>("PauseMasterSlider");
            if (settings == null || !settings.activeInHierarchy || slider == null || !slider.interactable)
                yield return Fail("settings panel did not become interactive");
            if (!InvokeButton("BtnPauseSettingsApply")) yield return Fail("settings apply missing");
            yield return null;
            if (!InvokeButton("BtnPauseResume")) yield return Fail("resume button missing");
            yield return new WaitForSecondsRealtime(0.15f);
            if (ShopLocalPauseState.IsPaused || Time.timeScale == 0f)
                yield return Fail("resume did not restore gameplay");
            Debug.Log("[BuildSmoke] PAUSE_SETTINGS_OK");

            ShopClawMachineNetwork machine = FindFirstObjectByType<ShopClawMachineNetwork>();
            if (machine == null) yield return Fail("scoop machine missing");
            if (!machine.BeginScoopFloorVerification(50)) yield return Fail("floor verifier did not start");
            deadline = Time.realtimeSinceStartup + 120f;
            while (machine.FloorContactSamples < 50 && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (machine.FloorContactSamples < 50 || machine.LastFloorPenetrationMillimeters >= 1f)
                yield return Fail("floor verification failed");
            Debug.Log("[BuildSmoke] FLOOR_OK contacts=" + machine.FloorContactSamples +
                      " penetrationMm=" + machine.LastFloorPenetrationMillimeters.ToString("F3"));

            pause.SendMessage("Open", SendMessageOptions.DontRequireReceiver);
            yield return new WaitForSecondsRealtime(0.15f);
            if (!InvokeButton("BtnPauseMainMenu")) yield return Fail("main menu button missing");
            yield return null;
            if (!InvokeButton("BtnPauseConfirmYes")) yield return Fail("main menu confirm missing");
            deadline = Time.realtimeSinceStartup + 20f;
            while (SceneManager.GetActiveScene().name != ShopLaunchContext.MainMenuScene &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            if (SceneManager.GetActiveScene().name != ShopLaunchContext.MainMenuScene)
                yield return Fail("return to title timeout");
            Debug.Log("[BuildSmoke] RETURN_TO_TITLE_OK cursor=" + Cursor.lockState + "/" + Cursor.visible);
            if (!InvokeButton("BtnMainStart")) yield return Fail("restart main start missing");
            yield return null;
            if (!InvokeButton("BtnSolo")) yield return Fail("restart solo missing");
            deadline = Time.realtimeSinceStartup + 20f;
            while (SceneManager.GetActiveScene().name == ShopLaunchContext.MainMenuScene &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            if (SceneManager.GetActiveScene().name == ShopLaunchContext.MainMenuScene)
                yield return Fail("restart gameplay timeout");
            deadline = Time.realtimeSinceStartup + 20f;
            while ((ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsSpawned) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            if (ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsSpawned)
                yield return Fail("restart network host timeout");
            Debug.Log("[BuildSmoke] RESTART_OK scene=" + SceneManager.GetActiveScene().name);

            Debug.Log("[BuildSmoke] COMPLETE");
            Application.Quit(0);
        }

        private IEnumerator Fail(string reason)
        {
            Debug.LogError("[BuildSmoke] FAILED " + reason);
            Application.Quit(2);
            while (true) yield return null;
        }

        private static bool InvokeButton(string name)
        {
            Button button = FindComponent<Button>(name);
            if (button == null) return false;
            button.onClick.Invoke();
            return true;
        }

        private static bool ValidateCatTheme(out string result)
        {
            ShopProductDefinition[] products = Resources.LoadAll<ShopProductDefinition>(
                "Products/CatCatalog");
            if (products.Length != 200)
            {
                result = "cat products=" + products.Length;
                return false;
            }
            int common = products.Count(product => product.Rarity == ShopProductRarity.Common);
            int uncommon = products.Count(product => product.Rarity == ShopProductRarity.Uncommon);
            int rare = products.Count(product => product.Rarity == ShopProductRarity.Rare);
            int ultra = products.Count(product => product.Rarity == ShopProductRarity.UltraRare);
            if (common != 110 || uncommon != 40 || rare != 40 || ultra != 10 ||
                products.Any(product => !ShopProductLocalization.IsCatTheme(product.Category)))
            {
                result = "rarity/category distribution invalid";
                return false;
            }
            string[] legacy =
            {
                "동물 친구들", "음식 캐릭터", "우주 탐험대", "달토끼",
                "레트로 로봇", "오늘의 한정", "별빛 가챠관"
            };
            string[] visibleTexts = FindObjectsByType<Text>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Select(text => text.text)
                .Concat(FindObjectsByType<TextMesh>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Select(text => text.text)).ToArray();
            string remaining = legacy.FirstOrDefault(old =>
                visibleTexts.Any(text => !string.IsNullOrEmpty(text) && text.Contains(old)));
            if (!string.IsNullOrEmpty(remaining))
            {
                result = "legacy world text=" + remaining;
                return false;
            }
            result = "products=200 rarity=110/40/40/10 legacyText=0";
            return true;
        }

        private static IEnumerator Capture(string fileName)
        {
            yield return new WaitForEndOfFrame();
            string folder = Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
            string path = Path.Combine(folder, fileName);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForEndOfFrame();
            Debug.Log("[BuildSmoke] SCREENSHOT " + path);
        }

        private static GameObject FindObject(string name)
        {
            Transform transform = Resources.FindObjectsOfTypeAll<Transform>()
                .FirstOrDefault(value => value.name == name && value.gameObject.scene.IsValid());
            return transform != null ? transform.gameObject : null;
        }

        private static T FindComponent<T>(string name) where T : Component
        {
            return Resources.FindObjectsOfTypeAll<T>()
                .FirstOrDefault(value => value.name == name && value.gameObject.scene.IsValid());
        }
    }
}
#endif
