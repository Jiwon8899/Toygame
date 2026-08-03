using System.Collections.Generic;
using Blocks.Gameplay.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    public sealed class ShopUserSettingsData
    {
        public float MasterVolume = 0.85f;
        public float MusicVolume = 0.7f;
        public float EffectsVolume = 0.85f;
        public float MouseSensitivity = 1f;
        public bool InvertY;
        public bool CameraShake = true;
        public bool GamepadVibration = true;
        public bool Fullscreen;
        public bool VSync = true;
        public int Width = 1920;
        public int Height = 1080;
        public float UiScale = 1f;
    }

    public static class ShopUserSettings
    {
        private const string Prefix = "PickAndPlaceShop.Settings.";
        private static ShopUserSettingsData current;

        public static ShopUserSettingsData Current => current ?? (current = Load());

        public static ShopUserSettingsData Defaults()
        {
            return new ShopUserSettingsData
            {
                Fullscreen = Screen.fullScreen,
                Width = Screen.currentResolution.width,
                Height = Screen.currentResolution.height
            };
        }

        public static ShopUserSettingsData Load()
        {
            ShopUserSettingsData data = Defaults();
            data.MasterVolume = PlayerPrefs.GetFloat(Prefix + "Master", data.MasterVolume);
            data.MusicVolume = PlayerPrefs.GetFloat(Prefix + "Music", data.MusicVolume);
            data.EffectsVolume = PlayerPrefs.GetFloat(Prefix + "Effects", data.EffectsVolume);
            data.MouseSensitivity = PlayerPrefs.GetFloat(Prefix + "Sensitivity", data.MouseSensitivity);
            data.InvertY = PlayerPrefs.GetInt(Prefix + "InvertY", 0) != 0;
            data.CameraShake = PlayerPrefs.GetInt(Prefix + "CameraShake", 1) != 0;
            data.GamepadVibration = PlayerPrefs.GetInt(Prefix + "Vibration", 1) != 0;
            data.Fullscreen = PlayerPrefs.GetInt(Prefix + "Fullscreen", data.Fullscreen ? 1 : 0) != 0;
            data.VSync = PlayerPrefs.GetInt(Prefix + "VSync", 1) != 0;
            data.Width = PlayerPrefs.GetInt(Prefix + "Width", data.Width);
            data.Height = PlayerPrefs.GetInt(Prefix + "Height", data.Height);
            data.UiScale = PlayerPrefs.GetFloat(Prefix + "UiScale", data.UiScale);
            return data;
        }

        public static void Save(ShopUserSettingsData data)
        {
            current = data ?? Defaults();
            PlayerPrefs.SetFloat(Prefix + "Master", current.MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "Music", current.MusicVolume);
            PlayerPrefs.SetFloat(Prefix + "Effects", current.EffectsVolume);
            PlayerPrefs.SetFloat(Prefix + "Sensitivity", current.MouseSensitivity);
            PlayerPrefs.SetInt(Prefix + "InvertY", current.InvertY ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "CameraShake", current.CameraShake ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "Vibration", current.GamepadVibration ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "Fullscreen", current.Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "VSync", current.VSync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "Width", current.Width);
            PlayerPrefs.SetInt(Prefix + "Height", current.Height);
            PlayerPrefs.SetFloat(Prefix + "UiScale", current.UiScale);
            PlayerPrefs.Save();
            Apply(current, true);
        }

        public static void ResetToDefaults()
        {
            current = Defaults();
            Save(current);
        }

        public static void Apply(ShopUserSettingsData data, bool applyResolution)
        {
            if (data == null) return;
            AudioListener.volume = Mathf.Clamp01(data.MasterVolume);
            QualitySettings.vSyncCount = data.VSync ? 1 : 0;
            if (applyResolution)
            {
                Screen.SetResolution(Mathf.Max(640, data.Width), Mathf.Max(360, data.Height), data.Fullscreen);
            }
            ShopUserSettingsApplier.ApplyNow();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Object.FindFirstObjectByType<ShopUserSettingsApplier>() != null) return;
            GameObject host = new GameObject("[Global] Shop User Settings");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<ShopUserSettingsApplier>();
        }
    }

    [DefaultExecutionOrder(-9000)]
    public sealed class ShopUserSettingsApplier : MonoBehaviour
    {
        private static ShopUserSettingsApplier instance;
        private readonly Dictionary<int, float> baseVolumes = new();
        private float nextApply;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextApply) return;
            nextApply = Time.unscaledTime + 1f;
            ApplyInternal();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyNow();
        }

        public static void ApplyNow()
        {
            if (instance != null) instance.ApplyInternal();
        }

        private void ApplyInternal()
        {
            ShopUserSettingsData data = ShopUserSettings.Current;
            AudioListener.volume = Mathf.Clamp01(data.MasterVolume);

            foreach (AudioSource source in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                if (source == null || !source.gameObject.scene.IsValid()) continue;
                int id = source.GetInstanceID();
                if (!baseVolumes.TryGetValue(id, out float baseVolume))
                {
                    baseVolume = source.volume;
                    baseVolumes[id] = baseVolume;
                }
                string lower = source.name.ToLowerInvariant();
                bool music = source.loop && (lower.Contains("music") || lower.Contains("bgm") || lower.Contains("ambience"));
                source.volume = baseVolume * (music ? data.MusicVolume : data.EffectsVolume);
            }

            foreach (CoreCameraController cameraController in Resources.FindObjectsOfTypeAll<CoreCameraController>())
            {
                if (cameraController == null || !cameraController.gameObject.scene.IsValid()) continue;
                cameraController.SetLookSensitivity(Mathf.Clamp(data.MouseSensitivity, 0.1f, 4f));
                cameraController.SetInvertY(data.InvertY);
            }

            foreach (CanvasScaler scaler in Resources.FindObjectsOfTypeAll<CanvasScaler>())
            {
                if (scaler == null || !scaler.gameObject.scene.IsValid() || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;
                float scale = Mathf.Clamp(data.UiScale, 0.8f, 1.3f);
                scaler.referenceResolution = new Vector2(1920f / scale, 1080f / scale);
            }
        }
    }
}
