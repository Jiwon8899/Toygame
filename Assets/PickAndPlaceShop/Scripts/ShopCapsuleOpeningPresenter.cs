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
        private Image productIcon;
        private Image resultGlow;
        private Text sourceText;
        private Text rarityText;
        private Text productText;
        private Text categoryText;
        private Text storageText;
        private Text closeText;
        private Coroutine sequence;
        private bool closeRequested;
        private ShopProductRarity presentedRarity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        public static void Show(string productName, ShopProductRarity rarity, Color capsuleColor)
        {
            EnsureInstance();
            instance.Begin("뽑기 결과", productName, "분류 정보 없음", rarity, capsuleColor,
                "획득한 상품을 보관했습니다.");
        }

        public static void Show(string sourceLabel, ShopProductDefinition product, Color accentColor,
            string storageMessage)
        {
            if (product == null) return;
            EnsureInstance();
            instance.Begin(sourceLabel, product.DisplayName,
                ShopProductLocalization.CategoryLabel(product.Category), product.Rarity,
                accentColor, storageMessage);
        }

        private static void EnsureInstance()
        {
            if (instance != null) return;
            GameObject host = new("[Arcade] Shared Result Presenter");
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
            Hide();
        }

        private void Update()
        {
            if (group == null || group.alpha <= 0f) return;
            if (Keyboard.current != null &&
                (Keyboard.current.eKey.wasPressedThisFrame ||
                 Keyboard.current.spaceKey.wasPressedThisFrame ||
                 Keyboard.current.escapeKey.wasPressedThisFrame))
                closeRequested = true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                closeRequested = true;
            if (Gamepad.current != null &&
                (Gamepad.current.buttonSouth.wasPressedThisFrame ||
                 Gamepad.current.buttonEast.wasPressedThisFrame))
                closeRequested = true;
        }

        private void Begin(string sourceLabel, string productName, string category,
            ShopProductRarity rarity, Color color, string storageMessage)
        {
            if (sequence != null) StopCoroutine(sequence);
            ShopInputModeManager.Pop(this);
            closeRequested = false;
            presentedRarity = rarity;
            sourceText.text = sourceLabel;
            productText.text = productName;
            categoryText.text = "카테고리 · " + category;
            storageText.text = storageMessage;
            rarityText.text = ShopProductLocalization.RarityLabel(rarity);
            rarityText.color = RarityColor(rarity);
            resultGlow.color = new Color(rarityText.color.r, rarityText.color.g, rarityText.color.b,
                rarity >= ShopProductRarity.Rare ? 0.34f : 0.16f);
            capsuleImage.color = color;
            ShopProductDefinition product = ShopProductVisuals.FindByName(productName);
            productIcon.sprite = product != null ? product.Icon : null;
            productIcon.color = productIcon.sprite != null ? Color.white : Color.clear;
            sequence = StartCoroutine(PlaySequence());
        }

        private IEnumerator PlaySequence()
        {
            ShopInputModeManager.Push(this, ShopInputMode.UI);
            group.blocksRaycasts = true;
            group.interactable = true;
            productText.canvasRenderer.SetAlpha(0f);
            rarityText.canvasRenderer.SetAlpha(0f);
            categoryText.canvasRenderer.SetAlpha(0f);
            storageText.canvasRenderer.SetAlpha(0f);
            productIcon.canvasRenderer.SetAlpha(0f);

            float elapsed = 0f;
            const float revealDuration = 1.15f;
            while (elapsed < revealDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / revealDuration);
                group.alpha = Mathf.Clamp01(progress * 4f);
                float open = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.62f, progress));
                capsule.localScale = Vector3.one * Mathf.Lerp(0.72f, 1.12f, progress) *
                                     (1f + Mathf.Sin(progress * Mathf.PI * 8f) * (1f - open) * 0.06f);
                capsule.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Lerp(-8f, 8f, Mathf.Sin(progress * 18f)));
                resultGlow.rectTransform.localScale = Vector3.one *
                    (1f + Mathf.Sin(progress * Mathf.PI * 5f) *
                     (presentedRarity >= ShopProductRarity.Rare ? 0.12f : 0.04f));
                productText.canvasRenderer.SetAlpha(open);
                rarityText.canvasRenderer.SetAlpha(open);
                categoryText.canvasRenderer.SetAlpha(open);
                storageText.canvasRenderer.SetAlpha(open);
                productIcon.canvasRenderer.SetAlpha(open);
                yield return null;
            }

            group.alpha = 1f;
            while (!closeRequested)
            {
                float pulse = presentedRarity >= ShopProductRarity.Rare
                    ? 1f + Mathf.Sin(Time.unscaledTime * 4.5f) * 0.045f
                    : 1f;
                resultGlow.rectTransform.localScale = Vector3.one * pulse;
                yield return null;
            }

            Hide();
            capsule.localScale = Vector3.one;
            capsule.localRotation = Quaternion.identity;
            ShopInputModeManager.Pop(this);
            sequence = null;
        }

        private void Hide()
        {
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
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
                new Color(0.01f, 0.02f, 0.04f, 0.82f), Vector2.zero);
            RectTransform shadeRect = shade.GetComponent<RectTransform>();
            shadeRect.anchorMin = Vector2.zero;
            shadeRect.anchorMax = Vector2.one;
            shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;

            resultGlow = CreateImage("결과 광원", shade.transform, null,
                new Color(1f, 1f, 1f, 0.2f), new Vector2(520f, 420f)).GetComponent<Image>();
            resultGlow.rectTransform.anchorMin = resultGlow.rectTransform.anchorMax =
                resultGlow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            resultGlow.rectTransform.anchoredPosition = new Vector2(0f, 45f);

            GameObject capsuleObject = CreateImage("상품 이미지 자리", shade.transform, CreateCapsuleSprite(),
                Color.white, new Vector2(310f, 262f));
            capsule = capsuleObject.GetComponent<RectTransform>();
            capsule.anchorMin = capsule.anchorMax = capsule.pivot = new Vector2(0.5f, 0.5f);
            capsule.anchoredPosition = new Vector2(0f, 70f);
            capsuleImage = capsuleObject.GetComponent<Image>();

            GameObject iconObject = CreateImage("Product Icon", shade.transform, null,
                Color.clear, new Vector2(250f, 250f));
            productIcon = iconObject.GetComponent<Image>();
            productIcon.preserveAspect = true;
            productIcon.rectTransform.anchorMin = productIcon.rectTransform.anchorMax =
                productIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            productIcon.rectTransform.anchoredPosition = new Vector2(0f, 70f);

            sourceText = CreateText("결과 제목", shade.transform, "뽑기 결과", 34,
                new Vector2(700f, 52f), new Vector2(0f, 300f));
            rarityText = CreateText("희귀도", shade.transform, "일반", 38,
                new Vector2(700f, 56f), new Vector2(0f, -105f));
            productText = CreateText("상품명", shade.transform, "상품", 52,
                new Vector2(1000f, 82f), new Vector2(0f, -170f));
            categoryText = CreateText("카테고리", shade.transform, "카테고리", 26,
                new Vector2(900f, 44f), new Vector2(0f, -225f));
            storageText = CreateText("보관 결과", shade.transform, "가방에 넣었습니다.", 24,
                new Vector2(1100f, 46f), new Vector2(0f, -275f));
            storageText.color = new Color(0.64f, 1f, 0.82f);
            closeText = CreateText("닫기 안내", shade.transform, "클릭 또는 E · 닫기", 20,
                new Vector2(500f, 40f), new Vector2(0f, -340f));
            closeText.color = new Color(0.78f, 0.84f, 0.92f);
        }

        private static Color RarityColor(ShopProductRarity rarity) => rarity switch
        {
            ShopProductRarity.UltraRare => new Color(1f, 0.72f, 0.12f),
            ShopProductRarity.Rare => new Color(0.78f, 0.46f, 1f),
            ShopProductRarity.Uncommon => new Color(0.35f, 0.65f, 1f),
            _ => Color.white
        };

        private static GameObject CreateImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
        {
            GameObject item = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            Image image = item.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
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
                else if (Mathf.Abs(y - center.y) <= 3f)
                    pixels[y * width + x] = new Color(0.08f, 0.1f, 0.14f, 1f);
                else pixels[y * width + x] = Color.white;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
