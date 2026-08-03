using UnityEngine;

namespace PickAndPlaceShop
{
    public enum ShopStaffRole
    {
        Cashier,
        Stocker,
        Collector
    }

    [CreateAssetMenu(menuName = "Pick And Place Shop/Workforce Config", fileName = "ShopWorkforceConfig")]
    public sealed class ShopWorkforceConfig : ScriptableObject
    {
        public const string ResourcePath = "Operations/ShopWorkforceConfig";

        [Header("Hiring and daily payroll")]
        [SerializeField] private int[] hireCosts = { 600, 800, 1000 };
        [SerializeField] private int[] dailyWages = { 80, 100, 120 };

        [Header("Work balance")]
        [Min(1f)] [SerializeField] private float cashierDurationMultiplier = 1.5f;
        [Min(0.25f)] [SerializeField] private float stockerWorkInterval = 4f;
        [Min(0.25f)] [SerializeField] private float collectorWorkInterval = 5f;
        [Min(0.1f)] [SerializeField] private float walkSpeed = 1.45f;
        [Min(0.1f)] [SerializeField] private float workReachDistance = 0.8f;

        [Header("Visible staff appearance pool")]
        [SerializeField] private GameObject[] appearancePrefabs;

        public float CashierDurationMultiplier => Mathf.Max(1f, cashierDurationMultiplier);
        public float StockerWorkInterval => Mathf.Max(0.25f, stockerWorkInterval);
        public float CollectorWorkInterval => Mathf.Max(0.25f, collectorWorkInterval);
        public float WalkSpeed => Mathf.Max(0.1f, walkSpeed);
        public float WorkReachDistance => Mathf.Max(0.1f, workReachDistance);
        public GameObject[] AppearancePrefabs => appearancePrefabs;

        public int HireCost(ShopStaffRole role) => ValueAt(hireCosts, role);
        public int DailyWage(ShopStaffRole role) => ValueAt(dailyWages, role);

        private static int ValueAt(int[] values, ShopStaffRole role)
        {
            int index = (int)role;
            return values != null && index >= 0 && index < values.Length ? Mathf.Max(0, values[index]) : 0;
        }

        public static ShopWorkforceConfig Load() => Resources.Load<ShopWorkforceConfig>(ResourcePath);
    }
}
