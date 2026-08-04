using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShopKujiStationNetwork : NetworkBehaviour
    {
        [SerializeField] private ShopKujiPoolConfig config;
        [SerializeField] private Transform interactionPoint;
        [SerializeField] private Transform ticket;
        [SerializeField] private Transform ticketStart;
        [SerializeField] private Transform ticketEnd;
        [SerializeField] private Transform drawHandle;
        [SerializeField] private Renderer statusLamp;
        [SerializeField] private TextMesh informationText;
        [SerializeField] private ShopKujiScratchView scratchView;
        [SerializeField] private float interactionRange = 4.5f;
        [SerializeField, Range(0.4f, 0.9f)] private float requiredScratchProgress = 0.65f;
        [SerializeField, Min(5f)] private float scratchTimeout = 20f;
        [SerializeField, Min(0.5f)] private float minimumScratchSeconds = 1.25f;

        public NetworkVariable<ShopKujiState> State = new(ShopKujiState.Idle);
        public NetworkVariable<ulong> OccupantClientId = new(ShopClawRules.NoOccupant);
        public NetworkVariable<int> AttemptId = new(0);
        public NetworkVariable<int> StockS = new(0);
        public NetworkVariable<int> StockA = new(0);
        public NetworkVariable<int> StockB = new(0);
        public NetworkVariable<int> StockC = new(0);
        public NetworkVariable<int> StockD = new(0);
        public NetworkVariable<float> StateProgress = new(0f);
        public NetworkVariable<ShopKujiRank> ResultRank = new(ShopKujiRank.D);
        public NetworkVariable<FixedString64Bytes> ResultProduct = new(new FixedString64Bytes("티켓 준비 중"));
        public NetworkVariable<FixedString128Bytes> ResultStorageMessage =
            new(new FixedString128Bytes("획득 결과를 확인하는 중입니다."));
        public NetworkVariable<bool> LastPrizeAwarded = new(false);
        public NetworkVariable<bool> CurrentDrawHasLastPrize = new(false);
        public NetworkVariable<bool> CurrentDrawHasCeiling = new(false);
        public NetworkVariable<int> DrawsSinceCeiling = new(0);
        public NetworkVariable<float> ScratchProgress = new(0f);

        private readonly ShopAcquisitionAwardLedger awardLedger = new();
        private float stateElapsed;
        private float scratchElapsed;
        private int presentedAttempt = -1;

        public string PoolId => config != null ? config.PoolId : name;
        public int TicketPrice => config != null ? config.TicketPrice : 0;
        public int EffectiveTicketPrice => config == null
            ? 0
            : Mathf.Max(1, Mathf.RoundToInt(config.TicketPrice *
                (ShopNetworkGame.Instance != null ? ShopNetworkGame.Instance.KujiCostMultiplier : 1f)));
        public int TotalRemaining => StockS.Value + StockA.Value + StockB.Value + StockC.Value + StockD.Value;
        public string LastPrize => config != null ? config.LastPrize : "마지막상";
        public string CeilingPrize => config != null ? config.CeilingPrize : "천장 보너스";
        public int CeilingDraws => config != null ? config.CeilingDraws : 1;
        public string InteractionPrompt => State.Value == ShopKujiState.Idle
            ? config.DisplayName + " 긁기 티켓 구매 (-" + EffectiveTicketPrice + " 가게 자금)"
            : State.Value == ShopKujiState.AwaitingScratch || State.Value == ShopKujiState.Scratching
                ? config.DisplayName + " 마우스로 긁는 중"
                : config.DisplayName + " 등급 공개 중";
        public Vector3 InteractionWorldPosition => interactionPoint != null ? interactionPoint.position : transform.position;

#if UNITY_EDITOR
        public void EditorConfigure(ShopKujiPoolConfig poolConfig, Transform usePoint, Transform ticketTransform,
            Transform start, Transform end, Transform handle, Renderer lamp, TextMesh display,
            ShopKujiScratchView view)
        {
            config = poolConfig;
            interactionPoint = usePoint;
            ticket = ticketTransform;
            ticketStart = start;
            ticketEnd = end;
            drawHandle = handle;
            statusLamp = lamp;
            informationText = display;
            scratchView = view;
        }
#endif

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // Keep the station on the session owner in the project's Distributed Authority
                // topology; clients can request a draw but cannot own or mutate its results.
                if (NetworkManager.NetworkConfig.NetworkTopology == NetworkTopologyTypes.DistributedAuthority)
                    NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.SessionOwner, true);
                ShopKujiStock stock = config != null ? config.InitialStock : default;
                StockS.Value = stock.S;
                StockA.Value = stock.A;
                StockB.Value = stock.B;
                StockC.Value = stock.C;
                StockD.Value = stock.D;
                State.Value = ShopKujiState.Idle;
                OccupantClientId.Value = ShopClawRules.NoOccupant;
                LastPrizeAwarded.Value = false;
                CurrentDrawHasLastPrize.Value = false;
                CurrentDrawHasCeiling.Value = false;
                DrawsSinceCeiling.Value = 0;
                ScratchProgress.Value = 0f;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
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
            if (!ShopClawRules.CanOperateDuring(game.Phase.Value))
            {
                game.ServerSetEvent("쿠지는 준비 또는 영업 시간에 구매할 수 있습니다.");
                return;
            }
            if (State.Value != ShopKujiState.Idle)
            {
                game.ServerSetEvent("현재 쿠지 티켓을 확인 중입니다. 잠시 기다려주세요.");
                return;
            }
            if (!IsPlayerInRange(sender))
            {
                game.ServerSetEvent("쿠지 진열대 가까이에서 E키를 눌러주세요.");
                return;
            }
            ShopKujiStock stock = CurrentStock();
            if (stock.Total <= 0)
            {
                game.ServerSetEvent(config.DisplayName + "의 모든 티켓이 판매되었습니다.");
                return;
            }
            int balance = game.Coins.Value;
            if (!ShopEconomy.TrySpend(ref balance, EffectiveTicketPrice))
            {
                game.ServerSetEvent("쿠지 티켓을 구매할 가게 자금이 부족합니다.");
                return;
            }

            game.Coins.Value = balance;
            ShopKujiRank rank = ShopAcquisitionRules.SelectKujiRank(Random.Range(0, stock.Total), stock);
            if (!stock.TryTake(rank)) return;
            ApplyStock(stock);
            bool lastPrize = ShopAcquisitionRules.ShouldAwardLastPrize(stock.Total, LastPrizeAwarded.Value);
            if (lastPrize) LastPrizeAwarded.Value = true;
            bool ceiling = ShopAcquisitionRules.ShouldAwardCeilingPrize(DrawsSinceCeiling.Value,
                config.CeilingDraws);
            DrawsSinceCeiling.Value = ceiling ? 0 : DrawsSinceCeiling.Value + 1;
            CurrentDrawHasLastPrize.Value = lastPrize;
            CurrentDrawHasCeiling.Value = ceiling;
            ScratchProgress.Value = 0f;
            ResultRank.Value = rank;
            OccupantClientId.Value = sender;
            AttemptId.Value++;
            ResultProduct.Value = new FixedString64Bytes(config.PrizeFor(rank, AttemptId.Value));
            SetState(ShopKujiState.DrawingTicket);
            game.ServerSetEvent(config.DisplayName + ": 티켓을 뽑았습니다. 마우스로 은박을 긁어주세요.");
        }

        public void RequestScratchProgress(float progress)
        {
            if (IsSpawned) SubmitScratchProgressRpc(Mathf.Clamp01(progress), AttemptId.Value);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitScratchProgressRpc(float proposedProgress, int attemptId, RpcParams rpcParams = default)
        {
            if (attemptId != AttemptId.Value || OccupantClientId.Value != rpcParams.Receive.SenderClientId) return;
            if (State.Value != ShopKujiState.AwaitingScratch && State.Value != ShopKujiState.Scratching) return;
            float allowance = Mathf.Clamp01(scratchElapsed / Mathf.Max(0.5f, minimumScratchSeconds));
            float accepted = ShopAcquisitionRules.ClampServerScratchProgress(ScratchProgress.Value,
                proposedProgress, allowance);
            if (accepted <= ScratchProgress.Value) return;
            ScratchProgress.Value = accepted;
            if (State.Value == ShopKujiState.AwaitingScratch) SetState(ShopKujiState.Scratching);
            if (ScratchProgress.Value >= requiredScratchProgress) SetState(ShopKujiState.RevealingTicket);
        }

        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || State.Value == ShopKujiState.Idle) return;
            if (State.Value == ShopKujiState.AwaitingScratch || State.Value == ShopKujiState.Scratching)
            {
                scratchElapsed += Time.fixedDeltaTime;
                StateProgress.Value = ScratchProgress.Value;
                if (scratchElapsed >= scratchTimeout)
                {
                    ScratchProgress.Value = 1f;
                    SetState(ShopKujiState.RevealingTicket);
                }
                return;
            }
            stateElapsed += Time.fixedDeltaTime;
            float duration = DurationFor(State.Value);
            float progress = duration <= 0f ? 1f : Mathf.Clamp01(stateElapsed / duration);
            if (Mathf.Abs(StateProgress.Value - progress) >= 0.04f || progress >= 1f)
                StateProgress.Value = progress;
            if (stateElapsed < duration) return;

            switch (State.Value)
            {
                case ShopKujiState.DrawingTicket:
                    scratchElapsed = 0f;
                    SetState(ShopKujiState.AwaitingScratch);
                    break;
                case ShopKujiState.RevealingTicket:
                    ServerAwardResult();
                    SetState(ShopKujiState.Result);
                    break;
                case ShopKujiState.Result: SetState(ShopKujiState.Cooldown); break;
                case ShopKujiState.Cooldown:
                    OccupantClientId.Value = ShopClawRules.NoOccupant;
                    CurrentDrawHasLastPrize.Value = false;
                    CurrentDrawHasCeiling.Value = false;
                    ScratchProgress.Value = 0f;
                    SetState(ShopKujiState.Idle);
                    break;
            }
        }

        private void Update()
        {
            ApplyVisuals();
            UpdateInformationText();
            TryPresentResult();
        }

        private void ServerAwardResult()
        {
            if (!awardLedger.TryRecord(AttemptId.Value) || ShopNetworkGame.Instance == null) return;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            int rewards = ShopAcquisitionRules.KujiRewardCount(CurrentDrawHasLastPrize.Value,
                CurrentDrawHasCeiling.Value);
            bool rare = ShopAcquisitionRules.IsRareKujiReward(ResultRank.Value,
                            CurrentDrawHasLastPrize.Value) || CurrentDrawHasCeiling.Value;
            List<ShopProductDefinition> rewardProducts = new()
            {
                config.PrizeDefinitionFor(ResultRank.Value, AttemptId.Value)
            };
            if (CurrentDrawHasLastPrize.Value)
                rewardProducts.Add(config.LastPrizeDefinition);
            if (CurrentDrawHasCeiling.Value)
                rewardProducts.Add(config.CeilingPrizeDefinition);
            int storedRewards = 0;
            ShopContainerKind lastDestination = ShopContainerKind.PersonalInventory;
            foreach (ShopProductDefinition product in rewardProducts)
            {
                if (product == null)
                {
                    Debug.LogError("[Arcade Kuji] " + config.DisplayName +
                                   "의 보상 상품 데이터 참조가 비어 있습니다. 문자열 fallback은 사용하지 않습니다.", this);
                    continue;
                }
                int visualIndex = ShopClawPrizeNetwork.FindCatalogIndex(product.PrizePrefab);
                if (!game.ServerTryAcquireItem(OccupantClientId.Value, product, visualIndex,
                        out lastDestination)) break;
                storedRewards++;
            }
            ResultStorageMessage.Value = new FixedString128Bytes(storedRewards == rewards
                ? lastDestination == ShopContainerKind.PersonalInventory
                    ? storedRewards + "개 상품을 가방에 넣었습니다."
                    : storedRewards + "개 상품을 창고로 보냈습니다."
                : storedRewards + "/" + rewards + "개 보관 · 용량 부족");
            game.ServerRecordAcquired(storedRewards);
            ShopProgressionManager progression = ShopProgressionManager.Instance;
            if (progression == null)
                Debug.LogError("[Progression] 쿠지 컬렉션 관리자를 찾지 못했습니다.", this);
            else
                progression.RecordAcquisition(config.PrizeDefinitionFor(ResultRank.Value, AttemptId.Value)?.StableItemId,
                    config.PrizeDefinitionFor(ResultRank.Value, AttemptId.Value)?.DisplayName,
                    ShopProductLocalization.CategoryId(
                        config.PrizeDefinitionFor(ResultRank.Value, AttemptId.Value)?.Category ??
                        ShopProductCategory.CatSeasonal),
                    rare, storedRewards);
            game.ServerSetEvent(config.DisplayName + " " + ResultRank.Value + "상: " +
                                ResultProduct.Value + " · " + storedRewards + "/" + rewards + "개 보관");
            Debug.Log("[Arcade Kuji] AWARD pool=" + PoolId + " attempt=" + AttemptId.Value +
                      " rank=" + ResultRank.Value + " last=" + CurrentDrawHasLastPrize.Value +
                      " ceiling=" + CurrentDrawHasCeiling.Value + " rewards=" + rewards +
                      " stored=" + storedRewards + " destination=" + lastDestination +
                      " remaining=" + TotalRemaining + " coins=" + game.Coins.Value +
                      " inventory=" + game.Inventory.Value);
        }

        private void TryPresentResult()
        {
            if (State.Value != ShopKujiState.Result || AttemptId.Value == presentedAttempt ||
                config == null || NetworkManager.Singleton == null ||
                OccupantClientId.Value != NetworkManager.Singleton.LocalClientId) return;
            ShopProductDefinition product = config.PrizeDefinitionFor(ResultRank.Value, AttemptId.Value);
            if (product == null) return;
            presentedAttempt = AttemptId.Value;
            ShopCapsuleOpeningPresenter.Show("쿠지 결과 · " + ResultRank.Value + "상", product,
                ResultAccent(ResultRank.Value), ResultStorageMessage.Value.ToString());
            Debug.Log("[Arcade Result UI] kuji shown attempt=" + AttemptId.Value +
                      " product=" + product.DisplayName, this);
        }

        private static Color ResultAccent(ShopKujiRank rank) => rank switch
        {
            ShopKujiRank.S => new Color(1f, 0.72f, 0.12f),
            ShopKujiRank.A => new Color(0.78f, 0.42f, 1f),
            ShopKujiRank.B => new Color(0.32f, 0.62f, 1f),
            ShopKujiRank.C => new Color(0.32f, 0.92f, 0.88f),
            _ => Color.white
        };

        private ShopKujiStock CurrentStock() => new(StockS.Value, StockA.Value, StockB.Value, StockC.Value, StockD.Value);

        private void ApplyStock(ShopKujiStock stock)
        {
            StockS.Value = stock.S;
            StockA.Value = stock.A;
            StockB.Value = stock.B;
            StockC.Value = stock.C;
            StockD.Value = stock.D;
        }

        private bool IsPlayerInRange(ulong clientId)
        {
            return NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                   client.PlayerObject != null &&
                   Vector3.Distance(client.PlayerObject.transform.position, InteractionWorldPosition) <= interactionRange;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (OccupantClientId.Value != clientId) return;
            if (IsServer && (State.Value == ShopKujiState.AwaitingScratch ||
                             State.Value == ShopKujiState.Scratching))
            {
                ScratchProgress.Value = 1f;
                SetState(ShopKujiState.RevealingTicket);
                return;
            }
            OccupantClientId.Value = ShopClawRules.NoOccupant;
        }

        private void SetState(ShopKujiState next)
        {
            State.Value = next;
            stateElapsed = 0f;
            StateProgress.Value = 0f;
        }

        private static float DurationFor(ShopKujiState state) => state switch
        {
            ShopKujiState.DrawingTicket => 0.75f,
            ShopKujiState.RevealingTicket => 1.1f,
            ShopKujiState.Result => 3f,
            ShopKujiState.Cooldown => 0.35f,
            _ => 0f
        };

        private void ApplyVisuals()
        {
            if (drawHandle != null)
            {
                float angle = State.Value == ShopKujiState.DrawingTicket ? Mathf.Sin(StateProgress.Value * Mathf.PI) * 35f : 0f;
                drawHandle.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
            if (ticket != null && ticketStart != null && ticketEnd != null)
            {
                bool visible = State.Value == ShopKujiState.DrawingTicket ||
                               State.Value == ShopKujiState.AwaitingScratch ||
                               State.Value == ShopKujiState.Scratching ||
                               State.Value == ShopKujiState.RevealingTicket || State.Value == ShopKujiState.Result;
                ticket.gameObject.SetActive(visible);
                float travel = State.Value == ShopKujiState.DrawingTicket ? StateProgress.Value : 1f;
                ticket.position = Vector3.Lerp(ticketStart.position, ticketEnd.position, travel);
                float revealRotation = State.Value == ShopKujiState.RevealingTicket ?
                    StateProgress.Value * 360f : 0f;
                ticket.localRotation = Quaternion.Euler(0f, revealRotation, 0f);
            }
            if (statusLamp != null)
            {
                Color color = State.Value == ShopKujiState.Idle ? new Color(0.2f, 1f, 0.5f) :
                    State.Value == ShopKujiState.Result ? new Color(1f, 0.65f, 0.2f) : new Color(0.65f, 0.35f, 1f);
                MaterialPropertyBlock block = new();
                statusLamp.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_EmissionColor", color * 1.35f);
                statusLamp.SetPropertyBlock(block);
            }
        }

        private void UpdateInformationText()
        {
            if (informationText == null || config == null) return;
            string result = State.Value == ShopKujiState.Result
                ? "\n결과: " + ResultRank.Value + "상 " + ResultProduct.Value +
                  (CurrentDrawHasLastPrize.Value ? " + 마지막상" : string.Empty) +
                  (CurrentDrawHasCeiling.Value ? " + 천장" : string.Empty)
                : string.Empty;
            bool detailed = ShopNetworkGame.Instance != null &&
                            ShopNetworkGame.Instance.KujiDetailedInformation;
            string detail = detailed
                ? "\nS " + StockS.Value + " / A " + StockA.Value + " / B " + StockB.Value +
                  " / C " + StockC.Value + " / D " + StockD.Value +
                  "\n마지막상 " + (LastPrizeAwarded.Value ? "지급 완료" : "남은 티켓 소진 시 지급")
                : "\nS " + StockS.Value + " / A " + StockA.Value + " / B " + StockB.Value;
            informationText.text = config.DisplayName + "\n티켓 " + EffectiveTicketPrice +
                                   (EffectiveTicketPrice < config.TicketPrice ? " (할인)" : string.Empty) +
                                   " | 전체 " + TotalRemaining + detail +
                                   "\n천장 " + DrawsSinceCeiling.Value + "/" + config.CeilingDraws + result;
        }
    }
}
