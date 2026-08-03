using Blocks.Gameplay.Core;
using UnityEngine;

namespace PickAndPlaceShop
{
    public sealed class ShopKoreanFontApplier : MonoBehaviour
    {
        public static Font KoreanFont { get; private set; }

        private void Awake()
        {
            KoreanFont = GlobalGameFontApplier.LegacyFont;
            GlobalGameFontApplier.ApplyTo(gameObject);
        }
    }
}
