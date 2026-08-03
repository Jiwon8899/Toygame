using System.Collections.Generic;
using UnityEngine;

namespace PickAndPlaceShop
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ShopClawScoopRig : MonoBehaviour
    {
        [SerializeField] private Rigidbody body;
        [SerializeField] private BoxCollider bottomCollider;
        [SerializeField] private BoxCollider[] rimColliders;
        [SerializeField] private Transform visualRoot;

        private readonly List<Collider> ownColliders = new();
        private readonly RaycastHit[] floorHits = new RaycastHit[32];

        public Rigidbody Body => body;
        public BoxCollider BottomCollider => bottomCollider;
        public IReadOnlyList<BoxCollider> RimColliders => rimColliders;
        public Transform VisualRoot => visualRoot;
        public int CompoundColliderCount => (bottomCollider != null ? 1 : 0) +
                                            (rimColliders != null ? rimColliders.Length : 0);
        public float BottomWorldY => bottomCollider != null
            ? bottomCollider.bounds.min.y
            : transform.position.y;

#if UNITY_EDITOR
        public void EditorConfigure(Rigidbody scoopBody, BoxCollider bottom,
            BoxCollider[] rims, Transform visuals)
        {
            body = scoopBody;
            bottomCollider = bottom;
            rimColliders = rims;
            visualRoot = visuals;
            CacheColliders();
        }
#endif

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            ConfigureBody();
            CacheColliders();
        }

        public void ConfigureBody()
        {
            if (body == null) return;
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.None;
        }

        public bool SweepMove(Vector3 targetPosition, Quaternion targetRotation,
            float skin, bool stopOnPrize, out RaycastHit blockingHit, out bool touchedPrize)
        {
            blockingHit = default;
            touchedPrize = false;
            if (body == null) return false;

            Vector3 delta = targetPosition - body.position;
            float distance = delta.magnitude;
            float allowedDistance = distance;
            if (distance > 0.00001f)
            {
                Vector3 direction = delta / distance;
                RaycastHit[] hits = body.SweepTestAll(direction, distance + skin,
                    QueryTriggerInteraction.Ignore);
                foreach (RaycastHit hit in hits)
                {
                    if (hit.collider == null || IsOwnCollider(hit.collider)) continue;
                    // SweepTest can report distance-zero contacts that the scoop is moving away
                    // from (notably the overhead frame at its parked pose). Only a surface whose
                    // normal opposes travel is allowed to clamp the requested movement.
                    if (hit.distance <= skin * 2f)
                    {
                        Vector3 awayFromSurface = body.worldCenterOfMass - hit.collider.bounds.center;
                        if (awayFromSurface.sqrMagnitude > 0.000001f &&
                            Vector3.Dot(awayFromSurface.normalized, direction) > 0.1f) continue;
                    }
                    if (Vector3.Dot(hit.normal, direction) > -0.05f) continue;
                    ShopClawPrizeNetwork prize = hit.collider.GetComponentInParent<ShopClawPrizeNetwork>();
                    if (prize != null)
                    {
                        touchedPrize = true;
                        if (!stopOnPrize) continue;
                    }
                    float candidate = Mathf.Max(0f, hit.distance - skin);
                    if (candidate >= allowedDistance) continue;
                    allowedDistance = candidate;
                    blockingHit = hit;
                }

                body.MovePosition(body.position + direction * allowedDistance);
            }
            body.MoveRotation(targetRotation);
            return blockingHit.collider != null;
        }

        public bool TryGetFloorSurface(out float floorY)
        {
            floorY = float.NegativeInfinity;
            Vector3 origin = new(transform.position.x, BottomWorldY + 1.25f, transform.position.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, floorHits, 8f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = floorHits[index];
                if (hit.collider == null || IsOwnCollider(hit.collider) ||
                    hit.collider.GetComponentInParent<ShopClawPrizeNetwork>() != null) continue;
                if (hit.point.y > BottomWorldY + 0.5f || hit.point.y <= floorY) continue;
                floorY = hit.point.y;
            }
            return !float.IsNegativeInfinity(floorY);
        }

        public bool ContainsPrize(ShopClawPrizeNetwork prize, float diameter, float rimHeight)
        {
            if (prize == null || prize.Awarded.Value) return false;
            Collider[] colliders = prize.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0) return false;
            Bounds bounds = colliders[0].bounds;
            for (int index = 1; index < colliders.Length; index++) bounds.Encapsulate(colliders[index].bounds);
            Vector3 local = transform.InverseTransformPoint(bounds.center);
            float radius = Mathf.Max(0.1f, diameter * 0.5f - 0.06f);
            return new Vector2(local.x, local.z).sqrMagnitude <= radius * radius &&
                   bounds.min.y >= BottomWorldY - 0.16f &&
                   bounds.center.y <= BottomWorldY + rimHeight + bounds.extents.y + 0.08f;
        }

        public void SetEntryLipsOpen(bool open, float closedHeight, float openHeight)
        {
            SetEntryLipHeight(open ? Mathf.Min(openHeight, closedHeight) : closedHeight,
                closedHeight, openHeight);
        }

        public void SetEntryLipHeight(float entryHeight, float closedHeight, float openHeight)
        {
            if (rimColliders == null || rimColliders.Length < 5) return;
            for (int index = 0; index < rimColliders.Length; index++)
                SetLipHeight(rimColliders[index], closedHeight);
            SetLipHeight(rimColliders[0], Mathf.Clamp(entryHeight, openHeight, closedHeight));
            SetLipHeight(rimColliders[4], Mathf.Clamp(entryHeight, openHeight, closedHeight));
        }

        public void SetPourOpening(Vector3 localDirection, float closedHeight, float openHeight)
        {
            if (rimColliders == null || rimColliders.Length == 0) return;
            Vector3 horizontal = Vector3.ProjectOnPlane(localDirection, Vector3.up).normalized;
            for (int index = 0; index < rimColliders.Length; index++)
            {
                BoxCollider rim = rimColliders[index];
                if (rim == null) continue;
                Vector3 rimDirection = Vector3.ProjectOnPlane(
                    rim.transform.localPosition, Vector3.up).normalized;
                float height = Vector3.Dot(rimDirection, horizontal) >= 0.45f
                    ? Mathf.Min(openHeight, closedHeight)
                    : closedHeight;
                SetLipHeight(rim, height);
            }
        }

        private static void SetLipHeight(BoxCollider collider, float height)
        {
            if (collider == null) return;
            float bottom = collider.transform.localPosition.y + collider.center.y - collider.size.y * 0.5f;
            Vector3 size = collider.size;
            size.y = height;
            collider.size = size;
            Vector3 center = collider.center;
            center.y = bottom - collider.transform.localPosition.y + height * 0.5f;
            collider.center = center;
            Transform visual = collider.transform.Find("RimVisual");
            if (visual == null) return;
            Vector3 visualScale = visual.localScale;
            visualScale.y = height;
            visual.localScale = visualScale;
            visual.localPosition = collider.center;
        }

        private void CacheColliders()
        {
            ownColliders.Clear();
            ownColliders.AddRange(GetComponentsInChildren<Collider>(true));
        }

        private bool IsOwnCollider(Collider candidate)
        {
            for (int index = 0; index < ownColliders.Count; index++)
                if (ownColliders[index] == candidate) return true;
            return false;
        }
    }
}
