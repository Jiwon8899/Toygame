using Blocks.Gameplay.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    public enum ShopUiFontWeight
    {
        Regular,
        Medium,
        Bold
    }

    public enum ShopUiIcon
    {
        Paw,
        Moon,
        Coin,
        Star,
        Store,
        Package,
        Gift,
        Target,
        Capsule,
        People,
        Shoe,
        Ticket,
        Idea,
        Expand
    }

    [CreateAssetMenu(fileName = "ShopUiTheme", menuName = "Pick And Place Shop/UI Theme")]
    public sealed class ShopUiTheme : ScriptableObject
    {
        private const string ResourceName = "ShopUiTheme";
        private static ShopUiTheme instance;

        [Header("Fonts")]
        [SerializeField] private Font regularFont;
        [SerializeField] private Font mediumFont;
        [SerializeField] private Font boldFont;

        [Header("Nine Slice")]
        [SerializeField] private Sprite radius12;
        [SerializeField] private Sprite radius20;
        [SerializeField] private Sprite radius28;
        [SerializeField] private Sprite pillCapsule;
        [SerializeField] private Sprite foilGradient;
        [SerializeField] private Sprite foilGradientShine;

        [Header("Icons")]
        [SerializeField] private Sprite paw;
        [SerializeField] private Sprite moon;
        [SerializeField] private Sprite coin;
        [SerializeField] private Sprite star;
        [SerializeField] private Sprite store;
        [SerializeField] private Sprite package;
        [SerializeField] private Sprite gift;
        [SerializeField] private Sprite target;
        [SerializeField] private Sprite capsule;
        [SerializeField] private Sprite people;
        [SerializeField] private Sprite shoe;
        [SerializeField] private Sprite ticket;
        [SerializeField] private Sprite idea;
        [SerializeField] private Sprite expand;

        [Header("Warm Palette")]
        [SerializeField] private Color creamBackground = new Color32(0xF6, 0xEC, 0xDA, 0xFF);
        [SerializeField] private Color creamCard = new Color32(0xFE, 0xF9, 0xF1, 0xFF);
        [SerializeField] private Color brownDeep = new Color32(0x42, 0x29, 0x1A, 0xFF);
        [SerializeField] private Color brownMid = new Color32(0x6B, 0x45, 0x2B, 0xFF);
        [SerializeField] private Color tealPrimary = new Color32(0x1C, 0x6B, 0x63, 0xFF);
        [SerializeField] private Color pinkAccent = new Color32(0xDE, 0x8C, 0x9E, 0xFF);
        [SerializeField] private Color orangeAccent = new Color32(0xE5, 0x8C, 0x40, 0xFF);
        [SerializeField] private Color textBody = new Color32(0x4D, 0x38, 0x29, 0xFF);
        [SerializeField] private Color textMuted = new Color32(0x8C, 0x75, 0x61, 0xFF);
        [SerializeField] private Color divider = new Color32(0xDB, 0xCC, 0xB2, 0xFF);
        [SerializeField] private Color currency = new Color32(0xFF, 0xC7, 0x40, 0xFF);
        [SerializeField] private Color danger = new Color32(0x99, 0x29, 0x33, 0xFF);

        public static ShopUiTheme Instance
        {
            get
            {
                if (instance == null) instance = Resources.Load<ShopUiTheme>(ResourceName);
                return instance;
            }
        }

        public Font RegularFont => regularFont;
        public Font MediumFont => mediumFont != null ? mediumFont : regularFont;
        public Font BoldFont => boldFont != null ? boldFont : MediumFont;
        public Sprite Radius12 => radius12;
        public Sprite Radius20 => radius20;
        public Sprite Radius28 => radius28;
        public Sprite PillCapsule => pillCapsule;
        public Sprite FoilGradient => foilGradient;
        public Sprite FoilGradientShine => foilGradientShine;
        public Color CreamBackground => creamBackground;
        public Color CreamCard => creamCard;
        public Color BrownDeep => brownDeep;
        public Color BrownMid => brownMid;
        public Color TealPrimary => tealPrimary;
        public Color PinkAccent => pinkAccent;
        public Color OrangeAccent => orangeAccent;
        public Color TextBody => textBody;
        public Color TextMuted => textMuted;
        public Color Divider => divider;
        public Color Currency => currency;
        public Color Danger => danger;

        public Font Font(ShopUiFontWeight weight)
        {
            return weight switch
            {
                ShopUiFontWeight.Bold => BoldFont,
                ShopUiFontWeight.Medium => MediumFont,
                _ => RegularFont
            };
        }

        public Sprite Icon(ShopUiIcon icon)
        {
            return icon switch
            {
                ShopUiIcon.Paw => paw,
                ShopUiIcon.Moon => moon,
                ShopUiIcon.Coin => coin,
                ShopUiIcon.Star => star,
                ShopUiIcon.Store => store,
                ShopUiIcon.Package => package,
                ShopUiIcon.Gift => gift,
                ShopUiIcon.Target => target,
                ShopUiIcon.Capsule => capsule,
                ShopUiIcon.People => people,
                ShopUiIcon.Shoe => shoe,
                ShopUiIcon.Ticket => ticket,
                ShopUiIcon.Idea => idea,
                ShopUiIcon.Expand => expand,
                _ => null
            };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;
    }

    public static class ShopUiFonts
    {
        private static Font fallback;

        public static Font Regular => Resolve(ShopUiFontWeight.Regular);
        public static Font Medium => Resolve(ShopUiFontWeight.Medium);
        public static Font Bold => Resolve(ShopUiFontWeight.Bold);

        public static Font Resolve(ShopUiFontWeight weight)
        {
            Font configured = ShopUiTheme.Instance != null ? ShopUiTheme.Instance.Font(weight) : null;
            if (configured != null) return configured;
            if (GlobalGameFontApplier.LegacyFont != null) return GlobalGameFontApplier.LegacyFont;
            return fallback ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        public static Font Resolve(FontStyle style)
        {
            return style == FontStyle.Bold || style == FontStyle.BoldAndItalic ? Bold : Regular;
        }

        public static void Apply(Text text, ShopUiFontWeight weight = ShopUiFontWeight.Regular)
        {
            if (text == null) return;
            text.font = Resolve(weight);
            text.fontStyle = FontStyle.Normal;
        }

        public static void Apply(TextMesh text, ShopUiFontWeight weight = ShopUiFontWeight.Regular)
        {
            if (text == null) return;
            Font font = Resolve(weight);
            text.font = font;
            Renderer renderer = text.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = font.material;
        }
    }

    public static class ShopUiSkin
    {
        private static Color Fallback(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        public static Color CreamBackground => ShopUiTheme.Instance != null ? ShopUiTheme.Instance.CreamBackground : Fallback(0xF6, 0xEC, 0xDA);
        public static Color CreamCard => ShopUiTheme.Instance != null ? ShopUiTheme.Instance.CreamCard : Fallback(0xFE, 0xF9, 0xF1);
        public static Color BrownDeep => ShopUiTheme.Instance != null ? ShopUiTheme.Instance.BrownDeep : Fallback(0x42, 0x29, 0x1A);
        public static Color BrownMid => ShopUiTheme.Instance != null ? ShopUiTheme.Instance.BrownMid : Fallback(0x6B, 0x45, 0x2B);
        public static Color Teal => ShopUiTheme.Instance != null ? ShopUiTheme.Instance.TealPrimary : Fallback(0x1C, 0x6B, 0x63);
        public static Color Pink => ShopUiTheme.Instance != null ? ShopUiTheme.Instance.PinkAccent : Fallback(0xDE, 0x8C, 0x9E);
        public static Color Orange => ShopUiTheme.Instance != null ? ShopUiTheme.Instance.OrangeAccent : Fallback(0xE5, 0x8C, 0x40);
        public static Color TextBody => ShopUiTheme.Instance != null ? ShopUiTheme.Instance.TextBody : Fallback(0x4D, 0x38, 0x29);
        public static Color TextMuted => ShopUiTheme.Instance != null ? ShopUiTheme.Instance.TextMuted : Fallback(0x8C, 0x75, 0x61);
        public static Color Divider => ShopUiTheme.Instance != null ? ShopUiTheme.Instance.Divider : Fallback(0xDB, 0xCC, 0xB2);
        public static Color Currency => ShopUiTheme.Instance != null ? ShopUiTheme.Instance.Currency : Fallback(0xFF, 0xC7, 0x40);
        public static Color Danger => ShopUiTheme.Instance != null ? ShopUiTheme.Instance.Danger : Fallback(0x99, 0x29, 0x33);

        public static void Round(Image image, int radius)
        {
            if (image == null || ShopUiTheme.Instance == null) return;
            image.sprite = radius switch
            {
                <= 12 => ShopUiTheme.Instance.Radius12,
                <= 20 => ShopUiTheme.Instance.Radius20,
                _ => ShopUiTheme.Instance.Radius28
            };
            image.type = Image.Type.Sliced;
        }

        public static void Pill(Image image)
        {
            if (image == null || ShopUiTheme.Instance == null) return;
            image.sprite = ShopUiTheme.Instance.PillCapsule;
            image.type = Image.Type.Sliced;
        }

        public static Image AddIcon(string name, Transform parent, ShopUiIcon icon, Color badgeColor,
            Vector2 size, Vector2 position, Vector2 anchor)
        {
            GameObject badge = new(name + "Badge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(parent, false);
            RectTransform badgeRect = badge.GetComponent<RectTransform>();
            badgeRect.anchorMin = badgeRect.anchorMax = badgeRect.pivot = anchor;
            badgeRect.sizeDelta = size;
            badgeRect.anchoredPosition = position;
            Image badgeImage = badge.GetComponent<Image>();
            badgeImage.color = badgeColor;
            Round(badgeImage, 20);

            GameObject iconObject = new(name, typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(badge.transform, false);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.2f, 0.2f);
            iconRect.anchorMax = new Vector2(0.8f, 0.8f);
            iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;
            Image iconImage = iconObject.GetComponent<Image>();
            iconImage.sprite = ShopUiTheme.Instance != null ? ShopUiTheme.Instance.Icon(icon) : null;
            iconImage.color = Color.white;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            return iconImage;
        }
    }
}
