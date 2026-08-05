using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(800)]
    public sealed class ShopCapsuleOpeningPresenter : MonoBehaviour
    {
        private sealed class ResultItem
        {
            public string Source;
            public ShopProductDefinition Product;
            public Color Accent;
            public string Storage;
            public int Order;
        }

        private static ShopCapsuleOpeningPresenter instance;
        private readonly List<ResultItem> pending = new();
        private readonly List<RectTransform> rareCards = new();
        private CanvasGroup group;
        private RectTransform cardsRoot;
        private GridLayoutGroup grid;
        private Text sourceText;
        private Text countText;
        private Coroutine sequence;
        private Coroutine batchDelay;
        private bool closeRequested;
        private int enqueueOrder;
        private float lastEnqueueTime;
        private Sprite rarityCircle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        public static void Show(string productName, ShopProductRarity rarity, Color capsuleColor)
        {
            ShopProductDefinition product = ShopProductVisuals.FindByName(productName);
            if (product == null) return;
            Show("뽑기 결과", product, capsuleColor, "개인 인벤토리에 보관했습니다.");
        }

        public static void Show(string sourceLabel, ShopProductDefinition product, Color accentColor,
            string storageMessage)
        {
            if (product == null) return;
            EnsureInstance();
            instance.Enqueue(sourceLabel, product, accentColor, storageMessage);
        }

        public static void ShowBatch(string sourceLabel, IEnumerable<ShopProductDefinition> products,
            Color accentColor, string storageMessage)
        {
            if (products == null) return;
            EnsureInstance();
            foreach (ShopProductDefinition product in products)
                if (product != null) instance.Enqueue(sourceLabel, product, accentColor, storageMessage);
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
                (Keyboard.current.eKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame ||
                 Keyboard.current.escapeKey.wasPressedThisFrame)) closeRequested = true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) closeRequested = true;
            if (Gamepad.current != null &&
                (Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.buttonEast.wasPressedThisFrame))
                closeRequested = true;
        }

        private void Enqueue(string sourceLabel, ShopProductDefinition product, Color accentColor,
            string storageMessage)
        {
            pending.Add(new ResultItem
            {
                Source = sourceLabel,
                Product = product,
                Accent = accentColor,
                Storage = StorageLabel(storageMessage),
                Order = enqueueOrder++
            });
            lastEnqueueTime = Time.unscaledTime;
            if (batchDelay == null) batchDelay = StartCoroutine(WaitForBatch());
        }

        private IEnumerator WaitForBatch()
        {
            do
            {
                float observed = lastEnqueueTime;
                yield return new WaitForSecondsRealtime(0.3f);
                if (Mathf.Approximately(observed, lastEnqueueTime)) break;
            } while (true);
            batchDelay = null;
            if (sequence == null) BeginPendingBatch();
        }

        private void BeginPendingBatch()
        {
            if (pending.Count == 0) return;
            List<ResultItem> batch = pending
                .OrderBy(item => item.Product.Rarity >= ShopProductRarity.Rare ? 1 : 0)
                .ThenBy(item => item.Order)
                .ToList();
            pending.Clear();
            BuildCards(batch);
            closeRequested = false;
            sequence = StartCoroutine(PlaySequence(batch.Count));
        }

        private void BuildCards(IReadOnlyList<ResultItem> batch)
        {
            for (int i = cardsRoot.childCount - 1; i >= 0; i--) Destroy(cardsRoot.GetChild(i).gameObject);
            rareCards.Clear();
            int columns = Mathf.Min(5, Mathf.Max(1, batch.Count));
            int rows = Mathf.CeilToInt(batch.Count / (float)columns);
            Vector2 cell = batch.Count == 1 ? new Vector2(430f, 500f) :
                batch.Count <= 3 ? new Vector2(360f, 440f) : new Vector2(292f, 390f);
            grid.constraintCount = columns;
            grid.cellSize = cell;
            grid.spacing = new Vector2(18f, 18f);
            cardsRoot.sizeDelta = new Vector2(columns * cell.x + (columns - 1) * grid.spacing.x,
                rows * cell.y + (rows - 1) * grid.spacing.y);
            sourceText.text = batch.Select(item => item.Source).Distinct().Count() == 1
                ? batch[0].Source : "획득 결과";
            countText.text = batch.Count + "개 획득";
            for (int i = 0; i < batch.Count; i++) CreateCard(batch[i], i);
        }

        private void CreateCard(ResultItem item, int index)
        {
            Color rarity = RarityColor(item.Product.Rarity);
            GameObject cardObject = CreateImage("ResultCard_" + index, cardsRoot, null,
                new Color(0.035f, 0.065f, 0.09f, 0.97f), Vector2.zero);
            Outline border = cardObject.AddComponent<Outline>();
            border.effectColor = new Color(rarity.r, rarity.g, rarity.b, 0.95f);
            border.effectDistance = item.Product.Rarity >= ShopProductRarity.Rare
                ? new Vector2(5f, -5f) : new Vector2(3f, -3f);
            RectTransform card = cardObject.GetComponent<RectTransform>();
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            if (item.Product.Rarity >= ShopProductRarity.Rare) rareCards.Add(card);

            Image glow = CreateImage("희귀도 배경", card, rarityCircle,
                new Color(rarity.r, rarity.g, rarity.b, 0.34f), new Vector2(255f, 255f)).GetComponent<Image>();
            glow.rectTransform.anchorMin = glow.rectTransform.anchorMax = glow.rectTransform.pivot =
                new Vector2(0.5f, 0.5f);
            glow.rectTransform.anchoredPosition = new Vector2(0f, 72f);

            Image icon = CreateImage("상품 아이콘", card, item.Product.Icon, Color.white,
                new Vector2(225f, 225f)).GetComponent<Image>();
            icon.preserveAspect = true;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot =
                new Vector2(0.5f, 0.5f);
            icon.rectTransform.anchoredPosition = new Vector2(0f, 76f);

            Text rarityText = CreateText("희귀도", card, ShopProductLocalization.RarityLabel(item.Product.Rarity),
                27, new Vector2(260f, 38f), new Vector2(0f, -80f));
            rarityText.color = rarity;
            CreateText("상품명", card, item.Product.DisplayName, 29,
                new Vector2(270f, 70f), new Vector2(0f, -132f));
            string category = ShopProductLocalization.CategoryLabel(item.Product.Category);
            if (!string.IsNullOrWhiteSpace(category) && !category.Contains("정보 없음"))
            {
                Text categoryText = CreateText("카테고리", card, category, 20,
                    new Vector2(270f, 32f), new Vector2(0f, -178f));
                categoryText.color = new Color(0.72f, 0.82f, 0.9f);
            }
            Text storage = CreateText("보관 결과", card, item.Storage, 18,
                new Vector2(270f, 42f), new Vector2(0f, -218f));
            storage.color = item.Storage.Contains("창고")
                ? new Color(1f, 0.72f, 0.28f) : new Color(0.55f, 1f, 0.78f);
        }

        private IEnumerator PlaySequence(int cardCount)
        {
            ShopInputModeManager.Push(this, ShopInputMode.UI);
            group.blocksRaycasts = true;
            group.interactable = true;
            group.alpha = 0f;
            cardsRoot.localScale = Vector3.one * 0.82f;
            float elapsed = 0f;
            while (elapsed < 0.55f)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / 0.55f);
                group.alpha = Mathf.SmoothStep(0f, 1f, progress);
                cardsRoot.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, Mathf.SmoothStep(0f, 1f, progress));
                yield return null;
            }
            group.alpha = 1f;
            while (!closeRequested)
            {
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 5f) * 0.035f;
                for (int i = 0; i < rareCards.Count; i++)
                    if (rareCards[i] != null) rareCards[i].localScale = Vector3.one * pulse;
                yield return null;
            }
            Hide();
            ShopInputModeManager.Pop(this);
            sequence = null;
            if (pending.Count > 0) BeginPendingBatch();
        }

        private void Hide()
        {
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        private void BuildUi()
        {
            rarityCircle = CreateGradientCircleSprite();
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
                new Color(0.005f, 0.015f, 0.025f, 0.9f), Vector2.zero);
            RectTransform shadeRect = shade.GetComponent<RectTransform>();
            shadeRect.anchorMin = Vector2.zero;
            shadeRect.anchorMax = Vector2.one;
            shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
            sourceText = CreateText("결과 제목", shade.transform, "획득 결과", 42,
                new Vector2(900f, 62f), new Vector2(0f, 455f));
            countText = CreateText("획득 개수", shade.transform, "1개 획득", 25,
                new Vector2(500f, 42f), new Vector2(0f, 405f));
            countText.color = new Color(0.62f, 0.94f, 1f);

            GameObject root = new("결과 카드 그리드", typeof(RectTransform), typeof(GridLayoutGroup));
            root.transform.SetParent(shade.transform, false);
            cardsRoot = root.GetComponent<RectTransform>();
            cardsRoot.anchorMin = cardsRoot.anchorMax = cardsRoot.pivot = new Vector2(0.5f, 0.5f);
            cardsRoot.anchoredPosition = new Vector2(0f, -5f);
            grid = root.GetComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.childAlignment = TextAnchor.MiddleCenter;
            CreateText("닫기 안내", shade.transform, "클릭 또는 E · 닫기", 22,
                new Vector2(600f, 44f), new Vector2(0f, -485f)).color = new Color(0.72f, 0.8f, 0.88f);
        }

        private static string StorageLabel(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "개인 인벤토리에 보관했습니다.";
            if (message.Contains("창고")) return "공용 창고에 보관했습니다.";
            if (message.Contains("가방") || message.Contains("개인")) return "개인 인벤토리에 보관했습니다.";
            return message;
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
            item.GetComponent<RectTransform>().sizeDelta = size;
            Image image = item.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return item;
        }

        private static Text CreateText(string name, Transform parent, string value, int size,
            Vector2 dimensions, Vector2 position)
        {
            GameObject item = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = dimensions;
            rect.anchoredPosition = position;
            Text text = item.GetComponent<Text>();
            text.font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, size);
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            Outline outline = item.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        private static Sprite CreateGradientCircleSprite()
        {
            const int size = 128;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            {
                name = "RuntimeRarityCircle", filterMode = FilterMode.Bilinear
            };
            Color[] pixels = new Color[size * size];
            Vector2 center = Vector2.one * (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center) / (size * 0.5f);
                float alpha = Mathf.Clamp01(1f - Mathf.SmoothStep(0.55f, 1f, distance));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
