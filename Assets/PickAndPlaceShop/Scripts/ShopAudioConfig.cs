using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Audio Config", fileName = "ShopAudioConfig")]
    public sealed class ShopAudioConfig : ScriptableObject
    {
        public const string ResourcePath = "Audio/ShopAudioConfig";

        [Header("Clips")]
        [SerializeField] private AudioClip backgroundMusic;
        [SerializeField] private AudioClip titleButtonClick;
        [SerializeField] private AudioClip moneyIncrease;

        [Header("Base volume (user settings are applied afterwards)")]
        [SerializeField, Range(0f, 1f)] private float backgroundMusicVolume = 0.55f;
        [SerializeField, Range(0f, 1f)] private float titleButtonVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float moneyIncreaseVolume = 0.85f;
        [SerializeField, Min(0f)] private float moneyMinimumIntervalSeconds = 0.14f;

        public AudioClip BackgroundMusic => backgroundMusic;
        public AudioClip TitleButtonClick => titleButtonClick;
        public AudioClip MoneyIncrease => moneyIncrease;
        public float BackgroundMusicVolume => backgroundMusicVolume;
        public float TitleButtonVolume => titleButtonVolume;
        public float MoneyIncreaseVolume => moneyIncreaseVolume;
        public float MoneyMinimumIntervalSeconds => moneyMinimumIntervalSeconds;

        public static ShopAudioConfig Load() => Resources.Load<ShopAudioConfig>(ResourcePath);

#if UNITY_EDITOR
        public void EditorConfigure(AudioClip music, AudioClip buttonClick, AudioClip fundsIncrease)
        {
            backgroundMusic = music;
            titleButtonClick = buttonClick;
            moneyIncrease = fundsIncrease;
        }
#endif
    }
}
