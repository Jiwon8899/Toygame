using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Claw Machine Config", fileName = "ClawMachineConfig")]
    public sealed class ShopClawMachineConfig : ScriptableObject
    {
        [SerializeField] private int machineId = 1;
        [SerializeField] private string displayName = "인형뽑기";
        [Min(1)] [SerializeField] private int attemptCost = 120;
        [Range(10f, 40f)] [SerializeField] private float aimDuration = 25f;
        [Range(0.5f, 5f)] [SerializeField] private float moveSpeed = 2.1f;
        [Range(1f, 15f)] [SerializeField] private float acceleration = 8f;
        [SerializeField] private Vector2 xBounds = new(-1.2f, 1.2f);
        [SerializeField] private Vector2 zBounds = new(-0.75f, 0.85f);
        [SerializeField] private float topHeight = 3.55f;
        [SerializeField] private float dropHeight = 1.25f;
        [Range(0.3f, 5f)] [SerializeField] private float descendSpeed = 1.8f;
        [Range(0.3f, 5f)] [SerializeField] private float liftSpeed = 1.65f;
        [Range(0.3f, 5f)] [SerializeField] private float returnSpeed = 2.2f;
        [Range(0.1f, 2f)] [SerializeField] private float closeDuration = 0.55f;
        [Range(0.1f, 2f)] [SerializeField] private float releaseDuration = 0.45f;
        [Range(1f, 8f)] [SerializeField] private float interactionRange = 4.5f;
        [Range(0.05f, 0.2f)] [SerializeField] private float inputSendInterval = 0.06f;
        [Range(1f, 10f)] [SerializeField] private float autoDropDelay = 3f;
        [SerializeField] private ShopClawPrizePool prizePool;
        [SerializeField] private ShopRarityWeights rarityWeights = new()
        {
            common = 110,
            uncommon = 40,
            rare = 40,
            ultraRare = 10
        };
        [SerializeField] private ShopMultiPrizePolicy multiPrizePolicy =
            ShopMultiPrizePolicy.SingleAndReturnExtras;

        [Header("Operator camera")]
        [Range(2.5f, 7f)] [SerializeField] private float operatorCameraDistance = 4.2f;
        [Range(15f, 65f)] [SerializeField] private float operatorCameraPitch = 35f;
        [Range(40f, 85f)] [SerializeField] private float operatorCameraFieldOfView = 60f;
        [Range(0.8f, 3.5f)] [SerializeField] private float operatorCameraFocusHeight = 2.0f;

        [Header("Finger layout")]
        [Min(0.05f)] [SerializeField] private float fingerLayoutRadius = 0.69f;
        [SerializeField] private float fingerLayoutHeight = -0.38f;
        [Range(0f, 180f)] [SerializeField] private float fingerLayoutTilt = 120f;

        [Header("Physical carriage and suspension")]
        [Min(0.5f)] [SerializeField] private float clawMass = 1.35f;
        [Range(0.04f, 0.3f)] [SerializeField] private float suspensionTravel = 0.14f;
        [Min(1f)] [SerializeField] private float suspensionSpring = 42f;
        [Min(0.1f)] [SerializeField] private float suspensionDamper = 5.5f;
        [Range(0.1f, 12f)] [SerializeField] private float housingSwingDamper = 2.1f;
        [Min(0.1f)] [SerializeField] private float verticalAcceleration = 4.5f;
        [Range(0.5f, 1f)] [SerializeField] private float loadedLiftSpeedMultiplier = 0.82f;

        [Header("Torque driven fingers")]
        [Range(-45f, 60f)] [SerializeField] private float closedFingerAngle = -18f;
        [Min(0f)] [SerializeField] private float closedFingerClearanceAngle = 8f;
        [Range(-45f, 45f)] [SerializeField] private float openFingerAngle = 32f;
        [Min(1f)] [SerializeField] private float closeMotorTorque = 24f;
        [Min(1f)] [SerializeField] private float openMotorTorque = 18f;
        [Min(1f)] [SerializeField] private float closeMotorSpeed = 82f;
        [Min(1f)] [SerializeField] private float openMotorSpeed = 68f;
        [Range(0.1f, 1f)] [SerializeField] private float ascentGripTorqueMultiplier = 0.68f;
        [Range(0f, 0.1f)] [SerializeField] private float contactCenteringMultiplier = 0.02f;
        [Range(0.25f, 8f)] [SerializeField] private float chuteSettleDuration = 0.45f;
        [Range(0.01f, 1f)] [SerializeField] private float chuteSettleLinearSpeed = 0.18f;
        [Range(0.1f, 10f)] [SerializeField] private float antiStuckDelay = 3.5f;

        [Header("State timeouts")]
        [Min(0.1f)] [SerializeField] private float reservedTimeout = 1f;
        [Min(0.1f)] [SerializeField] private float descendTimeout = 5f;
        [Min(0.1f)] [SerializeField] private float closeTimeout = 2f;
        [Min(0.1f)] [SerializeField] private float ascendTimeout = 6f;
        [Min(0.1f)] [SerializeField] private float returnTimeout = 6f;
        [Min(0.1f)] [SerializeField] private float releaseTimeout = 2f;
        [Min(0.1f)] [SerializeField] private float judgeTimeout = 1.6f;

        [Header("Physics materials")]
        [SerializeField] private PhysicsMaterial clawFingerMaterial;
        [SerializeField] private PhysicsMaterial machineFloorMaterial;

        public int MachineId => machineId;
        public string DisplayName => displayName;
        public int AttemptCost => attemptCost;
        public float AimDuration => aimDuration;
        public float MoveSpeed => moveSpeed;
        public float Acceleration => acceleration;
        public Vector2 XBounds => xBounds;
        public Vector2 ZBounds => zBounds;
        public float TopHeight => topHeight;
        public float DropHeight => dropHeight;
        public float DescendSpeed => descendSpeed;
        public float LiftSpeed => liftSpeed;
        public float ReturnSpeed => returnSpeed;
        public float CloseDuration => closeDuration;
        public float ReleaseDuration => releaseDuration;
        public float InteractionRange => interactionRange;
        public float InputSendInterval => inputSendInterval;
        public float AutoDropDelay => autoDropDelay;
        public ShopClawPrizePool PrizePool => prizePool;
        public ShopRarityWeights RarityWeights => rarityWeights.Total > 0
            ? rarityWeights
            : ShopOperationsConfig.Load()?.StandardRarityWeights ?? default;
        public ShopMultiPrizePolicy MultiPrizePolicy => multiPrizePolicy;
        public float OperatorCameraDistance => operatorCameraDistance;
        public float OperatorCameraPitch => operatorCameraPitch;
        public float OperatorCameraFieldOfView => operatorCameraFieldOfView;
        public float OperatorCameraFocusHeight => operatorCameraFocusHeight;
        public float FingerLayoutRadius => fingerLayoutRadius;
        public float FingerLayoutHeight => fingerLayoutHeight;
        public float FingerLayoutTilt => fingerLayoutTilt;
        public float ClawMass => clawMass;
        public float SuspensionTravel => suspensionTravel;
        public float SuspensionSpring => suspensionSpring;
        public float SuspensionDamper => suspensionDamper;
        public float HousingSwingDamper => housingSwingDamper;
        public float VerticalAcceleration => verticalAcceleration;
        public float LoadedLiftSpeedMultiplier => loadedLiftSpeedMultiplier;
        public float ClosedFingerAngle => closedFingerAngle;
        public float ClosedFingerClearanceAngle => closedFingerClearanceAngle;
        public float OpenFingerAngle => openFingerAngle;
        public float CloseMotorTorque => closeMotorTorque;
        public float OpenMotorTorque => openMotorTorque;
        public float CloseMotorSpeed => closeMotorSpeed;
        public float OpenMotorSpeed => openMotorSpeed;
        public float AscentGripTorqueMultiplier => ascentGripTorqueMultiplier;
        public float ContactCenteringMultiplier => contactCenteringMultiplier;
        public float ChuteSettleDuration => chuteSettleDuration;
        public float ChuteSettleLinearSpeed => chuteSettleLinearSpeed;
        public float AntiStuckDelay => antiStuckDelay;
        public float ReservedTimeout => reservedTimeout;
        public float DescendTimeout => descendTimeout;
        public float CloseTimeout => closeTimeout;
        public float AscendTimeout => ascendTimeout;
        public float ReturnTimeout => returnTimeout;
        public float ReleaseTimeout => releaseTimeout;
        public float JudgeTimeout => judgeTimeout;
        public PhysicsMaterial ClawFingerMaterial => clawFingerMaterial;
        public PhysicsMaterial MachineFloorMaterial => machineFloorMaterial;

#if UNITY_EDITOR
        public void EditorConfigure(int id, string label, int cost, float aimSeconds, float speed,
            float accel, Vector2 x, Vector2 z, float top, float bottom, float downSpeed,
            float upSpeed, float chuteSpeed, float close, float release, float threshold,
            float strength, Vector2 breakForce, float useRange, float sendInterval)
        {
            machineId = id;
            displayName = label;
            attemptCost = cost;
            aimDuration = aimSeconds;
            moveSpeed = speed;
            acceleration = accel;
            xBounds = x;
            zBounds = z;
            topHeight = top;
            dropHeight = bottom;
            descendSpeed = downSpeed;
            liftSpeed = upSpeed;
            returnSpeed = chuteSpeed;
            closeDuration = close;
            releaseDuration = release;
            interactionRange = useRange;
            inputSendInterval = sendInterval;
        }

        public void EditorConfigurePhysical(ShopClawPrizePool pool, float bodyMass,
            float travel, float spring, float damper, float gripSpring, float gripDamper,
            float maxGripForce, float fingerAngle, float verticalAccel, float loadedLiftMultiplier,
            PhysicsMaterial fingerMaterial, PhysicsMaterial floorMaterial)
        {
            prizePool = pool;
            clawMass = Mathf.Max(0.5f, bodyMass);
            suspensionTravel = Mathf.Clamp(travel, 0.04f, 0.3f);
            suspensionSpring = Mathf.Max(1f, spring);
            suspensionDamper = Mathf.Max(0.1f, damper);
            closeMotorTorque = Mathf.Max(1f, maxGripForce);
            closedFingerAngle = Mathf.Clamp(fingerAngle, -45f, 60f);
            verticalAcceleration = Mathf.Max(0.1f, verticalAccel);
            loadedLiftSpeedMultiplier = Mathf.Clamp(loadedLiftMultiplier, 0.5f, 1f);
            clawFingerMaterial = fingerMaterial;
            machineFloorMaterial = floorMaterial;
        }

        public void EditorConfigureTorque(float closedAngle, float openAngle, float closedClearance,
            float closeTorque,
            float openTorque, float closeSpeed, float openSpeed, float ascentMultiplier,
            float swingDamper, float settleDuration, float settleSpeed, float stuckDelay)
        {
            closedFingerAngle = Mathf.Clamp(closedAngle, -45f, 60f);
            openFingerAngle = Mathf.Clamp(openAngle, -45f, 75f);
            closedFingerClearanceAngle = Mathf.Clamp(closedClearance, -30f, 30f);
            closeMotorTorque = Mathf.Max(1f, closeTorque);
            openMotorTorque = Mathf.Max(1f, openTorque);
            closeMotorSpeed = Mathf.Max(1f, closeSpeed);
            openMotorSpeed = Mathf.Max(1f, openSpeed);
            ascentGripTorqueMultiplier = Mathf.Clamp(ascentMultiplier, 0.1f, 1f);
            housingSwingDamper = Mathf.Max(0.1f, swingDamper);
            chuteSettleDuration = Mathf.Max(0.25f, settleDuration);
            chuteSettleLinearSpeed = Mathf.Max(0.01f, settleSpeed);
            antiStuckDelay = Mathf.Max(0.1f, stuckDelay);
            closeDuration = Mathf.Max(closeDuration, 1.2f);
            closeTimeout = 4.5f;
            judgeTimeout = Mathf.Max(chuteSettleDuration + 1.5f, 3.25f);
        }

        public void EditorConfigureCaptureMotion(float lowestHeadHeight, float upwardSpeed,
            float downwardSpeed)
        {
            dropHeight = Mathf.Clamp(lowestHeadHeight, 0.65f, topHeight - 0.5f);
            liftSpeed = Mathf.Max(0.4f, upwardSpeed);
            descendSpeed = Mathf.Max(0.1f, downwardSpeed);
            descendTimeout = Mathf.Max(5f, (topHeight - dropHeight) / descendSpeed + 2f);
        }

        public void EditorConfigureSuspension(float travel, float spring, float damper)
        {
            suspensionTravel = Mathf.Clamp(travel, 0.02f, 0.3f);
            suspensionSpring = Mathf.Max(1f, spring);
            suspensionDamper = Mathf.Max(0.1f, damper);
        }

        public void EditorConfigureReturnSpeed(float speed)
        {
            returnSpeed = Mathf.Clamp(speed, 0.3f, 5f);
        }

        public void EditorConfigureGripAssist(float multiplier)
        {
            contactCenteringMultiplier = Mathf.Clamp(multiplier, 0f, 0.1f);
        }

        public void EditorConfigureOperator(float idleDropSeconds, float distance, float pitch,
            float fieldOfView, float focusHeight)
        {
            autoDropDelay = Mathf.Clamp(idleDropSeconds, 1f, 10f);
            operatorCameraDistance = Mathf.Clamp(distance, 2.5f, 7f);
            operatorCameraPitch = Mathf.Clamp(pitch, 15f, 65f);
            operatorCameraFieldOfView = Mathf.Clamp(fieldOfView, 40f, 85f);
            operatorCameraFocusHeight = Mathf.Clamp(focusHeight, 0.8f, 3.5f);
        }

        public void EditorConfigureFingerLayout(float radius, float height, float tilt)
        {
            fingerLayoutRadius = Mathf.Max(0.05f, radius);
            fingerLayoutHeight = height;
            fingerLayoutTilt = Mathf.Clamp(tilt, 0f, 180f);
        }

        public void EditorConfigureBounds(Vector2 horizontal, Vector2 depth)
        {
            xBounds = horizontal;
            zBounds = depth;
        }

        public void EditorConfigureRarity(ShopRarityWeights weights, ShopMultiPrizePolicy policy)
        {
            rarityWeights = weights;
            multiPrizePolicy = policy;
        }
#endif
    }
}
