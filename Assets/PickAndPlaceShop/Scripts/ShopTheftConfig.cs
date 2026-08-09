using UnityEngine;

namespace PickAndPlaceShop
{
    public enum ShopTheftAction : byte
    {
        ClawChute,
        GachaBreak,
        KujiBreak
    }

    [CreateAssetMenu(menuName = "Pick And Place Shop/Theft Config", fileName = "ShopTheftConfig")]
    public sealed class ShopTheftConfig : ScriptableObject
    {
        public const string ResourcePath = "ShopTheftConfig";

        [Header("Attack")]
        [Range(0.04f, 0.2f)] [SerializeField] private float attackMinimumClickInterval = 0.08f;
        [Range(0.2f, 1.5f)] [SerializeField] private float attackReferenceClickInterval = 0.65f;
        [Range(0f, 0.25f)] [SerializeField] private float attackTransitionSeconds = 0.06f;
        [Range(0.5f, 3f)] [SerializeField] private float attackAnimationSpeed = 1.15f;
        [Range(1f, 4f)] [SerializeField] private float attackMaximumAnimationSpeed = 2.8f;
        [Range(0.1f, 1f)] [SerializeField] private float attackMoveMultiplier = 0.4f;
        [Min(0.05f)] [SerializeField] private float attackMoveSlowSeconds = 0.5f;
        [Min(0.2f)] [SerializeField] private float hitRadius = 2.2f;
        [Range(1f, 180f)] [SerializeField] private float hitAngle = 78f;
        [Min(0f)] [SerializeField] private float hitForwardOffset = 0.8f;
        [Min(1)] [SerializeField] private int attackDamage = 15;

        [Header("Claw machine theft")]
        [Min(0.01f)] [SerializeField] private float clawImpulse = 0.72f;
        [Min(0f)] [SerializeField] private float clawVerticalImpulse = 0.18f;
        [Range(0f, 1f)] [SerializeField] private float clawVerticalImpulseMinimumMultiplier = 0.65f;
        [Min(0.1f)] [SerializeField] private float clawTheftWindow = 3f;
        [Min(0.1f)] [SerializeField] private float maximumCapsuleSpeed = 2.7f;
        [Min(0.1f)] [SerializeField] private float maximumCapsuleAngularSpeed = 8f;

        [Header("Machine durability")]
        [Min(1)] [SerializeField] private int gachaDurability = 100;
        [Min(1)] [SerializeField] private int kujiDurability = 150;
        [Min(1f)] [SerializeField] private float brokenRecoverySeconds = 210f;
        [Range(0f, 1f)] [SerializeField] private float theftUncommonChance = 0.12f;
        [Range(0f, 1f)] [SerializeField] private float theftRareChance = 0.015f;
        [Range(0f, 1f)] [SerializeField] private float theftKujiBChance = 0.08f;
        [Range(0f, 1f)] [SerializeField] private float theftKujiAChance = 0.015f;
        [Range(0f, 1f)] [SerializeField] private float theftKujiCChance = 0.455f;
        [Min(0.01f)] [SerializeField] private float damageShakeSeconds = 0.22f;
        [Min(0.1f)] [SerializeField] private float damageShakeFrequency = 85f;
        [Min(0f)] [SerializeField] private float damageShakeDistance = 0.035f;

        [Header("Personal alert")]
        [Min(1f)] [SerializeField] private float maximumAlert = 100f;
        [Min(0f)] [SerializeField] private float clawAlert = 28f;
        [Min(0f)] [SerializeField] private float gachaAlert = 46f;
        [Min(0f)] [SerializeField] private float kujiAlert = 56f;
        [Min(0f)] [SerializeField] private float alertDecayDelay = 10f;
        [Min(0f)] [SerializeField] private float insideShopDecayPerSecond = 1.5f;
        [Min(0f)] [SerializeField] private float outsideShopDecayPerSecond = 0.1f;
        [Min(1f)] [SerializeField] private float shopSafeRadius = 18f;
        [Range(0f, 1f)] [SerializeField] private float alertAfterArrestNormalized;
        [Min(0.05f)] [SerializeField] private float alertHudFadeSeconds = 0.45f;

