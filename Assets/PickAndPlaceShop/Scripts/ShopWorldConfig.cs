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
        [Range(0f, 0.45f)] [SerializeField] private float pedestrianSpeedVariance = 0.2f;
        [Min(0f)] [SerializeField] private float pedestrianSpawnStagger = 0.65f;
        [Range(0f, 1f)] [SerializeField] private float pedestrianPauseChance = 0.28f;
        [SerializeField] private Vector2 pedestrianPauseSeconds = new(0.4f, 1.4f);
        [Min(0.5f)] [SerializeField] private float pedestrianLaneSpacing = 1.45f;

        public float FallRecoveryHeight => fallRecoveryHeight;
        public float SafePointHeightOffset => safePointHeightOffset;
        public float SafetyPollInterval => Mathf.Max(0.05f, safetyPollInterval);
        public float RecoveryFadeSeconds => Mathf.Max(0f, recoveryFadeSeconds);
        public int MaximumPedestrians => Mathf.Clamp(maximumPedestrians, 0, 12);
        public float PedestrianWalkSpeed => Mathf.Max(0.1f, pedestrianWalkSpeed);
        public float PedestrianSpeedVariance => Mathf.Clamp(pedestrianSpeedVariance, 0f, 0.45f);
        public float PedestrianSpawnStagger => Mathf.Max(0f, pedestrianSpawnStagger);
        public float PedestrianPauseChance => Mathf.Clamp01(pedestrianPauseChance);
        public Vector2 PedestrianPauseSeconds => new(
            Mathf.Max(0f, Mathf.Min(pedestrianPauseSeconds.x, pedestrianPauseSeconds.y)),
            Mathf.Max(0f, Mathf.Max(pedestrianPauseSeconds.x, pedestrianPauseSeconds.y)));
        public float PedestrianLaneSpacing => Mathf.Max(0.5f, pedestrianLaneSpacing);

        public static ShopWorldConfig Load() => Resources.Load<ShopWorldConfig>(ResourcePath);
    }
}
