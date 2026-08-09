using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace PickAndPlaceShop
{
    /// <summary>Adds build-safe collision to imported city shells and parked cars.</summary>
    public static class ShopCityCollisionBootstrap
    {
        private const string CollisionChildName = "[Runtime] City Collision";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            Apply(SceneManager.GetActiveScene());
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode _) => Apply(scene);

        private static void Apply(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            ShopWorldConfig config = ShopWorldConfig.Load();
            float buildingScale = config != null ? config.BuildingColliderScale : 0.96f;
            float vehicleScale = config != null ? config.VehicleColliderScale : 0.92f;
            int buildings = 0;
            int vehicles = 0;
            int storeFixtures = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter == null || filter.sharedMesh == null) continue;
                bool building = filter.transform.parent != null &&
                                filter.transform.parent.name == "CITY_Buildings" &&
                                filter.name.EndsWith("_body", System.StringComparison.OrdinalIgnoreCase);
                bool vehicle = filter.name.StartsWith("P_Car_", System.StringComparison.OrdinalIgnoreCase) ||
                               (filter.transform.parent != null && filter.transform.parent.name.StartsWith(
                                   "P_Car_", System.StringComparison.OrdinalIgnoreCase));
                bool storeFixture = IsStoreFixture(filter.transform);
                if (!building && !vehicle && !storeFixture) continue;

                GameObject target = vehicle && filter.transform.parent != null &&
                                    filter.transform.parent.name.StartsWith("P_Car_",
                                        System.StringComparison.OrdinalIgnoreCase)
                    ? filter.transform.parent.gameObject
                    : filter.gameObject;
                if (target.GetComponent<Collider>() != null || target.transform.Find(CollisionChildName) != null)
                    continue;
                if (!TryGetLocalBounds(target, out Bounds bounds)) continue;

                GameObject collisionHost = new(CollisionChildName);
                collisionHost.transform.SetParent(target.transform, false);
                collisionHost.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                collisionHost.transform.localScale = Vector3.one;
                BoxCollider collider = collisionHost.AddComponent<BoxCollider>();
                collider.center = bounds.center;
                collider.size = Vector3.Scale(bounds.size,
                    Vector3.one * (building ? buildingScale : storeFixture ? 0.98f : vehicleScale));

                NavMeshObstacle obstacle = collisionHost.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = collider.center;
                obstacle.size = collider.size;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
                if (building) buildings++;
                else if (storeFixture) storeFixtures++;
                else vehicles++;
            }

            if (buildings > 0 || vehicles > 0 || storeFixtures > 0)
                Debug.Log("[CityCollision] buildings=" + buildings + " vehicles=" + vehicles +
                          " storeFixtures=" + storeFixtures);
        }

        private static bool IsStoreFixture(Transform target)
        {
            if (target == null) return false;
            if (target.name == "Counter") return true;
            bool shelfPart = target.name.StartsWith("Shelf_", System.StringComparison.Ordinal) ||
                             target.name.StartsWith("ShelfBack_", System.StringComparison.Ordinal);
            bool warehousePart = target.name.StartsWith("Rack", System.StringComparison.Ordinal) ||
                                 target.name.StartsWith("Box", System.StringComparison.Ordinal) ||
                                 target.name.StartsWith("Pallet", System.StringComparison.Ordinal) ||
                                 target.name.StartsWith("PalBox", System.StringComparison.Ordinal) ||
                                 target.name.StartsWith("WH_Mark", System.StringComparison.Ordinal);
            for (Transform current = target.parent; current != null; current = current.parent)
            {
                if (shelfPart && current.name == "Shared Display Shelves") return true;
                if (warehousePart && current.name == "Zone_Warehouse") return true;
            }
            return false;
        }

        private static bool TryGetLocalBounds(GameObject target, out Bounds bounds)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }
            bounds = new Bounds(target.transform.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds world = renderers[i].bounds;
                Vector3 min = world.min;
                Vector3 max = world.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    bounds.Encapsulate(target.transform.InverseTransformPoint(point));
                }
            }
            return bounds.size.sqrMagnitude > 0.0001f;
        }
    }
}