        [Header("Police")]
        [SerializeField] private GameObject policeAppearancePrefab;
        [Min(0.1f)] [SerializeField] private float policeSpeed = 4.8f;
        [Min(0.02f)] [SerializeField] private float policeTargetRefreshSeconds = 0.15f;
        [Min(0.1f)] [SerializeField] private float arrestDistance = 1.5f;
        [Min(0.05f)] [SerializeField] private float arrestHoldSeconds = 1f;
        [Min(1f)] [SerializeField] private float chaseTimeoutSeconds = 40f;
        [Min(0)] [SerializeField] private int arrestFine = 300;
        [Min(1f)] [SerializeField] private float policeSpawnDistance = 8f;
        [Min(0.1f)] [SerializeField] private float policeNavMeshSampleRadius = 6f;
        [Min(0f)] [SerializeField] private float arrestTeleportHeightOffset = 0.15f;
        [Min(0.1f)] [SerializeField] private float policeProxyLerpSpeed = 12f;
        [Min(0.1f)] [SerializeField] private float policeAccelerationMultiplier = 2f;
        [Min(1f)] [SerializeField] private float policeAngularSpeed = 720f;
        [Range(0f, 1f)] [SerializeField] private float policeStoppingDistanceMultiplier = 0.65f;
        [Range(-180f, 180f)] [SerializeField] private float policeVisualYawOffset = 180f;
        [Min(0.1f)] [SerializeField] private float policeCollisionRadius = 0.32f;
        [Min(0.5f)] [SerializeField] private float policeCollisionHeight = 1.75f;
        [Min(0.05f)] [SerializeField] private float policeObstacleAvoidanceSeconds = 0.65f;
        [Range(10f, 85f)] [SerializeField] private float policeObstacleAvoidanceAngle = 68f;

        public float AttackMinimumClickInterval => Mathf.Clamp(attackMinimumClickInterval, 0.04f, 0.2f);
        public float AttackReferenceClickInterval =>
            Mathf.Max(AttackMinimumClickInterval, attackReferenceClickInterval);
        public float AttackTransitionSeconds => Mathf.Clamp(attackTransitionSeconds, 0f, 0.25f);
        public float AttackAnimationSpeed => Mathf.Clamp(attackAnimationSpeed, 0.5f, 3f);
        public float AttackMaximumAnimationSpeed =>
            Mathf.Max(AttackAnimationSpeed, attackMaximumAnimationSpeed);
        public float AttackSpeedForClickInterval(float clickInterval) => ShopTheftRules.AttackSpeedForClickInterval(
            clickInterval, AttackMinimumClickInterval, AttackReferenceClickInterval,
            AttackAnimationSpeed, AttackMaximumAnimationSpeed);
        public float AttackMoveMultiplier => Mathf.Clamp(attackMoveMultiplier, 0.1f, 1f);
        public float AttackMoveSlowSeconds => Mathf.Max(0.05f, attackMoveSlowSeconds);
        public float HitRadius => Mathf.Max(0.2f, hitRadius);
        public float HitAngle => Mathf.Clamp(hitAngle, 1f, 180f);
        public float HitForwardOffset => Mathf.Max(0f, hitForwardOffset);
        public int AttackDamage => Mathf.Max(1, attackDamage);
        public float ClawImpulse => Mathf.Max(0.01f, clawImpulse);
        public float ClawVerticalImpulse => Mathf.Max(0f, clawVerticalImpulse);
        public float ClawVerticalImpulseMinimumMultiplier => Mathf.Clamp01(clawVerticalImpulseMinimumMultiplier);
        public float ClawTheftWindow => Mathf.Max(0.1f, clawTheftWindow);
        public float MaximumCapsuleSpeed => Mathf.Max(0.1f, maximumCapsuleSpeed);
        public float MaximumCapsuleAngularSpeed => Mathf.Max(0.1f, maximumCapsuleAngularSpeed);
        public int GachaDurability => Mathf.Max(1, gachaDurability);
        public int KujiDurability => Mathf.Max(1, kujiDurability);
        public float BrokenRecoverySeconds => Mathf.Max(1f, brokenRecoverySeconds);
        public float TheftUncommonChance => Mathf.Clamp01(theftUncommonChance);
        public float TheftRareChance => Mathf.Min(Mathf.Clamp01(theftRareChance), 1f - TheftUncommonChance);
        public float TheftKujiBChance => Mathf.Clamp01(theftKujiBChance);
        public float TheftKujiAChance => Mathf.Min(Mathf.Clamp01(theftKujiAChance), 1f - TheftKujiBChance);
        public float TheftKujiCChance => Mathf.Min(Mathf.Clamp01(theftKujiCChance),
            1f - TheftKujiAChance - TheftKujiBChance);
        public float DamageShakeSeconds => Mathf.Max(0.01f, damageShakeSeconds);
        public float DamageShakeFrequency => Mathf.Max(0.1f, damageShakeFrequency);
        public float DamageShakeDistance => Mathf.Max(0f, damageShakeDistance);
        public float MaximumAlert => Mathf.Max(1f, maximumAlert);
        public float AlertFor(ShopTheftAction action) => action switch
        {
            ShopTheftAction.GachaBreak => Mathf.Max(0f, gachaAlert),
            ShopTheftAction.KujiBreak => Mathf.Max(0f, kujiAlert),
            _ => Mathf.Max(0f, clawAlert)
        };
        public float AlertDecayDelay => Mathf.Max(0f, alertDecayDelay);
        public float InsideShopDecayPerSecond => Mathf.Max(0f, insideShopDecayPerSecond);
        public float OutsideShopDecayPerSecond => Mathf.Max(0f, outsideShopDecayPerSecond);
        public float ShopSafeRadius => Mathf.Max(1f, shopSafeRadius);
        public float AlertAfterArrest => MaximumAlert * Mathf.Clamp01(alertAfterArrestNormalized);
        public float AlertHudFadeSeconds => Mathf.Max(0.05f, alertHudFadeSeconds);
        public GameObject PoliceAppearancePrefab => policeAppearancePrefab;
        public float PoliceSpeed => Mathf.Max(0.1f, policeSpeed);
        public float PoliceTargetRefreshSeconds => Mathf.Max(0.02f, policeTargetRefreshSeconds);
        public float ArrestDistance => Mathf.Max(0.1f, arrestDistance);
        public float ArrestHoldSeconds => Mathf.Max(0.05f, arrestHoldSeconds);
        public float ChaseTimeoutSeconds => Mathf.Max(1f, chaseTimeoutSeconds);
        public int ArrestFine => Mathf.Max(0, arrestFine);
        public float PoliceSpawnDistance => Mathf.Max(1f, policeSpawnDistance);
        public float PoliceNavMeshSampleRadius => Mathf.Max(0.1f, policeNavMeshSampleRadius);
        public float ArrestTeleportHeightOffset => Mathf.Max(0f, arrestTeleportHeightOffset);
        public float PoliceProxyLerpSpeed => Mathf.Max(0.1f, policeProxyLerpSpeed);
        public float PoliceAccelerationMultiplier => Mathf.Max(0.1f, policeAccelerationMultiplier);
        public float PoliceAngularSpeed => Mathf.Max(1f, policeAngularSpeed);
        public float PoliceStoppingDistanceMultiplier => Mathf.Clamp01(policeStoppingDistanceMultiplier);
        public float PoliceVisualYawOffset => Mathf.Clamp(policeVisualYawOffset, -180f, 180f);
        public float PoliceCollisionRadius => Mathf.Max(0.1f, policeCollisionRadius);
        public float PoliceCollisionHeight => Mathf.Max(PoliceCollisionRadius * 2f, policeCollisionHeight);
        public float PoliceObstacleAvoidanceSeconds => Mathf.Max(0.05f, policeObstacleAvoidanceSeconds);
        public float PoliceObstacleAvoidanceAngle => Mathf.Clamp(policeObstacleAvoidanceAngle, 10f, 85f);

