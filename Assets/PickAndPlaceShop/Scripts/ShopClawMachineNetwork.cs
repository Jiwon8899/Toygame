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
        [SerializeField] private ShopClawScoopRig scoopRig;
        [SerializeField] private Transform cable;
        [SerializeField] private Transform chuteDropPoint;
        [SerializeField] private Transform[] prizeSpawnPoints;
        [SerializeField] private Transform joystickStick;
        [SerializeField] private Renderer statusLamp;
        [SerializeField] private Renderer[] localGlassRenderers;

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
        public NetworkVariable<int> RemainingCapsules = new(0);
        public NetworkVariable<FixedString64Bytes> LastAwardedName =
            new(new FixedString64Bytes(""));
        public NetworkVariable<int> LastAwardedRarity = new(0);
        public NetworkVariable<Color> LastAwardedCapsuleColor = new(Color.white);
        public NetworkVariable<int> LastAwardedProductId = new(-1);
        public NetworkVariable<int> LastAwardedDestination = new((int)ShopContainerKind.PersonalInventory);
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
        private GameObject aimGroundMarker;
        private readonly Dictionary<ulong, float> chuteStableSeconds = new();
        private readonly Dictionary<ulong, float> chuteLastObservationTime = new();
        private readonly Dictionary<ulong, float> abnormalStuckSeconds = new();
        private bool physicalClawReady;
        private float verticalVelocity;
        private float autoDropIdleElapsed;
        private System.Random prizeRandom;
        private Vector2 scrapeOrigin;
        private Vector2 scrapeTarget;
        private float scoopTiltAngle;
        private float scoopAngularVelocity;
        private float scoopCurlDirection = 1f;
        private bool scoopReachedDigAngle;
        private Quaternion scoopTargetRotation = Quaternion.identity;
        private Vector3 scoopPourDirectionLocal = Vector3.right;
        private bool scoopBlockedDuringDescent;
        private bool scoopTouchedPrize;
        private float lastFloorPenetrationMillimeters;
        private int floorContactSamples;
        private bool floorVerificationOverride;
        private bool floorVerificationRunning;
        private int loggedReleaseAttempt = -1;
        private GameObject spawnGuardRoot;
        private Collider chuteAwardVolume;
        private ShopOperationsConfig operations;

        private ShopOperationsConfig Operations => operations != null
            ? operations
            : operations = ShopOperationsConfig.Load();

        public ShopClawMachineConfig Config => config;
        public bool IsManuallyBusy => OccupantClientId.Value != ShopClawRules.NoOccupant ||
                                      (State.Value != ShopClawMachineState.Idle &&
                                       State.Value != ShopClawMachineState.Cooldown);
        public Vector3 OperatorWorldPosition => operatorPoint != null ? operatorPoint.position : transform.position;
        public string InteractionPrompt => State.Value == ShopClawMachineState.Idle
            ? RemainingCapsules.Value <= 0
                ? (config != null ? config.DisplayName : "물리 인형뽑기") + " · 재고 소진"
                : (config != null ? config.DisplayName : "물리 인형뽑기") + " 조작 시작"
            : OccupantClientId.Value == ShopClawRules.NoOccupant ? "인형뽑기 초기화 중" : "인형뽑기 조작 중";
        public static bool LocalOperatorActive => LocalActiveMachine != null && LocalActiveMachine.localMode;
        public bool LocalGlassHidden => localMode && localGlassRenderers != null &&
                                        Array.TrueForAll(localGlassRenderers,
                                            renderer => renderer == null || renderer.forceRenderingOff);
        public Vector3 OperatorCameraPosition => operatorCamera != null
            ? operatorCamera.transform.position
            : Vector3.zero;
        public int LocalCameraPreset => localCameraPreset;
        public ShopClawScoopRig ScoopRig => scoopRig;
        public float LastFloorPenetrationMillimeters => lastFloorPenetrationMillimeters;
        public int FloorContactSamples => floorContactSamples;
        public int AvailableCapsules => Mathf.Max(0, RemainingCapsules.Value);
        public Vector3 ChuteWorldPosition => chuteDropPoint != null
            ? chuteDropPoint.position
            : transform.position;

        public bool BeginScoopFloorVerification(int repetitions = 50)
        {
            if (!IsServer || scoopRig == null || floorVerificationRunning) return false;
            StartCoroutine(ServerVerifyScoopFloorContacts(Mathf.Clamp(repetitions, 1, 100)));
            return true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool RefillPrizesForScoopVerification()
        {
            if (!IsServer || !IsSpawned) return false;
            ServerRefillPrizes();
            return true;
        }
#endif

#if UNITY_EDITOR
        public void EditorConfigure(ShopClawMachineConfig machineConfig, GameObject networkPrizePrefab,
            ShopClawPrizeDefinition[] definitions, Transform operatorTransform, Camera localCamera,
            Transform lookPivot, Transform head, Rigidbody headBody, Transform wire,
            Transform chute, Transform[] spawns, Transform joystick, Renderer lamp,
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
            chuteDropPoint = chute;
            prizeSpawnPoints = spawns;
            joystickStick = joystick;
            statusLamp = lamp;
            operatorHud = hud;
            operatorHudText = hudText;
            debugText = developmentText;
        }

        public void EditorConfigureTransition(CanvasGroup transition) => cameraTransition = transition;

        public void EditorConfigureScoopRig(Rigidbody authoredCarriage, ShopClawScoopRig authoredScoop)
        {
            carriageBody = authoredCarriage;
            scoopRig = authoredScoop;
            clawHead = authoredScoop != null ? authoredScoop.transform : clawHead;
            clawBody = authoredScoop != null ? authoredScoop.Body : clawBody;
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
            EnsureSpawnGuard();
            EnsureChuteGlassOpening();
            AwardedCount.OnValueChanged += OnAwardedCountChanged;
            if (IsServer)
            {
                SetupServerPhysicalScoop();
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
            ExitLocalMode();
            if (aimGroundMarker != null) Destroy(aimGroundMarker);
            if (spawnGuardRoot != null) Destroy(spawnGuardRoot);
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
            if (RemainingCapsules.Value <= 0)
            {
                ShopNetworkGame.Instance?.ServerSetEvent("이 기계는 오늘 캡슐을 모두 사용했습니다. 다음 날 준비 시간에 20개가 리필됩니다.");
                ResultMessage.Value = new FixedString128Bytes("재고 소진 · 다음 날 리필");
                return;
            }
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

            if (RemainingCapsules.Value <= 0)
            {
                ResultMessage.Value = new FixedString128Bytes("재고 소진 · 다음 날 리필");
                return false;
            }

            int coins = ShopNetworkGame.Instance.Coins.Value;
            bool freeTutorialAttempt = ShopTutorialRuntime.FreeScoopAttempt;
            if (freeTutorialAttempt) chargedAttempts.Add(attemptId);
            if (!freeTutorialAttempt &&
                !ShopClawRules.TryChargeAttempt(ref coins, config.AttemptCost, attemptId, chargedAttempts))
            {
                ResultMessage.Value = new FixedString128Bytes("가게 자금이 부족합니다.");
                ShopNetworkGame.Instance.ServerSetEvent("가게 자금이 부족해 팬을 내릴 수 없습니다.");
                return false;
            }

            ShopNetworkGame.Instance.Coins.Value = coins;
            OperatorInput.Value = Vector2.zero;
            railVelocity = Vector2.zero;
            ResultMessage.Value = new FixedString128Bytes("퍼올리기 팬 하강 중");
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
                    float sweepTargetHeight = config.DropHeight -
                                              Mathf.Max(0.12f, config.ScoopBottomThickness * 2f);
                    ServerMoveClawHeight(sweepTargetHeight, config.DescendSpeed, dt);
                    if ((stateElapsed >= 0.12f && scoopBlockedDuringDescent) ||
                        stateElapsed >= config.DescendTimeout)
                        SetState(ShopClawMachineState.Close);
                    break;
                case ShopClawMachineState.Close:
                    bool curlComplete = ServerUpdateScoopCurl(dt);
                    UpdateScoopLoadDiagnostics();
                    if (curlComplete || stateElapsed >= config.CloseTimeout)
                        SetState(ShopClawMachineState.Ascend);
                    break;
                case ShopClawMachineState.Ascend:
                    UpdateScoopLoadDiagnostics();
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
                    UpdateScoopLoadDiagnostics();
                    Vector3 chuteLocal = transform.InverseTransformPoint(chuteDropPoint.position);
                    Vector2 chuteDirection = new(chuteLocal.x, chuteLocal.z);
                    if (chuteDirection.sqrMagnitude > 0.0001f) chuteDirection.Normalize();
                    else chuteDirection = Vector2.right;
                    // Stop one pan radius before the chute.  Driving the pan centre all
                    // the way to the drop point makes its outer rim collide with the
                    // cabinet corner post, preventing the pour rotation entirely.
                    float stopDistance = Mathf.Max(config.SweepSkin,
                        config.ScoopDiameter * 0.5f + config.SweepSkin -
                        config.ScoopReturnInset);
                    Vector2 chute = new Vector2(chuteLocal.x, chuteLocal.z) -
                                    chuteDirection * stopDistance;
                    // The pan centre is not the prize centre. A prize usually rests against
                    // one side of the bowl, so align that live cargo centroid with the chute
                    // on the lateral axis while preserving the authored wall-clearance inset.
                    chute -= GetScoopCargoLateralOffset(chuteDirection);
                    RailPosition.Value = Vector2.MoveTowards(RailPosition.Value, chute, config.ReturnSpeed * dt);
                    if ((RailPosition.Value - chute).sqrMagnitude < 0.0025f ||
                        stateElapsed >= config.ReturnTimeout)
                        SetState(ShopClawMachineState.Release);
                    break;
                case ShopClawMachineState.Release:
                    if (stateElapsed >= config.ReleaseDuration || stateElapsed >= config.ReleaseTimeout)
                        SetState(ShopClawMachineState.Judge);
                    break;
                case ShopClawMachineState.Judge:
                    ServerObserveChuteCandidates();
                    if (stateElapsed >= config.JudgeTimeout)
                        SetState(ShopClawMachineState.Cooldown);
                    break;
                case ShopClawMachineState.Cooldown:
                    // A capsule can need a little longer than the authored judge window to
                    // settle, especially when several prizes enter together. Keep observing
                    // during cooldown so a sleeping body never misses the trigger callback.
                    ServerObserveChuteCandidates();
                    if (stateElapsed >= 12f)
                        ServerResetMachine();
                    break;
            }
            ServerDrivePhysicalScoop(dt);
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
            ShopBuildSafeMaterials.ApplyLitColor(markerRenderer, new Color(0.03f, 0.03f, 0.04f, 0.5f));
            markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markerRenderer.receiveShadows = false;
        }

        private void EnsureSpawnGuard()
        {
            if (config == null || !config.SpawnGuardEnabled || spawnGuardRoot != null) return;
            Transform existing = transform.Find("CapsuleSpawnGuard");
            if (existing != null)
            {
                spawnGuardRoot = existing.gameObject;
                return;
            }

            spawnGuardRoot = new GameObject("CapsuleSpawnGuard");
            spawnGuardRoot.transform.SetParent(transform, false);
            float floorY = config.DropHeight - config.ScoopBottomThickness;
            float halfWidth = config.SpawnGuardWidth * 0.5f;
            float halfDepth = config.SpawnGuardDepth * 0.5f;
            float slope = Mathf.Tan(config.SpawnGuardSlopeAngle * Mathf.Deg2Rad);
            // The feeder must finish on the playfield, not underneath it.  Keeping the
            // whole ramp above floor level also removes the small seam that used to catch
            // sleeping capsules at the guard entrance.
            float guardCenterY = floorY + 0.03f +
                                 slope * (config.SpawnGuardFeederLength + halfDepth);
            float feederCenterY = floorY + 0.025f +
                                  slope * config.SpawnGuardFeederLength * 0.5f;
            CreateSpawnGuardPart("경사 바닥", new Vector3(0f, guardCenterY, config.SpawnGuardCenterZ),
                new Vector3(config.SpawnGuardWidth, 0.06f, config.SpawnGuardDepth),
                Quaternion.Euler(-config.SpawnGuardSlopeAngle, 0f, 0f), true);
            float guardFront = config.SpawnGuardCenterZ - halfDepth;
            CreateSpawnGuardPart("완만한 유도 경사",
                new Vector3(0f, feederCenterY,
                    guardFront - config.SpawnGuardFeederLength * 0.5f),
                new Vector3(config.SpawnGuardWidth * 0.92f, 0.05f,
                    config.SpawnGuardFeederLength),
                Quaternion.Euler(-config.SpawnGuardSlopeAngle, 0f, 0f), true);
            CreateSpawnGuardPart("왼쪽 가림벽",
                new Vector3(-halfWidth, guardCenterY + config.SpawnGuardHeight * 0.5f,
                    config.SpawnGuardCenterZ),
                new Vector3(0.08f, config.SpawnGuardHeight, config.SpawnGuardDepth),
                Quaternion.identity);
            CreateSpawnGuardPart("오른쪽 가림벽",
                new Vector3(halfWidth, guardCenterY + config.SpawnGuardHeight * 0.5f,
                    config.SpawnGuardCenterZ),
                new Vector3(0.08f, config.SpawnGuardHeight, config.SpawnGuardDepth),
                Quaternion.identity);
            CreateSpawnGuardPart("뒤 가림벽",
                new Vector3(0f, guardCenterY + config.SpawnGuardHeight * 0.5f,
                    config.SpawnGuardCenterZ + halfDepth),
                new Vector3(config.SpawnGuardWidth, config.SpawnGuardHeight, 0.08f),
                Quaternion.identity);
        }

        private void CreateSpawnGuardPart(string partName, Vector3 localPosition, Vector3 localScale,
            Quaternion localRotation, bool glideSurface = false)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            part.transform.SetParent(spawnGuardRoot.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            Renderer renderer = part.GetComponent<Renderer>();
            ShopBuildSafeMaterials.ApplyLitColor(renderer, config.SpawnGuardColor);
            BoxCollider collider = part.GetComponent<BoxCollider>();
            collider.size = new Vector3(0.94f, 0.9f, 0.94f);
            collider.material = glideSurface && config.ScoopOuterPhysicsMaterial != null
                ? config.ScoopOuterPhysicsMaterial
                : config.MachineFloorMaterial;
        }

        private void EnsureChuteGlassOpening()
        {
            if (config == null || chuteDropPoint == null || localGlassRenderers == null ||
                localGlassRenderers.Length == 0) return;

            Vector3 chuteDirection = Vector3.ProjectOnPlane(
                chuteDropPoint.position - transform.position, transform.up).normalized;
            Renderer selectedRenderer = null;
            BoxCollider selectedCollider = null;
            float bestAlignment = float.NegativeInfinity;
            foreach (Renderer glassRenderer in localGlassRenderers)
            {
                if (glassRenderer == null) continue;
                BoxCollider glassCollider = glassRenderer.GetComponent<BoxCollider>();
                if (glassCollider == null || !glassCollider.enabled || glassCollider.isTrigger ||
                    glassCollider.transform.Find("ChuteCollisionGap") != null) continue;
                Vector3 fromMachine = Vector3.ProjectOnPlane(
                    glassRenderer.bounds.center - transform.position, transform.up).normalized;
                float alignment = Vector3.Dot(fromMachine, chuteDirection);
                if (alignment <= bestAlignment) continue;
                bestAlignment = alignment;
                selectedRenderer = glassRenderer;
                selectedCollider = glassCollider;
            }
            if (selectedRenderer == null || selectedCollider == null || bestAlignment < 0.5f) return;

            Transform glassTransform = selectedCollider.transform;
            Vector3 size = selectedCollider.size;
            Vector3 center = selectedCollider.center;
            bool splitZ = size.z >= size.x;
            float fullMin = (splitZ ? center.z : center.x) - (splitZ ? size.z : size.x) * 0.5f;
            float fullMax = (splitZ ? center.z : center.x) + (splitZ ? size.z : size.x) * 0.5f;
            Vector3 chuteInGlass = glassTransform.InverseTransformPoint(chuteDropPoint.position);
            float gapCenter = splitZ ? chuteInGlass.z : chuteInGlass.x;
            Collider awardTrigger = null;
            foreach (Collider childCollider in GetComponentsInChildren<Collider>(true))
            {
                if (childCollider != null && childCollider.isTrigger &&
                    childCollider.name == "PrizeAwardTrigger")
                {
                    awardTrigger = childCollider;
                    break;
                }
            }
            float axisScale = splitZ
                ? Mathf.Abs(glassTransform.lossyScale.z)
                : Mathf.Abs(glassTransform.lossyScale.x);
            float triggerWidth = awardTrigger != null
                ? (splitZ ? awardTrigger.bounds.size.z : awardTrigger.bounds.size.x)
                : 0f;
            float requiredScoopOpening = config.ScoopDiameter +
                                         2f * (config.SweepSkin + config.SpawnGuardScoopClearance);
            float gapWidth = Mathf.Max(requiredScoopOpening, triggerWidth) /
                             Mathf.Max(0.001f, axisScale);
            float gapMin = Mathf.Clamp(gapCenter - gapWidth * 0.5f, fullMin, fullMax);
            float gapMax = Mathf.Clamp(gapCenter + gapWidth * 0.5f, fullMin, fullMax);
            if (gapMin <= fullMin || gapMax >= fullMax || gapMax <= gapMin) return;

            PhysicsMaterial material = selectedCollider.material;
            selectedCollider.enabled = false;
            AddGlassCollisionSegment(selectedRenderer.gameObject, center, size, splitZ,
                fullMin, gapMin, material);
            AddGlassCollisionSegment(selectedRenderer.gameObject, center, size, splitZ,
                gapMax, fullMax, material);
            GameObject marker = new("ChuteCollisionGap");
            marker.transform.SetParent(glassTransform, false);
        }

        private static void AddGlassCollisionSegment(GameObject target, Vector3 originalCenter,
            Vector3 originalSize, bool splitZ, float minimum, float maximum,
            PhysicsMaterial material)
        {
            if (target == null || maximum - minimum <= 0.01f) return;
            BoxCollider segment = target.AddComponent<BoxCollider>();
            Vector3 center = originalCenter;
            Vector3 size = originalSize;
            if (splitZ)
            {
                center.z = (minimum + maximum) * 0.5f;
                size.z = maximum - minimum;
            }
            else
            {
                center.x = (minimum + maximum) * 0.5f;
                size.x = maximum - minimum;
            }
            segment.center = center;
            segment.size = size;
            segment.material = material;
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
                config.XBounds, config.ScoopZBounds);
            if (autoDropIdleElapsed >= config.AutoDropDelay || aimRemaining <= 0f)
            {
                ResultMessage.Value = new FixedString128Bytes(aimRemaining <= 0f
                    ? "조작 시간이 끝나 자동으로 투하합니다."
                    : "3초 동안 이동이 없어 자동으로 투하합니다.");
                ServerBeginDrop(AttemptId.Value);
            }
        }

        private void UpdateScoopLoadDiagnostics()
        {
            ShopClawPrizeNetwork best = null;
            int loadedCount = 0;
            foreach (ShopClawPrizeNetwork prize in activePrizes)
            {
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value) continue;
                if (scoopRig == null || !scoopRig.ContainsPrize(prize,
                        config.ScoopDiameter, config.ScoopRimHeight)) continue;
                loadedCount++;
                if (best == null) best = prize;
            }

            LastGripScore.Value = loadedCount;
            LastJointBreakForce.Value = 0f;
            if (best != null)
            {
                HeldPrizeNetworkObjectId.Value = best.NetworkObjectId;
                if (State.Value == ShopClawMachineState.Ascend &&
                    GetPrizePhysicalCenter(best).y > transform.TransformPoint(
                        new Vector3(0f, config.DropHeight + 0.28f, 0f)).y)
                    roundHadPhysicalLift = true;
                ResultMessage.Value = new FixedString128Bytes(loadedCount + "개 상품을 팬에 담았습니다.");
            }
            else
            {
                if (HeldPrizeNetworkObjectId.Value != 0)
                    ResultMessage.Value = new FixedString128Bytes("상품이 팬에서 미끄러졌습니다.");
                HeldPrizeNetworkObjectId.Value = 0;
            }
        }

        private Vector2 GetScoopCargoLateralOffset(Vector2 chuteDirection)
        {
            if (scoopRig == null || scoopRig.Body == null) return Vector2.zero;
            Vector3 bodyLocal = transform.InverseTransformPoint(scoopRig.Body.position);
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (ShopClawPrizeNetwork prize in activePrizes)
            {
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value ||
                    !scoopRig.ContainsPrize(prize, config.ScoopDiameter, config.ScoopRimHeight)) continue;
                Vector3 prizeLocal = transform.InverseTransformPoint(GetPrizePhysicalCenter(prize));
                sum += new Vector2(prizeLocal.x - bodyLocal.x, prizeLocal.z - bodyLocal.z);
                count++;
            }
            if (count == 0) return Vector2.zero;
            Vector2 average = sum / count;
            Vector2 forward = chuteDirection.sqrMagnitude > 0.0001f
                ? chuteDirection.normalized : Vector2.right;
            Vector2 lateral = average - Vector2.Dot(average, forward) * forward;
            return Vector2.ClampMagnitude(lateral, config.ScoopDiameter * 0.35f);
        }

        private void ServerMoveClawHeight(float targetHeight, float maxSpeed, float dt)
        {
            float direction = Mathf.Sign(targetHeight - ClawHeight.Value);
            float desiredVelocity = direction * maxSpeed;
            verticalVelocity = Mathf.MoveTowards(verticalVelocity, desiredVelocity,
                config.ScoopVerticalAcceleration * dt);
            float next = Mathf.MoveTowards(ClawHeight.Value, targetHeight,
                Mathf.Abs(verticalVelocity) * dt);
            if (Mathf.Approximately(next, targetHeight)) verticalVelocity = 0f;
            ClawHeight.Value = next;
        }

        public void ServerObserveChutePrize(ShopClawPrizeNetwork prize, Collider chuteVolume)
        {
            if (!IsServer || prize == null || !prize.IsSpawned || chuteVolume == null) return;
            if (!chargedAttempts.Contains(AttemptId.Value) ||
                !ShopClawRules.CanAwardChutePrize(State.Value)) return;
            if (!TryGetPrizePhysicalBounds(prize, out Bounds prizeBounds) ||
                !ShopClawRules.IsFullyInsideChute(prizeBounds, chuteVolume.bounds,
                    config.ChuteHorizontalInset) ||
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

        private void ServerObserveChuteCandidates()
        {
            if (!IsServer) return;
            if (chuteAwardVolume == null)
            {
                ShopClawChuteTrigger trigger = GetComponentInChildren<ShopClawChuteTrigger>(true);
                chuteAwardVolume = trigger != null ? trigger.GetComponent<Collider>() : null;
            }
            if (chuteAwardVolume == null || !chuteAwardVolume.enabled) return;
            foreach (ShopClawPrizeNetwork prize in activePrizes)
            {
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value) continue;
                ServerObserveChutePrize(prize, chuteAwardVolume);
            }
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
            RemainingCapsules.Value = Mathf.Max(0, RemainingCapsules.Value - 1);
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
                    product != null ? ShopProductLocalization.CategoryId(product.Category) : "cat_plush",
                    product != null && product.Rarity >= ShopProductRarity.Rare);
            LastAwardedName.Value = new FixedString64Bytes(
                product != null ? product.DisplayName : prize.VisualDisplayName.Value.ToString());
            LastAwardedRarity.Value = product != null ? (int)product.Rarity : 0;
            LastAwardedCapsuleColor.Value = prize.VisualColor.Value;
            LastAwardedProductId.Value = product != null ? product.ProductId : -1;
            LastAwardedDestination.Value = (int)destination;
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
            ShopProductDefinition product = ShopProductVisuals.Find(LastAwardedProductId.Value);
            if (product == null) return;
            ShopContainerKind destination = (ShopContainerKind)LastAwardedDestination.Value;
            ShopCapsuleOpeningPresenter.Show("뽑기 결과", product, LastAwardedCapsuleColor.Value,
                destination == ShopContainerKind.PersonalInventory
                    ? "개인 인벤토리에 보관했습니다."
                    : "공용 창고에 보관했습니다.");
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
            if (next == ShopClawMachineState.Descend)
            {
                scoopBlockedDuringDescent = false;
                scoopTouchedPrize = false;
                scoopTiltAngle = 0f;
                scoopAngularVelocity = 0f;
                scoopReachedDigAngle = false;
                scoopRig?.ClearPrizeContactHistory();
            }
            if (next == ShopClawMachineState.Close)
            {
                scrapeOrigin = RailPosition.Value;
                float preferredDirection = RailPosition.Value.y + config.ScrapeDistance <= config.ScoopZBounds.y
                    ? 1f
                    : -1f;
                scrapeTarget = scrapeOrigin;
                scoopCurlDirection = preferredDirection;
                scoopTiltAngle = 0f;
                scoopAngularVelocity = 0f;
                scoopReachedDigAngle = false;
                ResultMessage.Value = new FixedString128Bytes("팬을 기울여 상품을 퍼올립니다.");
            }
            if (next == ShopClawMachineState.Release)
            {
                HeldPrizeNetworkObjectId.Value = 0;
                chuteStableSeconds.Clear();
                chuteLastObservationTime.Clear();
                Vector3 bodyLocal = scoopRig != null && scoopRig.Body != null
                    ? transform.InverseTransformPoint(scoopRig.Body.position)
                    : Vector3.zero;
                Vector3 chuteLocal = transform.InverseTransformPoint(ChuteWorldPosition);
                scoopPourDirectionLocal = Vector3.ProjectOnPlane(chuteLocal - bodyLocal, Vector3.up);
                if (scoopPourDirectionLocal.sqrMagnitude < 0.0001f)
                    scoopPourDirectionLocal = Vector3.right;
                else
                    scoopPourDirectionLocal.Normalize();
            }
            if (next == ShopClawMachineState.Cooldown && IsServer && ShopNetworkGame.Instance != null)
            {
                ShopClawPrizeNetwork nearest = null;
                float nearestHorizontal = float.MaxValue;
                Vector3 chutePosition = ChuteWorldPosition;
                foreach (ShopClawPrizeNetwork prize in activePrizes)
                {
                    if (prize == null || prize.Awarded.Value) continue;
                    Vector3 prizePosition = prize.transform.position;
                    float horizontal = Vector2.Distance(
                        new Vector2(prizePosition.x, prizePosition.z),
                        new Vector2(chutePosition.x, chutePosition.z));
                    if (horizontal >= nearestHorizontal) continue;
                    nearestHorizontal = horizontal;
                    nearest = prize;
                }
                Debug.Log("[ScoopPhysics] POUR_COMPLETE attempt=" + AttemptId.Value +
                          " awards=" + roundAwardCount +
                          " nearestHorizontal=" + nearestHorizontal.ToString("F3") +
                          " nearest=" + (nearest != null
                              ? nearest.transform.position.ToString("F3") +
                                " velocity=" + nearest.Body.linearVelocity.ToString("F3")
                              : "none") +
                          " scoopRotation=" + (scoopRig != null && scoopRig.Body != null
                              ? scoopRig.Body.rotation.eulerAngles.ToString("F1")
                              : "none") +
                          " scoopBlocker=" + (scoopRig != null
                              ? scoopRig.LastPoseBlockerName
                              : "none"), this);
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
            ServerObserveChuteCandidates();
            if (HasPendingChutePrize())
            {
                ResultMessage.Value = new FixedString128Bytes("투하구 안의 상품을 판정하고 있습니다. 잠시 후 다시 움직이세요.");
                return;
            }
            HeldPrizeNetworkObjectId.Value = 0;
            OperatorInput.Value = Vector2.zero;
            railVelocity = Vector2.zero;
            verticalVelocity = 0f;
            FingerClosed.Value = 0f;
            scoopTiltAngle = 0f;
            scoopAngularVelocity = 0f;
            scoopReachedDigAngle = false;
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
            ResetPhysicalScoopPose();
            SetState(ShopClawMachineState.Reserved);
        }

        private bool HasPendingChutePrize()
        {
            if (chuteAwardVolume == null)
            {
                ShopClawChuteTrigger trigger = GetComponentInChildren<ShopClawChuteTrigger>(true);
                chuteAwardVolume = trigger != null ? trigger.GetComponent<Collider>() : null;
            }
            if (chuteAwardVolume == null || !chuteAwardVolume.enabled) return false;
            foreach (ShopClawPrizeNetwork prize in activePrizes)
            {
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value) continue;
                if (TryGetPrizePhysicalBounds(prize, out Bounds prizeBounds) &&
                    ShopClawRules.IsFullyInsideChute(prizeBounds, chuteAwardVolume.bounds,
                        config.ChuteHorizontalInset)) return true;
            }
            return false;
        }

        private void ResetPhysicalScoopPose()
        {
            if (scoopRig == null || scoopRig.Body == null) return;
            Vector3 target = transform.TransformPoint(new Vector3(
                RailPosition.Value.x, ClawHeight.Value, RailPosition.Value.y));
            scoopRig.SetPhysicalCollisionsEnabled(true);
            scoopRig.Body.position = target;
            scoopRig.Body.rotation = transform.rotation;
            scoopRig.Body.linearVelocity = Vector3.zero;
            scoopRig.Body.angularVelocity = Vector3.zero;
            scoopRig.SetEntryLipsOpen(true, config.ScoopRimHeight, config.ScoopOpenRimHeight);
        }

        private void ServerResetMachine()
        {
            if (!IsServer) return;
            HeldPrizeNetworkObjectId.Value = 0;
            OperatorInput.Value = Vector2.zero;
            railVelocity = Vector2.zero;
            verticalVelocity = 0f;
            FingerClosed.Value = 0f;
            scoopTiltAngle = 0f;
            scoopAngularVelocity = 0f;
            scoopReachedDigAngle = false;
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
            ResetPhysicalScoopPose();
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
                if (saved.remainingCapsules == 0)
                {
                    RemainingCapsules.Value = 0;
                    Debug.Log("[ClawMachine] SOLD_OUT_SAVE_RESTORED machine=" + config.MachineId, this);
                    return;
                }
                Debug.Log("[ClawMachine] EMPTY_SAVE_REFILL machine=" + config.MachineId, this);
            }

            prizeRandom = new System.Random(unchecked((config != null ? config.MachineId : 0) * 73856093 ^
                                                       observedDay * 19349663 ^ AttemptId.Value));
            int count = Operations != null
                ? Operations.MachineDailyCapsuleCapacity
                : hasPool ? pool.MaxConcurrentPrizes : Mathf.Max(6, prizeSpawnPoints != null ? prizeSpawnPoints.Length : 0);
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
                    spawnPosition, spawnRotation, visualIndex, spawnedRarity,
                    config != null ? config.CapsuleMaxDepenetrationVelocity : 1.4f);
                if (prize.Body != null && config != null)
                    prize.Body.mass = config.GetCapsuleMass(spawnedRarity);
                activePrizes.Add(prize);
                occupied.Add((spawnPosition, Mathf.Max(0.16f, definition.Size * 0.48f) + clearance));
            }
            RemainingCapsules.Value = activePrizes.Count;
            Debug.Log("[ClawMachine] POOL_SPAWNED machine=" + (config != null ? config.MachineId : 0) +
                      " pool=" + (hasPool ? pool.PoolId : "legacy") + " count=" + activePrizes.Count +
                      " limit=" + count, this);
        }

        private void ServerRestorePrizes(ShopClawMachineSave saved)
        {
            if (saved?.prizes == null) return;
            int capacity = Operations != null ? Operations.MachineDailyCapsuleCapacity : int.MaxValue;
            foreach (ShopClawPrizeSave item in saved.prizes)
            {
                if (activePrizes.Count >= capacity) break;
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
                    (ShopProductRarity)Mathf.Clamp(item.rarity, 0, 3),
                    config != null ? config.CapsuleMaxDepenetrationVelocity : 1.4f);
                if (prize.Body != null && config != null)
                    prize.Body.mass = config.GetCapsuleMass(
                        (ShopProductRarity)Mathf.Clamp(item.rarity, 0, 3));
                activePrizes.Add(prize);
            }
            RemainingCapsules.Value = saved.remainingCapsules >= 0
                ? Mathf.Min(capacity, saved.remainingCapsules)
                : activePrizes.Count;
            Debug.Log("[ClawMachine] SAVE_RESTORED machine=" + config.MachineId +
                      " count=" + activePrizes.Count, this);
        }

        public ShopClawMachineSave CaptureSaveState()
        {
            var saved = new ShopClawMachineSave
            {
                machineId = config != null ? config.MachineId : 0,
                remainingCapsules = RemainingCapsules.Value
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

        public bool ServerTryPeekAutomationCapsule(out ShopProductRarity rarity)
        {
            rarity = ShopProductRarity.Common;
            if (!IsServer || RemainingCapsules.Value <= 0) return false;
            foreach (ShopClawPrizeNetwork prize in activePrizes)
            {
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value) continue;
                ShopProductRarity candidateRarity = (ShopProductRarity)Mathf.Clamp(
                    prize.SpawnedRarity.Value, 0, (int)ShopProductRarity.UltraRare);
                if (candidateRarity == ShopProductRarity.UltraRare) continue;
                rarity = candidateRarity;
                return true;
            }
            return false;
        }

        public bool ServerTryConsumeAutomationCapsule(ShopProductRarity expectedRarity)
        {
            if (!IsServer || RemainingCapsules.Value <= 0) return false;
            ShopClawPrizeNetwork consumed = null;
            foreach (ShopClawPrizeNetwork prize in activePrizes)
            {
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value) continue;
                ShopProductRarity candidateRarity = (ShopProductRarity)Mathf.Clamp(
                    prize.SpawnedRarity.Value, 0, (int)ShopProductRarity.UltraRare);
                if (candidateRarity == ShopProductRarity.UltraRare || candidateRarity != expectedRarity) continue;
                consumed = prize;
                break;
            }
            if (consumed == null) return false;
            if (!consumed.ServerMarkAwarded()) return false;
            RemainingCapsules.Value = Mathf.Max(0, RemainingCapsules.Value - 1);
            StartCoroutine(ServerDespawnAwardedPrize(consumed));
            return true;
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
                if (config.SpawnGuardEnabled)
                {
                    float halfWidth = config.SpawnGuardWidth * 0.5f - radius;
                    float halfDepth = config.SpawnGuardDepth * 0.5f - radius * 0.35f;
                    // Fill the guarded drop zone in three non-overlapping columns and
                    // vertical tiers.  Randomly clamping all spawn points into this small
                    // zone previously collapsed them to the same edge and skipped most of
                    // the requested pool.  Vertical tiers are collision-free at creation
                    // and settle naturally onto the shallow feeder afterwards.
                    int slot = occupied.Count;
                    int column = slot % 3;
                    int tier = slot / 3;
                    candidateLocal.x = Mathf.Lerp(-halfWidth, halfWidth, column * 0.5f);
                    candidateLocal.z = Mathf.Clamp(config.SpawnGuardCenterZ,
                        config.SpawnGuardCenterZ - halfDepth,
                        config.SpawnGuardCenterZ + halfDepth);
                    candidateLocal.y += tier * (radius * 2f + clearance);
                }
                else
                {
                    candidateLocal.x = Mathf.Clamp(candidateLocal.x, config.XBounds.x + 0.05f,
                        config.XBounds.y - 0.05f);
                    candidateLocal.z = Mathf.Clamp(candidateLocal.z, config.ZBounds.x + 0.05f,
                        config.ZBounds.y - 0.05f);
                }
                candidate = transform.TransformPoint(candidateLocal);
                var occupiedPositions = new List<Vector3>(occupied.Count);
                var occupiedRadii = new List<float>(occupied.Count);
                foreach ((Vector3 otherPosition, float otherRadius) in occupied)
                {
                    occupiedPositions.Add(otherPosition);
                    occupiedRadii.Add(otherRadius);
                }
                bool canPlace = true;
                if (config.SpawnGuardEnabled)
                {
                    // Guard tiers are deliberately separated vertically, so validate the
                    // real 3D distance here.  The legacy helper is planar by design and
                    // would reject every safe tier above the first three capsules.
                    for (int i = 0; i < occupiedPositions.Count; i++)
                    {
                        float required = radius + occupiedRadii[i];
                        if ((candidate - occupiedPositions[i]).sqrMagnitude < required * required)
                        {
                            canPlace = false;
                            break;
                        }
                    }
                }
                else
                {
                    canPlace = ShopClawSpawnRules.CanPlace(candidate, radius, occupiedPositions,
                        occupiedRadii);
                }
                if (!canPlace) continue;
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
                if (products[i] != null && !products[i].ExclusiveReward && ShopProductLocalization.IsCatTheme(products[i].Category) &&
                    products[i].Rarity == rarity)
                    matches.Add(products[i]);
            if (matches.Count == 0 && rarity == ShopProductRarity.UltraRare)
            {
                for (int i = 0; i < products.Length; i++)
                    if (products[i] != null && !products[i].ExclusiveReward && ShopProductLocalization.IsCatTheme(products[i].Category) &&
                        products[i].Rarity == ShopProductRarity.Rare)
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
            ShopTutorialRuntime.Report(ShopTutorialAction.MachineEntered);
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
            bool wasLocal = localMode;
            localMode = false;
            ShopInputModeManager.Pop(this);
            if (LocalActiveMachine == this) LocalActiveMachine = null;
            if (!wasLocal) return;
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
                costText.text = "1회 " + config.AttemptCost + "원   ·   가게 자금 " + coins +
                                "원   ·   남은 캡슐 " + RemainingCapsules.Value + " / " +
                                (Operations != null ? Operations.MachineDailyCapsuleCapacity : RemainingCapsules.Value);
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

        private void ApplyUpgradeAppearance()
        {
            int level = ShopNetworkGame.Instance != null
                ? ShopNetworkGame.Instance.ClawUpgradeLevel.Value
                : 0;
            if (level == appliedClawUpgradeAppearance || scoopRig == null ||
                scoopRig.VisualRoot == null) return;
            appliedClawUpgradeAppearance = level;
            Color color = level switch
            {
                1 => new Color(0.2f, 0.85f, 1f),
                2 => new Color(1f, 0.72f, 0.18f),
                _ => new Color(0.55f, 0.58f, 0.62f)
            };
            foreach (Renderer renderer in scoopRig.VisualRoot.GetComponentsInChildren<Renderer>(true))
            {
                MaterialPropertyBlock block = new();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_EmissionColor", level > 0 ? color * 0.55f : Color.black);
                renderer.SetPropertyBlock(block);
            }
        }

        private void CachePhysicalPresentation()
        {
            if (localGlassRenderers == null || localGlassRenderers.Length == 0)
            {
                Transform glass = transform.Find("Cabinet/Glass");
                if (glass != null) localGlassRenderers = glass.GetComponentsInChildren<Renderer>(true);
            }
        }

        private void SetupServerPhysicalScoop()
        {
            if (!IsServer || config == null || physicalClawReady) return;
            if (scoopRig == null) scoopRig = GetComponentInChildren<ShopClawScoopRig>(true);
            if (scoopRig == null || scoopRig.Body == null)
            {
                Debug.LogError("[ScoopPhysics] " + name +
                               ": 프리팹에 저장된 ShopClawScoopRig/Rigidbody가 없습니다.", this);
                return;
            }

            clawHead = scoopRig.transform;
            clawBody = scoopRig.Body;
            scoopRig.ConfigureBody();
            scoopRig.ConfigureOuterSurface(config.ScoopOuterPhysicsMaterial);
            if (carriageBody != null)
            {
                carriageBody.isKinematic = true;
                carriageBody.useGravity = false;
                carriageBody.interpolation = RigidbodyInterpolation.Interpolate;
            }
            physicalClawReady = true;
            scoopTargetRotation = transform.rotation;
            Vector3 start = transform.TransformPoint(
                new Vector3(RailPosition.Value.x, ClawHeight.Value, RailPosition.Value.y));
            clawBody.position = start;
            clawBody.rotation = scoopTargetRotation;
            Debug.Log("[ScoopPhysics] READY machine=" + config.MachineId +
                      " colliders=" + scoopRig.CompoundColliderCount +
                      " diameter=" + config.ScoopDiameter.ToString("F2") +
                      " rim=" + config.ScoopRimHeight.ToString("F2"), this);
        }

        private bool ServerUpdateScoopCurl(float dt)
        {
            float target = scoopReachedDigAngle
                ? config.ScoopCarryAngle
                : config.ScoopDigAngle;
            float direction = Mathf.Sign(target - scoopTiltAngle);
            float desiredVelocity = direction * config.ScoopMaxAngularSpeed;
            scoopAngularVelocity = Mathf.MoveTowards(scoopAngularVelocity, desiredVelocity,
                config.ScoopAngularAcceleration * dt);
            scoopTiltAngle = Mathf.MoveTowards(scoopTiltAngle, target,
                Mathf.Abs(scoopAngularVelocity) * dt);
            if (!scoopReachedDigAngle &&
                Mathf.Abs(scoopTiltAngle - config.ScoopDigAngle) <= 0.05f)
            {
                scoopReachedDigAngle = true;
                scoopAngularVelocity = 0f;
            }

            if (!scoopReachedDigAngle)
            {
                FingerClosed.Value = 0.5f * Mathf.Clamp01(
                    Mathf.Abs(scoopTiltAngle) / Mathf.Max(0.01f, config.ScoopDigAngle));
                return false;
            }

            float returnRange = Mathf.Max(0.01f,
                Mathf.Abs(config.ScoopDigAngle - config.ScoopCarryAngle));
            FingerClosed.Value = 0.5f + 0.5f * Mathf.Clamp01(
                1f - Mathf.Abs(scoopTiltAngle - config.ScoopCarryAngle) / returnRange);
            return Mathf.Abs(scoopTiltAngle - config.ScoopCarryAngle) <= 0.05f;
        }

        private void ServerDrivePhysicalScoop(float dt)
        {
            if (floorVerificationOverride || !physicalClawReady || scoopRig == null ||
                scoopRig.Body == null) return;

            bool pouring = State.Value == ShopClawMachineState.Release ||
                           State.Value == ShopClawMachineState.Judge;
            bool entryOpen = State.Value == ShopClawMachineState.Idle ||
                             State.Value == ShopClawMachineState.Reserved ||
                             State.Value == ShopClawMachineState.Aiming ||
                             State.Value == ShopClawMachineState.Descend ||
                             State.Value == ShopClawMachineState.Close;
            scoopRig.SetPourSurface(pouring, config.ScoopOuterPhysicsMaterial);
            scoopRig.SetPhysicalCollisionsEnabled(State.Value != ShopClawMachineState.Judge);
            if (pouring)
                scoopRig.SetPourOpening(scoopPourDirectionLocal, config.ScoopRimHeight,
                    config.ScoopOpenRimHeight);
            else if (State.Value == ShopClawMachineState.Ascend)
                scoopRig.SetEntryLipHeight(Mathf.Lerp(config.ScoopOpenRimHeight,
                        config.ScoopRimHeight,
                        Mathf.Clamp01(stateElapsed / config.ScoopLipCloseDuration)),
                    config.ScoopRimHeight, config.ScoopOpenRimHeight);
            else
                scoopRig.SetEntryLipsOpen(entryOpen, config.ScoopRimHeight,
                    config.ScoopOpenRimHeight);

            Vector3 targetWorld = transform.TransformPoint(
                new Vector3(RailPosition.Value.x, ClawHeight.Value, RailPosition.Value.y));
            // Lower the pan by its own rim height before tipping.  At the rail's parked
            // height a full pour rotation intersects the cabinet ceiling, so the sweep
            // correctly rejects it and prizes never leave the pan.
            if (pouring)
                targetWorld -= transform.up * config.ScoopRimHeight;
            Quaternion targetLocalRotation = Quaternion.identity;
            if (State.Value == ShopClawMachineState.Close)
            {
                targetLocalRotation = Quaternion.Euler(
                    scoopTiltAngle * scoopCurlDirection, 0f, 0f);
            }
            else if (State.Value == ShopClawMachineState.Release ||
                     State.Value == ShopClawMachineState.Judge)
            {
                float progress = State.Value == ShopClawMachineState.Judge
                    ? 1f
                    : Mathf.Clamp01(stateElapsed / Mathf.Max(0.05f, config.ReleaseDuration));
                float angle = config.PourAngle * progress * Mathf.Deg2Rad;
                Vector3 tiltedNormal = Vector3.up * Mathf.Cos(angle) +
                                       scoopPourDirectionLocal * Mathf.Sin(angle);
                targetLocalRotation = Quaternion.FromToRotation(Vector3.up, tiltedNormal);
                // As the pan becomes vertical its lower lip retracts horizontally. Move
                // the centre toward the chute by the same amount so the open lip remains
                // directly above the drop opening throughout the tip.
                float lipRetraction = config.ScoopDiameter * 0.5f *
                                       (1f - Mathf.Cos(angle));
                Vector3 pourDirectionWorld = transform.TransformDirection(scoopPourDirectionLocal);
                targetWorld += pourDirectionWorld * lipRetraction;
                if (State.Value == ShopClawMachineState.Judge)
                {
                    // Pull the now-vertical pan back out from under the capsule while the
                    // chute is still in its judging state.  This gives the prize time to
                    // fall and settle before the award window closes.
                    float clearanceProgress = Mathf.Clamp01(stateElapsed /
                        Mathf.Max(0.01f, config.ReleaseDuration));
                    targetWorld -= pourDirectionWorld * lipRetraction * clearanceProgress;
                }
                FingerClosed.Value = 1f - progress;
            }
            else if (State.Value == ShopClawMachineState.Ascend ||
                     State.Value == ShopClawMachineState.Return)
            {
                targetLocalRotation = Quaternion.Euler(
                    config.ScoopCarryAngle * scoopCurlDirection, 0f, 0f);
                FingerClosed.Value = 1f;
            }
            else
            {
                FingerClosed.Value = 0f;
            }

            scoopTargetRotation = transform.rotation * targetLocalRotation;
            float tilt = Mathf.Abs(targetLocalRotation.eulerAngles.x);
            if (tilt > 180f) tilt = 360f - tilt;
            bool pivotCurl = State.Value == ShopClawMachineState.Close ||
                             State.Value == ShopClawMachineState.Ascend ||
                             State.Value == ShopClawMachineState.Return;
            if (pivotCurl)
            {
                Vector3 pivotLocal = scoopRig.CurlPivotLocalPosition;
                Vector3 pivotWorld = targetWorld + transform.rotation * pivotLocal;
                targetWorld = pivotWorld - scoopTargetRotation * pivotLocal;
            }
            if (tilt > 0.01f && !pivotCurl)
                targetWorld += transform.up * (config.ScoopDiameter * 0.5f *
                                               Mathf.Sin(tilt * Mathf.Deg2Rad));
            bool wasBlocked = scoopBlockedDuringDescent;
            bool blocked = scoopRig.SweepMove(targetWorld, scoopTargetRotation,
                config.SweepSkin, false,
                out RaycastHit hit, out bool touchedPrize, config.ScoopRotationSweepStep);
            scoopTouchedPrize |= touchedPrize;

            if (State.Value == ShopClawMachineState.Release &&
                loggedReleaseAttempt != AttemptId.Value)
            {
                loggedReleaseAttempt = AttemptId.Value;
                Vector3 bodyPosition = scoopRig.Body.position;
                Vector3 chutePosition = ChuteWorldPosition;
                float nearestPrizeDistance = float.MaxValue;
                Vector3 nearestPrizePosition = Vector3.zero;
                foreach (ShopClawPrizeNetwork prize in activePrizes)
                {
                    if (prize == null || prize.Awarded.Value) continue;
                    float distance = Vector3.Distance(prize.transform.position, bodyPosition);
                    if (distance >= nearestPrizeDistance) continue;
                    nearestPrizeDistance = distance;
                    nearestPrizePosition = prize.transform.position;
                }
                Debug.Log("[ScoopPhysics] RELEASE_POSE attempt=" + AttemptId.Value +
                          " load=" + LastGripScore.Value.ToString("0") +
                          " body=" + bodyPosition.ToString("F3") +
                          " chute=" + chutePosition.ToString("F3") +
                          " horizontalError=" +
                          Vector2.Distance(new Vector2(bodyPosition.x, bodyPosition.z),
                              new Vector2(chutePosition.x, chutePosition.z)).ToString("F3"), this);
                Debug.Log("[ScoopPhysics] RELEASE_NEAREST attempt=" + AttemptId.Value +
                          " distance=" + nearestPrizeDistance.ToString("F3") +
                          " prize=" + nearestPrizePosition.ToString("F3"), this);
            }

            if (State.Value != ShopClawMachineState.Descend || !blocked) return;
            scoopBlockedDuringDescent = true;
            Vector3 actualLocal = transform.InverseTransformPoint(scoopRig.Body.position);
            ClawHeight.Value = Mathf.Max(actualLocal.y, config.DropHeight);
            if (wasBlocked) return;

            ShopClawPrizeNetwork blockedPrize = hit.collider != null
                ? hit.collider.GetComponentInParent<ShopClawPrizeNetwork>()
                : null;
            if (blockedPrize != null)
            {
                Debug.Log("[ScoopPhysics] CAPSULE_CONTACT machine=" + config.MachineId +
                          " prize=" + blockedPrize.NetworkObjectId, this);
                return;
            }

            float floorY = hit.point.y;
            if (scoopRig.TryGetFloorSurface(out float measuredFloor)) floorY = measuredFloor;
            lastFloorPenetrationMillimeters = Mathf.Max(0f,
                floorY + config.FloorClearance - scoopRig.BottomWorldY) * 1000f;
            floorContactSamples++;
            Debug.Log("[ScoopPhysics] FLOOR_CONTACT machine=" + config.MachineId +
                      " bottom=" + scoopRig.BottomWorldY.ToString("F5") +
                      " floor=" + floorY.ToString("F5") +
                      " penetrationMm=" + lastFloorPenetrationMillimeters.ToString("F3") +
                      " blocker=" + (hit.collider != null ? hit.collider.name : "none") +
                      " hit=" + hit.point.ToString("F3"), this);
        }

        private IEnumerator ServerVerifyScoopFloorContacts(int repetitions)
        {
            floorVerificationRunning = true;
            floorVerificationOverride = true;
            Vector3 savedPosition = scoopRig.Body.position;
            Quaternion savedRotation = scoopRig.Body.rotation;
            var prizeColliders = new List<(Collider collider, bool enabled)>();
            foreach (ShopClawPrizeNetwork prize in activePrizes)
            {
                if (prize == null) continue;
                foreach (Collider collider in prize.GetComponentsInChildren<Collider>(true))
                {
                    prizeColliders.Add((collider, collider.enabled));
                    collider.enabled = false;
                }
            }

            int contacts = 0;
            float maximumPenetration = 0f;
            try
            {
                for (int repetition = 1; repetition <= repetitions; repetition++)
                {
                    scoopRig.Body.position = transform.TransformPoint(
                        new Vector3(0f, config.TopHeight, 0f));
                    scoopRig.Body.rotation = transform.rotation;
                    Physics.SyncTransforms();
                    yield return new WaitForFixedUpdate();

                    for (int step = 0; step < 220; step++)
                    {
                        Vector3 target = scoopRig.Body.position - transform.up * 0.045f;
                        bool blocked = scoopRig.SweepMove(target, transform.rotation,
                            config.SweepSkin, false, out RaycastHit hit, out _);
                        yield return new WaitForFixedUpdate();
                        if (!blocked) continue;
                        float floorY = hit.point.y;
                        if (scoopRig.TryGetFloorSurface(out float measuredFloor)) floorY = measuredFloor;
                        float penetration = Mathf.Max(0f,
                            floorY + config.FloorClearance - scoopRig.BottomWorldY) * 1000f;
                        maximumPenetration = Mathf.Max(maximumPenetration, penetration);
                        contacts++;
                        Debug.Log("[ScoopPhysics] FLOOR_CONTACT verify=" + repetition +
                                  " bottom=" + scoopRig.BottomWorldY.ToString("F5") +
                                  " floor=" + floorY.ToString("F5") +
                                  " penetrationMm=" + penetration.ToString("F3"), this);
                        break;
                    }
                }
            }
            finally
            {
                foreach ((Collider collider, bool enabled) entry in prizeColliders)
                    if (entry.collider != null) entry.collider.enabled = entry.enabled;
                scoopRig.Body.position = savedPosition;
                scoopRig.Body.rotation = savedRotation;
                Physics.SyncTransforms();
                floorContactSamples = contacts;
                lastFloorPenetrationMillimeters = maximumPenetration;
                floorVerificationOverride = false;
                floorVerificationRunning = false;
                Debug.Log("[ScoopPhysics] FLOOR_VERIFY_COMPLETE machine=" + config.MachineId +
                          " contacts=" + contacts + "/" + repetitions +
                          " maxPenetrationMm=" + maximumPenetration.ToString("F3"), this);
            }
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
                "\n시도 " + AttemptId.Value + "\n팬 적재 수 " + LastGripScore.Value.ToString("0") +
                "\n바닥 관통(mm) " + LastFloorPenetrationMillimeters.ToString("0.000") +
                "\n팬 위 상품 " + HeldPrizeNetworkObjectId.Value;
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
                ShopClawMachineState.Descend => "팬 하강 중",
                ShopClawMachineState.Close => "상품 퍼올리는 중",
                ShopClawMachineState.Ascend => "팬 상승 중",
                ShopClawMachineState.Return => "출구로 이동 중",
                ShopClawMachineState.Release => "팬을 기울여 배출 중",
                ShopClawMachineState.Judge => "투하구 안정 판정 중",
                ShopClawMachineState.Cooldown => "결과",
                _ => state.ToString()
            };
        }
    }
}
