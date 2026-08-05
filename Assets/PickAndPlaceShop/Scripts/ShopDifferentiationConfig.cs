using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Differentiation Config",
        fileName = "ShopDifferentiationConfig")]
    public sealed class ShopDifferentiationConfig : ScriptableObject
    {
        private const string ResourcePath = "ShopDifferentiationConfig";

        [Header("빈 캡슐 회수함")]
        [SerializeField] private ShopProductDefinition emptyCapsuleProduct;
        [SerializeField, Min(1)] private int capsuleRecyclerSlots = 10;
        [SerializeField] private int[] upcycleThresholds = { 20, 50, 100 };
        [SerializeField] private Vector3 capsuleRecyclerPosition = new(12.7f, 0f, 3.2f);

        [Header("쿠지 라스트원상")]
        [SerializeField, Min(1)] private int kujiSetSize = 40;
        [SerializeField] private Vector2 kujiRefillSeconds = new(5f, 8f);
        [SerializeField] private int[] lastOneTitleThresholds = { 1, 10, 30, 100 };

        [Header("단골 발도장 카드")]
        [SerializeField, Min(2)] private int vipPurchaseThreshold = 6;
        [SerializeField, Min(1)] private int visibleStampCardCount = 8;

        [Header("리뷰 게시판")]
        [SerializeField, Min(1)] private int reviewHistoryCapacity = 5;
        [SerializeField] private Vector3 reviewBoardPosition = new(1.25f, 0f, -3.8f);

        [Header("굿즈 감정소")]
        [SerializeField] private Vector3 appraisalPosition = new(12.7f, 0f, 0.2f);
        [SerializeField] private int[] appraisalFees = { 20, 40, 80, 140 };
        [SerializeField] private float[] appraisalPriceMultipliers = { 1f, 1.05f, 1.2f, 1.4f };
        [SerializeField] private float[] appraisalCurationMultipliers = { 1f, 1.02f, 1.08f, 1.15f };
        [SerializeField] private Vector4[] appraisalGradeWeights =
        {
            new(60f, 28f, 10f, 2f),
            new(45f, 32f, 18f, 5f),
            new(25f, 35f, 28f, 12f),
            new(10f, 25f, 40f, 25f)
        };

        [Header("위탁 판매")]
        [SerializeField] private Vector3 consignmentPosition = new(12.7f, 0f, 6.1f);
        [SerializeField, Min(1f)] private float consignmentVisitSeconds = 90f;
        [SerializeField, Min(5f)] private float consignmentOfferDurationSeconds = 45f;
        [SerializeField, Min(1)] private int consignmentSlots = 3;
        [SerializeField] private Vector2Int consignmentOfferCount = new(1, 3);
        [SerializeField, Range(0f, 1f)] private float missingCollectionWeight = 0.75f;
        [SerializeField, Min(1f)] private float consignmentPriceMultiplier = 1.35f;
        [SerializeField] private int[] consignmentMachineAverageCosts = { 100, 130, 170, 230 };
        [SerializeField, Min(0)] private int consignmentUnlockReputation = 30;

        [Header("진열 큐레이션")]
        [SerializeField] private Vector2 idealDensityPercent = new(30f, 90f);
        [SerializeField] private int automaticLayoutScore = 45;
        [SerializeField] private float shelfPlacementRotationSpeed = 90f;
        [SerializeField, Min(1)] private int maximumCurationPlacements = 30;
        [SerializeField] private Vector4 curationScoreWeights = new(0.25f, 0.25f, 0.25f, 0.25f);
        [SerializeField] private Vector3Int curationGradeThresholds = new(45, 65, 85);
        [SerializeField] private Vector3 curationDeskPosition = new(10.5f, 0f, 7.2f);

        public ShopProductDefinition EmptyCapsuleProduct => emptyCapsuleProduct;
        public int CapsuleRecyclerSlots => Mathf.Max(1, capsuleRecyclerSlots);
        public int[] UpcycleThresholds => upcycleThresholds ?? System.Array.Empty<int>();
        public Vector3 CapsuleRecyclerPosition => capsuleRecyclerPosition;
        public int KujiSetSize => Mathf.Max(1, kujiSetSize);
        public Vector2 KujiRefillSeconds => new(Mathf.Max(0.1f, kujiRefillSeconds.x),
            Mathf.Max(kujiRefillSeconds.x, kujiRefillSeconds.y));
        public int[] LastOneTitleThresholds => lastOneTitleThresholds ?? System.Array.Empty<int>();
        public int VipPurchaseThreshold => Mathf.Max(2, vipPurchaseThreshold);
        public int VisibleStampCardCount => Mathf.Max(1, visibleStampCardCount);
        public int ReviewHistoryCapacity => Mathf.Max(1, reviewHistoryCapacity);
        public Vector3 ReviewBoardPosition => reviewBoardPosition;
        public Vector3 AppraisalPosition => appraisalPosition;
        public int AppraisalFee(ShopProductRarity rarity) => ValueAt(appraisalFees, (int)rarity, 20);
        public float AppraisalPriceMultiplier(ShopAppraisalGrade grade) =>
            ValueAt(appraisalPriceMultipliers, Mathf.Max(0, (int)grade - 1), 1f);
        public float AppraisalCurationMultiplier(ShopAppraisalGrade grade) =>
            ValueAt(appraisalCurationMultipliers, Mathf.Max(0, (int)grade - 1), 1f);
        public ShopAppraisalGrade AppraisalGradeFor(ShopProductRarity rarity, float roll01)
        {
            int count = appraisalGradeWeights != null ? appraisalGradeWeights.Length : 0;
            int index = Mathf.Clamp((int)rarity, 0, Mathf.Max(0, count - 1));
            Vector4 weights = appraisalGradeWeights != null && appraisalGradeWeights.Length > 0
                ? appraisalGradeWeights[index] : new Vector4(60f, 28f, 10f, 2f);
            float total = Mathf.Max(0f, weights.x) + Mathf.Max(0f, weights.y) +
                          Mathf.Max(0f, weights.z) + Mathf.Max(0f, weights.w);
            float roll = Mathf.Clamp01(roll01) * Mathf.Max(0.001f, total);
            if ((roll -= Mathf.Max(0f, weights.x)) <= 0f) return ShopAppraisalGrade.C;
            if ((roll -= Mathf.Max(0f, weights.y)) <= 0f) return ShopAppraisalGrade.B;
            if ((roll -= Mathf.Max(0f, weights.z)) <= 0f) return ShopAppraisalGrade.A;
            return ShopAppraisalGrade.S;
        }
        public Vector3 ConsignmentPosition => consignmentPosition;
        public float ConsignmentVisitSeconds => Mathf.Max(1f, consignmentVisitSeconds);
        public float ConsignmentOfferDurationSeconds => Mathf.Max(5f, consignmentOfferDurationSeconds);
        public int ConsignmentSlots => Mathf.Max(1, consignmentSlots);
        public Vector2Int ConsignmentOfferCount => new(Mathf.Max(1, consignmentOfferCount.x),
            Mathf.Max(consignmentOfferCount.x, consignmentOfferCount.y));
        public float MissingCollectionWeight => Mathf.Clamp01(missingCollectionWeight);
        public float ConsignmentPriceMultiplier => Mathf.Max(1f, consignmentPriceMultiplier);
        public int ConsignmentUnlockReputation => Mathf.Max(0, consignmentUnlockReputation);
        public int ConsignmentPrice(ShopProductRarity rarity) => Mathf.CeilToInt(
            ValueAt(consignmentMachineAverageCosts, (int)rarity, 100) * ConsignmentPriceMultiplier);
        public Vector2 IdealDensityPercent => idealDensityPercent;
        public int AutomaticLayoutScore => Mathf.Clamp(automaticLayoutScore, 0, 100);
        public float ShelfPlacementRotationSpeed => Mathf.Max(1f, shelfPlacementRotationSpeed);
        public int MaximumCurationPlacements => Mathf.Max(1, maximumCurationPlacements);
        public Vector4 CurationScoreWeights => curationScoreWeights;
        public string CurationGrade(int score) => score >= curationGradeThresholds.z ? "S"
            : score >= curationGradeThresholds.y ? "A"
            : score >= curationGradeThresholds.x ? "B" : "C";
        public Vector3 CurationDeskPosition => curationDeskPosition;

        public static ShopDifferentiationConfig Load() => Resources.Load<ShopDifferentiationConfig>(ResourcePath);

        private static int ValueAt(int[] values, int index, int fallback) =>
            values != null && index >= 0 && index < values.Length ? Mathf.Max(0, values[index]) : fallback;

        private static float ValueAt(float[] values, int index, float fallback) =>
            values != null && index >= 0 && index < values.Length ? Mathf.Max(0f, values[index]) : fallback;

#if UNITY_EDITOR
        public void EditorSetEmptyCapsuleProduct(ShopProductDefinition product) => emptyCapsuleProduct = product;
#endif
    }
}
