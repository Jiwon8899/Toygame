using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(380)]
    public sealed class ShopStreetLifeController : MonoBehaviour
    {
        private sealed class Pedestrian
        {
            public GameObject Root;
            public Animator Animator;
            public int Waypoint;
        }

        private static readonly int MovingParameter = Animator.StringToHash("Moving");
        private static ShopStreetLifeController instance;
        private readonly List<GameObject> decor = new();
        private readonly List<Pedestrian> pedestrians = new();
        private readonly List<Light> streetLights = new();
        private Vector3[] waypoints;
        private ShopWorldConfig world;
        private ShopWorkforceConfig appearances;
        private float nextSetup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[World] Street Life");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopStreetLifeController>();
        }

        private void Awake()
        {
            world = ShopWorldConfig.Load();
            appearances = ShopWorkforceConfig.Load();
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Clear();
            if (instance == this) instance = null;
        }

        private void HandleSceneLoaded(Scene _, LoadSceneMode __)
        {
            Clear();
            nextSetup = 0f;
        }

        private void Update()
        {
            if (ShopNightSalesSystem.Instance == null)
            {
                if (decor.Count > 0 || pedestrians.Count > 0) Clear();
                return;
            }
            if (waypoints == null && Time.unscaledTime >= nextSetup)
            {
                nextSetup = Time.unscaledTime + 1f;
                SetupStreet();
            }
            UpdatePedestrians();
            bool night = ShopNetworkGame.Instance != null &&
                         (ShopNetworkGame.Instance.Phase.Value == ShopPhase.Open ||
                          ShopNetworkGame.Instance.Phase.Value == ShopPhase.Summary);
            for (int i = 0; i < streetLights.Count; i++) if (streetLights[i] != null) streetLights[i].enabled = night;
        }

        private void SetupStreet()
        {
            if (world == null || appearances == null) return;
            Vector3 origin = ShopNightSalesSystem.Instance.RoadsidePosition;
            waypoints = new[]
            {
                origin + new Vector3(-12f, 0f, -1.5f), origin + new Vector3(12f, 0f, -1.5f),
                origin + new Vector3(12f, 0f, 1.5f), origin + new Vector3(-12f, 0f, 1.5f)
            };
            for (int i = 0; i < 4; i++)
            {
                float x = -10f + i * 6.5f;
                CreatePlanter(origin + new Vector3(x, 0f, 3f));
                if (i < 3) CreateLamp(origin + new Vector3(x + 2.7f, 0f, -3f));
            }
            CreateBench(origin + new Vector3(-6f, 0f, 3.2f));
            CreateBench(origin + new Vector3(7f, 0f, 3.2f));
            CreateStreetSign(origin + new Vector3(0f, 0f, 3.4f));

            GameObject[] pool = appearances.AppearancePrefabs;
            int count = Mathf.Min(world.MaximumPedestrians, pool?.Length ?? 0);
            for (int i = 0; i < count; i++) CreatePedestrian(pool[i % pool.Length], i);
        }

        private void CreatePedestrian(GameObject prefab, int index)
        {
            if (prefab == null || waypoints == null) return;
            GameObject root = new("StreetPedestrian_" + (index + 1));
            root.transform.SetParent(transform, false);
            root.transform.position = waypoints[index % waypoints.Length] + Vector3.right * (index * 0.55f);
            root.AddComponent<ShopWorldSafetyAgent>();
            GameObject visual = Instantiate(prefab, root.transform);
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.applyRootMotion = false;
            pedestrians.Add(new Pedestrian { Root = root, Animator = animator, Waypoint = (index + 1) % waypoints.Length });
        }

        private void UpdatePedestrians()
        {
            if (waypoints == null || world == null) return;
            for (int i = 0; i < pedestrians.Count; i++)
            {
                Pedestrian pedestrian = pedestrians[i];
                if (pedestrian.Root == null) continue;
                Vector3 delta = waypoints[pedestrian.Waypoint] - pedestrian.Root.transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude < 0.2f)
                {
                    pedestrian.Waypoint = (pedestrian.Waypoint + 1) % waypoints.Length;
                    continue;
                }
                pedestrian.Root.transform.position += delta.normalized *
                    Mathf.Min(delta.magnitude, world.PedestrianWalkSpeed * Time.deltaTime);
                pedestrian.Root.transform.rotation = Quaternion.Slerp(pedestrian.Root.transform.rotation,
                    Quaternion.LookRotation(delta.normalized), Time.deltaTime * 7f);
                SetMoving(pedestrian.Animator, true);
            }
        }

        private void CreatePlanter(Vector3 position)
        {
            GameObject root = NewDecor("StreetPlanter", position);
            CreatePrimitive(root.transform, PrimitiveType.Cube, "Pot", new Vector3(0f, 0.35f, 0f),
                new Vector3(1.15f, 0.7f, 1.15f), new Color(0.45f, 0.2f, 0.1f), true);
            CreatePrimitive(root.transform, PrimitiveType.Sphere, "Shrub", new Vector3(0f, 1f, 0f),
                Vector3.one * 1.25f, new Color(0.12f, 0.48f, 0.23f), false);
        }

        private void CreateBench(Vector3 position)
        {
            GameObject root = NewDecor("StreetBench", position);
            Color wood = new(0.52f, 0.28f, 0.12f);
            CreatePrimitive(root.transform, PrimitiveType.Cube, "Seat", new Vector3(0f, 0.55f, 0f), new Vector3(2.2f, 0.18f, 0.65f), wood, true);
            CreatePrimitive(root.transform, PrimitiveType.Cube, "Back", new Vector3(0f, 1.05f, 0.28f), new Vector3(2.2f, 0.75f, 0.14f), wood, true);
        }

        private void CreateLamp(Vector3 position)
        {
            GameObject root = NewDecor("StreetLamp", position);
            CreatePrimitive(root.transform, PrimitiveType.Cylinder, "Pole", new Vector3(0f, 1.8f, 0f), new Vector3(0.14f, 1.8f, 0.14f), Color.black, true);
            GameObject bulb = CreatePrimitive(root.transform, PrimitiveType.Sphere, "Bulb", new Vector3(0f, 3.55f, 0f), Vector3.one * 0.42f, new Color(1f, 0.72f, 0.3f), false);
            Light light = bulb.AddComponent<Light>();
            light.type = LightType.Point; light.range = 7f; light.intensity = 2.2f;
            light.color = new Color(1f, 0.72f, 0.38f);
            streetLights.Add(light);
        }

        private void CreateStreetSign(Vector3 position)
        {
            GameObject root = NewDecor("CollectorStreetSign", position);
            CreatePrimitive(root.transform, PrimitiveType.Cylinder, "Post", new Vector3(0f, 1.1f, 0f), new Vector3(0.12f, 1.1f, 0.12f), Color.black, true);
            CreatePrimitive(root.transform, PrimitiveType.Cube, "Sign", new Vector3(0f, 2.15f, 0f), new Vector3(2.5f, 0.7f, 0.18f), new Color(0.18f, 0.55f, 0.72f), true);
        }

        private GameObject NewDecor(string name, Vector3 position)
        {
            GameObject root = new(name);
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            root.isStatic = true;
            decor.Add(root);
            return root;
        }

        private static GameObject CreatePrimitive(Transform parent, PrimitiveType type, string name,
            Vector3 localPosition, Vector3 scale, Color color, bool keepCollider)
        {
            GameObject item = GameObject.CreatePrimitive(type);
            item.name = name; item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition; item.transform.localScale = scale;
            item.GetComponent<Renderer>().material.color = color;
            if (!keepCollider) { Collider collider = item.GetComponent<Collider>(); if (collider != null) Destroy(collider); }
            item.isStatic = true;
            return item;
        }

        private static void SetMoving(Animator animator, bool moving)
        {
            if (animator == null) return;
            for (int i = 0; i < animator.parameterCount; i++)
                if (animator.parameters[i].nameHash == MovingParameter && animator.parameters[i].type == AnimatorControllerParameterType.Bool)
                { animator.SetBool(MovingParameter, moving); return; }
        }

        private void Clear()
        {
            waypoints = null;
            for (int i = 0; i < pedestrians.Count; i++) if (pedestrians[i].Root != null) Destroy(pedestrians[i].Root);
            for (int i = 0; i < decor.Count; i++) if (decor[i] != null) Destroy(decor[i]);
            pedestrians.Clear(); decor.Clear(); streetLights.Clear();
        }
    }
}
