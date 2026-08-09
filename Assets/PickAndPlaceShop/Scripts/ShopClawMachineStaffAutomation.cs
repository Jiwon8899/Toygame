using Unity.Collections;
using UnityEngine;

namespace PickAndPlaceShop
{
    public sealed partial class ShopClawMachineNetwork
    {
        private const ulong StaffAutomationOccupant = ulong.MaxValue - 1UL;

        private bool staffAutomationActive;
        private bool staffAutomationResultReady;
        private bool staffAutomationSucceeded;
        private bool staffAutomationUsesBuffer;
        private bool staffAutomationDropRequested;
        private ulong staffAutomationBufferOwner;
        private float staffAutomationCostMultiplier = 1f;
        private float staffAutomationElapsed;
        private float staffAutomationCompletedElapsed;
        private int staffAutomationChargedCost;
        private int staffAutomationStartingAwardCount;
        private Vector2 staffAutomationTarget;
        private ShopClawStaffAutomationConfig staffAutomationConfig;

        public bool IsStaffAutomationActive => staffAutomationActive;

        public bool ServerBeginStaffAutomationRound(ulong bufferOwner, bool useBuffer,
            float costMultiplier)
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (!IsServer || !IsSpawned || config == null || game == null || staffAutomationActive ||
                staffAutomationResultReady || State.Value != ShopClawMachineState.Idle ||
                OccupantClientId.Value != ShopClawRules.NoOccupant || RemainingCapsules.Value <= 0 ||
                !ShopClawRules.CanOperateDuring(game.Phase.Value) ||
                !TrySelectStaffAutomationTarget(out staffAutomationTarget)) return false;

            int plannedCost = Mathf.Max(1,
                Mathf.RoundToInt(config.AttemptCost * Mathf.Clamp(costMultiplier, 0.1f, 1f)));
            if (game.Coins.Value < plannedCost) return false;

            staffAutomationActive = true;
            staffAutomationSucceeded = false;
            staffAutomationUsesBuffer = useBuffer;
            staffAutomationDropRequested = false;
            staffAutomationBufferOwner = bufferOwner;
            staffAutomationCostMultiplier = Mathf.Clamp(costMultiplier, 0.1f, 1f);
            staffAutomationElapsed = 0f;
            staffAutomationCompletedElapsed = 0f;
            staffAutomationChargedCost = 0;
            staffAutomationStartingAwardCount = AwardedCount.Value;

            OccupantClientId.Value = StaffAutomationOccupant;
            AttemptId.Value++;
            aimRemaining = EffectiveAimDuration;
            AimSecondsRemaining.Value = Mathf.CeilToInt(aimRemaining);
            autoDropIdleElapsed = 0f;
            AutoDropSecondsRemaining.Value = Mathf.CeilToInt(config.AutoDropDelay);
            LastResultSuccess.Value = false;
            roundAwardCount = 0;
            roundHadPhysicalLift = false;
            ResultMessage.Value = new FixedString128Bytes("알바가 실제 팬 크레인을 조작하고 있습니다.");
            SetState(ShopClawMachineState.Reserved);
            Debug.Log("[StaffClawPilot] START machine=" + config.MachineId +
                      " target=" + staffAutomationTarget.ToString("F2") +
                      " cost=" + plannedCost, this);
            return true;
        }

        public bool ServerTryConsumeStaffAutomationResult(out bool succeeded, out int chargedCost,
            out float elapsedSeconds, out bool usedBuffer)
        {
            succeeded = false;
            chargedCost = 0;
            elapsedSeconds = 0f;
            usedBuffer = false;
            if (!IsServer || !staffAutomationResultReady) return false;
            succeeded = staffAutomationSucceeded;
            chargedCost = staffAutomationChargedCost;
            elapsedSeconds = staffAutomationCompletedElapsed;
            usedBuffer = staffAutomationUsesBuffer;
            staffAutomationResultReady = false;
            return true;
        }

