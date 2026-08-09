using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PickAndPlaceShop
{
    public readonly struct ShopStaffMachineOption
    {
        public readonly int Assignment;
        public readonly string Label;
        public ShopStaffMachineOption(int assignment, string label)
        {
            Assignment = assignment;
            Label = label;
        }
    }

    public static class ShopStaffMachineAssignment
    {
        private const int ClawBase = 1000;
        private const int KujiBase = 2000;

        public static List<ShopStaffMachineOption> Options()
        {
            List<ShopStaffMachineOption> result = new() { new ShopStaffMachineOption(0, "기본 업무") };
            ShopClawMachineNetwork[] claws = Object.FindObjectsByType<ShopClawMachineNetwork>(
                FindObjectsSortMode.None);
            System.Array.Sort(claws, (a, b) => MachineId(a).CompareTo(MachineId(b)));
            for (int i = 0; i < claws.Length; i++)
                if (claws[i] != null) result.Add(new ShopStaffMachineOption(ClawBase + MachineId(claws[i]),
                    ClawLabel(claws[i])));

            ShopKujiStationNetwork[] kuji = Object.FindObjectsByType<ShopKujiStationNetwork>(
                FindObjectsSortMode.None);
            System.Array.Sort(kuji, (a, b) => string.CompareOrdinal(a != null ? a.PoolId : string.Empty,
                b != null ? b.PoolId : string.Empty));
            for (int i = 0; i < kuji.Length; i++)
                if (kuji[i] != null) result.Add(new ShopStaffMachineOption(KujiBase + i, kuji[i].DisplayName));
            return result;
        }

        public static bool IsAvailable(int assignment) => assignment == 0 || TryResolve(assignment, out _, out _);

        public static string Label(int assignment) => TryResolve(assignment, out _, out string label)
            ? label : assignment == 0 ? "기본 업무" : "사용할 수 없는 기계";

        public static bool TryResolve(int assignment, out Component machine, out string label)
        {
            machine = null;
            label = "기본 업무";
            if (assignment == 0) return true;
            if (assignment >= ClawBase && assignment < KujiBase)
            {
                int id = assignment - ClawBase;
                ShopClawMachineNetwork[] claws = Object.FindObjectsByType<ShopClawMachineNetwork>(
                    FindObjectsSortMode.None);
                for (int i = 0; i < claws.Length; i++)
                    if (claws[i] != null && MachineId(claws[i]) == id)
                    {
                        machine = claws[i];
                        label = ClawLabel(claws[i]);
                        return true;
                    }
                return false;
            }
            if (assignment >= KujiBase)
            {
                int index = assignment - KujiBase;
                ShopKujiStationNetwork[] kuji = Object.FindObjectsByType<ShopKujiStationNetwork>(
                    FindObjectsSortMode.None);
                System.Array.Sort(kuji, (a, b) => string.CompareOrdinal(a != null ? a.PoolId : string.Empty,
                    b != null ? b.PoolId : string.Empty));
                if (index < 0 || index >= kuji.Length || kuji[index] == null) return false;
                machine = kuji[index];
                label = kuji[index].DisplayName;
                return true;
            }
            return false;
        }

        private static int MachineId(ShopClawMachineNetwork machine) =>
            machine != null && machine.Config != null ? machine.Config.MachineId : 0;

        private static string ClawLabel(ShopClawMachineNetwork machine)
        {
            if (machine != null && machine.Config != null &&
                !string.IsNullOrWhiteSpace(machine.Config.DisplayName))
                return machine.Config.DisplayName;
            return "인형뽑기 " + MachineId(machine) + "호기";
        }
    }

    [DefaultExecutionOrder(360)]
    public sealed class ShopStaffManager : MonoBehaviour
    {
        private sealed class StaffActor
        {
            public ShopStaffRole Role;
            public GameObject Root;
            public Animator Animator;
            public CharacterController Controller;
            public float NextWorkTime;
            public Vector3 Target;
            public Vector3 RequestedTarget;
            public float AvoidanceTimer;
            public float AvoidanceSign = 1f;
            public float VisualStationarySeconds;
            public bool VisualMoving;
            public float NavigationStuckSeconds;
            public int NavigationRecoveryAttempts;
            public float LastTargetDistance = float.PositiveInfinity;
        }

        private static readonly int MovingParameter = Animator.StringToHash("Moving");
        private static ShopStaffManager instance;
        private readonly List<StaffActor> actors = new();
        private readonly Collider[] targetOverlapBuffer = new Collider[24];
        private ShopWorkforceConfig config;
        private float nextRefresh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[Operations] Visible Staff");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopStaffManager>();
        }

        private void Awake()
        {
            config = ShopWorkforceConfig.Load();
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (instance == this) instance = null;
        }

        private void HandleSceneLoaded(Scene _, LoadSceneMode __)
        {
            ClearActors();
            nextRefresh = 0f;
        }

        private void Update()
        {
            if (config == null || ShopNetworkGame.Instance == null || ShopNightSalesSystem.Instance == null)
            {
                ClearActors();
                return;
            }

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.5f;
                RefreshAttendance();
            }
            UpdateActors();
        }

        public static void NotifyAssignmentChanged()
        {
            if (instance == null) return;
            for (int i = 0; i < instance.actors.Count; i++)
            {
                StaffActor actor = instance.actors[i];
                if (actor == null || actor.Root == null) continue;
                actor.RequestedTarget = new Vector3(float.PositiveInfinity, 0f, 0f);
                actor.NextWorkTime = Time.unscaledTime + 0.25f;
            }
        }

        private void RefreshAttendance()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            for (int i = 0; i < 3; i++)
            {
                ShopStaffRole role = (ShopStaffRole)i;
                if (isServer && game.IsStaffHired(role) && !game.IsStaffAttending(role))
                {
                    int wage = config.DailyWage(role);
                    if (game.Coins.Value >= wage)
                    {
                        game.Coins.Value -= wage;
                        game.ServerSetStaffAttendance(role, true);
                        game.ServerSetEvent(RoleName(role) + " 알바가 급여를 받고 다시 출근했습니다.");
                        ShopProgressionManager.Instance?.SaveNow();
                    }
                }

                StaffActor actor = Find(role);
                bool visible = game.IsStaffHired(role) && game.IsStaffAttending(role);
                if (visible && actor == null) CreateActor(role);
                else if (!visible && actor != null) RemoveActor(actor);
            }
        }

        private void CreateActor(ShopStaffRole role)
        {
            ShopNightSalesSystem sales = ShopNightSalesSystem.Instance;
            GameObject root = new("Staff_" + role);
            root.transform.SetParent(transform, false);
            root.transform.position = sales.EntrancePosition + Vector3.right * ((int)role - 1) * 0.75f;
            root.AddComponent<ShopWorldSafetyAgent>();

            GameObject visual = null;
            GameObject[] pool = config.AppearancePrefabs;
            if (pool != null && pool.Length > 0 && pool[(int)role % pool.Length] != null)
                visual = Instantiate(pool[(int)role % pool.Length], root.transform);
            if (visual == null)
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visual.name = "FallbackStaffVisual";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.up;
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            CharacterController controller = root.AddComponent<CharacterController>();
            controller.center = new Vector3(0f, 0.95f, 0f);
            controller.height = 1.8f;
            controller.radius = 0.32f;
            controller.stepOffset = 0.25f;
            controller.slopeLimit = 48f;
            controller.minMoveDistance = 0f;
            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.applyRootMotion = false;

            Vector3 requestedTarget = TargetFor(role);
            StaffActor actor = new()
            {
                Role = role,
                Root = root,
                Animator = animator,
                Controller = controller,
                RequestedTarget = requestedTarget,
                Target = requestedTarget,
                NextWorkTime = Time.unscaledTime + 1f
            };
            actor.Target = ResolveCollisionSafeTarget(actor, requestedTarget);
            actors.Add(actor);
        }

        private void UpdateActors()
        {
            bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            for (int i = 0; i < actors.Count; i++)
            {
                StaffActor actor = actors[i];
                if (actor.Root == null) continue;
                Vector3 requestedTarget = TargetFor(actor.Role);
                if ((requestedTarget - actor.RequestedTarget).sqrMagnitude > 0.0025f)
                {
                    actor.RequestedTarget = requestedTarget;
                    actor.Target = ResolveCollisionSafeTarget(actor, requestedTarget);
                    actor.NavigationStuckSeconds = 0f;
                    actor.NavigationRecoveryAttempts = 0;
                    actor.LastTargetDistance = float.PositiveInfinity;
                }
                Vector3 before = actor.Root.transform.position;
                Vector3 delta = actor.Target - actor.Root.transform.position;
                delta.y = 0f;
                bool moving = delta.sqrMagnitude > config.WorkReachDistance * config.WorkReachDistance;
                if (moving)
                {
                    Vector3 desiredDirection = delta.normalized;
                    if (actor.AvoidanceTimer > 0f)
                    {
                        actor.AvoidanceTimer -= Time.deltaTime;
                        desiredDirection = Quaternion.Euler(0f, 68f * actor.AvoidanceSign, 0f) *
                                           desiredDirection;
                    }
                    float distance = Mathf.Min(config.WalkSpeed * Time.deltaTime, delta.magnitude);
                    CollisionFlags collision = actor.Controller != null && actor.Controller.enabled
                        ? actor.Controller.Move(desiredDirection * distance + Vector3.down *
                            (2f * Time.deltaTime))
                        : CollisionFlags.None;
                    if ((collision & CollisionFlags.Sides) != 0)
                    {
                        actor.AvoidanceTimer = 0.65f;
                        actor.AvoidanceSign *= -1f;
                    }
                    if (desiredDirection.sqrMagnitude > 0.0001f)
                        actor.Root.transform.rotation = Quaternion.Slerp(actor.Root.transform.rotation,
                            Quaternion.LookRotation(desiredDirection, Vector3.up), Time.deltaTime * 8f);
                }
                Vector3 actualMovement = actor.Root.transform.position - before;
                actualMovement.y = 0f;
                if (actualMovement.sqrMagnitude > 0.000004f)
                {
                    actor.VisualMoving = true;
                    actor.VisualStationarySeconds = 0f;
                }
                else
                {
                    actor.VisualStationarySeconds += Time.deltaTime;
                    if (actor.VisualStationarySeconds >= 0.16f) actor.VisualMoving = false;
                }
                if (moving)
                {
                    float remainingDistance = Vector3.Distance(actor.Root.transform.position, actor.Target);
                    if (remainingDistance + 0.02f < actor.LastTargetDistance)
                        actor.NavigationStuckSeconds = 0f;
                    else actor.NavigationStuckSeconds += Time.deltaTime;
                    actor.LastTargetDistance = remainingDistance;
                }
                else
                {
                    actor.NavigationStuckSeconds = 0f;
                    actor.LastTargetDistance = 0f;
                }
                if (moving && actor.NavigationStuckSeconds >= 1.25f)
                    RecoverBlockedActor(actor);
                SetMoving(actor.Animator, actor.VisualMoving);
                if (!isServer || moving || Time.unscaledTime < actor.NextWorkTime) continue;
                PerformWork(actor);
            }
        }

        private Vector3 ResolveCollisionSafeTarget(StaffActor actor, Vector3 requested)
        {
            if (actor.Controller == null) return requested;
            Vector3 approach = requested - actor.Root.transform.position;
            approach.y = 0f;
            if (approach.sqrMagnitude < 0.01f) approach = actor.Root.transform.forward;
            approach.Normalize();
            float radius = Mathf.Max(0.1f, actor.Controller.radius);
            float height = Mathf.Max(radius * 2f, actor.Controller.height);
            for (float correction = 0f; correction <= 2.5f; correction += 0.15f)
            {
                Vector3 candidate = requested - approach * correction;
                Vector3 bottom = candidate + Vector3.up * radius;
                Vector3 top = candidate + Vector3.up * Mathf.Max(radius, height - radius);
                int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius + 0.06f,
                    targetOverlapBuffer, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                for (int i = 0; i < count; i++)
                {
                    Collider hit = targetOverlapBuffer[i];
                    if (hit == null || hit.transform == actor.Root.transform ||
                        hit.transform.IsChildOf(actor.Root.transform) ||
                        hit.bounds.max.y <= candidate.y + 0.12f) continue;
                    blocked = true;
                    break;
                }
                if (!blocked) return candidate;
            }
            // Do not silently cancel a machine assignment when every sampled point is
            // occupied by the machine's own cabinet collider. The controller can still
            // approach the configured front offset and the stuck recovery below will
            // resolve a genuinely blocked route.
            return requested;
        }

        private void RecoverBlockedActor(StaffActor actor)
        {
            actor.NavigationStuckSeconds = 0f;
            actor.LastTargetDistance = float.PositiveInfinity;
            actor.NavigationRecoveryAttempts++;
            if (actor.NavigationRecoveryAttempts < 3)
            {
                actor.AvoidanceTimer = 0.9f;
                actor.AvoidanceSign *= -1f;
                return;
            }

            // Some machines are across the street behind large decorative walls and the
            // lightweight CharacterController agent has no baked NavMesh route. After two
            // visible avoidance attempts, recover beside the assigned machine instead of
            // leaving the employee permanently wedged in scenery.
            Vector3 safeTarget = ResolveCollisionSafeTarget(actor, actor.RequestedTarget);
            if (actor.Controller != null) actor.Controller.enabled = false;
            actor.Root.transform.position = safeTarget;
            if (actor.Controller != null) actor.Controller.enabled = true;
            actor.Target = safeTarget;
            actor.NavigationRecoveryAttempts = 0;
            Debug.LogWarning("[StaffNavigation] Recovered blocked " + actor.Role +
                             " beside assigned workstation.", actor.Root);
        }

        private void PerformWork(StaffActor actor)
        {
            ShopNightSalesSystem sales = ShopNightSalesSystem.Instance;
            int assignment = AssignmentFor(actor.Role);
            if (assignment != 0 && ShopStaffMachineAssignment.TryResolve(assignment,
                    out Component assignedMachine, out string assignedLabel))
            {
                bool attempted = false;
                bool acquired = false;
                if (assignedMachine is ShopClawMachineNetwork claw)
                {
                    ShopClawAutomationDevice device = claw.GetComponent<ShopClawAutomationDevice>();
                    attempted = device != null && device.ServerTryStaffAttempt(
                        config.StaffMachineCostMultiplier, out acquired);
                }
                else if (assignedMachine is ShopKujiStationNetwork kuji)
                {
                    attempted = kuji.ServerTryStaffDraw(config.StaffMachineCostMultiplier,
                        out _, out int stored);
                    acquired = stored > 0;
                }
                if (attempted)
                    ShopNetworkGame.Instance.ServerSetEvent(RoleName(actor.Role) + " 알바: " + assignedLabel +
                        " 자동 플레이" + (acquired ? " 성공" : " 실패"));
                actor.NextWorkTime = Time.unscaledTime + config.MachineWorkInterval;
                return;
            }
            switch (actor.Role)
            {
                case ShopStaffRole.Cashier:
                    sales.ServerTryStaffCheckout(config.CashierDurationMultiplier);
                    actor.NextWorkTime = Time.unscaledTime + 0.5f;
                    break;
                case ShopStaffRole.Stocker:
                    sales.ServerTryStaffRestockDisplay();
                    actor.NextWorkTime = Time.unscaledTime + config.StockerWorkInterval;
                    break;
                case ShopStaffRole.Collector:
                    int moved = 0;
                    ShopClawAutomationDevice[] devices =
                        FindObjectsByType<ShopClawAutomationDevice>(FindObjectsSortMode.None);
                    for (int i = 0; i < devices.Length; i++)
                        if (devices[i] != null) moved += devices[i].ServerStaffCollectToStorage();
                    if (moved > 0) ShopNetworkGame.Instance.ServerSetEvent("수거 알바가 자동 수집함에서 " + moved + "개를 창고로 옮겼습니다.");
                    actor.NextWorkTime = Time.unscaledTime + config.CollectorWorkInterval;
                    break;
            }
        }

        private Vector3 TargetFor(ShopStaffRole role)
        {
            ShopNightSalesSystem sales = ShopNightSalesSystem.Instance;
            if (sales == null) return Vector3.zero;
            int assignment = AssignmentFor(role);
            if (assignment != 0 && ShopStaffMachineAssignment.TryResolve(assignment,
                    out Component machine, out _))
                return machine.transform.position + machine.transform.forward * 1.15f;
            return role switch
            {
                ShopStaffRole.Cashier => sales.CheckoutPosition,
                ShopStaffRole.Stocker => sales.DisplayWorkPosition,
                ShopStaffRole.Collector => FindCollectorTarget(sales.EntrancePosition),
                _ => sales.EntrancePosition
            };
        }

        private static int AssignmentFor(ShopStaffRole role)
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null) return 0;
            return role == ShopStaffRole.Stocker ? game.StaffAssignmentSlot2.Value :
                role == ShopStaffRole.Collector ? game.StaffAssignmentSlot3.Value : 0;
        }

        private static Vector3 FindCollectorTarget(Vector3 fallback)
        {
            ShopClawAutomationDevice[] devices =
                FindObjectsByType<ShopClawAutomationDevice>(FindObjectsSortMode.None);
            for (int i = 0; i < devices.Length; i++)
                if (devices[i] != null && devices[i].Installed.Value && devices[i].BufferedItemCount > 0)
                    return devices[i].transform.position + devices[i].transform.forward * 1.1f;
            return fallback + Vector3.right * 1.5f;
        }

        private static void SetMoving(Animator animator, bool moving)
        {
            if (animator == null) return;
            for (int i = 0; i < animator.parameterCount; i++)
                if (animator.parameters[i].nameHash == MovingParameter &&
                    animator.parameters[i].type == AnimatorControllerParameterType.Bool)
                {
                    animator.SetBool(MovingParameter, moving);
                    return;
                }
        }

        private StaffActor Find(ShopStaffRole role) => actors.Find(actor => actor.Role == role);
        private void RemoveActor(StaffActor actor)
        {
            actors.Remove(actor);
            if (actor.Root != null) Destroy(actor.Root);
        }
        private void ClearActors()
        {
            for (int i = actors.Count - 1; i >= 0; i--) RemoveActor(actors[i]);
        }
        private static string RoleName(ShopStaffRole role) => role switch
        {
            ShopStaffRole.Cashier => "계산",
            ShopStaffRole.Stocker => "진열",
            _ => "수거"
        };
    }
}
