using System.Collections.Generic;
using UnityEngine;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(520)]
    public sealed class ShopWarehouseStockVisualizer : MonoBehaviour
    {
        private const string ArcadeFloorPath = "PickAndPlaceShop_Generated/Architecture/ArcadeFloor";

        private readonly List<StockEntry> stock = new();
        private static ShopWarehouseStockVisualizer instance;
        private ShopWarehouseVisualConfig config;
        private Transform stackRoot;
        private TextMesh label;
        private int observedSignature;
        private bool hasObservedSignature;
        private float nextRefresh;

        private readonly struct StockEntry
        {
            public readonly ShopProductDefinition Product;
            public readonly int Quantity;

            public StockEntry(ShopProductDefinition product, int quantity)
            {
                Product = product;
                Quantity = quantity;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[World] Warehouse Stock");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopWarehouseStockVisualizer>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            config = ShopWarehouseVisualConfig.Load();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            FaceLabelToCamera();
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + (config != null ? config.RefreshInterval : 0.25f);

            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsSpawned || config == null) return;
            if (stackRoot == null && !CreateStackRoot()) return;

            int signature = ReadSharedStorage(game, stock, out int totalQuantity);
            if (hasObservedSignature && signature == observedSignature) return;
            hasObservedSignature = true;
            observedSignature = signature;
            RebuildProducts(totalQuantity);
        }

        private bool CreateStackRoot()
        {
            GameObject floor = GameObject.Find(ArcadeFloorPath);
            if (floor == null) return false;

            Bounds floorBounds = CalculateBounds(floor);
            Vector2 anchor = config.NormalizedFloorAnchor;
            Vector3 worldAnchor = new(
                Mathf.Lerp(floorBounds.min.x, floorBounds.max.x, anchor.x),
                floorBounds.max.y + 0.02f,
                Mathf.Lerp(floorBounds.min.z, floorBounds.max.z, anchor.y));

            GameObject root = new("Warehouse Product Stock");
            root.transform.SetParent(floor.transform, true);
            root.transform.position = worldAnchor;
            root.AddComponent<ShopInteractable>().Configure(ShopAction.WarehousePickup,
                "창고 상품 1개 가져가기");
            stackRoot = root.transform;
            return true;
        }

        private void RebuildProducts(int totalQuantity)
        {
            for (int i = stackRoot.childCount - 1; i >= 0; i--)
                Destroy(stackRoot.GetChild(i).gameObject);
            label = null;
            if (totalQuantity <= 0 || stock.Count == 0) return;

            int representedPerVisual = config.ItemsRepresentedPerVisual;
            int visibleCount = Mathf.Min(config.MaximumVisibleProducts,
                Mathf.CeilToInt(totalQuantity / (float)representedPerVisual));
            for (int i = 0; i < visibleCount; i++)
            {
                ShopProductDefinition product = SelectProportionalProduct(i, visibleCount, totalQuantity);
                if (product == null) continue;
                CreateProductVisual(product, i);
            }

            CreateLabel(totalQuantity, visibleCount, representedPerVisual);
        }

        private ShopProductDefinition SelectProportionalProduct(int index, int visibleCount, int totalQuantity)
        {
            float sample = (index + 0.5f) * totalQuantity / Mathf.Max(1f, visibleCount);
            int cumulative = 0;
            for (int i = 0; i < stock.Count; i++)
            {
                cumulative += stock[i].Quantity;
                if (sample < cumulative) return stock[i].Product;
            }
            return stock[^1].Product;
        }

        private void CreateProductVisual(ShopProductDefinition product, int index)
        {
            GameObject slot = new($"Stock_{index:00}_{product.ProductId}");
            slot.transform.SetParent(stackRoot, false);

            int rowIndex = index / config.Columns;
            int layer = rowIndex / config.RowsPerLayer;
            int row = rowIndex % config.RowsPerLayer;
            int column = index % config.Columns;
            slot.transform.localPosition = new Vector3(column * config.ColumnSpacing,
                layer * config.LayerSpacing, -row * config.RowSpacing);
            float yaw = Mathf.Lerp(config.YawRange.x, config.YawRange.y,
                Mathf.Repeat(index * 0.618034f, 1f));
            slot.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            GameObject visual = ShopProductVisuals.Instantiate(product, slot.transform);
            if (visual == null) return;
            visual.name = product.DisplayName + " GLB Visual";
            NormalizeAndGround(visual, config.TargetLongestSide);
        }

        private static void NormalizeAndGround(GameObject visual, float targetLongestSide)
        {
            if (!TryCalculateRendererBounds(visual, out Bounds bounds)) return;
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (longest <= 0.0001f) return;

            float scale = targetLongestSide / longest;
            visual.transform.localScale *= scale;
            if (!TryCalculateRendererBounds(visual, out bounds)) return;
            visual.transform.position += Vector3.up * (visual.transform.parent.position.y - bounds.min.y);
        }

        private void CreateLabel(int totalQuantity, int visibleCount, int representedPerVisual)
        {
            GameObject sign = new("WarehouseStockLabel", typeof(TextMesh));
            sign.transform.SetParent(stackRoot, false);
            sign.transform.localPosition = config.LabelOffset;
            label = sign.GetComponent<TextMesh>();
            label.font = ShopUiFonts.Bold;
            label.fontSize = 64;
            label.characterSize = 0.035f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = new Color(1f, 0.86f, 0.5f);

            int representedQuantity = Mathf.Min(totalQuantity, visibleCount * representedPerVisual);
            int hidden = Mathf.Max(0, totalQuantity - representedQuantity);
            label.text = hidden > 0
                ? $"창고 재고 {totalQuantity}개 · 실물 {visibleCount}개\n+{hidden}개 수량 표시 · [E] 하나 가져가기"
                : $"창고 재고 {totalQuantity}개 · 실물 {visibleCount}개\n[E] 하나 가져가기";
            FaceLabelToCamera();
        }

        private void FaceLabelToCamera()
        {
            Camera camera = Camera.main;
            if (label == null || camera == null) return;
            Vector3 awayFromCamera = label.transform.position - camera.transform.position;
            if (awayFromCamera.sqrMagnitude > 0.0001f)
                label.transform.rotation = Quaternion.LookRotation(awayFromCamera.normalized, Vector3.up);
        }

        private static int ReadSharedStorage(ShopNetworkGame game, List<StockEntry> target,
            out int totalQuantity)
        {
            target.Clear();
            totalQuantity = 0;
            Dictionary<int, int> quantities = new();
            for (int i = 0; i < game.ItemContainers.Count; i++)
            {
                ShopContainerItem item = game.ItemContainers[i];
                if (!ShopContainerRules.BelongsTo(item, ShopContainerRules.SharedOwner,
                        ShopContainerKind.SharedStorage) || item.Quantity <= 0) continue;
                quantities.TryGetValue(item.ProductId, out int current);
                quantities[item.ProductId] = current + item.Quantity;
                totalQuantity += item.Quantity;
            }

            List<int> productIds = new(quantities.Keys);
            productIds.Sort();
            unchecked
            {
                int signature = 17;
                for (int i = 0; i < productIds.Count; i++)
                {
                    int productId = productIds[i];
                    int quantity = quantities[productId];
                    signature = signature * 31 + productId;
                    signature = signature * 31 + quantity;
                    ShopProductDefinition product = ShopProductVisuals.Find(productId);
                    if (product != null) target.Add(new StockEntry(product, quantity));
                }
                return signature;
            }
        }

        private static bool TryCalculateRendererBounds(GameObject source, out Bounds bounds)
        {
            Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { bounds = default; return false; }
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        private static Bounds CalculateBounds(GameObject source)
        {
            Collider collider = source.GetComponentInChildren<Collider>();
            if (collider != null) return collider.bounds;
            Renderer renderer = source.GetComponentInChildren<Renderer>();
            return renderer != null ? renderer.bounds : new Bounds(source.transform.position,
                new Vector3(5f, 0.1f, 5f));
        }
    }
}
