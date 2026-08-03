using TMPro;
using UnityEngine;

namespace Blocks.Gameplay.Core
{
    [CreateAssetMenu(fileName = "GlobalGameFontSettings", menuName = "Blocks/Global Game Font Settings")]
    public sealed class GlobalGameFontSettings : ScriptableObject
    {
        [SerializeField] private Font legacyFont;
        [SerializeField] private TMP_FontAsset textMeshProFont;

        public Font LegacyFont => legacyFont;
        public TMP_FontAsset TextMeshProFont => textMeshProFont;
        public bool IsConfigured => legacyFont != null &&
                                    textMeshProFont != null &&
                                    textMeshProFont.material != null &&
                                    textMeshProFont.atlasTexture != null;
    }
}
