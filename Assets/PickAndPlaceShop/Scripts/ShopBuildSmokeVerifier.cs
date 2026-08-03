#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
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
