#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Editor
{
    public static class ShopPhysicalClawPlayVerifier
    {
        public static bool Completed { get; internal set; }
        public static string LastReportPath { get; internal set; } = string.Empty;

        public static void StartVerification(int rounds = 20, float soakSeconds = 180f)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Play Mode에서만 물리 검증을 시작할 수 있습니다.");
            Completed = false;
            LastReportPath = string.Empty;
            GameObject existing = GameObject.Find("[QA] Physical Claw Verifier");
            if (existing != null) UnityEngine.Object.Destroy(existing);
            GameObject host = new("[QA] Physical Claw Verifier");
            ShopPhysicalClawQaDriver driver = host.AddComponent<ShopPhysicalClawQaDriver>();
            driver.Configure(Mathf.Max(20, rounds), Mathf.Max(120f, soakSeconds));
        }
    }

    [DefaultExecutionOrder(-20000)]
    public sealed class ShopPhysicalClawQaDriver : MonoBehaviour
    {
        private readonly List<ShopPhysicalClawRoundResult> rounds = new();
        private readonly Dictionary<ShopClawMachineState, float> maximumStateDurations = new();
        private ShopClawMachineNetwork machine;
        private NetworkObject player;
        private int targetRounds;
        private float soakDuration;
        private float startedAt;
        private float stateStartedAt;
        private float soakStartedAt = -1f;
        private float nextSampleAt;
        private float nextInteractionPulseAt;
        private float nextCommandAt;
        private ShopClawMachineState previousState;
        private Vector2 targetRail;
        private int trackedAttempt = -1;
        private int awardCountAtAttemptStart;
        private bool requestedInteraction;
        private bool screenshotClose;
        private bool screenshotAscend;
        private bool screenshotRelease;
        private int outsideEvents;
        private int deepPenetrations;
        private int highReleaseVelocityEvents;
        private int multiLiftEvents;
        private int stationaryJitterEvents;
        private int samples;
        private float accumulatedFps;
        private int initialPrizeCount;
        private float peakContactPercent;
        private float peakPrizeHeight;
        private float minimumOpenTipGap = float.MaxValue;
        private float minimumClosedTipGap = float.MaxValue;
        private float maximumCapsuleDiameter;
        private int capsuleColliderViolations;

        public void Configure(int roundsToRun, float soakSeconds)
        {
            targetRounds = roundsToRun;
            soakDuration = soakSeconds;
            startedAt = Time.unscaledTime;
        }

        private void OnDisable()
        {
            if (machine != null && machine.IsSpawned) machine.RequestInput(Vector2.zero);
        }

        private void Update()
        {
            if (machine == null || !machine.IsSpawned)
            {
                machine = FindObjectsByType<ShopClawMachineNetwork>(FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None)
                    .FirstOrDefault(item => item.Config != null && item.Config.MachineId == 101);
                if (machine == null) return;
                previousState = machine.State.Value;
                stateStartedAt = Time.unscaledTime;
                initialPrizeCount = CountPrizes();
                if (ShopNetworkGame.Instance != null && ShopNetworkGame.Instance.IsServer)
                {
                    int requiredBudget = targetRounds * machine.Config.AttemptCost + 1000;
                    ShopNetworkGame.Instance.Coins.Value =
                        Mathf.Max(ShopNetworkGame.Instance.Coins.Value, requiredBudget);
                }
            }
            if (player == null)
            {
                NetworkManager manager = NetworkManager.Singleton;
                player = manager?.LocalClient?.PlayerObject;
                if (player == null) return;
            }

            ObserveStateTransition();
            SamplePhysics();

            if (soakStartedAt >= 0f)
            {
                machine.RequestInput(Vector2.zero);
                if (Time.unscaledTime - soakStartedAt >= soakDuration) Finish();
                return;
            }

            if (!requestedInteraction)
            {
                MovePlayerToOperator();
                machine.RequestUse();
                requestedInteraction = true;
                nextInteractionPulseAt = Time.unscaledTime + 2f;
                return;
            }

            if (machine.OccupantClientId.Value == ShopClawRules.NoOccupant)
            {
                if (Time.unscaledTime >= nextInteractionPulseAt)
                {
                    MovePlayerToOperator();
                    machine.RequestUse();
                    nextInteractionPulseAt = Time.unscaledTime + 2f;
                }
                return;
            }

            DriveRound();
        }

        private void DriveRound()
        {
            switch (machine.State.Value)
            {
                case ShopClawMachineState.Reserved:
                    machine.RequestInput(Vector2.zero);
                    break;
                case ShopClawMachineState.Aiming:
                    EnsureAttemptTarget();
                    Vector2 delta = targetRail - machine.RailPosition.Value;
                    if (delta.magnitude > 0.012f)
                    {
                        machine.RequestInput(Vector2.ClampMagnitude(delta / 0.15f, 1f));
                        nextCommandAt = Time.unscaledTime + 0.75f;
                    }
                    else if (Time.unscaledTime >= nextCommandAt)
                    {
                        if (ShopNetworkGame.Instance != null && ShopNetworkGame.Instance.IsServer &&
                            ShopNetworkGame.Instance.Coins.Value < machine.Config.AttemptCost)
                            ShopNetworkGame.Instance.Coins.Value =
                                targetRounds * machine.Config.AttemptCost + 1000;
                        machine.RequestInput(Vector2.zero);
                        machine.RequestDrop();
                        nextCommandAt = Time.unscaledTime + 0.5f;
                    }
                    break;
                case ShopClawMachineState.Cooldown:
                    RecordRound();
                    if (rounds.Count >= targetRounds)
                    {
                        soakStartedAt = Time.unscaledTime;
                        Capture("soak_start");
                        machine.RequestInput(Vector2.zero);
                    }
                    else if (Time.unscaledTime >= nextCommandAt)
                    {
                        machine.RequestReplay();
                        nextCommandAt = Time.unscaledTime + 0.75f;
                    }
                    break;
                default:
                    machine.RequestInput(Vector2.zero);
                    break;
            }
        }

        private void EnsureAttemptTarget()
        {
            if (trackedAttempt == machine.AttemptId.Value) return;
            trackedAttempt = machine.AttemptId.Value;
            awardCountAtAttemptStart = machine.AwardedCount.Value;
            targetRail = PickTarget();
            screenshotClose = false;
            screenshotAscend = false;
            screenshotRelease = false;
            peakContactPercent = 0f;
            peakPrizeHeight = float.MinValue;
        }

        private Vector2 PickTarget()
        {
            Vector2 fallback = Vector2.zero;
            float best = float.MaxValue;
            foreach (ShopClawPrizeNetwork prize in FindObjectsByType<ShopClawPrizeNetwork>(
                         FindObjectsSortMode.None))
            {
                if (prize == null || !prize.IsSpawned || prize.Awarded.Value ||
                    prize.MachineNetworkObjectId.Value != machine.NetworkObjectId) continue;
                Collider[] physicalColliders = prize.GetComponentsInChildren<Collider>(true)
                    .Where(collider => !collider.isTrigger).ToArray();
                if (physicalColliders.Length == 0) continue;
                Bounds physicalBounds = physicalColliders[0].bounds;
                for (int i = 1; i < physicalColliders.Length; i++)
                    physicalBounds.Encapsulate(physicalColliders[i].bounds);
                Vector3 local = machine.transform.InverseTransformPoint(physicalBounds.center);
                Vector2 candidate = ShopClawRules.ClampRail(new Vector2(local.x, local.z),
                    machine.Config.XBounds, machine.Config.ZBounds);
                float volume = physicalColliders
                    .Select(collider => collider.bounds.size.x * collider.bounds.size.y *
                                        collider.bounds.size.z)
                    .DefaultIfEmpty(0f).Max();
                // Regression runs target a physically catchable body instead of a tiny accessory.
                float priority = candidate.sqrMagnitude - volume * 8f;
                if (priority >= best) continue;
                best = priority;
                fallback = candidate;
            }
            return fallback;
        }

        private void ObserveStateTransition()
        {
            ShopClawMachineState current = machine.State.Value;
            if (current == previousState) return;
            float duration = Time.unscaledTime - stateStartedAt;
            if (!maximumStateDurations.TryGetValue(previousState, out float maximum) || duration > maximum)
                maximumStateDurations[previousState] = duration;
            previousState = current;
            stateStartedAt = Time.unscaledTime;

            if (rounds.Count == 0 && current == ShopClawMachineState.Close && !screenshotClose)
            {
                screenshotClose = true;
                LogGripGeometry("CLOSE_START");
                Capture("round01_close");
            }
            if (rounds.Count == 0 && current == ShopClawMachineState.Ascend && !screenshotAscend)
            {
                screenshotAscend = true;
                LogGripGeometry("ASCEND_START");
                Capture("round01_ascend");
            }
            if (rounds.Count == 0 && current == ShopClawMachineState.Release && !screenshotRelease)
            {
                screenshotRelease = true;
                Capture("round01_release");
            }
        }

        private void LogGripGeometry(string phase)
        {
            ConfigurableJoint head = machine.GetComponentInChildren<ConfigurableJoint>(true);
            if (head == null) return;
            Vector3 headLocal = machine.transform.InverseTransformPoint(head.transform.position);
            ShopClawPrizeNetwork nearest = null;
            Bounds nearestBounds = default;
            float nearestDistance = float.MaxValue;
            foreach (ShopClawPrizeNetwork prize in FindObjectsByType<ShopClawPrizeNetwork>(
                         FindObjectsSortMode.None))
            {
                if (prize == null || prize.Awarded.Value ||
                    prize.MachineNetworkObjectId.Value != machine.NetworkObjectId) continue;
                Collider physical = prize.GetComponentsInChildren<Collider>(true)
                    .FirstOrDefault(collider => !collider.isTrigger);
                if (physical == null) continue;
                Vector3 local = machine.transform.InverseTransformPoint(physical.bounds.center);
                float distance = new Vector2(local.x - headLocal.x, local.z - headLocal.z).magnitude;
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = prize;
                nearestBounds = physical.bounds;
            }
            if (nearest == null) return;
            Rigidbody carriage = machine.GetComponentsInChildren<Rigidbody>(true)
                .FirstOrDefault(body => body.name == "PhysicalClawCarriage");
            Vector3 carriageLocal = carriage != null
                ? machine.transform.InverseTransformPoint(carriage.position)
                : Vector3.positiveInfinity;
            string distances = string.Join(",", machine.GetComponentsInChildren<CapsuleCollider>(true)
                .Where(collider => collider.name == "FingerCollider_Tip")
                .Select(collider => Vector3.Distance(collider.ClosestPoint(nearestBounds.center),
                    nearestBounds.center).ToString("0.000")));
            string angles = string.Join(",", machine.GetComponentsInChildren<HingeJoint>(true)
                .Select(hinge => hinge.angle.ToString("0.0")));
            Collider[] fingerParts = machine.GetComponentsInChildren<CapsuleCollider>(true)
                .Where(collider => collider.name.StartsWith("FingerCollider_"))
                .Cast<Collider>().ToArray();
            int siblingOverlaps = 0;
            float deepestSiblingOverlap = 0f;
            var siblingPairs = new List<string>();
            for (int first = 0; first < fingerParts.Length; first++)
            for (int second = first + 1; second < fingerParts.Length; second++)
            {
                Rigidbody firstBody = fingerParts[first].attachedRigidbody;
                Rigidbody secondBody = fingerParts[second].attachedRigidbody;
                if (firstBody == null || secondBody == null || firstBody == secondBody) continue;
                if (!Physics.ComputePenetration(fingerParts[first], fingerParts[first].transform.position,
                        fingerParts[first].transform.rotation, fingerParts[second],
                        fingerParts[second].transform.position, fingerParts[second].transform.rotation,
                        out _, out float distance)) continue;
                siblingOverlaps++;
                deepestSiblingOverlap = Mathf.Max(deepestSiblingOverlap, distance);
                siblingPairs.Add(fingerParts[first].name + "/" + fingerParts[second].name);
            }
            Debug.Log("[PhysicalClawQA] " + phase + "_GEOMETRY target=" + nearest.name +
                      " targetRail=" + targetRail + " rail=" + machine.RailPosition.Value +
                      " carriage=" + carriageLocal + " head=" + headLocal +
                      " headXZ=" + nearestDistance.ToString("0.000") +
                      " tipSurfaceDistances=" + distances + " angles=" + angles +
                      " siblingOverlaps=" + siblingOverlaps +
                      " siblingDepth=" + deepestSiblingOverlap.ToString("0.000") +
                      " siblingPairs=" + string.Join(",", siblingPairs), machine);
        }

        private void RecordRound()
        {
            if (rounds.Any(item => item.attemptId == machine.AttemptId.Value)) return;
            rounds.Add(new ShopPhysicalClawRoundResult
            {
                attemptId = machine.AttemptId.Value,
                success = machine.LastResultSuccess.Value,
                awarded = Mathf.Max(0, machine.AwardedCount.Value - awardCountAtAttemptStart),
                contactPercent = machine.LastGripScore.Value,
                peakContactPercent = peakContactPercent,
                peakPrizeHeight = peakPrizeHeight,
                elapsedSeconds = Time.unscaledTime - stateStartedAt
            });
            Debug.Log("[PhysicalClawQA] ROUND " + rounds.Count + "/" + targetRounds +
                      " success=" + machine.LastResultSuccess.Value +
                      " peakContacts=" + peakContactPercent.ToString("0") +
                      " peakY=" + peakPrizeHeight.ToString("0.00") +
                      " awarded=" + (machine.AwardedCount.Value - awardCountAtAttemptStart));
        }

        private void SamplePhysics()
        {
            if (Time.unscaledTime < nextSampleAt) return;
            nextSampleAt = Time.unscaledTime + 0.20f;
            samples++;
            accumulatedFps += 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            peakContactPercent = Mathf.Max(peakContactPercent, machine.LastGripScore.Value);

            ShopClawMachineNetwork[] machines = FindObjectsByType<ShopClawMachineNetwork>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            ShopClawPrizeNetwork[] prizes = FindObjectsByType<ShopClawPrizeNetwork>(
                FindObjectsSortMode.None);
            SampleCapsuleAndFingerClearance(prizes);
            foreach (ShopClawPrizeNetwork prize in prizes)
            {
                if (prize == null || prize.Awarded.Value) continue;
                ShopClawMachineNetwork owner = machines.FirstOrDefault(
                    item => item.NetworkObjectId == prize.MachineNetworkObjectId.Value);
                if (owner == null) continue;
                Vector3 local = owner.transform.InverseTransformPoint(prize.transform.position);
                if (owner == machine) peakPrizeHeight = Mathf.Max(peakPrizeHeight, local.y);
                if (ShopClawRules.IsPrizeOutsidePlayableArea(local, owner.Config.XBounds,
                        owner.Config.ZBounds, 0.4f, 0.35f, 4.2f))
                    outsideEvents++;
                float speed = prize.Body.linearVelocity.magnitude;
                if (owner.State.Value == ShopClawMachineState.Idle && speed > 0.035f && speed < 0.4f)
                    stationaryJitterEvents++;
                if ((owner.State.Value == ShopClawMachineState.Release ||
                     owner.State.Value == ShopClawMachineState.Judge) && speed > 4.5f)
                    highReleaseVelocityEvents++;
            }

            if (machine.State.Value == ShopClawMachineState.Ascend)
            {
                int lifted = prizes.Count(prize =>
                    prize != null && prize.MachineNetworkObjectId.Value == machine.NetworkObjectId &&
                    machine.transform.InverseTransformPoint(prize.transform.position).y >
                    machine.Config.DropHeight + 0.45f);
                if (lifted > 1) multiLiftEvents++;
            }

            if (machine.State.Value == ShopClawMachineState.Close ||
                machine.State.Value == ShopClawMachineState.Ascend)
                CountDeepFingerPenetrations(prizes);
        }

        private void SampleCapsuleAndFingerClearance(ShopClawPrizeNetwork[] prizes)
        {
            foreach (ShopClawPrizeNetwork prize in prizes)
            {
                if (prize == null || prize.MachineNetworkObjectId.Value != machine.NetworkObjectId) continue;
                Collider[] colliders = prize.GetComponentsInChildren<Collider>(true);
                if (colliders.Length != 1 || colliders[0] is not SphereCollider)
                    capsuleColliderViolations++;
                if (colliders.Length > 0 && colliders[0] is SphereCollider sphere)
                {
                    float scale = Mathf.Max(sphere.transform.lossyScale.x,
                        sphere.transform.lossyScale.y, sphere.transform.lossyScale.z);
                    maximumCapsuleDiameter = Mathf.Max(maximumCapsuleDiameter, sphere.radius * scale * 2f);
                }
            }

            CapsuleCollider[] tips = machine.GetComponentsInChildren<CapsuleCollider>(true)
                .Where(collider => collider.name == "FingerCollider_Tip").ToArray();
            if (tips.Length != 3) return;
            float surfaceGap = float.MaxValue;
            for (int first = 0; first < tips.Length; first++)
            for (int second = first + 1; second < tips.Length; second++)
            {
                Vector3 firstCenter = tips[first].transform.TransformPoint(tips[first].center);
                Vector3 secondCenter = tips[second].transform.TransformPoint(tips[second].center);
                float firstRadius = tips[first].radius * Mathf.Max(
                    tips[first].transform.lossyScale.x, tips[first].transform.lossyScale.z);
                float secondRadius = tips[second].radius * Mathf.Max(
                    tips[second].transform.lossyScale.x, tips[second].transform.lossyScale.z);
                surfaceGap = Mathf.Min(surfaceGap,
                    Vector3.Distance(firstCenter, secondCenter) - firstRadius - secondRadius);
            }
            if (machine.State.Value == ShopClawMachineState.Aiming)
                minimumOpenTipGap = Mathf.Min(minimumOpenTipGap, surfaceGap);
            if (machine.State.Value == ShopClawMachineState.Close)
                minimumClosedTipGap = Mathf.Min(minimumClosedTipGap, surfaceGap);
        }

        private void CountDeepFingerPenetrations(ShopClawPrizeNetwork[] prizes)
        {
            Collider[] fingers = machine.GetComponentsInChildren<CapsuleCollider>(true)
                .Where(collider => collider.name.StartsWith("FingerCollider_"))
                .Cast<Collider>().ToArray();
            foreach (Collider finger in fingers)
            foreach (ShopClawPrizeNetwork prize in prizes)
            {
                if (prize == null || prize.MachineNetworkObjectId.Value != machine.NetworkObjectId) continue;
                foreach (Collider prizeCollider in prize.GetComponentsInChildren<Collider>(true))
                {
                    if (!Physics.ComputePenetration(finger, finger.transform.position, finger.transform.rotation,
                            prizeCollider, prizeCollider.transform.position, prizeCollider.transform.rotation,
                            out _, out float distance) || distance <= 0.04f) continue;
                    deepPenetrations++;
                }
            }
        }

        private void Finish()
        {
            int finalPrizeCount = CountPrizes();
            var report = new ShopPhysicalClawQaReport
            {
                requestedRounds = targetRounds,
                completedRounds = rounds.Count,
                successes = rounds.Count(item => item.success),
                failures = rounds.Count(item => !item.success),
                totalAwards = rounds.Sum(item => item.awarded),
                outsideEvents = outsideEvents,
                deepPenetrations = deepPenetrations,
                highReleaseVelocityEvents = highReleaseVelocityEvents,
                multiLiftEvents = multiLiftEvents,
                stationaryJitterEvents = stationaryJitterEvents,
                initialPrizeCount = initialPrizeCount,
                finalPrizeCount = finalPrizeCount,
                minimumOpenTipGap = minimumOpenTipGap < float.MaxValue ? minimumOpenTipGap : -1f,
                minimumClosedTipGap = minimumClosedTipGap < float.MaxValue ? minimumClosedTipGap : -1f,
                maximumCapsuleDiameter = maximumCapsuleDiameter,
                capsuleColliderViolations = capsuleColliderViolations,
                averageFps = samples > 0 ? accumulatedFps / samples : 0f,
                soakSeconds = Time.unscaledTime - soakStartedAt,
                totalSeconds = Time.unscaledTime - startedAt,
                rounds = rounds,
                stateDurations = maximumStateDurations.Select(pair => new ShopPhysicalClawStateDuration
                {
                    state = pair.Key.ToString(),
                    maximumSeconds = pair.Value
                }).ToList()
            };
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "ValidationReports/PhysicalClaw/Runtime"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "physical_claw_qa.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Capture("qa_complete");
            ShopPhysicalClawPlayVerifier.LastReportPath = path;
            ShopPhysicalClawPlayVerifier.Completed = true;
            Debug.Log("[PhysicalClawQA] COMPLETE rounds=" + rounds.Count +
                      " success=" + report.successes + " fail=" + report.failures +
                      " outside=" + outsideEvents + " penetration=" + deepPenetrations +
                      " fps=" + report.averageFps.ToString("0.0") + " report=" + path);
            machine.RequestInput(Vector2.zero);
            enabled = false;
        }

        private void MovePlayerToOperator()
        {
            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            player.transform.SetPositionAndRotation(machine.OperatorWorldPosition, machine.transform.rotation);
            if (controller != null) controller.enabled = true;
        }

        private static int CountPrizes()
        {
            return FindObjectsByType<ShopClawPrizeNetwork>(FindObjectsSortMode.None)
                .Count(prize => prize != null && prize.IsSpawned && !prize.Awarded.Value);
        }

        private static void Capture(string label)
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "ValidationReports/PhysicalClaw/Runtime"));
            Directory.CreateDirectory(folder);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, label + ".png"));
        }
    }

    [Serializable]
    public sealed class ShopPhysicalClawQaReport
    {
        public int requestedRounds;
        public int completedRounds;
        public int successes;
        public int failures;
        public int totalAwards;
        public int outsideEvents;
        public int deepPenetrations;
        public int highReleaseVelocityEvents;
        public int multiLiftEvents;
        public int stationaryJitterEvents;
        public int initialPrizeCount;
        public int finalPrizeCount;
        public float minimumOpenTipGap;
        public float minimumClosedTipGap;
        public float maximumCapsuleDiameter;
        public int capsuleColliderViolations;
        public float averageFps;
        public float soakSeconds;
        public float totalSeconds;
        public List<ShopPhysicalClawRoundResult> rounds;
        public List<ShopPhysicalClawStateDuration> stateDurations;
    }

    [Serializable]
    public sealed class ShopPhysicalClawRoundResult
    {
        public int attemptId;
        public bool success;
        public int awarded;
        public float contactPercent;
        public float peakContactPercent;
        public float peakPrizeHeight;
        public float elapsedSeconds;
    }

    [Serializable]
    public sealed class ShopPhysicalClawStateDuration
    {
        public string state;
        public float maximumSeconds;
    }
}
#endif
