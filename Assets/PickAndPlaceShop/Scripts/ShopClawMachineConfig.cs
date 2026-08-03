using UnityEngine;
using UnityEngine.Serialization;

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
            ShopMultiPrizePolicy.AwardAll;

        [Header("Scoop rig")]
        [Range(0.8f, 1.8f)] [SerializeField] private float scoopDiameter = 1.24f;
        [Range(0.04f, 0.18f)] [SerializeField] private float scoopBottomThickness = 0.065f;
        [Range(0.08f, 0.65f)] [SerializeField] private float scoopRimHeight = 0.46f;
        [Range(0.01f, 0.08f)] [SerializeField] private float scoopOpenRimHeight = 0.04f;
        [Range(0.1f, 1.5f)] [SerializeField] private float scoopLipCloseDuration = 0.55f;
        [Range(0.25f, 1.4f)] [SerializeField] private float scrapeDistance = 0.72f;
        [Range(0.2f, 2.5f)] [SerializeField] private float scrapeSpeed = 0.68f;
        [Range(2f, 22f)] [SerializeField] private float scrapeTiltAngle = 10f;
        [Range(25f, 85f)] [SerializeField] private float pourAngle = 62f;
        [Range(0.001f, 0.025f)] [SerializeField] private float sweepSkin = 0.006f;
        [Range(0.001f, 0.03f)] [SerializeField] private float floorClearance = 0.004f;
        [Range(0f, 0.1f)] [SerializeField] private float chuteHorizontalInset = 0f;
        [Min(0.1f)] [SerializeField] private float scoopVerticalAcceleration = 4.5f;
        [Range(0.5f, 1f)] [SerializeField] private float loadedLiftSpeedMultiplier = 0.82f;

        [Header("Capsule mass by rarity")]
        [Range(0.15f, 1.5f)] [SerializeField] private float commonCapsuleMass = 0.34f;
        [Range(0.15f, 1.5f)] [SerializeField] private float uncommonCapsuleMass = 0.40f;
        [Range(0.15f, 1.5f)] [SerializeField] private float rareCapsuleMass = 0.48f;
        [Range(0.15f, 1.5f)] [SerializeField] private float ultraRareCapsuleMass = 0.58f;

        [Header("Operator camera")]
        [Range(2.5f, 7f)] [SerializeField] private float operatorCameraDistance = 4.2f;
        [Range(15f, 65f)] [SerializeField] private float operatorCameraPitch = 35f;
        [Range(40f, 85f)] [SerializeField] private float operatorCameraFieldOfView = 60f;
        [Range(0.8f, 3.5f)] [SerializeField] private float operatorCameraFocusHeight = 2.0f;

        [Header("Prize settling and recovery")]
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
        [FormerlySerializedAs("clawFingerMaterial")]
        [SerializeField] private PhysicsMaterial scoopPhysicsMaterial;
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
        public float ScoopDiameter => scoopDiameter;
        public float ScoopBottomThickness => scoopBottomThickness;
        public float ScoopRimHeight => scoopRimHeight;
        public float ScoopOpenRimHeight => scoopOpenRimHeight;
        public float ScoopLipCloseDuration => scoopLipCloseDuration;
        public float ScrapeDistance => scrapeDistance;
        public float ScrapeSpeed => scrapeSpeed;
        public float ScrapeTiltAngle => scrapeTiltAngle;
        public float PourAngle => pourAngle;
        public float SweepSkin => sweepSkin;
        public float FloorClearance => floorClearance;
        public float ChuteHorizontalInset => chuteHorizontalInset;
        public float ScoopVerticalAcceleration => scoopVerticalAcceleration;
        public float OperatorCameraDistance => operatorCameraDistance;
        public float OperatorCameraPitch => operatorCameraPitch;
        public float OperatorCameraFieldOfView => operatorCameraFieldOfView;
        public float OperatorCameraFocusHeight => operatorCameraFocusHeight;
        public float LoadedLiftSpeedMultiplier => loadedLiftSpeedMultiplier;
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
        public PhysicsMaterial ScoopPhysicsMaterial => scoopPhysicsMaterial;
        public PhysicsMaterial MachineFloorMaterial => machineFloorMaterial;

        public float GetCapsuleMass(ShopProductRarity rarity)
        {
            return rarity switch
            {
                ShopProductRarity.Uncommon => uncommonCapsuleMass,
                ShopProductRarity.Rare => rareCapsuleMass,
                ShopProductRarity.UltraRare => ultraRareCapsuleMass,
                _ => commonCapsuleMass
            };
        }

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

        public void EditorConfigurePrizeCatalog(ShopClawPrizePool pool,
            PhysicsMaterial scoopMaterial, PhysicsMaterial floorMaterial)
        {
            prizePool = pool;
            scoopPhysicsMaterial = scoopMaterial;
            machineFloorMaterial = floorMaterial;
        }

        public void EditorConfigureCaptureMotion(float lowestHeadHeight, float upwardSpeed,
            float downwardSpeed)
        {
            dropHeight = Mathf.Clamp(lowestHeadHeight, 0.65f, topHeight - 0.5f);
            liftSpeed = Mathf.Max(0.4f, upwardSpeed);
            descendSpeed = Mathf.Max(0.1f, downwardSpeed);
            descendTimeout = Mathf.Max(5f, (topHeight - dropHeight) / descendSpeed + 2f);
        }

        public void EditorConfigureReturnSpeed(float speed)
        {
            returnSpeed = Mathf.Clamp(speed, 0.3f, 5f);
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

        public void EditorConfigureScoop(float diameter, float bottomThickness, float rimHeight,
            float forwardDistance, float forwardSpeed, float tiltAngle, float releaseAngle,
            float skin, float clearance, float loadedLiftMultiplier, Vector4 rarityMasses)
        {
            scoopDiameter = Mathf.Clamp(diameter, 0.8f, 1.8f);
            scoopBottomThickness = Mathf.Clamp(bottomThickness, 0.04f, 0.18f);
            scoopRimHeight = Mathf.Clamp(rimHeight, 0.08f, 0.65f);
            scoopOpenRimHeight = Mathf.Min(0.04f, scoopRimHeight);
            scoopLipCloseDuration = 0.55f;
            scrapeDistance = Mathf.Clamp(forwardDistance, 0.25f, 1.4f);
            scrapeSpeed = Mathf.Clamp(forwardSpeed, 0.2f, 2.5f);
            scrapeTiltAngle = Mathf.Clamp(tiltAngle, 2f, 22f);
            pourAngle = Mathf.Clamp(releaseAngle, 25f, 85f);
            sweepSkin = Mathf.Clamp(skin, 0.001f, 0.025f);
            floorClearance = Mathf.Clamp(clearance, 0.001f, 0.03f);
            chuteHorizontalInset = 0f;
            loadedLiftSpeedMultiplier = Mathf.Clamp(loadedLiftMultiplier, 0.5f, 1f);
            commonCapsuleMass = Mathf.Clamp(rarityMasses.x, 0.15f, 1.5f);
            uncommonCapsuleMass = Mathf.Clamp(rarityMasses.y, 0.15f, 1.5f);
            rareCapsuleMass = Mathf.Clamp(rarityMasses.z, 0.15f, 1.5f);
            ultraRareCapsuleMass = Mathf.Clamp(rarityMasses.w, 0.15f, 1.5f);
            multiPrizePolicy = ShopMultiPrizePolicy.AwardAll;
            closeDuration = Mathf.Max(closeDuration, scrapeDistance / scrapeSpeed);
        }
#endif
    }
}
