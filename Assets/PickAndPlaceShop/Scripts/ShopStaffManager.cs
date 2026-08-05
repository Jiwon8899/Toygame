using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PickAndPlaceShop
{
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
            public float AvoidanceTimer;
            public float AvoidanceSign = 1f;
        }

        private static readonly int MovingParameter = Animator.StringToHash("Moving");
        private static ShopStaffManager instance;
        private readonly List<StaffActor> actors = new();
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

            StaffActor actor = new()
            {
                Role = role,
                Root = root,
                Animator = animator,
                Controller = controller,
                Target = TargetFor(role),
                NextWorkTime = Time.unscaledTime + 1f
            };
            actors.Add(actor);
        }

        private void UpdateActors()
        {
            bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            for (int i = 0; i < actors.Count; i++)
            {
                StaffActor actor = actors[i];
                if (actor.Root == null) continue;
                actor.Target = TargetFor(actor.Role);
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
                SetMoving(actor.Animator, moving);
                if (!isServer || moving || Time.unscaledTime < actor.NextWorkTime) continue;
                PerformWork(actor);
            }
        }

        private void PerformWork(StaffActor actor)
        {
            ShopNightSalesSystem sales = ShopNightSalesSystem.Instance;
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
            return role switch
            {
                ShopStaffRole.Cashier => sales.CheckoutPosition,
                ShopStaffRole.Stocker => sales.DisplayWorkPosition,
                ShopStaffRole.Collector => FindCollectorTarget(sales.EntrancePosition),
                _ => sales.EntrancePosition
            };
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
