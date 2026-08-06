using TMPro;
using UnityEngine;

namespace Blocks.Gameplay.Core
{
    [CreateAssetMenu(fileName = "GlobalGameFontSettings", menuName = "Blocks/Global Game Font Settings")]
    public sealed class GlobalGameFontSettings : ScriptableObject
    {
        [SerializeField] private Font legacyFont;
        [SerializeField] private Font legacyMediumFont;
        [SerializeField] private Font legacyBoldFont;
        [SerializeField] private TMP_FontAsset textMeshProFont;

        public Font LegacyFont => legacyFont;
        public Font LegacyMediumFont => legacyMediumFont != null ? legacyMediumFont : legacyFont;
        public Font LegacyBoldFont => legacyBoldFont != null ? legacyBoldFont : LegacyMediumFont;
        public TMP_FontAsset TextMeshProFont => textMeshProFont;
        public bool IsConfigured => legacyFont != null &&
                                    textMeshProFont != null &&
                                    textMeshProFont.material != null &&
                                    textMeshProFont.atlasTexture != null;

        public bool IsLegacyFamilyFont(Font font)
        {
            return font != null && (font == legacyFont || font == legacyMediumFont || font == legacyBoldFont);
        }
    }
}