        internal void ServerTickStaffAutomation(float fixedDeltaTime)
        {
            if (!IsServer || !staffAutomationActive) return;
            staffAutomationElapsed += Mathf.Max(0f, fixedDeltaTime);
            ShopClawStaffAutomationConfig tuning = StaffAutomationTuning;
            if (staffAutomationElapsed >= tuning.MaximumCycleSeconds)
            {
                ServerCompleteStaffAutomation(false, "timeout");
                return;
            }

            if (State.Value == ShopClawMachineState.Aiming)
            {
                Vector2 delta = staffAutomationTarget - RailPosition.Value;
                if (delta.sqrMagnitude <= tuning.TargetArrivalTolerance * tuning.TargetArrivalTolerance)
                {
                    OperatorInput.Value = Vector2.zero;
                    if (!staffAutomationDropRequested)
                    {
                        staffAutomationDropRequested = true;
                        if (ServerBeginDrop(AttemptId.Value))
                        {
                            staffAutomationChargedCost = ServerGetCurrentAttemptCost();
                            Debug.Log("[StaffClawPilot] TARGET_REACHED machine=" + config.MachineId +
                                      " rail=" + RailPosition.Value.ToString("F2"), this);
                        }
                    }
                }
                else
                {
                    OperatorInput.Value = Vector2.ClampMagnitude(delta, 1f);
                }
            }
            else if (State.Value == ShopClawMachineState.Cooldown &&
                     stateElapsed >= tuning.CooldownObservationSeconds)
            {
                ServerCompleteStaffAutomation(AwardedCount.Value > staffAutomationStartingAwardCount,
                    "cooldown");
            }
            else if (State.Value == ShopClawMachineState.Idle)
            {
                ServerCompleteStaffAutomation(false, "reset");
            }
        }

        private int ServerGetCurrentAttemptCost()
        {
            if (config == null) return 0;
            return staffAutomationActive
                ? Mathf.Max(1, Mathf.RoundToInt(config.AttemptCost * staffAutomationCostMultiplier))
                : config.AttemptCost;
        }

        private bool ServerStoreStaffAutomationPrize(ShopNetworkGame game,
            ShopProductDefinition product, int visualIndex, out ShopContainerKind destination)
        {
            destination = ShopContainerKind.SharedStorage;
            if (!staffAutomationActive || game == null || product == null) return false;
            if (staffAutomationUsesBuffer)
                return game.ServerTryAcquireItem(ShopContainerRules.SharedOwner, product, visualIndex,
                    ShopAcquisitionSource.Automation, staffAutomationBufferOwner, out destination);
            return game.ServerTryAcquireSharedContainer(product, visualIndex,
                ShopContainerKind.SharedStorage, game.SharedStorageCapacity);
        }

        private bool TrySelectStaffAutomationTarget(out Vector2 target)
        {
            target = Vector2.zero;
            ShopClawPrizeNetwork selected = null;
            float bestScore = float.NegativeInfinity;
            Vector3 chuteLocal = transform.InverseTransformPoint(ChuteWorldPosition);
            Vector2 chute = new(chuteLocal.x, chuteLocal.z);
            ShopClawStaffAutomationConfig tuning = StaffAutomationTuning;
            for (int i = 0; i < activePrizes.Count; i++)
            {
                ShopClawPrizeNetwork prize = activePrizes[i];
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value) continue;
                ShopProductRarity rarity = (ShopProductRarity)Mathf.Clamp(prize.SpawnedRarity.Value,
                    0, (int)ShopProductRarity.UltraRare);
                if (rarity == ShopProductRarity.UltraRare) continue;
                Vector3 local = transform.InverseTransformPoint(GetPrizePhysicalCenter(prize));
                Vector2 rail = ShopClawRules.ClampRail(new Vector2(local.x, local.z),
                    config.XBounds, config.ScoopZBounds);
                float score = local.y * tuning.ExposedHeightWeight -
                              Vector2.Distance(rail, chute) * tuning.ChuteDistanceWeight;
                if (score <= bestScore) continue;
                bestScore = score;
                selected = prize;
                target = rail;
            }
            return selected != null;
        }

        private void ServerCompleteStaffAutomation(bool succeeded, string reason)
        {
            if (!staffAutomationActive) return;
            staffAutomationSucceeded = succeeded;
            staffAutomationCompletedElapsed = staffAutomationElapsed;
            staffAutomationResultReady = true;
            staffAutomationActive = false;
            Debug.Log("[StaffClawPilot] COMPLETE machine=" + (config != null ? config.MachineId : 0) +
                      " success=" + succeeded + " elapsed=" + staffAutomationElapsed.ToString("F2") +
                      " reason=" + reason, this);
            ServerResetMachine();
        }

        private ShopClawStaffAutomationConfig StaffAutomationTuning =>
            staffAutomationConfig != null
                ? staffAutomationConfig
                : staffAutomationConfig = ShopClawStaffAutomationConfig.Load();
    }
}
