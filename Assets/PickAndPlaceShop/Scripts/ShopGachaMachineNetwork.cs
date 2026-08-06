using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShopGachaMachineNetwork : NetworkBehaviour
    {
        [SerializeField] private ShopGachaMachineConfig config;
        [SerializeField] private Transform interactionPoint;
        [SerializeField] private Transform handle;
        [SerializeField] private Transform capsule;
        [SerializeField] private Transform capsuleStart;
        [SerializeField] private Transform capsuleEnd;
        [SerializeField] private Renderer capsuleRenderer;
        [SerializeField] private Renderer statusLamp;
        [SerializeField] private TextMesh informationText;
        [SerializeField] private float interactionRange = 4.5f;

        public NetworkVariable<ShopGachaState> State = new(ShopGachaState.Idle);
        public NetworkVariable<ulong> OccupantClientId = new(ShopClawRules.NoOccupant);
        public NetworkVariable<int> AttemptId = new(0);
        public NetworkVariable<int> RemainingStock = new(0);
        public NetworkVariable<float> StateProgress = new(0f);
        public NetworkVariable<ShopGachaRarity> ResultRarity = new(ShopGachaRarity.Common);
        public NetworkVariable<FixedString64Bytes> ResultProduct = new(new FixedString64Bytes("상품 준비 중"));
        public NetworkVariable<FixedString128Bytes> ResultStorageMessage =
            new(new FixedString128Bytes("획득 결과를 확인하는 중입니다."));
        public NetworkVariable<int> Durability = new(0);
        public NetworkVariable<float> BrokenSecondsRemaining = new(0f);
        public NetworkVariable<int> TheftHitSerial = new(0);

        private readonly ShopAcquisitionAwardLedger awardLedger = new();
        private float stateElapsed;
        private int presentedAttempt = -1;
        private int observedDay = -1;
        private bool dailyRefillPending;
        private Vector3 damageVisualBasePosition;
        private float damageShakeUntil;
        private int observedTheftHitSerial;
        private ShopTheftConfig theftConfig;

        public string MachineId => config != null ? config.MachineId : name;
        public int AttemptCost => config != null ? config.AttemptCost : 0;
        public int EffectiveAttemptCost => config == null
            ? 0
            : Mathf.Max(1, Mathf.RoundToInt(config.AttemptCost *
                (ShopNetworkGame.Instance != null ? ShopNetworkGame.Instance.GachaCostMultiplier : 1f)));
        public string InteractionPrompt => State.Value == ShopGachaState.Idle
            ? config.DisplayName + " 돌리기 (-" + EffectiveAttemptCost + " 가게 자금)"
            : config.DisplayName + " 진행 중";
        public Vector3 InteractionWorldPosition => interactionPoint != null ? interactionPoint.position : transform.position;
        public bool IsBroken => BrokenSecondsRemaining.Value > 0f;

#if UNITY_EDITOR
        public void EditorConfigure(ShopGachaMachineConfig machineConfig, Transform usePoint, Transform turnHandle,
            Transform capsuleTransform, Transform start, Transform end, Renderer capsuleVisual, Renderer lamp,
            TextMesh display)
        {
            config = machineConfig;
            interactionPoint = usePoint;
            handle = turnHandle;
            capsule = capsuleTransform;
            capsuleStart = start;
            capsuleEnd = end;
            capsuleRenderer = capsuleVisual;
            statusLamp = lamp;
            informationText = display;
        }
#endif

        public override void OnNetworkSpawn()
        {
            theftConfig = ShopTheftConfig.Load();
            damageVisualBasePosition = transform.localPosition;
            if (IsServer)
            {
                // The existing multiplayer sample uses Distributed Authority. Pin shared shop
                // stations to the session owner so only the host can mutate authoritative state.
                if (NetworkManager.NetworkConfig.NetworkTopology == NetworkTopologyTypes.DistributedAuthority)
                    NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.SessionOwner, true);
                RemainingStock.Value = config != null ? config.DailyStock : 0;
                observedDay = ShopNetworkGame.Instance != null ? ShopNetworkGame.Instance.Day.Value : 1;
                State.Value = ShopGachaState.Idle;
                OccupantClientId.Value = ShopClawRules.NoOccupant;
                Durability.Value = theftConfig != null ? theftConfig.GachaDurability : 1;
                BrokenSecondsRemaining.Value = 0f;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
            ApplyCapsuleColor();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        public void RequestUse()
        {
            if (IsSpawned) RequestUseRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestUseRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (config == null || game == null) return;
            if (IsBroken)
            {
                game.ServerSetEvent(config.DisplayName + "은(는) 파손되어 수리 중입니다.");
                return;
            }
            if (!ShopClawRules.CanOperateDuring(game.Phase.Value))
            {
                game.ServerSetEvent("가챠는 준비 또는 영업 시간에 이용할 수 있습니다.");
                return;
            }
            if (State.Value != ShopGachaState.Idle)
            {
                game.ServerSetEvent("가챠 연출이 끝날 때까지 기다려주세요.");
                return;
            }
            if (!IsPlayerInRange(sender))
            {
                game.ServerSetEvent("가챠 기계 가까이에서 E키를 눌러주세요.");
                return;
            }
            if (RemainingStock.Value <= 0)
            {
                game.ServerSetEvent(config.DisplayName + "의 오늘 재고가 모두 소진되었습니다.");
                return;
            }

            int balance = game.Coins.Value;
            if (!ShopEconomy.TrySpend(ref balance, EffectiveAttemptCost))
            {
                game.ServerSetEvent("가챠를 이용할 가게 자금이 부족합니다.");
                return;
            }

            game.Coins.Value = balance;
            RemainingStock.Value--;
            OccupantClientId.Value = sender;
            AttemptId.Value++;
            ResultRarity.Value = ShopAcquisitionRules.SelectGachaRarity(Random.value,
                config.UncommonChance, config.RareChance);
            ResultProduct.Value = new FixedString64Bytes(config.ProductFor(ResultRarity.Value, AttemptId.Value));
            SetState(ShopGachaState.InsertingCoin);
            game.ServerSetEvent(config.DisplayName + ": 동전을 넣었습니다. 캡슐을 뽑는 중입니다.");
        }

        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned) return;
            if (BrokenSecondsRemaining.Value > 0f)
            {
                BrokenSecondsRemaining.Value = Mathf.Max(0f,
                    BrokenSecondsRemaining.Value - Time.fixedDeltaTime);
                if (BrokenSecondsRemaining.Value <= 0f)
                {
                    ShopTheftConfig theft = ShopTheftConfig.Load();
                    Durability.Value = theft != null ? theft.GachaDurability : 1;
                    ShopNetworkGame.Instance?.ServerSetEvent(config.DisplayName + " 수리가 끝났습니다.");
                }
                return;
            }
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game != null && game.Day.Value != observedDay)
            {
                observedDay = game.Day.Value;
                dailyRefillPending = true;
            }
            if (dailyRefillPending && State.Value == ShopGachaState.Idle)
            {
                RemainingStock.Value = config != null ? config.DailyStock : 0;
                dailyRefillPending = false;
            }
            if (State.Value == ShopGachaState.Idle) return;
            stateElapsed += Time.fixedDeltaTime;
            float duration = DurationFor(State.Value);
            float progress = duration <= 0f ? 1f : Mathf.Clamp01(stateElapsed / duration);
            if (Mathf.Abs(StateProgress.Value - progress) >= 0.04f || progress >= 1f)
                StateProgress.Value = progress;
            if (stateElapsed < duration) return;

            switch (State.Value)
            {
                case ShopGachaState.InsertingCoin: SetState(ShopGachaState.TurningHandle); break;
                case ShopGachaState.TurningHandle: SetState(ShopGachaState.DispensingCapsule); break;
                case ShopGachaState.DispensingCapsule: SetState(ShopGachaState.OpeningCapsule); break;
                case ShopGachaState.OpeningCapsule:
                    ServerAwardResult();
                    SetState(ShopGachaState.Result);
                    break;
                case ShopGachaState.Result: SetState(ShopGachaState.Cooldown); break;
                case ShopGachaState.Cooldown:
                    OccupantClientId.Value = ShopClawRules.NoOccupant;
                    SetState(ShopGachaState.Idle);
                    break;
            }
        }

        private void Update()
        {
            ApplyTheftDamageVisual();
            ApplyVisuals();
            UpdateInformationText();
            TryPresentResult();
        }

        public bool ServerApplyTheftHit(ulong attackerClientId, int damage, ShopTheftConfig theft)
        {
            if (!IsServer || !IsSpawned || config == null || theft == null || IsBroken ||
                State.Value != ShopGachaState.Idle) return false;
            Durability.Value = Mathf.Max(0, Durability.Value - Mathf.Max(1, damage));
            TheftHitSerial.Value++;
            if (Durability.Value > 0)
            {
                ShopNetworkGame.Instance?.ServerSetEvent(config.DisplayName + " 내구도 " +
                                                          Durability.Value + "/" + theft.GachaDurability);
                return true;
            }

            ShopGachaRarity rarity = ShopTheftRules.SelectTheftGacha(Random.value, theft);
            ShopProductDefinition product = config.ProductDefinitionFor(rarity, TheftHitSerial.Value);
            if (product == null && rarity != ShopGachaRarity.Common)
                product = config.ProductDefinitionFor(ShopGachaRarity.Common, TheftHitSerial.Value);
            ShopNetworkGame game = ShopNetworkGame.Instance;
            int visualIndex = product != null ? ShopClawPrizeNetwork.FindCatalogIndex(product.PrizePrefab) : -1;
            if (game == null || product == null ||
                !game.ServerTryAcquireItem(attackerClientId, product, visualIndex,
                    ShopAcquisitionSource.Theft, 0, out ShopContainerKind destination))
            {
                Durability.Value = 1;
                game?.ServerSetEvent("보관 공간이 부족해 파손 보상을 획득하지 못했습니다.");
                return true;
            }

            BrokenSecondsRemaining.Value = theft.BrokenRecoverySeconds;
            game.ServerRecordAcquired(1);
            game.ServerSetEvent(config.DisplayName + " 파손 강탈: " + product.DisplayName +
                                (destination == ShopContainerKind.PersonalInventory
                                    ? " → 개인 가방"
                                    : " → 공용 창고"));
            ShopPlayerTheftNetwork.ServerReportTheftSuccess(attackerClientId, ShopTheftAction.GachaBreak);
            return true;
        }

        private void ApplyTheftDamageVisual()
        {
            if (theftConfig == null) theftConfig = ShopTheftConfig.Load();
            if (theftConfig == null) return;
            if (TheftHitSerial.Value != observedTheftHitSerial)
            {
                observedTheftHitSerial = TheftHitSerial.Value;
                damageShakeUntil = Time.time + theftConfig.DamageShakeSeconds;
            }
            if (Time.time < damageShakeUntil)
                transform.localPosition = damageVisualBasePosition +
                                          new Vector3(Mathf.Sin(Time.time * theftConfig.DamageShakeFrequency) *
                                                      theftConfig.DamageShakeDistance, 0f, 0f);
            else if (transform.localPosition != damageVisualBasePosition)
                transform.localPosition = damageVisualBasePosition;
        }

        private void ServerAwardResult()
        {
            if (!awardLedger.TryRecord(AttemptId.Value) || ShopNetworkGame.Instance == null) return;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            bool rare = ResultRarity.Value == ShopGachaRarity.Rare;
            ShopProductDefinition product = config.ProductDefinitionFor(
                ResultRarity.Value, AttemptId.Value);
            if (product == null)
            {
                Debug.LogError("[Arcade Gacha] " + config.DisplayName +
                               "의 보상 상품 데이터 참조가 비어 있습니다. 문자열 fallback은 사용하지 않습니다.", this);
                game.ServerSetEvent(config.DisplayName + ": 상품 데이터 오류로 획득을 보류했습니다.");
                return;
            }
            int visualIndex = product != null
                ? ShopClawPrizeNetwork.FindCatalogIndex(product.PrizePrefab)
                : -1;
            bool stored = game.ServerTryAcquireItem(OccupantClientId.Value, product, visualIndex,
                out ShopContainerKind destination);
            ResultStorageMessage.Value = new FixedString128Bytes(stored
                ? destination == ShopContainerKind.PersonalInventory
                    ? "가방에 넣었습니다."
                    : "가방이 가득 차 창고로 보냈습니다."
                : "가방과 창고가 가득 차 상품 획득을 보류했습니다.");
            if (stored)
            {
                game.ServerRecordAcquired(1);
                ShopDifferentiationController.Instance?.ServerCollectEmptyCapsule();
            }
            ShopProgressionManager progression = ShopProgressionManager.Instance;
            if (progression == null)
                Debug.LogError("[Progression] 가챠 컬렉션 관리자를 찾지 못했습니다.", this);
            else
                progression.RecordAcquisition(product.StableItemId, product.DisplayName,
                    config.MachineId, rare, stored ? 1 : 0);
            game.ServerSetEvent(config.DisplayName + " 결과: " + ResultProduct.Value + " (" +
                                RarityLabel(ResultRarity.Value) + ") - " +
                                (stored
                                    ? destination == ShopContainerKind.PersonalInventory
                                        ? "개인 인벤토리에 추가했습니다."
                                        : "개인 인벤토리가 가득 차 공용 창고로 이동했습니다."
                                    : "인벤토리와 창고가 모두 가득 차 지급을 보류했습니다."));
            Debug.Log("[Arcade Gacha] AWARD machine=" + MachineId + " attempt=" + AttemptId.Value +
                      " rarity=" + ResultRarity.Value + " product=" + ResultProduct.Value +
                       " stored=" + stored + " destination=" + destination +
                      " coins=" + game.Coins.Value + " inventory=" + game.Inventory.Value);
        }

        private void TryPresentResult()
        {
            if (State.Value != ShopGachaState.Result || AttemptId.Value == presentedAttempt ||
                config == null || NetworkManager.Singleton == null ||
                OccupantClientId.Value != NetworkManager.Singleton.LocalClientId) return;
            ShopProductDefinition product = config.ProductDefinitionFor(ResultRarity.Value, AttemptId.Value);
            if (product == null) return;
            presentedAttempt = AttemptId.Value;
            ShopCapsuleOpeningPresenter.Show("가챠 결과", product, config.CapsuleColor,
                ResultStorageMessage.Value.ToString());
            Debug.Log("[Arcade Result UI] gacha shown attempt=" + AttemptId.Value +
                      " product=" + product.DisplayName, this);
        }

        private bool IsPlayerInRange(ulong clientId)
        {
            return NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                   client.PlayerObject != null &&
                   Vector3.Distance(client.PlayerObject.transform.position, InteractionWorldPosition) <= interactionRange;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (OccupantClientId.Value == clientId) OccupantClientId.Value = ShopClawRules.NoOccupant;
        }

        private void SetState(ShopGachaState next)
        {
            State.Value = next;
            stateElapsed = 0f;
            StateProgress.Value = 0f;
        }

        private static float DurationFor(ShopGachaState state) => state switch
        {
            ShopGachaState.InsertingCoin => 0.35f,
            ShopGachaState.TurningHandle => 0.85f,
            ShopGachaState.DispensingCapsule => 0.65f,
            ShopGachaState.OpeningCapsule => 0.55f,
            ShopGachaState.Result => 1.8f,
            ShopGachaState.Cooldown => 0.35f,
            _ => 0f
        };

        private void ApplyVisuals()
        {
            if (handle != null)
            {
                float angle = State.Value == ShopGachaState.TurningHandle ? StateProgress.Value * 360f : 0f;
                handle.localRotation = Quaternion.Euler(angle, 0f, 0f);
            }
            if (capsule != null && capsuleStart != null && capsuleEnd != null)
            {
                bool visible = State.Value == ShopGachaState.DispensingCapsule ||
                               State.Value == ShopGachaState.OpeningCapsule || State.Value == ShopGachaState.Result;
                capsule.gameObject.SetActive(visible);
                float travel = State.Value == ShopGachaState.DispensingCapsule ? StateProgress.Value : 1f;
                capsule.position = Vector3.Lerp(capsuleStart.position, capsuleEnd.position, travel);
                float scale = State.Value == ShopGachaState.OpeningCapsule ? 1f + StateProgress.Value * 0.25f : 1f;
                capsule.localScale = Vector3.one * (0.32f * scale);
            }
            if (statusLamp != null)
            {
                Color color = IsBroken ? new Color(1f, 0.12f, 0.08f) :
                    State.Value == ShopGachaState.Idle ? new Color(0.2f, 1f, 0.5f) :
                    State.Value == ShopGachaState.Result ? new Color(1f, 0.8f, 0.2f) : new Color(0.2f, 0.65f, 1f);
                MaterialPropertyBlock block = new();
                statusLamp.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_EmissionColor", color * 1.4f);
                statusLamp.SetPropertyBlock(block);
            }
        }

        private void ApplyCapsuleColor()
        {
            if (capsuleRenderer == null || config == null) return;
            MaterialPropertyBlock block = new();
            capsuleRenderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", config.CapsuleColor);
            capsuleRenderer.SetPropertyBlock(block);
        }

        private void UpdateInformationText()
        {
            if (informationText == null || config == null) return;
            string result = State.Value == ShopGachaState.Result
                ? "\n결과: " + ResultProduct.Value + " / " + RarityLabel(ResultRarity.Value)
                : string.Empty;
            informationText.text = config.DisplayName + "\n1회 " + EffectiveAttemptCost +
                                   (EffectiveAttemptCost < config.AttemptCost ? " (할인)" : string.Empty) +
                                   " | 남은 캡슐 " +
                                   RemainingStock.Value + result +
                                   (IsBroken
                                       ? "\n파손 · 수리 " + Mathf.CeilToInt(BrokenSecondsRemaining.Value) + "초"
                                       : "\n내구도 " + Durability.Value + "/" +
                                         (ShopTheftConfig.Load()?.GachaDurability ?? Durability.Value));
        }

        private static string RarityLabel(ShopGachaRarity rarity) => rarity switch
        {
            ShopGachaRarity.Rare => "희귀",
            ShopGachaRarity.Uncommon => "고급",
            _ => "일반"
        };
    }
}