        public static ShopTheftConfig Load() => Resources.Load<ShopTheftConfig>(ResourcePath);

#if UNITY_EDITOR
        public void EditorSetPoliceAppearance(GameObject prefab) => policeAppearancePrefab = prefab;
#endif
    }

    public static class ShopTheftRules
    {
        public static float AttackSpeedForClickInterval(float clickInterval, float minimumInterval,
            float referenceInterval, float baseSpeed, float maximumSpeed)
        {
            float minimum = Mathf.Max(0.001f, minimumInterval);
            float reference = Mathf.Max(minimum, referenceInterval);
            float interval = Mathf.Clamp(clickInterval, minimum, reference);
            float fastAmount = Mathf.InverseLerp(reference, minimum, interval);
            return Mathf.Lerp(Mathf.Max(0.01f, baseSpeed), Mathf.Max(baseSpeed, maximumSpeed), fastAmount);
        }

        public static bool IsInsideAttackArc(Vector3 attackerForward, Vector3 toTarget, float radius, float angle)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(attackerForward, Vector3.up).normalized;
            Vector3 flatTarget = Vector3.ProjectOnPlane(toTarget, Vector3.up);
            if (flatTarget.sqrMagnitude > radius * radius) return false;
            if (flatTarget.sqrMagnitude < 0.0001f) return true;
            return Vector3.Angle(flatForward, flatTarget.normalized) <= angle * 0.5f;
        }

        public static ShopGachaRarity SelectTheftGacha(float roll, ShopTheftConfig config)
        {
            float value = Mathf.Clamp01(roll);
            if (value < config.TheftRareChance) return ShopGachaRarity.Rare;
            if (value < config.TheftRareChance + config.TheftUncommonChance) return ShopGachaRarity.Uncommon;
            return ShopGachaRarity.Common;
        }

        public static ShopKujiRank SelectTheftKuji(float roll, ShopTheftConfig config)
        {
            float value = Mathf.Clamp01(roll);
            if (value < config.TheftKujiAChance) return ShopKujiRank.A;
            if (value < config.TheftKujiAChance + config.TheftKujiBChance) return ShopKujiRank.B;
            return value < config.TheftKujiAChance + config.TheftKujiBChance + config.TheftKujiCChance
                ? ShopKujiRank.C
                : ShopKujiRank.D;
        }

        public static Vector3 ClampVelocity(Vector3 velocity, float maximum) =>
            Vector3.ClampMagnitude(velocity, Mathf.Max(0.1f, maximum));
    }
}
