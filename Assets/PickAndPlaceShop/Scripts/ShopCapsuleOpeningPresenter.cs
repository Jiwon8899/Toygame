using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(800)]
    public sealed class ShopCapsuleOpeningPresenter : MonoBehaviour
    {
        private static ShopCapsuleOpeningPresenter instance;
        private CanvasGroup group;
        private RectTransform capsule;
        private Image capsuleImage;
        private Text rarityText;
        private Text productText;
        private Text skipText;
        private Coroutine sequence;
        private bool skipRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        public static void Show(string productName, ShopProductRarity rarity, Color capsuleColor)
        {
            EnsureInstance();
            instance.Begin(productName, rarity, capsuleColor);
        }

        private static void EnsureInstance()
        {
            if (instance != null) return;
            GameObject host = new("[Claw] Capsule Opening");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopCapsuleOpeningPresenter>();
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
            BuildUi();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        private void Update()
        {
            if (group == null || group.alpha <= 0f) return;
            if (Keyboard.current != null &&
                (Keyboard.current.spaceKey.wasPressedThisFrame ||
                 Keyboard.current.escapeKey.wasPressedThisFrame))
                skipRequested = true;
            if (Gamepad.current != null &&
                (Gamepad.current.buttonSouth.wasPressedThisFrame ||
                 Gamepad.current.buttonEast.wasPressedThisFrame))
                skipRequested = true;
        }

        private void Begin(string productName, ShopProductRarity rarity, Color color)
        {
            if (sequence != null) StopCoroutine(sequence);
            skipRequested = false;
            productText.text = productName;
            rarityText.text = ShopProductLocalization.RarityLabel(rarity) + " 캡슐";
            rarityText.color = rarity switch
            {
                ShopProductRarity.Rare => new Color(1f, 0.78f, 0.2f),
                ShopProductRarity.Uncommon => new Color(0.45f, 0.72f, 1f),
                _ => Color.white
            };
            capsuleImage.color = color;
            sequence = StartCoroutine(PlaySequence());
        }

        private IEnumerator PlaySequence()
        {
            ShopInputModeManager.Push(this, ShopInputMode.UI);
            group.blocksRaycasts = true;
            float elapsed = 0f;
            const float revealDuration = 2.2f;
            while (elapsed < revealDuration && !skipRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / revealDuration);
                group.alpha = Mathf.Clamp01(progress * 4f) * Mathf.Clamp01((1f - progress) * 5f);
                float open = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.62f, progress));
                capsule.localScale = Vector3.one * Mathf.Lerp(0.72f, 1.12f, progress) *
                                     (1f + Mathf.Sin(progress * Mathf.PI * 8f) * (1f - open) * 0.06f);
                capsule.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-8f, 8f, Mathf.Sin(progress * 18f)));
                productText.canvasRenderer.SetAlpha(open);
                rarityText.canvasRenderer.SetAlpha(open);
                yield return null;
            }
            group.alpha = 0f;
            group.blocksRaycasts = false;
            capsule.localScale = Vector3.one;
            capsule.localRotation = Quaternion.identity;
            ShopInputModeManager.Pop(this);
            sequence = null;
        }

        private void BuildUi()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40000;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            group = canvasObject.GetComponent<CanvasGroup>();

            GameObject shade = CreateImage("배경", canvasObject.transform, null,
                new Color(0.01f, 0.02f, 0.04f, 0.76f), Vector2.zero);
            RectTransform shadeRect = shade.GetComponent<RectTransform>();
            shadeRect.anchorMin = Vector2.zero;
            shadeRect.anchorMax = Vector2.one;
            shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;

            GameObject capsuleObject = CreateImage("캡슐", shade.transform, CreateCapsuleSprite(),
                Color.white, new Vector2(310f, 262f));
            capsule = capsuleObject.GetComponent<RectTransform>();
            capsule.anchorMin = capsule.anchorMax = capsule.pivot = new Vector2(0.5f, 0.5f);
            capsule.anchoredPosition = new Vector2(0f, 70f);
            capsuleImage = capsuleObject.GetComponent<Image>();

            rarityText = CreateText("희귀도", shade.transform, "캡슐", 38,
                new Vector2(700f, 56f), new Vector2(0f, -135f));
            productText = CreateText("상품명", shade.transform, "상품", 52,
                new Vector2(1000f, 82f), new Vector2(0f, -205f));
            skipText = CreateText("건너뛰기", shade.transform, "Space / Esc · 건너뛰기", 20,
                new Vector2(500f, 40f), new Vector2(0f, -310f));
            skipText.color = new Color(0.78f, 0.84f, 0.92f);
        }

        private static GameObject CreateImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
        {
            GameObject item = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            Image image = item.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            return item;
        }

        private static Text CreateText(string name, Transform parent, string value, int size,
            Vector2 dimensions, Vector2 position)
        {
            GameObject item = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text),
                typeof(Outline));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = dimensions;
            rect.anchoredPosition = position;
            Text text = item.GetComponent<Text>();
            text.font = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, size);
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            Outline outline = item.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(3f, -3f);
            return text;
        }

        private static Sprite CreateCapsuleSprite()
        {
            const int width = 192;
            const int height = 160;
            Texture2D texture = new(width, height, TextureFormat.RGBA32, false)
            {
                name = "RuntimeCapsuleSprite",
                filterMode = FilterMode.Bilinear
            };
            Color[] pixels = new Color[width * height];
            Vector2 center = new(width * 0.5f, height * 0.5f);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Vector2 normalized = new((x - center.x) / (width * 0.46f),
                    (y - center.y) / (height * 0.43f));
                float distance = normalized.sqrMagnitude;
                if (distance > 1f) pixels[y * width + x] = Color.clear;
                else if (Mathf.Abs(y - center.y) <= 3f) pixels[y * width + x] = new Color(0.08f, 0.1f, 0.14f, 1f);
                else pixels[y * width + x] = Color.white;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
