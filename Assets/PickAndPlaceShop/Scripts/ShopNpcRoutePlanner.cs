using UnityEngine;
using UnityEngine.AI;

namespace PickAndPlaceShop
{
    public enum ShopNpcRouteStatus
    {
        Direct,
        NavMeshComplete,
        PhysicsDetour,
        Partial,
        Unreachable
    }

    /// <summary>
    /// Shared, allocation-free route probe for the lightweight CharacterController NPCs.
    /// It uses a baked NavMesh when one exists and falls back to collider-corner detours
    /// in generated scenes that intentionally ship without NavMesh data.
    /// </summary>
    public static class ShopNpcRoutePlanner
    {
        private static readonly RaycastHit[] CastHits = new RaycastHit[32];
        private static readonly Vector3[] Candidates = new Vector3[8];
        private static float nextNavMeshProbeTime;
        private static bool hasNavMeshData;

        public static bool TryGetNextWaypoint(Vector3 origin, Vector3 destination, float radius,
            float height, int attempt, Transform ignoreRoot, out Vector3 waypoint,
            out ShopNpcRouteStatus status)
        {
            origin.y = destination.y;
            radius = Mathf.Max(0.1f, radius);
            height = Mathf.Max(radius * 2f, height);

            if (Time.unscaledTime >= nextNavMeshProbeTime)
            {
                NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
                hasNavMeshData = triangulation.vertices != null && triangulation.vertices.Length > 0;
                nextNavMeshProbeTime = Time.unscaledTime + 2f;
            }
            if (hasNavMeshData &&
                NavMesh.SamplePosition(origin, out NavMeshHit start, 2f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(destination, out NavMeshHit end, 2f, NavMesh.AllAreas))
            {
                NavMeshPath path = new();
                bool calculated = NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path);
                if (calculated && path.status == NavMeshPathStatus.PathComplete && path.corners.Length > 1)
                {
                    waypoint = path.corners[1];
                    waypoint.y = destination.y;
                    status = ShopNpcRouteStatus.NavMeshComplete;
                    return true;
                }
                status = calculated ? ShopNpcRouteStatus.Partial : ShopNpcRouteStatus.Unreachable;
            }

            if (!TryFindBlockingCollider(origin, destination, radius, height, ignoreRoot,
                    out Collider blocker))
            {
                waypoint = destination;
                status = ShopNpcRouteStatus.Direct;
                return true;
            }

            Bounds bounds = ResolveObstacleBounds(blocker);
            float margin = radius + 0.42f;
            float left = bounds.min.x - margin;
            float right = bounds.max.x + margin;
            float back = bounds.min.z - margin;
            float front = bounds.max.z + margin;
            float y = destination.y;
            Candidates[0] = new Vector3(left, y, back);
            Candidates[1] = new Vector3(left, y, front);
            Candidates[2] = new Vector3(right, y, back);
            Candidates[3] = new Vector3(right, y, front);
            Candidates[4] = new Vector3(left, y, bounds.center.z);
            Candidates[5] = new Vector3(right, y, bounds.center.z);
            Candidates[6] = new Vector3(bounds.center.x, y, back);
            Candidates[7] = new Vector3(bounds.center.x, y, front);

            float bestScore = float.MaxValue;
            waypoint = destination;
            int offset = Mathf.Abs(attempt) % Candidates.Length;
            for (int index = 0; index < Candidates.Length; index++)
            {
                Vector3 candidate = Candidates[(index + offset) % Candidates.Length];
                if ((candidate - origin).sqrMagnitude <= 0.4f * 0.4f) continue;
                if (TryFindBlockingCollider(origin, candidate, radius, height, ignoreRoot, out _)) continue;
                float score = Vector3.Distance(origin, candidate) + Vector3.Distance(candidate, destination);
                // Alternate equally good left/right routes after a failed attempt.
                score += index * 0.002f;
                if (score >= bestScore) continue;
                bestScore = score;
                waypoint = candidate;
            }

            status = bestScore < float.MaxValue
                ? ShopNpcRouteStatus.PhysicsDetour
                : ShopNpcRouteStatus.Unreachable;
            return bestScore < float.MaxValue;
        }

        private static Bounds ResolveObstacleBounds(Collider blocker)
        {
            Bounds bounds = blocker.bounds;
            Transform group = blocker.transform;
            for (Transform current = blocker.transform; current != null; current = current.parent)
            {
                if (current.name == "Zone_Warehouse" || current.name == "Shared Display Shelves" ||
                    current.name == "TrashInteractionRoot")
                {
                    group = current;
                    break;
                }
            }
            if (group == blocker.transform) return bounds;
            Collider[] colliders = group.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                Collider item = colliders[index];
                if (item == null || !item.enabled || item.isTrigger) continue;
                bounds.Encapsulate(item.bounds);
            }
            return bounds;
        }

        private static bool TryFindBlockingCollider(Vector3 origin, Vector3 destination, float radius,
            float height, Transform ignoreRoot, out Collider blocker)
        {
            blocker = null;
            Vector3 direction = destination - origin;
            direction.y = 0f;
            float distance = direction.magnitude;
            if (distance <= 0.05f) return false;
            direction /= distance;
            Vector3 bottom = origin + Vector3.up * radius;
            Vector3 top = origin + Vector3.up * Mathf.Max(radius, height - radius);
            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, CastHits,
                distance, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int index = 0; index < count; index++)
            {
                Collider hit = CastHits[index].collider;
                if (hit == null || !hit.enabled || hit.isTrigger ||
                    ignoreRoot != null && (hit.transform == ignoreRoot || hit.transform.IsChildOf(ignoreRoot)) ||
                    hit.GetComponentInParent<ShopCustomerNetwork>() != null ||
                    hit.bounds.max.y <= origin.y + 0.12f) continue;
                if (CastHits[index].distance >= nearest) continue;
                nearest = CastHits[index].distance;
                blocker = hit;
            }
            return blocker != null;
        }
    }
}
