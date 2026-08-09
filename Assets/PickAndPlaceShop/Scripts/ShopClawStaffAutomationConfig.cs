using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Claw Staff Automation Config",
        fileName = "ShopClawStaffAutomationConfig")]
    public sealed class ShopClawStaffAutomationConfig : ScriptableObject
    {
        private const string ResourcePath = "Operations/ShopClawStaffAutomationConfig";

        [Header("Physical pilot")]
        [SerializeField, Min(0.03f)] private float targetArrivalTolerance = 0.1f;
        [SerializeField, Min(5f)] private float maximumCycleSeconds = 45f;
        [SerializeField, Min(0f)] private float cooldownObservationSeconds = 1f;
        [SerializeField, Range(0.01f, 1f)] private float minimumLegacySuccessRate = 0.05f;
        [SerializeField, Min(0f)] private float exposedHeightWeight = 2f;
        [SerializeField, Min(0f)] private float chuteDistanceWeight = 0.35f;
        [SerializeField, Range(0.01f, 1f)] private float measuredPhysicalSuccessRate = 0.144f;

        [Header("Operator animation")]
        [SerializeField, Min(0.1f)] private float armCycleFrequency = 4.2f;
        [SerializeField, Range(0f, 45f)] private float movingArmAngle = 12f;
        [SerializeField, Range(0f, 60f)] private float captureArmAngle = 28f;

        public float TargetArrivalTolerance => Mathf.Max(0.03f, targetArrivalTolerance);
        public float MaximumCycleSeconds => Mathf.Max(5f, maximumCycleSeconds);
        public float CooldownObservationSeconds => Mathf.Max(0f, cooldownObservationSeconds);
        public float ArmCycleFrequency => Mathf.Max(0.1f, armCycleFrequency);
        public float MovingArmAngle => Mathf.Clamp(movingArmAngle, 0f, 45f);
        public float CaptureArmAngle => Mathf.Clamp(captureArmAngle, 0f, 60f);
        public float ExposedHeightWeight => Mathf.Max(0f, exposedHeightWeight);
        public float ChuteDistanceWeight => Mathf.Max(0f, chuteDistanceWeight);

        public float BalancedPassiveCycleSeconds(float legacyAttemptInterval, float legacySuccessRate)
        {
            float legacyRate = Mathf.Clamp(legacySuccessRate, minimumLegacySuccessRate, 1f);
            return Mathf.Max(0f, legacyAttemptInterval) * measuredPhysicalSuccessRate / legacyRate;
        }

        public static ShopClawStaffAutomationConfig Load() =>
            Resources.Load<ShopClawStaffAutomationConfig>(ResourcePath);
    }
}
