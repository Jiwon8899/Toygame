using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Warehouse Visual Config",
        fileName = "ShopWarehouseVisualConfig")]
    public sealed class ShopWarehouseVisualConfig : ScriptableObject
    {
        private const string ResourcePath = "World/ShopWarehouseVisualConfig";

        [Header("Capacity")]
        [SerializeField, Min(1)] private int itemsRepresentedPerVisual = 1;
        [SerializeField, Min(1)] private int maximumVisibleProducts = 18;
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.25f;

        [Header("ArcadeFloor placement")]
        [SerializeField] private Vector2 normalizedFloorAnchor = new(0.14f, 0.84f);
        [SerializeField, Min(1)] private int columns = 6;
        [SerializeField, Min(0.05f)] private float columnSpacing = 0.58f;
        [SerializeField, Min(0.05f)] private float rowSpacing = 0.54f;
        [SerializeField, Min(0.05f)] private float layerSpacing = 0.42f;
        [SerializeField, Min(1)] private int rowsPerLayer = 2;
        [SerializeField, Min(0.05f)] private float targetLongestSide = 0.42f;
        [SerializeField] private Vector2 yawRange = new(-18f, 18f);

        [Header("Label")]
        [SerializeField] private Vector3 labelOffset = new(1.45f, 1.35f, -0.35f);

        public int ItemsRepresentedPerVisual => Mathf.Max(1, itemsRepresentedPerVisual);
        public int MaximumVisibleProducts => Mathf.Max(1, maximumVisibleProducts);
        public float RefreshInterval => Mathf.Max(0.05f, refreshInterval);
        public Vector2 NormalizedFloorAnchor => new(Mathf.Clamp01(normalizedFloorAnchor.x),
            Mathf.Clamp01(normalizedFloorAnchor.y));
        public int Columns => Mathf.Max(1, columns);
        public float ColumnSpacing => Mathf.Max(0.05f, columnSpacing);
        public float RowSpacing => Mathf.Max(0.05f, rowSpacing);
        public float LayerSpacing => Mathf.Max(0.05f, layerSpacing);
        public int RowsPerLayer => Mathf.Max(1, rowsPerLayer);
        public float TargetLongestSide => Mathf.Max(0.05f, targetLongestSide);
        public Vector2 YawRange => yawRange;
        public Vector3 LabelOffset => labelOffset;

        public static ShopWarehouseVisualConfig Load() =>
            Resources.Load<ShopWarehouseVisualConfig>(ResourcePath);
    }
}
