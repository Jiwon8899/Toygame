using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Product Visual Config",
        fileName = "ShopProductVisualConfig")]
    public sealed class ShopProductVisualConfig : ScriptableObject
    {
        [Min(0.05f)] [SerializeField] private float targetLongestSide = 0.3f;
        [Range(64, 512)] [SerializeField] private int thumbnailResolution = 256;
        [Min(0.05f)] [SerializeField] private float shelfSpacing = 0.42f;
        [Range(1, 6)] [SerializeField] private int shelfColumns = 5;
        [SerializeField] private Vector3 shelfOffset = new(0f, 0.78f, 0f);
        [SerializeField] private Vector3 shelfRotation = new(0f, 24f, 0f);

        public float TargetLongestSide => targetLongestSide;
        public int ThumbnailResolution => thumbnailResolution;
        public float ShelfSpacing => shelfSpacing;
        public int ShelfColumns => shelfColumns;
        public Vector3 ShelfOffset => shelfOffset;
        public Vector3 ShelfRotation => shelfRotation;
    }
}
