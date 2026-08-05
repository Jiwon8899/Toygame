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

        private ShopOperationsConfig Operations => ShopLiveOperationsNetwork.Instance != null
            ? ShopLiveOperationsNetwork.Instance.Config
            : ShopOperationsConfig.Load();

        private void Awake()
        {
            if (machine == null) machine = GetComponent<ShopClawMachineNetwork>();
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
            if (!IsServer || !IsSpawned || Operations == null || machine == null) return;

            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null) return;
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
            if (machine.IsManuallyBusy)
            {
                State.Value = ShopAutomationState.PausedForManualPlay;
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
            int cost = machine.Config != null ? machine.Config.AttemptCost : 0;
            if (game == null || game.Coins.Value < cost)
            {
                State.Value = ShopAutomationState.StoppedNoFunds;
                Enabled.Value = false;
                return;
            }

            game.Coins.Value -= cost;
            TodayCost.Value += cost;
            ShopLiveOperationsNetwork.Instance?.ServerRecordAutomation(0, cost);
            if (random.NextDouble() > Operations.AutomaticSuccessRate) return;

            ShopProductRarity rarity = machine.Config.RarityWeights.Pick(random, false);
            ShopProductDefinition product = PickProduct(rarity);
            if (product == null) return;
            if (!game.ServerTryAcquireItem(ShopContainerRules.SharedOwner, product, 0,
                    ShopAcquisitionSource.Automation, BufferOwner, out _))
            {
                State.Value = ShopAutomationState.StoppedStorageFull;
                Enabled.Value = false;
                return;
            }

            TodayAcquired.Value++;
            ShopLiveOperationsNetwork.Instance?.ServerRecordAutomation(1, 0);
            ShopProgressionManager.Instance?.RecordAcquisition(product.StableItemId,
                product.DisplayName, ShopProductLocalization.CategoryId(product.Category),
                product.Rarity >= ShopProductRarity.Rare);
        }

        private ShopProductDefinition PickProduct(ShopProductRarity rarity)
        {
            ShopProductDefinition[] all = Resources.LoadAll<ShopProductDefinition>("Products");
            List<ShopProductDefinition> candidates = new();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && ShopProductLocalization.IsCatTheme(all[i].Category) &&
                    all[i].Rarity == rarity) candidates.Add(all[i]);
            if (candidates.Count == 0)
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && ShopProductLocalization.IsCatTheme(all[i].Category) &&
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
                _ => new Color(0.25f, 0.55f, 1f)
            };
            ShopBuildSafeMaterials.ApplyLitColor(indicator, color, true);
        }

        private void HandleVisualChanged(bool _, bool __) => ApplyVisual();
        private void HandleStateChanged(ShopAutomationState _, ShopAutomationState __) => UpdateIndicator();

        private static string StateLabel(ShopAutomationState state) => state switch
        {
            ShopAutomationState.Running => "가동 중",
            ShopAutomationState.PausedForManualPlay => "수동 조작으로 일시 정지",
            ShopAutomationState.PausedForClosing => "마감 중 일시 정지",
            ShopAutomationState.StoppedNoFunds => "자금 부족 정지",
            ShopAutomationState.StoppedStorageFull => "창고 만재 정지",
            ShopAutomationState.Off => "꺼짐",
            _ => "미설치"
        };
    }
}
