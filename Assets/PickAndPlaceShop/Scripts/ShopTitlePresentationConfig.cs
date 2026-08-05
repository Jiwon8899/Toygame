using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Title Presentation Config",
        fileName = "ShopTitlePresentationConfig")]
    public sealed class ShopTitlePresentationConfig : ScriptableObject
    {
        public const string ResourcePath = "UI/ShopTitlePresentationConfig";

        [SerializeField] private Sprite background;
        [SerializeField] private Sprite logo;
        [SerializeField, Range(0.4f, 1f)] private float backgroundVeilAlpha = 0.58f;
        [SerializeField] private Vector2 logoSize = new(920f, 280f);
        [SerializeField] private Vector2 logoOffset = new(0f, 50f);
        [SerializeField, Range(0.3f, 1.2f)] private float entranceSeconds = 0.65f;
        [SerializeField, Range(0.7f, 0.95f)] private float entranceStartScale = 0.85f;
        [SerializeField, Range(1f, 8f)] private float idleAmplitudePixels = 4f;
        [SerializeField, Range(1.5f, 5f)] private float idlePeriodSeconds = 2.6f;

        public Sprite Background => background;
        public Sprite Logo => logo;
        public float BackgroundVeilAlpha => backgroundVeilAlpha;
        public Vector2 LogoSize => logoSize;
        public Vector2 LogoOffset => logoOffset;
        public float EntranceSeconds => entranceSeconds;
        public float EntranceStartScale => entranceStartScale;
        public float IdleAmplitudePixels => idleAmplitudePixels;
        public float IdlePeriodSeconds => idlePeriodSeconds;

        public static ShopTitlePresentationConfig Load() =>
            Resources.Load<ShopTitlePresentationConfig>(ResourcePath);

#if UNITY_EDITOR
        public void EditorConfigure(Sprite titleBackground, Sprite titleLogo)
        {
            background = titleBackground;
            logo = titleLogo;
        }
#endif
    }
}
