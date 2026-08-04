#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    [AddComponentMenu("")]
    public sealed class ShopScoopPlayVerifier : MonoBehaviour
    {
        private ShopClawMachineNetwork machine;
        private int targetAttempts;
        private int completedAttempts;
        private int previousAwardedCount;
        private int observedAttemptId = -1;
        private Vector2 targetRail;
        private readonly List<int> awards = new();
        private readonly List<int> peakLoads = new();
        private int peakLoad;
        private float previousTimeScale;
        private bool waitingForRefillSettle;
        private float replayReadyAt;
        private int nonContactEjections;
        private readonly HashSet<int> countedEjections = new();

        public static bool Begin(ShopClawMachineNetwork target, int attempts)
        {
            if (target == null || FindFirstObjectByType<ShopScoopPlayVerifier>() != null) return false;
            ShopScoopPlayVerifier verifier = target.gameObject.AddComponent<ShopScoopPlayVerifier>();
            verifier.machine = target;
            verifier.targetAttempts = Mathf.Clamp(attempts, 1, 50);
            verifier.previousAwardedCount = target.AwardedCount.Value;
            verifier.previousTimeScale = Time.timeScale;
            Time.timeScale = 8f;
            verifier.waitingForRefillSettle = true;
            verifier.replayReadyAt = Time.unscaledTime + 1f;
            return true;
        }

        private void Update()
        {
            if (machine == null || ShopNetworkGame.Instance == null || NetworkManager.Singleton == null)
            {
                Finish("required runtime object missing");
                return;
            }

            NetworkObject player = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (player != null) player.transform.position = machine.OperatorWorldPosition;
            MonitorNonContactEjections();

            if (waitingForRefillSettle)
            {
                if (Time.unscaledTime < replayReadyAt) return;
                waitingForRefillSettle = false;
                machine.RequestReplay();
                return;
            }

            switch (machine.State.Value)
            {
                case ShopClawMachineState.Idle:
                    if (completedAttempts >= targetAttempts) Finish(null);
                    else machine.RequestUse();
                    break;
                case ShopClawMachineState.Reserved:
                    break;
                case ShopClawMachineState.Aiming:
                    if (observedAttemptId != machine.AttemptId.Value)
                    {
                        observedAttemptId = machine.AttemptId.Value;
                        targetRail = ChoosePrizeApproach(observedAttemptId);
                    }
                    Vector2 error = targetRail - machine.RailPosition.Value;
                    if (error.magnitude <= 0.09f)
                    {
                        machine.RequestInput(Vector2.zero);
                        machine.RequestDrop();
                    }
                    else
                    {
                        machine.RequestInput(Vector2.ClampMagnitude(error.normalized, 1f));
                    }
                    break;
                case ShopClawMachineState.Cooldown:
                    if (observedAttemptId == -machine.AttemptId.Value) break;
                    int delta = Mathf.Max(0, machine.AwardedCount.Value - previousAwardedCount);
                    previousAwardedCount = machine.AwardedCount.Value;
                    awards.Add(delta);
                    peakLoads.Add(peakLoad);
                    peakLoad = 0;
                    completedAttempts++;
                    observedAttemptId = -machine.AttemptId.Value;
                    if (completedAttempts >= targetAttempts) Finish(null);
                    else
                    {
                        machine.RefillPrizesForScoopVerification();
                        // The verification refill deliberately resets the machine's
                        // network award counter. Rebase here so the next round's delta is
                        // measured from that reset instead of being under-counted.
                        previousAwardedCount = machine.AwardedCount.Value;
                        waitingForRefillSettle = true;
                        replayReadyAt = Time.unscaledTime + 0.75f;
                    }
                    break;
            }
            peakLoad = Mathf.Max(peakLoad, Mathf.RoundToInt(machine.LastGripScore.Value));
        }

        private Vector2 ChoosePrizeApproach(int attemptId)
        {
            ShopClawPrizeNetwork[] prizes = FindObjectsByType<ShopClawPrizeNetwork>(
                FindObjectsSortMode.None);
            var candidates = new List<ShopClawPrizeNetwork>();
            float radius = machine.Config.ScoopDiameter * 0.5f + machine.Config.SweepSkin;
            float minimumX = machine.Config.XBounds.x + radius;
            float maximumX = Mathf.Min(0f, machine.Config.XBounds.y - radius);
            Vector2 scoopZBounds = machine.Config.ScoopZBounds;
            float minimumZ = scoopZBounds.x + radius;
            float maximumZ = scoopZBounds.y;
            if (maximumZ < minimumZ)
                minimumZ = maximumZ = (scoopZBounds.x + scoopZBounds.y) * 0.5f;
            foreach (ShopClawPrizeNetwork prize in prizes)
            {
                if (prize == null || prize.Awarded.Value || !prize.IsSpawned ||
                    prize.MachineNetworkObjectId.Value != machine.NetworkObjectId) continue;
                Vector3 local = machine.transform.InverseTransformPoint(prize.transform.position);
                if (local.y < machine.Config.DropHeight - 0.1f ||
                    local.y > machine.Config.DropHeight + 1.1f) continue;
                if (local.x < minimumX || local.x > maximumX ||
                    local.z < minimumZ - radius || local.z > maximumZ + radius) continue;
                candidates.Add(prize);
            }

            if (candidates.Count == 0) return Vector2.zero;
            candidates.Sort((left, right) =>
            {
                Vector3 leftLocal = machine.transform.InverseTransformPoint(left.transform.position);
                Vector3 rightLocal = machine.transform.InverseTransformPoint(right.transform.position);
                return new Vector2(leftLocal.x, leftLocal.z).sqrMagnitude.CompareTo(
                    new Vector2(rightLocal.x, rightLocal.z).sqrMagnitude);
            });
            ShopClawPrizeNetwork target = candidates[0];
            Vector3 prizeLocal = machine.transform.InverseTransformPoint(target.transform.position);
            return new Vector2(Mathf.Clamp(prizeLocal.x, minimumX, maximumX),
                Mathf.Clamp(prizeLocal.z, minimumZ, maximumZ));
        }

        private void MonitorNonContactEjections()
        {
            ShopClawPrizeNetwork[] prizes = FindObjectsByType<ShopClawPrizeNetwork>(
                FindObjectsSortMode.None);
            foreach (ShopClawPrizeNetwork prize in prizes)
            {
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value ||
                    prize.MachineNetworkObjectId.Value != machine.NetworkObjectId) continue;
                Vector3 local = machine.transform.InverseTransformPoint(prize.transform.position);
                if (!ShopClawRules.IsPrizeOutsidePlayableArea(local,
                        machine.Config.XBounds, machine.Config.ZBounds)) continue;
                int id = prize.GetInstanceID();
                if (!countedEjections.Add(id) ||
                    (machine.ScoopRig != null && machine.ScoopRig.HasContactedPrize(prize))) continue;
                nonContactEjections++;
                Debug.LogError("[ScoopPhysics] NON_CONTACT_EJECTION prize=" + id +
                               " local=" + local.ToString("F3"), this);
            }
        }

        private void Finish(string error)
        {
            Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
            int total = 0;
            foreach (int count in awards) total += count;
            float average = awards.Count > 0 ? (float)total / awards.Count : 0f;
            Debug.Log("[ScoopPhysics] BALANCE_COMPLETE attempts=" + awards.Count +
                      " distribution=[" + string.Join(",", awards) + "] total=" + total +
                      " average=" + average.ToString("F2") +
                      " nonContactEjections=" + nonContactEjections +
                      " peakLoads=[" + string.Join(",", peakLoads) + "]" +
                      (string.IsNullOrEmpty(error) ? string.Empty : " error=" + error), this);
            Destroy(this);
        }
    }
}
#endif
