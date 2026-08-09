using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(NetworkObject), typeof(ShopClawMachineNetwork))]
    public sealed class ShopClawAutomationDevice : NetworkBehaviour
    {
        [SerializeField] private ShopClawMachineNetwork machine;
        [SerializeField] private Transform visualRoot;

        public NetworkVariable<bool> Installed = new(false);
        public NetworkVariable<bool> Enabled = new(false);
        public NetworkVariable<ShopAutomationState> State = new(ShopAutomationState.NotInstalled);
        public NetworkVariable<int> TodayAcquired = new(0);
        public NetworkVariable<int> TodayCost = new(0);
        public NetworkVariable<float> SecondsUntilAttempt = new(0f);

        private System.Random random;
        private int observedDay;
        private bool panelOpen;
        private Renderer indicator;
        private Transform operatorRoot;
        private Animator operatorAnimator;
        private Transform operatorArm;
        private Quaternion operatorArmRestRotation;
        private Vector3 operatorTargetLocal;
        private Quaternion operatorTargetLocalRotation;
        private bool operatorArrived;
        private float automationCycleStartedAt;
        private ShopClawStaffAutomationConfig staffAutomationConfig;
        private static readonly int MovingParameter = Animator.StringToHash("Moving");

        public int MachineId => machine != null && machine.Config != null ? machine.Config.MachineId : 0;
        public ulong BufferOwner => unchecked((ulong)(100000 + Mathf.Max(0, MachineId)));
        public int BufferedItemCount => ShopNetworkGame.Instance != null
            ? ShopNetworkGame.Instance.GetContainerSnapshot(BufferOwner, ShopContainerKind.AutomationBuffer).Used
            : 0;

        public int ServerStaffCollectToStorage()
        {
            if (!IsServer || ShopNetworkGame.Instance == null) return 0;
            return ShopNetworkGame.Instance.ServerMoveAutomationBuffer(BufferOwner,
                ShopContainerRules.SharedOwner, ShopContainerKind.SharedStorage);
        }

        public bool ServerTryStaffAttempt(float costMultiplier, out bool acquired)
        {
            acquired = false;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (!IsServer || machine == null || game == null || Operations == null ||
                machine.IsManuallyBusy || machine.AvailableCapsules <= 0 ||
                !ShopClawRules.CanOperateDuring(game.Phase.Value)) return false;
            bool started = machine.ServerBeginStaffAutomationRound(BufferOwner, false, costMultiplier);
            if (started) automationCycleStartedAt = Time.unscaledTime;
            return started;
        }

        private ShopOperationsConfig Operations => ShopLiveOperationsNetwork.Instance != null
            ? ShopLiveOperationsNetwork.Instance.Config
            : ShopOperationsConfig.Load();

        private void Awake()
        {
            if (machine == null) machine = GetComponent<ShopClawMachineNetwork>();
            ShopClawStaffAutomationDriver driver = GetComponent<ShopClawStaffAutomationDriver>();
            if (driver == null) driver = gameObject.AddComponent<ShopClawStaffAutomationDriver>();
            driver.Configure(machine);
            staffAutomationConfig = ShopClawStaffAutomationConfig.Load();
            EnsureVisual();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                random = new System.Random(unchecked(MachineId * 73856093 + Environment.TickCount));
                observedDay = ShopNetworkGame.Instance != null ? ShopNetworkGame.Instance.Day.Value : 1;
                SecondsUntilAttempt.Value = Operations != null ? Operations.AutomationAttemptInterval : 60f;
                if (ShopLiveOperationsNetwork.Instance != null &&
                    ShopLiveOperationsNetwork.Instance.TryConsumeAutomationSave(MachineId, out ShopAutomationMachineSave saved))
                {
                    Installed.Value = saved.installed;
                    Enabled.Value = saved.enabled;
                    SecondsUntilAttempt.Value = Mathf.Max(0f, saved.elapsedSeconds);
                    TodayAcquired.Value = Mathf.Max(0, saved.todayAcquired);
                    TodayCost.Value = Mathf.Max(0, saved.todayCost);
                }
            }
            ApplyVisual();
            Installed.OnValueChanged += HandleVisualChanged;
            State.OnValueChanged += HandleStateChanged;
        }

        public override void OnNetworkDespawn()
        {
            Installed.OnValueChanged -= HandleVisualChanged;
            State.OnValueChanged -= HandleStateChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (IsClient) UpdateLocalPanelInput();
            UpdateVisualOperator(Time.unscaledDeltaTime);
            if (!IsServer || !IsSpawned || Operations == null || machine == null) return;

            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null) return;
            ConsumePhysicalAutomationResult();
            if (game.Day.Value != observedDay)
            {
                observedDay = game.Day.Value;
                TodayAcquired.Value = 0;
                TodayCost.Value = 0;
            }

            if (!Installed.Value)
            {
                State.Value = ShopAutomationState.NotInstalled;
                return;
            }
            if (!Enabled.Value)
            {
                State.Value = ShopAutomationState.Off;
                return;
            }
            if (game.Phase.Value == ShopPhase.Summary || game.Phase.Value == ShopPhase.Complete)
            {
                State.Value = ShopAutomationState.PausedForClosing;
                return;
            }
            if (machine.IsStaffAutomationActive)
            {
                State.Value = ShopAutomationState.Running;
                return;
            }
            if (machine.IsManuallyBusy)
            {
                State.Value = ShopAutomationState.PausedForManualPlay;
                return;
            }
            if (machine.AvailableCapsules <= 0)
            {
                State.Value = ShopAutomationState.StoppedSoldOut;
                return;
            }

            State.Value = ShopAutomationState.Running;
            SecondsUntilAttempt.Value = Mathf.Max(0f, SecondsUntilAttempt.Value - Time.unscaledDeltaTime);
            if (SecondsUntilAttempt.Value > 0f) return;
            SecondsUntilAttempt.Value = Operations.AutomationAttemptInterval;
            ServerAttempt();
        }

        private void ServerAttempt()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (machine.AvailableCapsules <= 0)
            {
                State.Value = ShopAutomationState.StoppedSoldOut;
                return;
            }
            int cost = machine.Config != null ? machine.Config.AttemptCost : 0;
            if (game == null || game.Coins.Value < cost)
            {
                State.Value = ShopAutomationState.StoppedNoFunds;
                Enabled.Value = false;
                return;
            }

            if (machine.ServerBeginStaffAutomationRound(BufferOwner, true, 1f))
                automationCycleStartedAt = Time.unscaledTime;
        }

        private void ConsumePhysicalAutomationResult()
        {
            if (!IsServer || machine == null ||
                !machine.ServerTryConsumeStaffAutomationResult(out bool succeeded, out int chargedCost,
                    out float elapsedSeconds, out bool usedBuffer)) return;

            TodayCost.Value += Mathf.Max(0, chargedCost);
            if (succeeded) TodayAcquired.Value++;
            if (usedBuffer)
                ShopLiveOperationsNetwork.Instance?.ServerRecordAutomation(succeeded ? 1 : 0,
                    Mathf.Max(0, chargedCost));
            float interval = Operations != null ? Operations.AutomationAttemptInterval : 60f;
            if (usedBuffer && staffAutomationConfig != null && Operations != null)
                interval = staffAutomationConfig.BalancedPassiveCycleSeconds(interval,
                    Operations.AutomaticSuccessRate);
            float measuredElapsed = elapsedSeconds > 0f
                ? elapsedSeconds
                : Mathf.Max(0f, Time.unscaledTime - automationCycleStartedAt);
            SecondsUntilAttempt.Value = Mathf.Max(0f, interval - measuredElapsed);
        }

        private ShopProductDefinition PickProduct(ShopProductRarity rarity)
        {
            ShopProductDefinition[] all = Resources.LoadAll<ShopProductDefinition>("Products");
            List<ShopProductDefinition> candidates = new();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && !all[i].ExclusiveReward && ShopProductLocalization.IsCatTheme(all[i].Category) &&
                    all[i].Rarity == rarity) candidates.Add(all[i]);
            if (candidates.Count == 0)
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && !all[i].ExclusiveReward && ShopProductLocalization.IsCatTheme(all[i].Category) &&
                        all[i].Rarity != ShopProductRarity.UltraRare) candidates.Add(all[i]);
            return candidates.Count == 0 ? null : candidates[random.Next(candidates.Count)];
        }

        private void UpdateLocalPanelInput()
        {
            if (Keyboard.current == null || NetworkManager == null || NetworkManager.LocalClient == null) return;
            NetworkObject player = NetworkManager.LocalClient.PlayerObject;
            bool nearby = player != null && Vector3.Distance(player.transform.position, transform.position) <=
                (machine != null && machine.Config != null ? machine.Config.InteractionRange : 4.5f);
            if (!nearby)
            {
                panelOpen = false;
                return;
            }
            if (Keyboard.current.rKey.wasPressedThisFrame) panelOpen = !panelOpen;
            if (!panelOpen) return;
            if (Keyboard.current.digit1Key.wasPressedThisFrame) PurchaseOrToggleRpc();
            if (Keyboard.current.digit2Key.wasPressedThisFrame) EmptyBufferRpc(false);
            if (Keyboard.current.digit3Key.wasPressedThisFrame) EmptyBufferRpc(true);
        }

        private void OnGUI()
        {
            if (!panelOpen || !IsClient) return;
            ShopContainerSnapshot buffer = ShopNetworkGame.Instance != null
                ? ShopNetworkGame.Instance.GetContainerSnapshot(BufferOwner, ShopContainerKind.AutomationBuffer)
                : default;
            GUILayout.BeginArea(new Rect(Screen.width - 370, 150, 340, 250), GUI.skin.box);
            GUILayout.Label("자동 뽑기 장치");
            GUILayout.Label("상태: " + StateLabel(State.Value));
            GUILayout.Label("수집함: " + buffer.Used + " / " + buffer.Capacity + "칸");
            GUILayout.Label("오늘 획득 " + TodayAcquired.Value + "개 · 소모 " + TodayCost.Value + "원");
            GUILayout.Label(Installed.Value ? "[1] 켜기 / 끄기" : "[1] 장치 구매");
            GUILayout.Label("[2] 창고로 비우기  [3] 내 가방으로 비우기  [R] 닫기");
            GUILayout.EndArea();
        }

        [Rpc(SendTo.Server)]
        private void PurchaseOrToggleRpc(RpcParams rpcParams = default)
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || Operations == null) return;
            if (!Installed.Value)
            {
                if (game.Reputation.Value < Operations.AutomationUnlockReputation ||
                    game.Coins.Value < Operations.AutomationPurchasePrice) return;
                game.Coins.Value -= Operations.AutomationPurchasePrice;
                Installed.Value = true;
                Enabled.Value = true;
                SecondsUntilAttempt.Value = Operations.AutomationAttemptInterval;
            }
            else Enabled.Value = !Enabled.Value;
        }

        [Rpc(SendTo.Server)]
        private void EmptyBufferRpc(bool toPersonal, RpcParams rpcParams = default)
        {
            ShopNetworkGame.Instance?.ServerMoveAutomationBuffer(BufferOwner,
                rpcParams.Receive.SenderClientId,
                toPersonal ? ShopContainerKind.PersonalInventory : ShopContainerKind.SharedStorage);
        }

        public ShopAutomationMachineSave CaptureSave() => new()
        {
            machineId = MachineId,
            installed = Installed.Value,
            enabled = Enabled.Value,
            elapsedSeconds = SecondsUntilAttempt.Value,
            todayAcquired = TodayAcquired.Value,
            todayCost = TodayCost.Value
        };

        private void EnsureVisual()
        {
            if (visualRoot != null) return;
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "AutomationDeviceVisual";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0.78f, 1.15f, -0.58f);
            body.transform.localScale = new Vector3(0.34f, 0.52f, 0.24f);
            Collider col = body.GetComponent<Collider>();
            if (col != null) Destroy(col);
            visualRoot = body.transform;
            indicator = body.GetComponent<Renderer>();
        }

        private void ApplyVisual()
        {
            EnsureVisual();
            visualRoot.gameObject.SetActive(Installed.Value);
            UpdateIndicator();
        }

        private void UpdateIndicator()
        {
            if (indicator == null) return;
            Color color = State.Value switch
            {
                ShopAutomationState.Running => new Color(0.15f, 1f, 0.45f),
                ShopAutomationState.StoppedStorageFull => new Color(1f, 0.16f, 0.08f),
                ShopAutomationState.StoppedNoFunds => new Color(1f, 0.65f, 0.08f),
                ShopAutomationState.StoppedSoldOut => new Color(0.55f, 0.35f, 0.85f),
                _ => new Color(0.25f, 0.55f, 1f)
            };
            ShopBuildSafeMaterials.ApplyLitColor(indicator, color, true);
        }

        private void HandleVisualChanged(bool _, bool __) => ApplyVisual();
        private void HandleStateChanged(ShopAutomationState _, ShopAutomationState __) => UpdateIndicator();

        private void UpdateVisualOperator(float deltaTime)
        {
            bool shouldShow = Installed.Value && Enabled.Value;
            if (!shouldShow)
            {
                if (operatorRoot != null) operatorRoot.gameObject.SetActive(false);
                return;
            }
            EnsureOperatorVisual();
            if (operatorRoot == null) return;
            operatorRoot.gameObject.SetActive(true);
            if (!operatorArrived)
            {
                operatorRoot.localPosition = Vector3.MoveTowards(operatorRoot.localPosition,
                    operatorTargetLocal, Mathf.Max(0f, deltaTime) * 1.4f);
                operatorRoot.localRotation = Quaternion.Slerp(operatorRoot.localRotation,
                    operatorTargetLocalRotation, Mathf.Max(0f, deltaTime) * 6f);
                operatorArrived = (operatorRoot.localPosition - operatorTargetLocal).sqrMagnitude < 0.0025f;
                SetOperatorMoving(!operatorArrived);
                return;
            }

            SetOperatorMoving(false);
            ShopClawStaffAutomationConfig tuning = staffAutomationConfig;
            float frequency = tuning != null ? tuning.ArmCycleFrequency : 4.2f;
            float movingAngle = tuning != null ? tuning.MovingArmAngle : 12f;
            float captureAngle = tuning != null ? tuning.CaptureArmAngle : 28f;
            float workCycle = Mathf.Sin(Time.unscaledTime * frequency);
            ShopClawMachineState machineState = machine != null
                ? machine.State.Value
                : ShopClawMachineState.Idle;
            float armAngle = machineState == ShopClawMachineState.Descend ||
                             machineState == ShopClawMachineState.Close ||
                             machineState == ShopClawMachineState.Ascend
                ? captureAngle
                : workCycle * movingAngle;
            if (operatorArm != null)
                operatorArm.localRotation = operatorArmRestRotation * Quaternion.Euler(0f, 0f, armAngle);
            else
                operatorRoot.localPosition = operatorTargetLocal + Vector3.up * (0.015f * Mathf.Max(0f, workCycle));
        }

        private void EnsureOperatorVisual()
        {
            if (operatorRoot != null || machine == null) return;
            GameObject root = new("AutomationStaffVisual");
            root.transform.SetParent(transform, false);
            operatorRoot = root.transform;
            operatorTargetLocal = transform.InverseTransformPoint(machine.OperatorWorldPosition);
            operatorTargetLocalRotation = Quaternion.Inverse(transform.rotation) *
                                          Quaternion.LookRotation(transform.position - machine.OperatorWorldPosition,
                                              Vector3.up);
            operatorRoot.localPosition = operatorTargetLocal + Vector3.right * 2.2f;
            operatorRoot.localRotation = operatorTargetLocalRotation;

            ShopWorkforceConfig workforce = ShopWorkforceConfig.Load();
            GameObject[] pool = workforce != null ? workforce.AppearancePrefabs : null;
            GameObject visual = pool != null && pool.Length > 0 ? pool[(MachineId + 2) % pool.Length] : null;
            if (visual != null) visual = Instantiate(visual, operatorRoot);
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visual.name = "FallbackAutomationStaff";
                visual.transform.SetParent(operatorRoot, false);
                visual.transform.localPosition = Vector3.up;
            }
            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            operatorAnimator = visual.GetComponentInChildren<Animator>(true);
            if (operatorAnimator != null)
            {
                operatorAnimator.applyRootMotion = false;
                if (operatorAnimator.isHuman)
                    operatorArm = operatorAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            }
            if (operatorArm == null)
            {
                Transform[] bones = visual.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < bones.Length; i++)
                {
                    string boneName = bones[i].name;
                    if (boneName == "RightArm" || boneName.EndsWith(":RightArm", StringComparison.Ordinal))
                    {
                        operatorArm = bones[i];
                        break;
                    }
                }
            }
            if (operatorArm != null) operatorArmRestRotation = operatorArm.localRotation;
        }

        private void SetOperatorMoving(bool moving)
        {
            if (operatorAnimator == null) return;
            for (int i = 0; i < operatorAnimator.parameterCount; i++)
                if (operatorAnimator.parameters[i].nameHash == MovingParameter &&
                    operatorAnimator.parameters[i].type == AnimatorControllerParameterType.Bool)
                {
                    operatorAnimator.SetBool(MovingParameter, moving);
                    return;
                }
        }

        private static string StateLabel(ShopAutomationState state) => state switch
        {
            ShopAutomationState.Running => "가동 중",
            ShopAutomationState.PausedForManualPlay => "수동 조작으로 일시 정지",
            ShopAutomationState.PausedForClosing => "마감 중 일시 정지",
            ShopAutomationState.StoppedNoFunds => "자금 부족 정지",
            ShopAutomationState.StoppedSoldOut => "재고 소진 · 다음 날 리필",
            ShopAutomationState.StoppedStorageFull => "창고 만재 정지",
            ShopAutomationState.Off => "꺼짐",
            _ => "미설치"
        };
    }
}
