using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShopClawMachineNetwork : NetworkBehaviour
    {
        public static ShopClawMachineNetwork Instance { get; private set; }
        public static ShopClawMachineNetwork LocalActiveMachine { get; private set; }

        [Header("Data")]
        [SerializeField] private ShopClawMachineConfig config;
        [SerializeField] private GameObject prizePrefab;
        [SerializeField] private ShopClawPrizeDefinition[] prizeDefinitions;

        [Header("Machine")]
        [SerializeField] private Transform operatorPoint;
        [SerializeField] private Camera operatorCamera;
        [SerializeField] private Transform cameraLookPivot;
        [SerializeField] private Rigidbody carriageBody;
        [SerializeField] private Transform clawHead;
        [SerializeField] private Rigidbody clawBody;
        [SerializeField] private ConfigurableJoint suspensionJoint;
        [SerializeField] private Transform cable;
        [SerializeField] private Transform[] clawFingers;
        [SerializeField] private Transform gripVolume;
        [SerializeField] private Transform chuteDropPoint;
        [SerializeField] private Transform[] prizeSpawnPoints;
        [SerializeField] private Transform joystickStick;
        [SerializeField] private Renderer statusLamp;
        [SerializeField] private Renderer[] localGlassRenderers;
        [SerializeField] private Collider[] clawFingerSensors;

        [Header("HUD")]
        [SerializeField] private Canvas operatorHud;
        [SerializeField] private Text operatorHudText;
        [SerializeField] private Text debugText;
        [SerializeField] private CanvasGroup cameraTransition;
        [SerializeField] private bool showDebug;
        private Text countdownText;
        private Text costText;
        private Text instructionText;
        private Text toastText;

        public NetworkVariable<ShopClawMachineState> State = new(ShopClawMachineState.Idle);
        public NetworkVariable<ulong> OccupantClientId = new(ShopClawRules.NoOccupant);
        public NetworkVariable<int> AttemptId = new(0);
        public NetworkVariable<Vector2> RailPosition = new(Vector2.zero);
        public NetworkVariable<float> ClawHeight = new(3.55f);
        public NetworkVariable<Vector2> OperatorInput = new(Vector2.zero);
        public NetworkVariable<float> FingerClosed = new(0f);
        public NetworkVariable<int> AimSecondsRemaining = new(0);
        public NetworkVariable<int> AutoDropSecondsRemaining = new(0);
        public NetworkVariable<ulong> HeldPrizeNetworkObjectId = new(0);
        public NetworkVariable<float> LastGripScore = new(0f);
        public NetworkVariable<float> LastJointBreakForce = new(0f);
        public NetworkVariable<bool> LastResultSuccess = new(false);
        public NetworkVariable<int> AwardedCount = new(0);
        public NetworkVariable<FixedString64Bytes> LastAwardedName =
            new(new FixedString64Bytes(""));
        public NetworkVariable<int> LastAwardedRarity = new(0);
        public NetworkVariable<Color> LastAwardedCapsuleColor = new(Color.white);
        public NetworkVariable<FixedString128Bytes> ResultMessage =
            new(new FixedString128Bytes("사용 가능"));

        private readonly HashSet<int> chargedAttempts = new();
        private readonly ShopClawAwardLedger awardLedger = new();
        private readonly List<ShopClawPrizeNetwork> activePrizes = new();
        private Vector2 railVelocity;
        private float aimRemaining;
        private float stateElapsed;
        private float phaseExitElapsed;
        private float recoveryCheckElapsed;
        private int observedDay;
        private int roundAwardCount;
        private bool roundHadPhysicalLift;
        private bool localMode;
        private Camera previousCamera;
        private AudioListener previousListener;
        private AudioListener operatorListener;
        private NetworkObject localPlayer;
        private readonly List<Behaviour> disabledPlayerBehaviours = new();
        private readonly List<Renderer> hiddenLocalRenderers = new();
        private readonly List<Renderer> hiddenLocalGlassRenderers = new();
        private readonly List<Renderer> hiddenLocalOverheadRenderers = new();
        private readonly List<Renderer> hiddenLocalForegroundRenderers = new();
        private float localLookYaw;
        private float localLookPitch;
        private float localCameraDistance = 3f;
        private float localModeEnteredAt;
        private float toastUntil;
        private string lastObservedResult = string.Empty;
        private int localInstructionAttempt = -1;
        private int localCameraPreset;
        private float nextInputSendTime;
        private Vector2 lastSentInput;
        private bool qaClient;
        private float qaNextActionTime;
        private int qaCompletedAttempts;
        private int qaResultAttempt = -1;
        private int requestedReplayAttempt = -1;
        private int awardedAttemptId = -1;
        private int appliedClawUpgradeAppearance = -1;
        private readonly List<Rigidbody> physicalFingerBodies = new();
        private readonly List<HingeJoint> physicalFingerJoints = new();
        private readonly List<float> physicalFingerAngleOffsets = new();
        private readonly List<ShopClawFingerContactSensor> physicalFingerSensors = new();
        private readonly List<ShopClawPrizeNetwork> fingerContactScratch = new();
        private GameObject aimGroundMarker;
        private readonly Dictionary<ulong, float> chuteStableSeconds = new();
        private readonly Dictionary<ulong, float> chuteLastObservationTime = new();
        private readonly Dictionary<ulong, float> abnormalStuckSeconds = new();
        private bool physicalClawReady;
        private float verticalVelocity;
        private float autoDropIdleElapsed;
        private System.Random prizeRandom;

        public ShopClawMachineConfig Config => config;
        public bool IsManuallyBusy => OccupantClientId.Value != ShopClawRules.NoOccupant ||
                                      (State.Value != ShopClawMachineState.Idle &&
                                       State.Value != ShopClawMachineState.Cooldown);
        public Vector3 OperatorWorldPosition => operatorPoint != null ? operatorPoint.position : transform.position;
        public string InteractionPrompt => State.Value == ShopClawMachineState.Idle
            ? (config != null ? config.DisplayName : "물리 인형뽑기") + " 조작 시작"
            : OccupantClientId.Value == ShopClawRules.NoOccupant ? "인형뽑기 초기화 중" : "인형뽑기 조작 중";
        public static bool LocalOperatorActive => LocalActiveMachine != null && LocalActiveMachine.localMode;
        public bool LocalGlassHidden => localMode && localGlassRenderers != null &&
                                        Array.TrueForAll(localGlassRenderers,
                                            renderer => renderer == null || renderer.forceRenderingOff);
        public Vector3 OperatorCameraPosition => operatorCamera != null
            ? operatorCamera.transform.position
            : Vector3.zero;
        public int LocalCameraPreset => localCameraPreset;

#if UNITY_EDITOR
        public void EditorConfigure(ShopClawMachineConfig machineConfig, GameObject networkPrizePrefab,
            ShopClawPrizeDefinition[] definitions, Transform operatorTransform, Camera localCamera,
            Transform lookPivot, Transform head, Rigidbody headBody, Transform wire, Transform[] fingers,
            Transform grip, Transform chute, Transform[] spawns, Transform joystick, Renderer lamp,
            Canvas hud, Text hudText, Text developmentText)
        {
            config = machineConfig;
            prizePrefab = networkPrizePrefab;
            prizeDefinitions = definitions;
            operatorPoint = operatorTransform;
            operatorCamera = localCamera;
            cameraLookPivot = lookPivot;
            clawHead = head;
            clawBody = headBody;
            cable = wire;
            clawFingers = fingers;
            gripVolume = grip;
            chuteDropPoint = chute;
            prizeSpawnPoints = spawns;
            joystickStick = joystick;
            statusLamp = lamp;
            operatorHud = hud;
            operatorHudText = hudText;
            debugText = developmentText;
        }

        public void EditorConfigureTransition(CanvasGroup transition) => cameraTransition = transition;

        public void EditorConfigurePhysicalPresentation(Renderer[] glassRenderers, Collider[] fingerSensors)
        {
            localGlassRenderers = glassRenderers;
            clawFingerSensors = fingerSensors;
        }

        public void EditorConfigurePhysicalRig(Rigidbody authoredCarriage,
            ConfigurableJoint authoredSuspension)
        {
            carriageBody = authoredCarriage;
            suspensionJoint = authoredSuspension;
        }
#endif

        private void Awake()
        {
            if (Instance == null) Instance = this;
            qaClient = false;
            if (operatorCamera != null)
            {
                operatorCamera.enabled = false;
                operatorListener = operatorCamera.GetComponent<AudioListener>();
                if (operatorListener != null) operatorListener.enabled = false;
            }
            if (operatorHud != null) operatorHud.gameObject.SetActive(false);
            CachePhysicalPresentation();
            EnsureMinimalHud();
            EnsureCountdownUi();
        }

        public override void OnNetworkSpawn()
        {
            if (Instance == null) Instance = this;
            EnsureAimGroundMarker();
            AwardedCount.OnValueChanged += OnAwardedCountChanged;
            if (IsServer)
            {
                SetupServerPhysicalClaw();
                if (NetworkManager.NetworkConfig.NetworkTopology == NetworkTopologyTypes.DistributedAuthority)
                    NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.SessionOwner, true);
                State.Value = ShopClawMachineState.Idle;
                OccupantClientId.Value = ShopClawRules.NoOccupant;
                RailPosition.Value = Vector2.zero;
                ClawHeight.Value = config != null ? config.TopHeight : 3.55f;
                observedDay = ShopNetworkGame.Instance != null ? ShopNetworkGame.Instance.Day.Value : 1;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                StartCoroutine(ServerSpawnPrizesAfterFrame());
            }
        }

        public override void OnNetworkDespawn()
        {
            AwardedCount.OnValueChanged -= OnAwardedCountChanged;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            ExitLocalMode();
            if (Instance == this) Instance = null;
            if (LocalActiveMachine == this) LocalActiveMachine = null;
        }

        public override void OnDestroy()
        {
            if (aimGroundMarker != null) Destroy(aimGroundMarker);
            if (Instance == this) Instance = null;
            if (LocalActiveMachine == this) LocalActiveMachine = null;
            base.OnDestroy();
        }

        public void RequestUse()
        {
            if (IsSpawned) RequestUseRpc();
        }

        public void RequestDrop()
        {
            if (IsSpawned) RequestDropRpc(AttemptId.Value);
        }

        public void RequestCancel()
        {
            if (IsSpawned) RequestCancelRpc(AttemptId.Value);
        }

        public void RequestReplay()
        {
            if (IsSpawned) RequestReplayRpc(AttemptId.Value);
        }

        public void RequestInput(Vector2 input)
        {
            if (IsSpawned) SubmitInputRpc(Vector2.ClampMagnitude(input, 1f), AttemptId.Value);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestUseRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            bool inRange = TryGetPlayer(sender, out NetworkObject player) &&
                           Vector3.Distance(player.transform.position, OperatorWorldPosition) <= config.InteractionRange;
            bool canReserve = ShopClawRules.CanReserve(
                ShopNetworkGame.Instance != null ? ShopNetworkGame.Instance.Phase.Value : ShopPhase.Complete,
                OccupantClientId.Value, sender, inRange, false);
            if (!canReserve)
            {
                if (ShopNetworkGame.Instance != null)
                    ShopNetworkGame.Instance.ServerSetEvent(OccupantClientId.Value != ShopClawRules.NoOccupant
                        ? "인형뽑기가 이미 사용 중입니다."
                        : "PrizeHunt 단계에서 기계 가까이 서야 사용할 수 있습니다.");
                return;
            }

            OccupantClientId.Value = sender;
            AttemptId.Value++;
            aimRemaining = EffectiveAimDuration;
            AimSecondsRemaining.Value = Mathf.CeilToInt(aimRemaining);
            autoDropIdleElapsed = 0f;
            AutoDropSecondsRemaining.Value = Mathf.CeilToInt(config.AutoDropDelay);
            LastResultSuccess.Value = false;
            roundAwardCount = 0;
            roundHadPhysicalLift = false;
            ResultMessage.Value = new FixedString128Bytes("WASD로 위치를 정하고 Space를 눌러 투하하세요.");
            SetState(ShopClawMachineState.Reserved);
            ShopNetworkGame.Instance.ServerSetEvent("인형뽑기 조작을 시작했습니다.");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitInputRpc(Vector2 input, int attemptId, RpcParams rpcParams = default)
        {
            if (attemptId != AttemptId.Value ||
                !ShopClawRules.CanAcceptOperatorCommand(State.Value, OccupantClientId.Value,
                    rpcParams.Receive.SenderClientId)) return;
            OperatorInput.Value = Vector2.ClampMagnitude(input, 1f);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestDropRpc(int attemptId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (attemptId != AttemptId.Value ||
                !ShopClawRules.CanAcceptOperatorCommand(State.Value, OccupantClientId.Value, sender)) return;

            ServerBeginDrop(attemptId);
        }

        private bool ServerBeginDrop(int attemptId)
        {
            if (!IsServer || attemptId != AttemptId.Value ||
                State.Value != ShopClawMachineState.Aiming ||
                OccupantClientId.Value == ShopClawRules.NoOccupant || ShopNetworkGame.Instance == null)
                return false;

            int coins = ShopNetworkGame.Instance.Coins.Value;
            if (!ShopClawRules.TryChargeAttempt(ref coins, config.AttemptCost, attemptId, chargedAttempts))
            {
                ResultMessage.Value = new FixedString128Bytes("가게 자금이 부족합니다.");
                ShopNetworkGame.Instance.ServerSetEvent("가게 자금이 부족해 집게를 내릴 수 없습니다.");
                return false;
            }

            ShopNetworkGame.Instance.Coins.Value = coins;
            OperatorInput.Value = Vector2.zero;
            railVelocity = Vector2.zero;
            ResultMessage.Value = new FixedString128Bytes("집게 하강 중");
            SetState(ShopClawMachineState.Descend);
            AutoDropSecondsRemaining.Value = 0;
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestCancelRpc(int attemptId, RpcParams rpcParams = default)
        {
            if (attemptId != AttemptId.Value || OccupantClientId.Value != rpcParams.Receive.SenderClientId) return;
            if (State.Value != ShopClawMachineState.Aiming &&
                State.Value != ShopClawMachineState.Reserved &&
                State.Value != ShopClawMachineState.Cooldown) return;
            ResultMessage.Value = new FixedString128Bytes("조작을 취소했습니다. 비용은 차감되지 않았습니다.");
            ServerResetMachine();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestReplayRpc(int attemptId, RpcParams rpcParams = default)
        {
            if (attemptId != AttemptId.Value || OccupantClientId.Value != rpcParams.Receive.SenderClientId) return;
            if (State.Value != ShopClawMachineState.Cooldown) return;
            if (ShopNetworkGame.Instance == null ||
                !ShopClawRules.CanOperateDuring(ShopNetworkGame.Instance.Phase.Value)) return;
            ServerPrepareNextAttempt();
        }

        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || config == null) return;
            float dt = Time.fixedDeltaTime;
            stateElapsed += dt;
            HandleDayAndPhase(dt);
            if (State.Value == ShopClawMachineState.Idle)
            {
                recoveryCheckElapsed += dt;
                if (recoveryCheckElapsed >= 1f)
                {
                    recoveryCheckElapsed = 0f;
                    ServerRecoverOutOfBoundsPrizes();
                }
            }

            switch (State.Value)
            {
                case ShopClawMachineState.Reserved:
                    if (stateElapsed >= Mathf.Min(0.35f, config.ReservedTimeout))
                        SetState(ShopClawMachineState.Aiming);
                    break;
                case ShopClawMachineState.Aiming:
                    ServerUpdateAiming(dt);
                    break;
                case ShopClawMachineState.Descend:
                    ServerMoveClawHeight(config.DropHeight, config.DescendSpeed, dt);
                    float physicalHeadHeight = clawHead != null
                        ? transform.InverseTransformPoint(clawHead.position).y
                        : ClawHeight.Value;
                    bool reachedCaptureBand =
                        physicalHeadHeight <= config.DropHeight + 0.025f;
                    if ((stateElapsed >= 0.25f &&
                         (HasFingerApproach(0.02f) || HasAnyFingerContact())) ||
                        reachedCaptureBand ||
                        stateElapsed >= config.DescendTimeout)
                        SetState(ShopClawMachineState.Close);
                    break;
                case ShopClawMachineState.Close:
                    UpdatePhysicalGripDiagnostics();
                    if ((stateElapsed >= config.CloseDuration && AreFingersAtTarget(config.ClosedFingerAngle, 3f)) ||
                        stateElapsed >= config.CloseTimeout)
                        SetState(ShopClawMachineState.Ascend);
                    break;
                case ShopClawMachineState.Ascend:
                    UpdatePhysicalGripDiagnostics();
                    float liftSpeed = config.LiftSpeed *
                                      (HeldPrizeNetworkObjectId.Value != 0
                                          ? config.LoadedLiftSpeedMultiplier
                                          : 1f);
                    ServerMoveClawHeight(config.TopHeight, liftSpeed, dt);
                    if (Mathf.Approximately(ClawHeight.Value, config.TopHeight) ||
                        stateElapsed >= config.AscendTimeout)
                        SetState(ShopClawMachineState.Return);
                    break;
                case ShopClawMachineState.Return:
                    UpdatePhysicalGripDiagnostics();
                    Vector2 chute = new(chuteDropPoint.localPosition.x, chuteDropPoint.localPosition.z);
                    RailPosition.Value = Vector2.MoveTowards(RailPosition.Value, chute, config.ReturnSpeed * dt);
                    if ((RailPosition.Value - chute).sqrMagnitude < 0.0025f ||
                        stateElapsed >= config.ReturnTimeout)
                        SetState(ShopClawMachineState.Release);
                    break;
                case ShopClawMachineState.Release:
                    if ((stateElapsed >= config.ReleaseDuration &&
                         AreFingersAtTarget(config.OpenFingerAngle, 4f)) ||
                        stateElapsed >= config.ReleaseTimeout)
                        SetState(ShopClawMachineState.Judge);
                    break;
                case ShopClawMachineState.Judge:
                    if (stateElapsed >= config.JudgeTimeout)
                        SetState(ShopClawMachineState.Cooldown);
                    break;
                case ShopClawMachineState.Cooldown:
                    if (stateElapsed >= 12f)
                        ServerResetMachine();
                    break;
            }
            ServerDrivePhysicalClaw();
        }

        private void Update()
        {
            if (Debug.isDebugBuild && Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
                showDebug = !showDebug;
            ApplyVisualState();
            UpdateAimGroundMarker();
            ApplyUpgradeAppearance();
            UpdateDebugText();
            if (!IsClient || !IsSpawned || NetworkManager == null ||
                !NetworkManager.IsListening || !NetworkManager.IsConnectedClient)
            {
                if (localMode) ExitLocalMode();
                return;
            }

            bool shouldBeLocal = OccupantClientId.Value == NetworkManager.LocalClientId;
            if (shouldBeLocal && !localMode) EnterLocalMode();
            if (!shouldBeLocal && localMode) ExitLocalMode();
            if (localMode) UpdateLocalOperator();
            if (qaClient && NetworkManager.IsConnectedClient) UpdateQaClient();
        }

        private void EnsureAimGroundMarker()
        {
            if (aimGroundMarker != null) return;
            aimGroundMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            aimGroundMarker.name = "ClawGroundAimMarker";
            aimGroundMarker.transform.SetParent(transform, false);
            aimGroundMarker.transform.localScale = new Vector3(0.34f, 0.006f, 0.34f);
            Collider markerCollider = aimGroundMarker.GetComponent<Collider>();
            if (markerCollider != null) Destroy(markerCollider);
            Renderer markerRenderer = aimGroundMarker.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            markerRenderer.material = new Material(shader);
            markerRenderer.material.color = new Color(0.03f, 0.03f, 0.04f, 0.5f);
            markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markerRenderer.receiveShadows = false;
        }

        private void UpdateAimGroundMarker()
        {
            if (aimGroundMarker == null || config == null) return;
            Vector2 rail = RailPosition.Value;
            aimGroundMarker.transform.localPosition = new Vector3(rail.x, config.DropHeight - 0.34f, rail.y);
            aimGroundMarker.SetActive(State.Value != ShopClawMachineState.Idle || localMode);
        }

        private void ServerUpdateAiming(float dt)
        {
            aimRemaining = Mathf.Max(0f, aimRemaining - dt);
            AimSecondsRemaining.Value = Mathf.CeilToInt(aimRemaining);
            if (OperatorInput.Value.sqrMagnitude > 0.01f)
                autoDropIdleElapsed = 0f;
            else
                autoDropIdleElapsed += dt;
            AutoDropSecondsRemaining.Value = Mathf.Clamp(
                Mathf.CeilToInt(config.AutoDropDelay - autoDropIdleElapsed), 0,
                Mathf.CeilToInt(config.AutoDropDelay));
            Vector2 desired = Vector2.ClampMagnitude(OperatorInput.Value, 1f) * EffectiveMoveSpeed;
            railVelocity = Vector2.MoveTowards(railVelocity, desired, config.Acceleration * dt);
            RailPosition.Value = ShopClawRules.ClampRail(RailPosition.Value + railVelocity * dt,
                config.XBounds, config.ZBounds);
            if (autoDropIdleElapsed >= config.AutoDropDelay || aimRemaining <= 0f)
            {
                ResultMessage.Value = new FixedString128Bytes(aimRemaining <= 0f
                    ? "조작 시간이 끝나 자동으로 투하합니다."
                    : "3초 동안 이동이 없어 자동으로 투하합니다.");
                ServerBeginDrop(AttemptId.Value);
            }
        }

        private void UpdatePhysicalGripDiagnostics()
        {
            ShopClawPrizeNetwork best = null;
            int bestContacts = 0;
            foreach (ShopClawPrizeNetwork prize in activePrizes)
            {
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value) continue;
                int contacts = CountDistinctFingerContacts(prize);
                if (contacts <= bestContacts) continue;
                bestContacts = contacts;
                best = prize;
            }

            LastGripScore.Value = bestContacts / 3f * 100f;
            LastJointBreakForce.Value = State.Value == ShopClawMachineState.Ascend ||
                                        State.Value == ShopClawMachineState.Return
                ? EffectiveCloseMotorTorque * config.AscentGripTorqueMultiplier
                : EffectiveCloseMotorTorque;

            if (best != null && bestContacts > 0 && State.Value == ShopClawMachineState.Close &&
                best.Body != null)
            {
                Vector3 clawCenter = clawHead != null ? clawHead.position : transform.position;
                Vector3 towardCenter = Vector3.ProjectOnPlane(
                    clawCenter - GetPrizePhysicalCenter(best), transform.up);
                if (towardCenter.sqrMagnitude > 0.0001f)
                    best.Body.AddForce(towardCenter.normalized *
                        (EffectiveCloseMotorTorque * config.ContactCenteringMultiplier * bestContacts),
                        ForceMode.Force);
            }

            if (best != null && bestContacts >= 2)
            {
                HeldPrizeNetworkObjectId.Value = best.NetworkObjectId;
                if (State.Value == ShopClawMachineState.Ascend &&
                    GetPrizePhysicalCenter(best).y > transform.TransformPoint(
                        new Vector3(0f, config.DropHeight + 0.28f, 0f)).y)
                    roundHadPhysicalLift = true;
                ResultMessage.Value = new FixedString128Bytes("두 개 이상의 발톱이 상품을 물고 있습니다.");
            }
            else
            {
                if (HeldPrizeNetworkObjectId.Value != 0)
                    ResultMessage.Value = new FixedString128Bytes("상품이 마찰을 이기지 못하고 미끄러졌습니다.");
                HeldPrizeNetworkObjectId.Value = 0;
            }
        }

        public void ServerObserveChutePrize(ShopClawPrizeNetwork prize, Collider chuteVolume)
        {
            if (!IsServer || prize == null || !prize.IsSpawned || chuteVolume == null) return;
            if (!chargedAttempts.Contains(AttemptId.Value) ||
                !ShopClawRules.CanAwardChutePrize(State.Value)) return;
            if (!TryGetPrizePhysicalBounds(prize, out Bounds prizeBounds) ||
                !ShopClawRules.IsFullyInsideChute(prizeBounds, chuteVolume.bounds, 0.04f) ||
                !ShopClawRules.IsChuteSettled(prize.Body.linearVelocity, prize.Body.angularVelocity,
                    config.ChuteSettleLinearSpeed))
            {
                chuteStableSeconds.Remove(prize.NetworkObjectId);
                chuteLastObservationTime.Remove(prize.NetworkObjectId);
                return;
            }

            float now = Time.fixedTime;
            if (chuteLastObservationTime.TryGetValue(prize.NetworkObjectId, out float previousTime) &&
                Mathf.Approximately(previousTime, now)) return;
            chuteLastObservationTime[prize.NetworkObjectId] = now;
            float stable = chuteStableSeconds.TryGetValue(prize.NetworkObjectId, out float current)
                ? current + Time.fixedDeltaTime
                : Time.fixedDeltaTime;
            chuteStableSeconds[prize.NetworkObjectId] = stable;
            if (stable < config.ChuteSettleDuration) return;
            ServerAwardChutePrize(prize);
        }

        public void ServerForgetChutePrize(ShopClawPrizeNetwork prize)
        {
            if (prize == null) return;
            chuteStableSeconds.Remove(prize.NetworkObjectId);
            chuteLastObservationTime.Remove(prize.NetworkObjectId);
        }

        private void ServerAwardChutePrize(ShopClawPrizeNetwork prize)
        {
            bool belongs = prize.MachineNetworkObjectId.Value == NetworkObjectId;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (!belongs || prize.Awarded.Value) return;
            if (config != null && config.MultiPrizePolicy == ShopMultiPrizePolicy.SingleAndReturnExtras &&
                roundAwardCount > 0)
            {
                ResultMessage.Value = new FixedString128Bytes("기본 기계는 한 판에 1개만 획득합니다. 추가 캡슐을 부드럽게 돌려보냅니다.");
                ServerReturnPrizeToField(prize);
                return;
            }
            ShopProductRarity spawnedRarity = (ShopProductRarity)Mathf.Clamp(
                prize.SpawnedRarity.Value, 0, (int)ShopProductRarity.UltraRare);
            ShopProductDefinition product = FindProductForRarity(spawnedRarity,
                AttemptId.Value * 397 ^ (int)prize.NetworkObjectId);
            int awardedVisualIndex = product != null
                ? ShopClawPrizeNetwork.FindCatalogIndex(product.PrizePrefab)
                : prize.VisualPrefabIndex.Value;
            if (game == null || !game.ServerCanAcquireItem(OccupantClientId.Value, product))
            {
                ResultMessage.Value = new FixedString128Bytes("인벤토리와 창고가 가득 차 상품을 받을 수 없습니다.");
                game?.ServerSetEvent("인벤토리와 창고가 모두 가득 차 상품을 플레이필드로 돌려보냈습니다.");
                ServerReturnPrizeToField(prize);
                return;
            }
            ShopContainerKind destination = ShopContainerKind.PersonalInventory;
            bool stored = game != null && game.ServerTryAcquireItem(OccupantClientId.Value, product,
                awardedVisualIndex, out destination);
            if (!stored)
            {
                ResultMessage.Value = new FixedString128Bytes("인벤토리와 창고가 가득 차 상품을 받을 수 없습니다.");
                game?.ServerSetEvent("인벤토리와 창고가 모두 가득 차 상품을 플레이필드로 돌려보냈습니다.");
                ServerReturnPrizeToField(prize);
                return;
            }
            if (!awardLedger.TryAward(prize.NetworkObjectId, true, true, false)) return;
            if (!prize.ServerMarkAwarded()) return;
            awardedAttemptId = AttemptId.Value;
            roundAwardCount++;
            game.ServerRecordAcquired(1);
            ShopProgressionManager progression = ShopProgressionManager.Instance;
            if (progression == null)
                Debug.LogError("[Progression] 컬렉션 관리자를 찾지 못했습니다.", this);
            else
                progression.RecordAcquisition(
                    product != null ? "product:" + product.ProductId : "claw:" + awardedVisualIndex,
                    product != null ? product.DisplayName : "인형뽑기 상품",
                    product != null ? product.Category.ToString().ToLowerInvariant() : "claw",
                    product != null && product.Rarity >= ShopProductRarity.Rare);
            LastAwardedName.Value = new FixedString64Bytes(
                product != null ? product.DisplayName : prize.VisualDisplayName.Value.ToString());
            LastAwardedRarity.Value = product != null ? (int)product.Rarity : 0;
            LastAwardedCapsuleColor.Value = prize.VisualColor.Value;
            AwardedCount.Value++;
            LastResultSuccess.Value = true;
            ResultMessage.Value = new FixedString128Bytes(destination == ShopContainerKind.PersonalInventory
                ? "뽑기 성공! 개인 인벤토리에 상품이 들어왔습니다."
                : "뽑기 성공! 인벤토리가 가득 차 공용 창고로 이동했습니다.");
            game.ServerSetEvent("인형뽑기 성공! " +
                                (product != null ? product.DisplayName : "상품") +
                                (destination == ShopContainerKind.PersonalInventory
                                    ? " 1개를 개인 인벤토리에 획득했습니다."
                                    : " 1개가 공용 창고로 자동 이동했습니다."));
            StartCoroutine(ServerDespawnAwardedPrize(prize));
        }

        private void OnAwardedCountChanged(int previous, int current)
        {
            if (current <= previous) return;
            ShopCapsuleOpeningPresenter.Show(
                LastAwardedName.Value.ToString(),
                (ShopProductRarity)Mathf.Clamp(LastAwardedRarity.Value, 0, 3),
                LastAwardedCapsuleColor.Value);
        }

        private void HandleDayAndPhase(float dt)
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null) return;
            if (game.Day.Value != observedDay)
            {
                observedDay = game.Day.Value;
                ServerResetMachine();
                ServerRefillPrizes();
                return;
            }

            if (ShopClawRules.CanOperateDuring(game.Phase.Value))
            {
                phaseExitElapsed = 0f;
                return;
            }

            if (State.Value == ShopClawMachineState.Aiming ||
                State.Value == ShopClawMachineState.Reserved)
            {
                ResultMessage.Value = new FixedString128Bytes("상품 획득 단계가 끝나 조작을 취소했습니다.");
                ServerResetMachine();
            }
            else if (State.Value != ShopClawMachineState.Idle)
            {
                phaseExitElapsed += dt;
                // A paid drop that crossed the opening/closing boundary must finish naturally.
                // State-specific timeouts still guarantee recovery if physics becomes stuck.
            }
        }

        private void SetState(ShopClawMachineState next)
        {
            State.Value = next;
            stateElapsed = 0f;
            if (next == ShopClawMachineState.Descend || next == ShopClawMachineState.Ascend)
                verticalVelocity = 0f;
            if (next == ShopClawMachineState.Release)
            {
                HeldPrizeNetworkObjectId.Value = 0;
                chuteStableSeconds.Clear();
                chuteLastObservationTime.Clear();
            }
            if (next == ShopClawMachineState.Cooldown && IsServer && ShopNetworkGame.Instance != null)
            {
                LastResultSuccess.Value = roundAwardCount > 0;
                ShopNetworkGame.Instance.ServerRecordClawResult(LastResultSuccess.Value);
                ResultMessage.Value = new FixedString128Bytes(LastResultSuccess.Value
                    ? roundAwardCount + "개 획득! WASD를 누르면 바로 다음 판을 시작합니다."
                    : roundHadPhysicalLift
                        ? "상품이 미끄러졌습니다. WASD를 누르면 바로 다시 도전합니다."
                        : "뽑기 실패. WASD를 누르면 바로 다시 도전합니다.");
            }
        }

        private void ServerPrepareNextAttempt()
        {
            if (!IsServer || OccupantClientId.Value == ShopClawRules.NoOccupant) return;
            HeldPrizeNetworkObjectId.Value = 0;
            OperatorInput.Value = Vector2.zero;
            railVelocity = Vector2.zero;
            verticalVelocity = 0f;
            FingerClosed.Value = 0f;
            RailPosition.Value = Vector2.zero;
            ClawHeight.Value = config.TopHeight;
            AttemptId.Value++;
            aimRemaining = EffectiveAimDuration;
            AimSecondsRemaining.Value = Mathf.CeilToInt(aimRemaining);
            autoDropIdleElapsed = 0f;
            AutoDropSecondsRemaining.Value = Mathf.CeilToInt(config.AutoDropDelay);
            LastGripScore.Value = 0f;
            LastJointBreakForce.Value = 0f;
            LastResultSuccess.Value = false;
            roundAwardCount = 0;
            roundHadPhysicalLift = false;
            chuteStableSeconds.Clear();
            chuteLastObservationTime.Clear();
            ResultMessage.Value = new FixedString128Bytes("다음 판 준비 완료. WASD로 위치를 정하세요.");
            SetState(ShopClawMachineState.Reserved);
        }

        private void ServerResetMachine()
        {
            if (!IsServer) return;
            HeldPrizeNetworkObjectId.Value = 0;
            OperatorInput.Value = Vector2.zero;
            railVelocity = Vector2.zero;
            verticalVelocity = 0f;
            FingerClosed.Value = 0f;
            RailPosition.Value = Vector2.zero;
            ClawHeight.Value = config.TopHeight;
            AimSecondsRemaining.Value = 0;
            AutoDropSecondsRemaining.Value = 0;
            autoDropIdleElapsed = 0f;
            OccupantClientId.Value = ShopClawRules.NoOccupant;
            roundAwardCount = 0;
            roundHadPhysicalLift = false;
            chuteStableSeconds.Clear();
            chuteLastObservationTime.Clear();
            SetState(ShopClawMachineState.Idle);
        }

        private IEnumerator ServerSpawnPrizesAfterFrame()
        {
            yield return null;
            ServerRefillPrizes();
        }

        private void ServerRefillPrizes()
        {
            ShopClawPrizePool pool = config != null ? config.PrizePool : null;
            bool hasPool = pool != null && pool.Entries != null && pool.Entries.Count > 0;
            if (!IsServer || prizePrefab == null ||
                (!hasPool && (prizeDefinitions == null || prizeDefinitions.Length == 0))) return;
            foreach (ShopClawPrizeNetwork prize in activePrizes)
                if (prize != null && prize.IsSpawned) prize.NetworkObject.Despawn(true);
            activePrizes.Clear();
            awardLedger.Reset();
            AwardedCount.Value = 0;

            ShopProgressionManager progression = ShopProgressionManager.Instance;
            if (progression != null && config != null &&
                progression.TryConsumeClawMachineSave(config.MachineId, out ShopClawMachineSave saved))
            {
                if (saved.prizes != null && saved.prizes.Count > 0)
                {
                    ServerRestorePrizes(saved);
                    return;
                }
                Debug.Log("[ClawMachine] EMPTY_SAVE_REFILL machine=" + config.MachineId, this);
            }

            prizeRandom = new System.Random(unchecked((config != null ? config.MachineId : 0) * 73856093 ^
                                                       observedDay * 19349663 ^ AttemptId.Value));
            int count = hasPool
                ? pool.MaxConcurrentPrizes
                : Mathf.Max(6, prizeSpawnPoints != null ? prizeSpawnPoints.Length : 0);
            int attempts = hasPool ? pool.SpawnAttemptsPerPrize : 8;
            float clearance = hasPool ? pool.SpawnClearance : 0.035f;
            var occupied = new List<(Vector3 position, float radius)>();
            for (int i = 0; i < count; i++)
            {
                ShopProductRarity spawnedRarity = config != null
                    ? config.RarityWeights.Pick(prizeRandom, true)
                    : ShopProductRarity.Common;
                ShopClawPrizeDefinition definition = hasPool
                    ? pool.PickByRarity(spawnedRarity, prizeRandom)
                    : prizeDefinitions[prizeRandom.Next(prizeDefinitions.Length)];
                if (definition == null ||
                    !TryFindPrizeSpawn(definition, attempts, clearance, occupied, out Vector3 spawnPosition,
                        out Quaternion spawnRotation))
                {
                    Debug.Log("[ClawMachine] SPAWN_SKIPPED no non-overlapping position machine=" +
                              (config != null ? config.MachineId : 0) + " slot=" + i, this);
                    continue;
                }
                GameObject instance = Instantiate(prizePrefab, spawnPosition, spawnRotation);
                NetworkObject networkObject = instance.GetComponent<NetworkObject>();
                ShopClawPrizeNetwork prize = instance.GetComponent<ShopClawPrizeNetwork>();
                networkObject.Spawn(true);
                int visualIndex = definition.Product != null
                    ? ShopClawPrizeNetwork.FindCatalogIndex(definition.Product.PrizePrefab)
                    : -1;
                if (visualIndex < 0)
                    visualIndex = config != null ? config.MachineId * 11 + observedDay * 7 + i : i;
                int definitionIndex = FindDefinitionIndex(definition);
                prize.ServerInitialize(NetworkObjectId, definitionIndex, definition,
                    spawnPosition, spawnRotation, visualIndex, spawnedRarity);
                activePrizes.Add(prize);
                occupied.Add((spawnPosition, Mathf.Max(0.16f, definition.Size * 0.48f) + clearance));
            }
            Debug.Log("[ClawMachine] POOL_SPAWNED machine=" + (config != null ? config.MachineId : 0) +
                      " pool=" + (hasPool ? pool.PoolId : "legacy") + " count=" + activePrizes.Count +
                      " limit=" + count, this);
        }

        private void ServerRestorePrizes(ShopClawMachineSave saved)
        {
            if (saved?.prizes == null) return;
            foreach (ShopClawPrizeSave item in saved.prizes)
            {
                if (item == null) continue;
                ShopClawPrizeDefinition definition = FindDefinitionByProductId(item.productId);
                if (definition == null) continue;
                Vector3 local = item.localPosition;
                if (ShopClawRules.IsPrizeOutsidePlayableArea(local, config.XBounds, config.ZBounds,
                        0.35f, 0.45f, 3.7f)) continue;
                local.x = Mathf.Clamp(local.x, config.XBounds.x + 0.05f,
                    config.XBounds.y - 0.05f);
                local.z = Mathf.Clamp(local.z, config.ZBounds.x + 0.05f,
                    config.ZBounds.y - 0.05f);
                Vector3 position = transform.TransformPoint(local);
                Quaternion rotation = transform.rotation * item.localRotation;
                GameObject instance = Instantiate(prizePrefab, position, rotation);
                NetworkObject networkObject = instance.GetComponent<NetworkObject>();
                ShopClawPrizeNetwork prize = instance.GetComponent<ShopClawPrizeNetwork>();
                networkObject.Spawn(true);
                prize.ServerInitialize(NetworkObjectId, FindDefinitionIndex(definition), definition,
                    position, rotation, item.visualPrefabIndex,
                    (ShopProductRarity)Mathf.Clamp(item.rarity, 0, 3));
                activePrizes.Add(prize);
            }
            Debug.Log("[ClawMachine] SAVE_RESTORED machine=" + config.MachineId +
                      " count=" + activePrizes.Count, this);
        }

        public ShopClawMachineSave CaptureSaveState()
        {
            var saved = new ShopClawMachineSave
            {
                machineId = config != null ? config.MachineId : 0
            };
            foreach (ShopClawPrizeNetwork prize in activePrizes)
            {
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value) continue;
                saved.prizes.Add(new ShopClawPrizeSave
                {
                    productId = prize.ProductId.Value,
                    rarity = prize.SpawnedRarity.Value,
                    visualPrefabIndex = prize.VisualPrefabIndex.Value,
                    localPosition = transform.InverseTransformPoint(prize.transform.position),
                    localRotation = Quaternion.Inverse(transform.rotation) * prize.transform.rotation
                });
            }
            return saved;
        }

        private ShopClawPrizeDefinition FindDefinitionByProductId(int productId)
        {
            if (prizeDefinitions != null)
                foreach (ShopClawPrizeDefinition definition in prizeDefinitions)
                    if (definition?.Product != null && definition.Product.ProductId == productId)
                        return definition;
            ShopClawPrizePool pool = config != null ? config.PrizePool : null;
            if (pool != null)
                foreach (ShopClawPrizePoolEntry entry in pool.Entries)
                    if (entry?.Prize?.Product != null && entry.Prize.Product.ProductId == productId)
                        return entry.Prize;
            return null;
        }

        private bool TryFindPrizeSpawn(ShopClawPrizeDefinition definition, int attempts, float clearance,
            List<(Vector3 position, float radius)> occupied, out Vector3 position, out Quaternion rotation)
        {
            position = transform.position;
            rotation = Quaternion.identity;
            float radius = Mathf.Max(0.16f, definition.Size * 0.48f) + clearance;
            int spawnCount = prizeSpawnPoints != null ? prizeSpawnPoints.Length : 0;
            int spawnOffset = spawnCount > 0 ? prizeRandom.Next(spawnCount) : 0;
            for (int attempt = 0; attempt < Mathf.Max(1, attempts); attempt++)
            {
                Transform spawn = spawnCount > 0
                    ? prizeSpawnPoints[(spawnOffset + attempt) % spawnCount]
                    : transform;
                float jitterX = (float)(prizeRandom.NextDouble() * 2.0 - 1.0) * 0.16f;
                float jitterZ = (float)(prizeRandom.NextDouble() * 2.0 - 1.0) * 0.16f;
                Vector3 candidate = spawn.position + transform.right * jitterX + transform.forward * jitterZ;
                Vector3 candidateLocal = transform.InverseTransformPoint(candidate);
                candidateLocal.x = Mathf.Clamp(candidateLocal.x, config.XBounds.x + 0.05f,
                    config.XBounds.y - 0.05f);
                candidateLocal.z = Mathf.Clamp(candidateLocal.z, config.ZBounds.x + 0.05f,
                    config.ZBounds.y - 0.05f);
                candidate = transform.TransformPoint(candidateLocal);
                var occupiedPositions = new List<Vector3>(occupied.Count);
                var occupiedRadii = new List<float>(occupied.Count);
                foreach ((Vector3 otherPosition, float otherRadius) in occupied)
                {
                    occupiedPositions.Add(otherPosition);
                    occupiedRadii.Add(otherRadius);
                }
                if (!ShopClawSpawnRules.CanPlace(candidate, radius, occupiedPositions, occupiedRadii))
                    continue;
                position = candidate;
                rotation = Quaternion.Euler(0f, (float)prizeRandom.NextDouble() * 360f, 0f);
                return true;
            }
            return false;
        }

        private int FindDefinitionIndex(ShopClawPrizeDefinition target)
        {
            if (prizeDefinitions == null) return 0;
            for (int i = 0; i < prizeDefinitions.Length; i++)
                if (prizeDefinitions[i] == target) return i;
            return 0;
        }

        private void ServerRecoverOutOfBoundsPrizes()
        {
            if (!IsServer || config == null || prizeSpawnPoints == null || prizeSpawnPoints.Length == 0) return;
            foreach (ShopClawPrizeNetwork prize in activePrizes)
            {
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value) continue;
                Vector3 local = transform.InverseTransformPoint(prize.transform.position);
                bool outside = ShopClawRules.IsPrizeOutsidePlayableArea(local, config.XBounds, config.ZBounds);
                Vector3 chuteLocal = chuteDropPoint != null ? chuteDropPoint.localPosition : Vector3.zero;
                bool stuckAtChuteLip = Mathf.Abs(local.x - chuteLocal.x) < 0.72f &&
                                      Mathf.Abs(local.z - chuteLocal.z) < 0.62f &&
                                      local.y > 0.72f && local.y < 1.55f &&
                                      prize.Body.linearVelocity.sqrMagnitude < 0.018f;
                if (!outside && !stuckAtChuteLip)
                {
                    abnormalStuckSeconds.Remove(prize.NetworkObjectId);
                    continue;
                }

                float stuck = abnormalStuckSeconds.TryGetValue(prize.NetworkObjectId, out float elapsed)
                    ? elapsed + 1f
                    : 1f;
                abnormalStuckSeconds[prize.NetworkObjectId] = stuck;
                if (stuck < config.AntiStuckDelay) continue;

                abnormalStuckSeconds.Remove(prize.NetworkObjectId);
                if (outside)
                {
                    ServerReturnPrizeToField(prize);
                    Debug.Log("[ClawMachine] RECOVERED_OUT_OF_BOUNDS prize=" + prize.NetworkObjectId);
                }
                else
                {
                    prize.Body.AddForce((transform.forward + Vector3.up * 0.5f) * 0.32f,
                        ForceMode.VelocityChange);
                    Debug.Log("[ClawMachine] NUDGED_CHUTE_LIP prize=" + prize.NetworkObjectId);
                }
            }
        }

        private void ServerReturnPrizeToField(ShopClawPrizeNetwork prize)
        {
            if (prize == null || prizeSpawnPoints == null || prizeSpawnPoints.Length == 0) return;
            Vector3 local = transform.InverseTransformPoint(prize.transform.position);
            Transform nearest = prizeSpawnPoints[0];
            float nearestDistance = float.MaxValue;
            foreach (Transform spawn in prizeSpawnPoints)
            {
                if (spawn == null) continue;
                float distance = (spawn.localPosition - local).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = spawn;
            }
            prize.ServerReturnToField(nearest.position + Vector3.up * 0.12f, nearest.rotation);
        }

        private IEnumerator ServerDespawnAwardedPrize(ShopClawPrizeNetwork prize)
        {
            yield return new WaitForSeconds(0.6f);
            if (prize != null && prize.IsSpawned) prize.NetworkObject.Despawn(true);
        }

        private ShopProductDefinition FindProduct(int productId)
        {
            if (prizeDefinitions != null)
                foreach (ShopClawPrizeDefinition definition in prizeDefinitions)
                    if (definition != null && definition.Product != null && definition.Product.ProductId == productId)
                        return definition.Product;
            ShopClawPrizePool pool = config != null ? config.PrizePool : null;
            if (pool != null)
                foreach (ShopClawPrizePoolEntry entry in pool.Entries)
                    if (entry?.Prize != null && entry.Prize.Product != null &&
                        entry.Prize.Product.ProductId == productId)
                        return entry.Prize.Product;
            return null;
        }

        private static ShopProductDefinition FindProductForRarity(ShopProductRarity rarity, int seed)
        {
            ShopProductDefinition[] products = Resources.LoadAll<ShopProductDefinition>("Products");
            List<ShopProductDefinition> matches = new();
            for (int i = 0; i < products.Length; i++)
                if (products[i] != null && products[i].Rarity == rarity)
                    matches.Add(products[i]);
            if (matches.Count == 0 && rarity == ShopProductRarity.UltraRare)
            {
                for (int i = 0; i < products.Length; i++)
                    if (products[i] != null && products[i].Rarity == ShopProductRarity.Rare)
                        matches.Add(products[i]);
            }
            if (matches.Count == 0) return products.Length > 0 ? products[0] : null;
            var random = new System.Random(seed);
            return matches[random.Next(matches.Count)];
        }

        private bool TryGetPlayer(ulong clientId, out NetworkObject player)
        {
            player = null;
            return NetworkManager != null && NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                   (player = client.PlayerObject) != null;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer || OccupantClientId.Value != clientId) return;
            ResultMessage.Value = new FixedString128Bytes("연결 종료로 기계를 초기화했습니다.");
            ServerResetMachine();
        }

        private void ApplyVisualState()
        {
            if (clawHead != null && (!IsServer || !physicalClawReady))
                clawHead.localPosition = new Vector3(RailPosition.Value.x, ClawHeight.Value, RailPosition.Value.y);
            if (cable != null)
            {
                float ceiling = config != null ? config.TopHeight + 0.55f : 4.1f;
                Vector3 clawLocal = physicalClawReady && clawHead != null
                    ? transform.InverseTransformPoint(clawHead.position)
                    : new Vector3(RailPosition.Value.x, ClawHeight.Value, RailPosition.Value.y);
                float length = Mathf.Max(0.08f, ceiling - clawLocal.y);
                cable.localPosition = new Vector3(clawLocal.x, clawLocal.y + length * 0.5f, clawLocal.z);
                cable.localScale = new Vector3(0.035f, length * 0.5f, 0.035f);
            }
            if (joystickStick != null)
                joystickStick.localRotation = Quaternion.Euler(OperatorInput.Value.y * 16f, 0f, -OperatorInput.Value.x * 16f);
            if (statusLamp != null)
            {
                Color color = LastResultSuccess.Value && State.Value == ShopClawMachineState.Cooldown ? Color.green :
                    State.Value == ShopClawMachineState.Idle ? new Color(0.25f, 1f, 0.55f) :
                    State.Value == ShopClawMachineState.Descend ? new Color(1f, 0.6f, 0.1f) : new Color(0.25f, 0.65f, 1f);
                MaterialPropertyBlock block = new();
                statusLamp.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_EmissionColor", color * 1.5f);
                statusLamp.SetPropertyBlock(block);
            }
        }

        private void EnterLocalMode()
        {
            if (localMode || NetworkManager == null) return;
            if (LocalActiveMachine != null && LocalActiveMachine != this)
                LocalActiveMachine.ExitLocalMode();
            LocalActiveMachine = this;
            localMode = true;
            localModeEnteredAt = Time.unscaledTime;
            lastObservedResult = ResultMessage.Value.ToString();
            toastUntil = 0f;
            ShopInputModeManager.Push(this, ShopInputMode.Claw);
            SetCameraPreset(0, true);
            localPlayer = NetworkManager.LocalClient != null ? NetworkManager.LocalClient.PlayerObject : null;
            if (localPlayer != null)
            {
                CharacterController controller = localPlayer.GetComponent<CharacterController>();
                if (controller != null) controller.enabled = false;
                localPlayer.transform.SetPositionAndRotation(operatorPoint.position, operatorPoint.rotation);
                if (controller != null) controller.enabled = true;
                foreach (Renderer renderer in localPlayer.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.forceRenderingOff)
                    {
                        renderer.forceRenderingOff = true;
                        hiddenLocalRenderers.Add(renderer);
                    }
                }
            }

            previousCamera = Camera.main;
            if (previousCamera != null)
            {
                previousListener = previousCamera.GetComponent<AudioListener>();
                previousCamera.enabled = false;
                if (previousListener != null) previousListener.enabled = false;
            }
            if (operatorCamera != null)
            {
                operatorCamera.fieldOfView = config != null ? config.OperatorCameraFieldOfView : 60f;
                UpdateOperatorCamera(true);
                operatorCamera.enabled = true;
                if (operatorListener != null) operatorListener.enabled = true;
            }
            HideGlassForLocalOperator();
            HideOverheadForLocalOperator();
            HideForegroundForLocalOperator();
            if (operatorHud != null) operatorHud.gameObject.SetActive(true);
            if (cameraTransition != null) StartCoroutine(FadeCameraTransition());
        }

        private void ExitLocalMode()
        {
            if (!localMode) return;
            localMode = false;
            ShopInputModeManager.Pop(this);
            if (LocalActiveMachine == this) LocalActiveMachine = null;
            if (operatorCamera != null) operatorCamera.enabled = false;
            if (operatorListener != null) operatorListener.enabled = false;
            if (previousCamera != null) previousCamera.enabled = true;
            if (previousListener != null) previousListener.enabled = true;
            RestoreGlassAfterLocalOperator();
            RestoreOverheadAfterLocalOperator();
            RestoreForegroundAfterLocalOperator();
            disabledPlayerBehaviours.Clear();
            foreach (Renderer renderer in hiddenLocalRenderers)
                if (renderer != null) renderer.forceRenderingOff = false;
            hiddenLocalRenderers.Clear();
            if (operatorHud != null) operatorHud.gameObject.SetActive(false);
            if (cameraTransition != null) StartCoroutine(FadeCameraTransition());
        }

        private IEnumerator FadeCameraTransition()
        {
            cameraTransition.gameObject.SetActive(true);
            cameraTransition.alpha = 1f;
            const float duration = 0.3f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                cameraTransition.alpha = 1f - Mathf.Clamp01(elapsed / duration);
                yield return null;
            }
            cameraTransition.alpha = 0f;
        }

        private void UpdateLocalOperator()
        {
            Vector2 input = Vector2.zero;
            if (Keyboard.current != null)
            {
                input.x = (Keyboard.current.dKey.isPressed ? 1f : 0f) - (Keyboard.current.aKey.isPressed ? 1f : 0f);
                input.y = (Keyboard.current.wKey.isPressed ? 1f : 0f) - (Keyboard.current.sKey.isPressed ? 1f : 0f);
                if (State.Value == ShopClawMachineState.Aiming &&
                    Keyboard.current.spaceKey.wasPressedThisFrame) RequestDrop();
                if ((State.Value == ShopClawMachineState.Aiming ||
                     State.Value == ShopClawMachineState.Reserved || State.Value == ShopClawMachineState.Cooldown) &&
                    Keyboard.current.escapeKey.wasPressedThisFrame) RequestCancel();
                if (Keyboard.current.cKey.wasPressedThisFrame)
                    SetCameraPreset((localCameraPreset + 1) % 4, false);
                if (Keyboard.current.digit1Key.wasPressedThisFrame) SetCameraPreset(0, false);
                if (Keyboard.current.digit2Key.wasPressedThisFrame) SetCameraPreset(1, false);
                if (Keyboard.current.digit3Key.wasPressedThisFrame) SetCameraPreset(2, false);
                if (Keyboard.current.digit4Key.wasPressedThisFrame) SetCameraPreset(3, false);
            }
            if (Gamepad.current != null)
            {
                if (Gamepad.current.leftStick.ReadValue().sqrMagnitude > input.sqrMagnitude)
                    input = Gamepad.current.leftStick.ReadValue();
                if (State.Value == ShopClawMachineState.Aiming &&
                    Gamepad.current.buttonSouth.wasPressedThisFrame) RequestDrop();
                if ((State.Value == ShopClawMachineState.Aiming ||
                     State.Value == ShopClawMachineState.Reserved || State.Value == ShopClawMachineState.Cooldown) &&
                    Gamepad.current.buttonEast.wasPressedThisFrame) RequestCancel();
            }
            // Movement is always machine-relative. Camera orbit never changes WASD semantics.

            if (State.Value == ShopClawMachineState.Cooldown &&
                input.sqrMagnitude > 0.05f && requestedReplayAttempt != AttemptId.Value)
            {
                requestedReplayAttempt = AttemptId.Value;
                RequestReplay();
            }

            if (State.Value == ShopClawMachineState.Aiming &&
                (Time.unscaledTime >= nextInputSendTime || (input - lastSentInput).sqrMagnitude > 0.02f))
            {
                nextInputSendTime = Time.unscaledTime + config.InputSendInterval;
                lastSentInput = input;
                RequestInput(input);
            }

            Vector2 look = Mouse.current != null && !ShopInputModeManager.SuppressLookThisFrame
                ? Mouse.current.delta.ReadValue() * 0.055f
                : Vector2.zero;
            if (Gamepad.current != null) look += Gamepad.current.rightStick.ReadValue() * 1.5f;
            float keyboardOrbit = 0f;
            if (Keyboard.current != null)
                keyboardOrbit = (Keyboard.current.eKey.isPressed ? 1f : 0f) -
                                (Keyboard.current.qKey.isPressed ? 1f : 0f);
            localLookYaw = Mathf.Clamp(localLookYaw + look.x + keyboardOrbit * 70f * Time.unscaledDeltaTime,
                -115f, 115f);
            localLookPitch = Mathf.Clamp(localLookPitch - look.y, 12f, 62f);
            UpdateOperatorCamera(false);
            UpdateOperatorHud();
        }

        private void UpdateOperatorHud()
        {
            EnsureMinimalHud();
            UpdateCountdownUi();
            if (operatorHudText == null || config == null) return;
            int coins = ShopNetworkGame.Instance != null ? ShopNetworkGame.Instance.Coins.Value : 0;
            operatorHudText.text = config.DisplayName + "     조작 " + AimSecondsRemaining.Value +
                                   "초  ·  자동 투하 " + AutoDropSecondsRemaining.Value + "초";
            operatorHudText.color = AimSecondsRemaining.Value <= 5 && State.Value == ShopClawMachineState.Aiming
                ? new Color(1f, 0.3f, 0.18f)
                : Color.white;
            if (costText != null)
                costText.text = "1회 " + config.AttemptCost + "원   ·   가게 자금 " + coins + "원";
            if (instructionText != null)
            {
                if (localInstructionAttempt != AttemptId.Value)
                {
                    localInstructionAttempt = AttemptId.Value;
                    localModeEnteredAt = Time.unscaledTime;
                }
                bool showInstructions = Time.unscaledTime - localModeEnteredAt <= 4.5f;
                instructionText.gameObject.SetActive(showInstructions);
                if (showInstructions)
                    instructionText.text = "WASD 이동  ·  Space 투하  ·  마우스/QE 시점  ·  C 카메라  ·  Esc 나가기";
            }

            if (instructionText != null && instructionText.gameObject.activeSelf)
                instructionText.text += "\n시점을 돌려도 WASD 방향은 기계 기준으로 고정됩니다.";

            string currentResult = ResultMessage.Value.ToString();
            if (currentResult != lastObservedResult)
            {
                lastObservedResult = currentResult;
                toastUntil = Time.unscaledTime + 2.6f;
            }
            if (toastText != null)
            {
                bool showToast = Time.unscaledTime < toastUntil &&
                                 State.Value != ShopClawMachineState.Aiming;
                toastText.gameObject.SetActive(showToast);
                if (showToast) toastText.text = currentResult;
            }
        }

        private void EnsureMinimalHud()
        {
            if (operatorHud == null || operatorHudText == null) return;
            RectTransform canvasRect = operatorHud.GetComponent<RectTransform>();
            if (canvasRect != null)
            {
                canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
                canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
                canvasRect.pivot = new Vector2(0.5f, 0.5f);
                canvasRect.sizeDelta = new Vector2(1920f, 1080f);
                canvasRect.anchoredPosition = Vector2.zero;
                canvasRect.localRotation = Quaternion.identity;
                canvasRect.localScale = Vector3.one;
            }
            operatorHud.overrideSorting = true;
            operatorHud.sortingOrder = 30010;
            Transform oldPanel = operatorHudText.transform.parent;
            if (operatorHudText.transform.parent != operatorHud.transform)
                operatorHudText.transform.SetParent(operatorHud.transform, false);
            if (oldPanel != null && oldPanel != operatorHud.transform)
                oldPanel.gameObject.SetActive(false);
            operatorHudText.gameObject.SetActive(true);
            RectTransform top = operatorHudText.rectTransform;
            top.anchorMin = top.anchorMax = new Vector2(0.5f, 1f);
            top.pivot = new Vector2(0.5f, 1f);
            top.anchoredPosition = new Vector2(0f, -24f);
            top.sizeDelta = new Vector2(920f, 64f);
            operatorHudText.fontSize = 30;
            operatorHudText.fontStyle = FontStyle.Bold;
            operatorHudText.alignment = TextAnchor.MiddleCenter;

            if (costText == null)
                costText = CreateHudText("ClawCost", new Vector2(560f, 52f),
                    new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(26f, 24f), 24, TextAnchor.MiddleLeft);
            if (instructionText == null)
                instructionText = CreateHudText("ClawInstructions", new Vector2(980f, 52f),
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 24f), 22, TextAnchor.MiddleCenter);
            if (toastText == null)
                toastText = CreateHudText("ClawToast", new Vector2(960f, 68f),
                    new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, 28, TextAnchor.MiddleCenter);
        }

        private Text CreateHudText(string objectName, Vector2 size,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position,
            int fontSize, TextAnchor alignment)
        {
            GameObject item = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text),
                typeof(Outline));
            item.transform.SetParent(operatorHud.transform, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Text text = item.GetComponent<Text>();
            text.font = operatorHudText.font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            Outline outline = item.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        public float EffectiveAimDuration => config == null
            ? 0f
            : config.AimDuration + (ShopNetworkGame.Instance != null
                ? ShopNetworkGame.Instance.ClawAimTimeBonus
                : 0f);

        public float EffectiveMoveSpeed => config == null
            ? 0f
            : config.MoveSpeed * (ShopNetworkGame.Instance != null
                ? ShopNetworkGame.Instance.ClawMoveSpeedMultiplier
                : 1f);

        public float EffectiveCloseMotorTorque => config == null
            ? 0f
            : config.CloseMotorTorque * (ShopNetworkGame.Instance != null
                ? ShopNetworkGame.Instance.ClawStrengthMultiplier
                : 1f);

        private void ApplyUpgradeAppearance()
        {
            int level = ShopNetworkGame.Instance != null
                ? ShopNetworkGame.Instance.ClawUpgradeLevel.Value
                : 0;
            if (level == appliedClawUpgradeAppearance || clawFingers == null) return;
            appliedClawUpgradeAppearance = level;
            Color color = level switch
            {
                1 => new Color(0.2f, 0.85f, 1f),
                2 => new Color(1f, 0.72f, 0.18f),
                _ => new Color(0.55f, 0.58f, 0.62f)
            };
            foreach (Transform finger in clawFingers)
            {
                if (finger == null) continue;
                foreach (Renderer renderer in finger.GetComponentsInChildren<Renderer>(true))
                {
                    MaterialPropertyBlock block = new();
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", color);
                    block.SetColor("_EmissionColor", level > 0 ? color * 0.55f : Color.black);
                    renderer.SetPropertyBlock(block);
                }
            }
        }

        private void CachePhysicalPresentation()
        {
            if (localGlassRenderers == null || localGlassRenderers.Length == 0)
            {
                Transform glass = transform.Find("Cabinet/Glass");
                if (glass != null) localGlassRenderers = glass.GetComponentsInChildren<Renderer>(true);
            }
            if (clawFingers != null && !physicalClawReady)
            {
                foreach (Transform finger in clawFingers)
                {
                    if (finger == null) continue;
                    foreach (Collider fingerCollider in finger.GetComponentsInChildren<Collider>(true))
                        if (fingerCollider != null) fingerCollider.isTrigger = true;
                }
            }
            if (clawFingerSensors != null && clawFingerSensors.Length > 0) return;
            var sensors = new List<Collider>();
            if (clawFingers != null)
            {
                foreach (Transform finger in clawFingers)
                {
                    if (finger == null) continue;
                    foreach (Collider sensor in finger.GetComponentsInChildren<Collider>(true))
                        if (sensor != null && sensor.gameObject.name.Contains("센서")) sensors.Add(sensor);
                }
            }
            clawFingerSensors = sensors.ToArray();
        }

        private void SetupServerPhysicalClaw()
        {
            if (!IsServer || config == null || clawHead == null || clawBody == null || physicalClawReady) return;

            if (!ValidateAuthoredPhysicalClaw()) return;

            carriageBody.transform.localPosition =
                new Vector3(RailPosition.Value.x, ClawHeight.Value, RailPosition.Value.y);
            carriageBody.isKinematic = true;
            carriageBody.useGravity = false;
            carriageBody.interpolation = RigidbodyInterpolation.Interpolate;

            clawHead.position = carriageBody.position;
            clawBody.isKinematic = false;
            clawBody.useGravity = true;
            clawBody.mass = config.ClawMass;
            clawBody.linearDamping = 1.25f;
            clawBody.angularDamping = config.HousingSwingDamper;
            clawBody.interpolation = RigidbodyInterpolation.Interpolate;
            clawBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            clawBody.constraints = RigidbodyConstraints.None;
            clawBody.solverIterations = 12;
            clawBody.solverVelocityIterations = 6;
            clawBody.maxAngularVelocity = 8f;

            suspensionJoint.connectedBody = carriageBody;
            suspensionJoint.autoConfigureConnectedAnchor = false;
            suspensionJoint.anchor = Vector3.zero;
            suspensionJoint.connectedAnchor = Vector3.zero;
            suspensionJoint.xMotion = ConfigurableJointMotion.Limited;
            suspensionJoint.yMotion = ConfigurableJointMotion.Limited;
            suspensionJoint.zMotion = ConfigurableJointMotion.Limited;
            suspensionJoint.angularXMotion = ConfigurableJointMotion.Limited;
            suspensionJoint.angularYMotion = ConfigurableJointMotion.Limited;
            suspensionJoint.angularZMotion = ConfigurableJointMotion.Limited;
            suspensionJoint.linearLimit = new SoftJointLimit
            {
                limit = config.SuspensionTravel,
                bounciness = 0f,
                contactDistance = 0.015f
            };
            suspensionJoint.linearLimitSpring = new SoftJointLimitSpring
            {
                spring = config.SuspensionSpring,
                damper = config.SuspensionDamper
            };
            suspensionJoint.lowAngularXLimit = new SoftJointLimit { limit = -10f };
            suspensionJoint.highAngularXLimit = new SoftJointLimit { limit = 10f };
            suspensionJoint.angularYLimit = new SoftJointLimit { limit = 8f };
            suspensionJoint.angularZLimit = new SoftJointLimit { limit = 10f };
            suspensionJoint.projectionMode = JointProjectionMode.PositionAndRotation;
            suspensionJoint.projectionDistance = 0.06f;
            suspensionJoint.projectionAngle = 14f;
            suspensionJoint.enableCollision = false;

            if (config.MachineFloorMaterial != null)
            {
                foreach (Collider machineCollider in GetComponentsInChildren<Collider>(true))
                {
                    if (machineCollider == null || machineCollider.isTrigger) continue;
                    string colliderName = machineCollider.gameObject.name.ToLowerInvariant();
                    if (colliderName.Contains("floor") || colliderName.Contains("bed") ||
                        colliderName.Contains("bottom") || colliderName.Contains("바닥"))
                        machineCollider.material = config.MachineFloorMaterial;
                }
            }

            physicalFingerBodies.Clear();
            physicalFingerJoints.Clear();
            physicalFingerAngleOffsets.Clear();
            physicalFingerSensors.Clear();
            if (clawFingers != null)
            {
                foreach (Transform finger in clawFingers)
                {
                    if (finger == null) continue;
                    HingeJoint hinge = finger.GetComponent<HingeJoint>();
                    Vector3 authoredWorldPosition = finger.position;
                    Quaternion authoredWorldRotation = finger.rotation;
                    // Rigidbody children of another dynamic Rigidbody do not receive a stable,
                    // independent solver pose. Keep the authored components, but detach the
                    // three bodies while preserving their exact prefab world pose.
                    finger.SetParent(transform, true);
                    Rigidbody fingerBody = finger.GetComponent<Rigidbody>();
                    // Configure every joint while its authored open pose is frozen. Enabling
                    // gravity before all three anchors exist lets an asymmetric first solver
                    // step pull one finger down and can yield an invalid hinge angle.
                    fingerBody.isKinematic = true;
                    fingerBody.useGravity = false;
                    fingerBody.mass = Mathf.Max(0.08f, config.ClawMass * 0.12f);
                    fingerBody.linearDamping = 1.8f;
                    fingerBody.angularDamping = 2.4f;
                    fingerBody.interpolation = RigidbodyInterpolation.Interpolate;
                    fingerBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    fingerBody.solverIterations = 12;
                    fingerBody.solverVelocityIterations = 6;
                    foreach (Collider fingerCollider in finger.GetComponentsInChildren<Collider>(true))
                    {
                        fingerCollider.isTrigger = false;
                        if (config.ClawFingerMaterial != null)
                            fingerCollider.material = config.ClawFingerMaterial;
                    }

                    // Joint axis, anchors, limits, and initial motor are authored and saved by
                    // ShopPhysicalClawInstaller. Reassigning those structural fields here resets
                    // Unity's joint reference frame and exposes a transient NaN hinge angle.
                    float authoredClosedOffset = -config.ClosedFingerAngle +
                                                 config.ClosedFingerClearanceAngle;
                    ShopClawFingerContactSensor contactSensor =
                        finger.GetComponent<ShopClawFingerContactSensor>();
                    fingerBody.position = authoredWorldPosition;
                    fingerBody.rotation = authoredWorldRotation;
                    physicalFingerBodies.Add(fingerBody);
                    physicalFingerJoints.Add(hinge);
                    physicalFingerAngleOffsets.Add(authoredClosedOffset);
                    physicalFingerSensors.Add(contactSensor);
                }
            }
            Physics.SyncTransforms();
            physicalClawReady = true;
            CachePhysicalPresentation();
            LogPhysicalFingerLayout("setup");
            StartCoroutine(ActivateAuthoredFingerBodies());
            Debug.Log("[ClawMachine] PHYSICAL_CLAW_READY bodyMass=" + clawBody.mass +
                      " fingers=" + physicalFingerJoints.Count + " suspension=" +
                      config.SuspensionTravel, this);
        }

        private bool ValidateAuthoredPhysicalClaw()
        {
            if (carriageBody == null || suspensionJoint == null)
            {
                Debug.LogError("[PhysicalClaw] " + name +
                               ": 프리팹에 저장된 캐리지 Rigidbody/서스펜션 Joint가 없습니다. " +
                               "물리 컴포넌트는 런타임에 자동 생성하지 않습니다.", this);
                return false;
            }

            if (clawFingers == null || clawFingers.Length != 3)
            {
                Debug.LogError("[PhysicalClaw] " + name +
                               ": 프리팹에 저장된 집게발 참조가 정확히 3개여야 합니다.", this);
                return false;
            }

            bool valid = true;
            foreach (Transform finger in clawFingers)
            {
                if (finger == null || finger.GetComponent<Rigidbody>() == null ||
                    finger.GetComponent<HingeJoint>() == null ||
                    finger.GetComponent<ShopClawFingerContactSensor>() == null)
                {
                    Debug.LogError("[PhysicalClaw] " + name + "/" +
                                   (finger != null ? finger.name : "<missing>") +
                                   ": 편집 모드에서 저장된 Rigidbody, HingeJoint, 접촉 센서가 필요합니다. " +
                                   "런타임 AddComponent 대체는 사용하지 않습니다.", this);
                    valid = false;
                }
                else if (finger.GetComponent<HingeJoint>().connectedBody != clawBody)
                {
                    Debug.LogError("[PhysicalClaw] " + name + "/" + finger.name +
                                   ": authored HingeJoint의 Connected Body가 ClawHead가 아닙니다.", this);
                    valid = false;
                }
            }

            return valid;
        }

        private IEnumerator ActivateAuthoredFingerBodies()
        {
            // Authored joints enter the scene kinematic. Releasing them only after one
            // complete fixed step prevents the first solver sample from exposing a NaN
            // hinge angle while Unity initializes the three radial constraints.
            yield return new WaitForFixedUpdate();
            foreach (Rigidbody fingerBody in physicalFingerBodies)
            {
                if (fingerBody == null) continue;
                fingerBody.position = fingerBody.transform.position;
                fingerBody.rotation = fingerBody.transform.rotation;
                fingerBody.isKinematic = false;
                fingerBody.useGravity = true;
                fingerBody.linearVelocity = Vector3.zero;
                fingerBody.angularVelocity = Vector3.zero;
                fingerBody.WakeUp();
            }
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            LogPhysicalFingerLayout("firstDynamicFixed");
            yield return new WaitForSecondsRealtime(10f);
            LogPhysicalFingerLayout("10s");
        }

        private void LogPhysicalFingerLayout(string phase)
        {
            if (clawHead == null || physicalFingerJoints.Count == 0 || config == null) return;
            float maxPositionError = 0f;
            float maxRotationError = 0f;
            float minRadius = float.MaxValue;
            float maxRadius = 0f;
            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;
            float minAngle = float.MaxValue;
            float maxAngle = float.MinValue;
            int nanAngles = 0;
            for (int index = 0; index < physicalFingerJoints.Count; index++)
            {
                HingeJoint hinge = physicalFingerJoints[index];
                if (hinge == null) continue;
                float angle = 360f / physicalFingerJoints.Count * index;
                Vector3 expectedPosition = new(
                    Mathf.Sin(angle * Mathf.Deg2Rad) * config.FingerLayoutRadius,
                    config.FingerLayoutHeight,
                    Mathf.Cos(angle * Mathf.Deg2Rad) * config.FingerLayoutRadius);
                Quaternion expectedRotation = Quaternion.Euler(config.FingerLayoutTilt, angle, 0f);
                Vector3 localPosition = clawHead.InverseTransformPoint(hinge.transform.position);
                Quaternion localRotation = Quaternion.Inverse(clawHead.rotation) * hinge.transform.rotation;
                maxPositionError = Mathf.Max(maxPositionError,
                    Vector3.Distance(localPosition, expectedPosition));
                maxRotationError = Mathf.Max(maxRotationError,
                    Quaternion.Angle(localRotation, expectedRotation));
                float radius = new Vector2(localPosition.x, localPosition.z).magnitude;
                minRadius = Mathf.Min(minRadius, radius);
                maxRadius = Mathf.Max(maxRadius, radius);
                minHeight = Mathf.Min(minHeight, localPosition.y);
                maxHeight = Mathf.Max(maxHeight, localPosition.y);
                if (phase != "setup" && float.IsNaN(hinge.angle)) nanAngles++;
                else if (phase != "setup")
                {
                    minAngle = Mathf.Min(minAngle, hinge.angle);
                    maxAngle = Mathf.Max(maxAngle, hinge.angle);
                }
            }
            float angleSpread = phase == "setup"
                ? 0f
                : nanAngles == physicalFingerJoints.Count ? float.NaN : maxAngle - minAngle;
            Debug.Log($"[ClawLayout] machine={config.MachineId} phase={phase} " +
                      $"maxPos={maxPositionError:F6} maxRot={maxRotationError:F4} " +
                      $"radialSpread={maxRadius - minRadius:F6} " +
                      $"heightSpread={maxHeight - minHeight:F6} " +
                      $"hingeSpread={angleSpread:F3} nan={nanAngles}", this);
        }

        private void ServerDrivePhysicalClaw()
        {
            if (!physicalClawReady || carriageBody == null) return;
            Vector3 targetWorld = transform.TransformPoint(
                new Vector3(RailPosition.Value.x, ClawHeight.Value, RailPosition.Value.y));
            carriageBody.MovePosition(targetWorld);
            bool shouldClose = State.Value == ShopClawMachineState.Close ||
                               State.Value == ShopClawMachineState.Ascend ||
                               State.Value == ShopClawMachineState.Return;
            float targetAngle = shouldClose ? config.ClosedFingerAngle : config.OpenFingerAngle;
            if (State.Value == ShopClawMachineState.Descend)
            {
                // A real claw reaches around the prize while fully open, then closes in the
                // dedicated Close state. Pre-closing here pushed capsules out of the target
                // footprint before two or more fingers could make contact.
                targetAngle = config.OpenFingerAngle;
                shouldClose = false;
            }
            float motorSpeed = shouldClose ? config.CloseMotorSpeed : config.OpenMotorSpeed;
            float motorTorque = shouldClose ? EffectiveCloseMotorTorque : config.OpenMotorTorque;
            if (State.Value == ShopClawMachineState.Ascend || State.Value == ShopClawMachineState.Return)
                motorTorque *= config.AscentGripTorqueMultiplier;

            float closedSum = 0f;
            int validJoints = 0;
            for (int index = 0; index < physicalFingerJoints.Count; index++)
            {
                HingeJoint hinge = physicalFingerJoints[index];
                if (hinge == null) continue;
                float offset = index < physicalFingerAngleOffsets.Count
                    ? physicalFingerAngleOffsets[index]
                    : 0f;
                float physicalTargetAngle = targetAngle + offset;
                float error = physicalTargetAngle - hinge.angle;
                JointMotor motor = hinge.motor;
                motor.force = motorTorque;
                motor.freeSpin = false;
                motor.targetVelocity = Mathf.Abs(error) <= 1.25f ? 0f : Mathf.Sign(error) * motorSpeed;
                hinge.motor = motor;
                hinge.useMotor = true;
                closedSum += Mathf.InverseLerp(config.OpenFingerAngle + offset,
                    config.ClosedFingerAngle + offset, hinge.angle);
                validJoints++;
            }
            if (validJoints > 0) FingerClosed.Value = Mathf.Clamp01(closedSum / validJoints);
        }

        private void ServerMoveClawHeight(float targetHeight, float maxSpeed, float dt)
        {
            float direction = Mathf.Sign(targetHeight - ClawHeight.Value);
            float desiredVelocity = direction * maxSpeed;
            verticalVelocity = Mathf.MoveTowards(verticalVelocity, desiredVelocity,
                config.VerticalAcceleration * dt);
            float next = Mathf.MoveTowards(ClawHeight.Value, targetHeight,
                Mathf.Abs(verticalVelocity) * dt);
            if (Mathf.Approximately(next, targetHeight)) verticalVelocity = 0f;
            ClawHeight.Value = next;
        }

        private int CountDistinctFingerContacts(ShopClawPrizeNetwork prize)
        {
            if (prize == null) return 0;
            int contacts = 0;
            foreach (ShopClawFingerContactSensor sensor in physicalFingerSensors)
                if (sensor != null && sensor.IsTouching(prize)) contacts++;
            return contacts;
        }

        private bool HasAnyFingerContact()
        {
            foreach (ShopClawFingerContactSensor sensor in physicalFingerSensors)
                if (sensor != null && sensor.HasRecentContact) return true;
            return false;
        }

        private bool HasFingerApproach(float clearance)
        {
            if (!physicalClawReady) return false;
            var fingerColliders = new List<Collider>(physicalFingerBodies.Count * 3);
            foreach (Rigidbody body in physicalFingerBodies)
            {
                if (body == null) continue;
                foreach (Collider collider in body.GetComponentsInChildren<Collider>(true))
                    if (collider != null && collider.enabled)
                        fingerColliders.Add(collider);
            }
            foreach (ShopClawPrizeNetwork prize in activePrizes)
            {
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value) continue;
                foreach (Collider prizeCollider in prize.GetComponentsInChildren<Collider>(true))
                {
                    if (prizeCollider == null || !prizeCollider.enabled || prizeCollider.isTrigger)
                        continue;
                    Vector3 prizeCenter = prizeCollider.bounds.center;
                    foreach (Collider fingerCollider in fingerColliders)
                    {
                        Vector3 fingerSurface = fingerCollider.ClosestPoint(prizeCenter);
                        Vector3 prizeSurface = prizeCollider.ClosestPoint(fingerSurface);
                        if ((fingerSurface - prizeSurface).sqrMagnitude <= clearance * clearance)
                            return true;
                    }
                }
            }
            return false;
        }

        private bool AreFingersAtTarget(float target, float tolerance)
        {
            if (physicalFingerJoints.Count == 0) return stateElapsed >= config.CloseDuration;
            for (int index = 0; index < physicalFingerJoints.Count; index++)
            {
                HingeJoint hinge = physicalFingerJoints[index];
                float offset = index < physicalFingerAngleOffsets.Count
                    ? physicalFingerAngleOffsets[index]
                    : 0f;
                if (hinge != null && Mathf.Abs(Mathf.DeltaAngle(hinge.angle, target + offset)) > tolerance)
                    return false;
            }
            return true;
        }

        private static Vector3 GetPrizePhysicalCenter(ShopClawPrizeNetwork prize)
        {
            return TryGetPrizePhysicalBounds(prize, out Bounds bounds)
                ? bounds.center
                : prize != null ? prize.transform.position : Vector3.zero;
        }

        private static bool TryGetPrizePhysicalBounds(ShopClawPrizeNetwork prize, out Bounds combined)
        {
            combined = default;
            if (prize == null) return false;
            Collider[] colliders = prize.GetComponentsInChildren<Collider>(true);
            bool hasBounds = false;
            foreach (Collider prizeCollider in colliders)
            {
                if (prizeCollider == null || !prizeCollider.enabled) continue;
                if (!hasBounds)
                {
                    combined = prizeCollider.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(prizeCollider.bounds);
                }
            }
            return hasBounds;
        }

        private Vector2 ConvertInputToCameraRelative(Vector2 rawInput)
        {
            if (operatorCamera == null || rawInput.sqrMagnitude <= 0.0001f) return rawInput;
            Vector3 cameraRight = transform.InverseTransformDirection(operatorCamera.transform.right);
            Vector3 cameraForward = transform.InverseTransformDirection(operatorCamera.transform.forward);
            cameraRight.y = 0f;
            cameraForward.y = 0f;
            if (cameraRight.sqrMagnitude <= 0.0001f || cameraForward.sqrMagnitude <= 0.0001f)
                return rawInput;
            cameraRight.Normalize();
            cameraForward.Normalize();
            Vector3 relative = cameraRight * rawInput.x + cameraForward * rawInput.y;
            return Vector2.ClampMagnitude(new Vector2(relative.x, relative.z), 1f);
        }

        private void EnsureCountdownUi()
        {
            if (countdownText != null || operatorHud == null) return;
            GameObject countdownObject = new("ClawCountdown",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
            countdownObject.transform.SetParent(operatorHud.transform, false);
            RectTransform rect = countdownObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 125f);
            rect.sizeDelta = new Vector2(420f, 220f);
            countdownText = countdownObject.GetComponent<Text>();
            countdownText.font = operatorHudText != null ? operatorHudText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            countdownText.fontSize = 92;
            countdownText.fontStyle = FontStyle.Bold;
            countdownText.alignment = TextAnchor.MiddleCenter;
            countdownText.horizontalOverflow = HorizontalWrapMode.Overflow;
            countdownText.verticalOverflow = VerticalWrapMode.Overflow;
            countdownText.raycastTarget = false;
            Outline outline = countdownObject.GetComponent<Outline>();
            outline.effectColor = new Color(0.08f, 0.035f, 0.01f, 0.95f);
            outline.effectDistance = new Vector2(5f, -5f);
            countdownObject.SetActive(false);
        }

        private void UpdateCountdownUi()
        {
            EnsureCountdownUi();
            if (countdownText == null) return;
            int seconds = AutoDropSecondsRemaining.Value;
            bool visible = localMode &&
                           State.Value == ShopClawMachineState.Aiming &&
                           seconds >= 1 && seconds <= 3;
            if (countdownText.gameObject.activeSelf != visible)
                countdownText.gameObject.SetActive(visible);
            if (!visible) return;
            countdownText.text = seconds.ToString();
            countdownText.color = seconds == 3
                ? new Color(1f, 0.84f, 0.18f)
                : seconds == 2
                    ? new Color(1f, 0.52f, 0.12f)
                    : new Color(1f, 0.2f, 0.12f);
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 9f) * 0.055f;
            countdownText.rectTransform.localScale = Vector3.one * pulse;
        }

        private void HideGlassForLocalOperator()
        {
            CachePhysicalPresentation();
            hiddenLocalGlassRenderers.Clear();
            if (localGlassRenderers == null) return;
            foreach (Renderer glass in localGlassRenderers)
            {
                if (glass == null || glass.forceRenderingOff) continue;
                glass.forceRenderingOff = true;
                hiddenLocalGlassRenderers.Add(glass);
            }
        }

        private void RestoreGlassAfterLocalOperator()
        {
            foreach (Renderer glass in hiddenLocalGlassRenderers)
                if (glass != null) glass.forceRenderingOff = false;
            hiddenLocalGlassRenderers.Clear();
        }

        private void HideOverheadForLocalOperator()
        {
            hiddenLocalOverheadRenderers.Clear();
            Transform overhead = transform.Find("Cabinet/상단");
            if (overhead == null) return;
            foreach (Renderer renderer in overhead.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.forceRenderingOff) continue;
                renderer.forceRenderingOff = true;
                hiddenLocalOverheadRenderers.Add(renderer);
            }
        }

        private void RestoreOverheadAfterLocalOperator()
        {
            foreach (Renderer renderer in hiddenLocalOverheadRenderers)
                if (renderer != null) renderer.forceRenderingOff = false;
            hiddenLocalOverheadRenderers.Clear();
        }

        private void HideForegroundForLocalOperator()
        {
            hiddenLocalForegroundRenderers.Clear();
            foreach (Transform item in GetComponentsInChildren<Transform>(true))
            {
                if (item == null) continue;
                string itemName = item.name.ToLowerInvariant();
                if (!itemName.Contains("controlpanel") && !itemName.Contains("pricedisplay") &&
                    !itemName.Contains("조작부") && !itemName.Contains("가격")) continue;
                foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || renderer.forceRenderingOff) continue;
                    renderer.forceRenderingOff = true;
                    hiddenLocalForegroundRenderers.Add(renderer);
                }
            }
        }

        private void RestoreForegroundAfterLocalOperator()
        {
            foreach (Renderer renderer in hiddenLocalForegroundRenderers)
                if (renderer != null) renderer.forceRenderingOff = false;
            hiddenLocalForegroundRenderers.Clear();
        }

        private void SetCameraPreset(int preset, bool snap)
        {
            localCameraPreset = Mathf.Clamp(preset, 0, 3);
            float presetDistance = config != null ? config.OperatorCameraDistance : 4.2f;
            float presetPitch = config != null ? config.OperatorCameraPitch : 35f;
            switch (localCameraPreset)
            {
                case 1:
                    localLookYaw = -55f;
                    localLookPitch = presetPitch;
                    localCameraDistance = presetDistance * 1.05f;
                    break;
                case 2:
                    localLookYaw = 55f;
                    localLookPitch = presetPitch;
                    localCameraDistance = presetDistance * 1.05f;
                    break;
                case 3:
                    localLookYaw = 0f;
                    localLookPitch = 58f;
                    localCameraDistance = presetDistance;
                    break;
                default:
                    localLookYaw = 0f;
                    localLookPitch = presetPitch;
                    localCameraDistance = presetDistance;
                    break;
            }
            UpdateOperatorCamera(snap);
        }

        private void UpdateOperatorCamera(bool snap)
        {
            if (operatorCamera == null || clawHead == null) return;
            Vector3 focus = transform.TransformPoint(new Vector3(
                RailPosition.Value.x, config != null ? config.OperatorCameraFocusHeight : 2.0f,
                RailPosition.Value.y));
            Quaternion machineYaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Quaternion orbit = machineYaw * Quaternion.Euler(localLookPitch, localLookYaw, 0f);
            Vector3 targetPosition = focus + orbit * (Vector3.back * localCameraDistance);
            Quaternion targetRotation = Quaternion.LookRotation(focus - targetPosition, Vector3.up);
            float blend = snap ? 1f : 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
            operatorCamera.transform.position = Vector3.Lerp(operatorCamera.transform.position, targetPosition, blend);
            operatorCamera.transform.rotation = Quaternion.Slerp(operatorCamera.transform.rotation, targetRotation, blend);
            if (cameraLookPivot != null) cameraLookPivot.localRotation = Quaternion.identity;
        }

        private static string CameraPresetLabel(int preset) => preset switch
        {
            1 => "왼쪽 사선",
            2 => "오른쪽 사선",
            3 => "상단 확인",
            _ => "정면"
        };

        private void UpdateDebugText()
        {
            if (debugText == null) return;
            debugText.gameObject.SetActive(showDebug);
            if (!showDebug) return;
            debugText.text = "상태 " + StateLabel(State.Value) + "\n점유 " +
                (OccupantClientId.Value == ShopClawRules.NoOccupant ? "없음" : OccupantClientId.Value.ToString()) +
                "\n시도 " + AttemptId.Value + "\n집기 점수 " + LastGripScore.Value.ToString("0.0") +
                "\n파손 한계 " + LastJointBreakForce.Value.ToString("0.0") + "\n보유 상품 " + HeldPrizeNetworkObjectId.Value;
        }

        private void UpdateQaClient()
        {
            if (Time.unscaledTime < qaNextActionTime || ShopNetworkGame.Instance == null ||
                !ShopClawRules.CanOperateDuring(ShopNetworkGame.Instance.Phase.Value)) return;
            if (localPlayer == null && NetworkManager.LocalClient != null)
                localPlayer = NetworkManager.LocalClient.PlayerObject;

            if (OccupantClientId.Value == ShopClawRules.NoOccupant && qaCompletedAttempts < 2)
            {
                if (localPlayer != null)
                {
                    CharacterController controller = localPlayer.GetComponent<CharacterController>();
                    if (controller != null) controller.enabled = false;
                    localPlayer.transform.SetPositionAndRotation(operatorPoint.position, operatorPoint.rotation);
                    if (controller != null) controller.enabled = true;
                }
                RequestUse();
                qaNextActionTime = Time.unscaledTime + 1.2f;
                return;
            }

            if (OccupantClientId.Value == NetworkManager.LocalClientId &&
                State.Value == ShopClawMachineState.Aiming)
            {
                Vector2 target = qaCompletedAttempts == 0 ? new Vector2(1.15f, 0.78f) : FindQaSuccessTarget();
                Vector2 delta = target - RailPosition.Value;
                float remaining = delta.magnitude;
                if (remaining > 0.08f)
                {
                    RequestInput(delta.normalized);
                    qaNextActionTime = Time.unscaledTime + 0.08f;
                }
                else
                {
                    RequestInput(Vector2.zero);
                    RequestDrop();
                    Debug.Log("[Claw ClientQA] DROP_REQUEST attempt=" + AttemptId.Value + " target=" + target);
                    qaNextActionTime = Time.unscaledTime + 0.8f;
                }
            }

            if (State.Value == ShopClawMachineState.Cooldown && qaResultAttempt != AttemptId.Value)
            {
                qaResultAttempt = AttemptId.Value;
                qaCompletedAttempts++;
                Debug.Log("[Claw ClientQA] RESULT attempt=" + AttemptId.Value + " success=" +
                          LastResultSuccess.Value + " grip=" + LastGripScore.Value + " coins=" +
                          ShopNetworkGame.Instance.Coins.Value + " inventory=" + ShopNetworkGame.Instance.Inventory.Value);
                qaNextActionTime = Time.unscaledTime + 3f;
            }
        }

        private Vector2 FindQaSuccessTarget()
        {
            Vector2 fallback = new(-0.72f, -0.28f);
            ShopClawPrizeNetwork best = null;
            float bestPriority = float.MaxValue;
            foreach (ShopClawPrizeNetwork prize in FindObjectsByType<ShopClawPrizeNetwork>())
            {
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value ||
                    prize.MachineNetworkObjectId.Value != NetworkObjectId) continue;
                Vector3 local = transform.InverseTransformPoint(GetPrizePhysicalCenter(prize));
                Vector2 candidate = new(local.x, local.z);
                Vector2 clamped = ShopClawRules.ClampRail(candidate, config.XBounds, config.ZBounds);
                if ((candidate - clamped).sqrMagnitude > 0.01f) continue;
                float priority = prize.PrizeWeight.Value * 10f + candidate.sqrMagnitude;
                if (priority >= bestPriority) continue;
                bestPriority = priority;
                best = prize;
                fallback = candidate;
            }
            return best != null ? fallback : ShopClawRules.ClampRail(fallback, config.XBounds, config.ZBounds);
        }

        private static string StateLabel(ShopClawMachineState state)
        {
            return state switch
            {
                ShopClawMachineState.Idle => "사용 가능",
                ShopClawMachineState.Reserved => "조작 준비",
                ShopClawMachineState.Aiming => "조준",
                ShopClawMachineState.Descend => "집게 하강 중",
                ShopClawMachineState.Close => "토크로 발톱 닫는 중",
                ShopClawMachineState.Ascend => "집게 상승 중",
                ShopClawMachineState.Return => "출구로 이동 중",
                ShopClawMachineState.Release => "상품 놓는 중",
                ShopClawMachineState.Judge => "투하구 안정 판정 중",
                ShopClawMachineState.Cooldown => "결과",
                _ => state.ToString()
            };
        }
    }
}
