using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/World Config", fileName = "ShopWorldConfig")]
    public sealed class ShopWorldConfig : ScriptableObject
    {
        public const string ResourcePath = "World/ShopWorldConfig";

        [Header("Fall recovery")]
        [SerializeField] private float fallRecoveryHeight = -10f;
        [Min(0f)] [SerializeField] private float safePointHeightOffset = 0.2f;
        [Min(0.05f)] [SerializeField] private float safetyPollInterval = 0.15f;
        [Min(0f)] [SerializeField] private float recoveryFadeSeconds = 0.28f;

        [Header("Street")]
        [Range(0, 12)] [SerializeField] private int maximumPedestrians = 6;
        [Min(0.1f)] [SerializeField] private float pedestrianWalkSpeed = 1.45f;

        public float FallRecoveryHeight => fallRecoveryHeight;
        public float SafePointHeightOffset => safePointHeightOffset;
        public float SafetyPollInterval => Mathf.Max(0.05f, safetyPollInterval);
        public float RecoveryFadeSeconds => Mathf.Max(0f, recoveryFadeSeconds);
        public int MaximumPedestrians => Mathf.Clamp(maximumPedestrians, 0, 12);
        public float PedestrianWalkSpeed => Mathf.Max(0.1f, pedestrianWalkSpeed);

        public static ShopWorldConfig Load() => Resources.Load<ShopWorldConfig>(ResourcePath);
    }
}
