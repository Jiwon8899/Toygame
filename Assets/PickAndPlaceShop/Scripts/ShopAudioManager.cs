using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(-8900)]
    public sealed class ShopAudioManager : MonoBehaviour
    {
        private static ShopAudioManager instance;

        private readonly HashSet<int> hookedTitleButtons = new();
        private ShopAudioConfig config;
        private AudioSource musicSource;
        private AudioSource uiSource;
        private AudioSource moneySource;
        private ShopNetworkGame boundGame;
        private float nextMoneySoundTime;

        public static ShopAudioManager Instance => instance;
        public int DebugTitleClickPlayCount { get; private set; }
        public int DebugMoneyPlayCount { get; private set; }
        public int DebugMusicSourceInstanceId => musicSource != null ? musicSource.GetInstanceID() : 0;
        public int DebugMusicTimeSamples => musicSource != null ? musicSource.timeSamples : 0;
        public bool DebugMusicLooping => musicSource != null && musicSource.loop;
        public bool DebugMusicRequested => musicSource != null && musicSource.clip != null && musicSource.isPlaying;
        public int DebugHookedTitleButtonCount => hookedTitleButtons.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<ShopAudioManager>() != null) return;
            GameObject host = new("[Global] Shop Audio");
            DontDestroyOnLoad(host);
            host.AddComponent<ShopAudioManager>();
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
            config = ShopAudioConfig.Load();
            CreateSources();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            EnsureBackgroundMusic();
            StartCoroutine(RefreshSceneAudioNextFrame(SceneManager.GetActiveScene()));
        }

        private void Update()
        {
            if (boundGame != ShopNetworkGame.Instance) BindFundsSource(ShopNetworkGame.Instance);
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            BindFundsSource(null);
        }

        private void CreateSources()
        {
            musicSource = CreateSource("BGM Music", true);
            uiSource = CreateSource("UI SFX", false);
            moneySource = CreateSource("Economy SFX", false);
            if (config == null)
            {
                Debug.LogError("[Audio] ShopAudioConfig is missing from Resources/Audio.", this);
                return;
            }

            musicSource.clip = config.BackgroundMusic;
            musicSource.volume = config.BackgroundMusicVolume;
            uiSource.volume = config.TitleButtonVolume;
            moneySource.volume = config.MoneyIncreaseVolume;
        }

        private AudioSource CreateSource(string sourceName, bool loop)
        {
            GameObject child = new(sourceName, typeof(AudioSource));
            child.transform.SetParent(transform, false);
            AudioSource source = child.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            return source;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureBackgroundMusic();
            StartCoroutine(RefreshSceneAudioNextFrame(scene));
        }

        private IEnumerator RefreshSceneAudioNextFrame(Scene scene)
        {
            yield return null;
            AttachTitleButtonSounds(scene);
            ShopUserSettingsApplier.ApplyNow();
        }

        public void EnsureBackgroundMusic()
        {
            if (config == null || config.BackgroundMusic == null || musicSource == null) return;
            if (musicSource.clip != config.BackgroundMusic) musicSource.clip = config.BackgroundMusic;
            if (!musicSource.isPlaying)
            {
                musicSource.Play();
                Debug.Log("[Audio] BGM_PLAY_REQUESTED clip=" + config.BackgroundMusic.name, this);
            }
        }

        public void AttachTitleButtonSounds(Scene scene)
        {
            if (!scene.IsValid() || !scene.name.Contains("MainMenu")) return;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Button button in root.GetComponentsInChildren<Button>(true))
                {
                    if (button == null || !hookedTitleButtons.Add(button.GetInstanceID())) continue;
                    button.onClick.AddListener(PlayTitleButtonClick);
                }
            }
            Debug.Log("[Audio] TITLE_BUTTONS_HOOKED count=" + hookedTitleButtons.Count, this);
        }

        public void PlayTitleButtonClick()
        {
            ResumeWebAudioIfNeeded();
            EnsureBackgroundMusic();
            if (config == null || config.TitleButtonClick == null || uiSource == null) return;
            uiSource.PlayOneShot(config.TitleButtonClick);
            DebugTitleClickPlayCount++;
            Debug.Log("[Audio] TITLE_CLICK count=" + DebugTitleClickPlayCount, this);
        }

        private void BindFundsSource(ShopNetworkGame game)
        {
            if (boundGame != null) boundGame.Coins.OnValueChanged -= OnFundsChanged;
            boundGame = game;
            if (boundGame != null) boundGame.Coins.OnValueChanged += OnFundsChanged;
        }

        private void OnFundsChanged(int previousValue, int currentValue)
        {
            if (currentValue <= previousValue || config == null || config.MoneyIncrease == null || moneySource == null)
                return;
            if (Time.unscaledTime < nextMoneySoundTime) return;

            nextMoneySoundTime = Time.unscaledTime + config.MoneyMinimumIntervalSeconds;
            moneySource.PlayOneShot(config.MoneyIncrease);
            DebugMoneyPlayCount++;
            Debug.Log("[Audio] FUNDS_INCREASE previous=" + previousValue + " current=" + currentValue +
                      " count=" + DebugMoneyPlayCount, this);
        }

        private static void ResumeWebAudioIfNeeded()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ShopResumeWebAudio();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ShopResumeWebAudio();
#endif
    }
}
