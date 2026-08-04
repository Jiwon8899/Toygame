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
        [SerializeField] private Transform curlPivot;
        [SerializeField] private CapsuleCollider handleCollider;

        private readonly List<Collider> ownColliders = new();
        private readonly List<BoxCollider> ownBoxColliders = new();
        private readonly HashSet<int> contactedPrizeIds = new();
        private readonly RaycastHit[] floorHits = new RaycastHit[32];
        private readonly Collider[] poseOverlaps = new Collider[64];
        private PhysicsMaterial innerBottomMaterial;

        public Rigidbody Body => body;
        public BoxCollider BottomCollider => bottomCollider;
        public IReadOnlyList<BoxCollider> RimColliders => rimColliders;
        public Transform VisualRoot => visualRoot;
        public Transform CurlPivot => curlPivot;
        public Vector3 CurlPivotLocalPosition => curlPivot != null
            ? transform.InverseTransformPoint(curlPivot.position)
            : Vector3.up * 0.55f;
        public CapsuleCollider HandleCollider => handleCollider;
        public int CompoundColliderCount => (bottomCollider != null ? 1 : 0) +
                                            (rimColliders != null ? rimColliders.Length : 0);
        public float BottomWorldY => bottomCollider != null
            ? bottomCollider.bounds.min.y
            : transform.position.y;
        public string LastPoseBlockerName { get; private set; } = string.Empty;

#if UNITY_EDITOR
        public void EditorConfigure(Rigidbody scoopBody, BoxCollider bottom,
            BoxCollider[] rims, Transform visuals, Transform authoredCurlPivot = null,
            CapsuleCollider authoredHandleCollider = null)
        {
            body = scoopBody;
            bottomCollider = bottom;
            rimColliders = rims;
            visualRoot = visuals;
            curlPivot = authoredCurlPivot;
            handleCollider = authoredHandleCollider;
            CacheColliders();
        }
#endif

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (bottomCollider != null) innerBottomMaterial = bottomCollider.material;
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
            float skin, bool stopOnPrize, out RaycastHit blockingHit, out bool touchedPrize,
            float maximumRotationStep = 3f)
        {
            blockingHit = default;
            touchedPrize = false;
            LastPoseBlockerName = string.Empty;
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
                        contactedPrizeIds.Add(prize.GetInstanceID());
                        if (!stopOnPrize) continue;
                    }
                    float candidate = Mathf.Max(0f, hit.distance - skin);
                    if (candidate >= allowedDistance) continue;
                    allowedDistance = candidate;
                    blockingHit = hit;
                    LastPoseBlockerName = hit.collider.name;
                }
            }
            Vector3 startPosition = body.position;
            Quaternion startRotation = body.rotation;
            Vector3 translatedPosition = distance > 0.00001f
                ? body.position + delta.normalized * allowedDistance
                : body.position;
            float rotationAngle = Quaternion.Angle(startRotation, targetRotation);
            int rotationSteps = Mathf.Max(1, Mathf.CeilToInt(rotationAngle /
                Mathf.Max(0.25f, maximumRotationStep)));
            Vector3 safePosition = startPosition;
            Quaternion safeRotation = startRotation;
            for (int step = 1; step <= rotationSteps; step++)
            {
                float progress = step / (float)rotationSteps;
                Vector3 candidatePosition = Vector3.Lerp(startPosition, translatedPosition, progress);
                Quaternion candidateRotation = Quaternion.Slerp(startRotation, targetRotation, progress);
                if (IsPoseBlocked(candidatePosition, candidateRotation, skin, stopOnPrize,
                        ref touchedPrize)) break;
                safePosition = candidatePosition;
                safeRotation = candidateRotation;
            }
            body.MovePosition(safePosition);
            body.MoveRotation(safeRotation);
            return blockingHit.collider != null;
        }

        public void ConfigureOuterSurface(PhysicsMaterial material)
        {
            if (material == null || rimColliders == null) return;
            for (int index = 0; index < rimColliders.Length; index++)
            {
                BoxCollider source = rimColliders[index];
                if (source == null || source.transform.Find("OuterGlideCollider") != null) continue;
                GameObject shell = new("OuterGlideCollider", typeof(BoxCollider));
                shell.transform.SetParent(source.transform, false);
                BoxCollider glide = shell.GetComponent<BoxCollider>();
                Vector3 outwardWorld = source.bounds.center - body.worldCenterOfMass;
                outwardWorld = Vector3.ProjectOnPlane(outwardWorld, transform.up).normalized;
                Vector3 outwardLocal = source.transform.InverseTransformDirection(outwardWorld);
                Vector3 size = source.size;
                Vector3 center = source.center;
                const float thickness = 0.025f;
                if (Mathf.Abs(outwardLocal.x) > Mathf.Abs(outwardLocal.z))
                {
                    float sign = Mathf.Sign(outwardLocal.x);
                    size.x = thickness;
                    center.x += sign * (source.size.x * 0.5f + thickness * 0.5f);
                }
                else
                {
                    float sign = Mathf.Sign(outwardLocal.z);
                    size.z = thickness;
                    center.z += sign * (source.size.z * 0.5f + thickness * 0.5f);
                }
                glide.size = size;
                glide.center = center;
                glide.material = material;
            }
            CacheColliders();
        }

        public void SetPourSurface(bool pouring, PhysicsMaterial glideMaterial)
        {
            if (bottomCollider == null) return;
            if (innerBottomMaterial == null && bottomCollider.material != glideMaterial)
                innerBottomMaterial = bottomCollider.material;
            PhysicsMaterial target = pouring && glideMaterial != null
                ? glideMaterial
                : innerBottomMaterial;
            if (target != null && bottomCollider.material != target)
                bottomCollider.material = target;
        }

        public void SetPhysicalCollisionsEnabled(bool enabled)
        {
            foreach (Collider ownCollider in ownColliders)
                if (ownCollider != null && ownCollider.enabled != enabled)
                    ownCollider.enabled = enabled;
        }

        public void ClearPrizeContactHistory() => contactedPrizeIds.Clear();

        public bool HasContactedPrize(ShopClawPrizeNetwork prize) =>
            prize != null && contactedPrizeIds.Contains(prize.GetInstanceID());

        private bool IsPoseBlocked(Vector3 bodyPosition, Quaternion bodyRotation, float skin,
            bool stopOnPrize, ref bool touchedPrize)
        {
            for (int boxIndex = 0; boxIndex < ownBoxColliders.Count; boxIndex++)
            {
                BoxCollider box = ownBoxColliders[boxIndex];
                if (box == null || !box.enabled) continue;
                Vector3 relativeTransformPosition = transform.InverseTransformPoint(box.transform.position);
                Quaternion relativeTransformRotation = Quaternion.Inverse(transform.rotation) *
                                                       box.transform.rotation;
                Vector3 predictedTransformPosition = bodyPosition +
                                                     bodyRotation * relativeTransformPosition;
                Quaternion predictedTransformRotation = bodyRotation * relativeTransformRotation;
                Vector3 predictedCenter = predictedTransformPosition +
                                          predictedTransformRotation *
                                          Vector3.Scale(box.center, box.transform.lossyScale);
                Vector3 scale = box.transform.lossyScale;
                Vector3 halfExtents = Vector3.Scale(box.size * 0.5f,
                    new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                halfExtents = Vector3.Max(halfExtents - Vector3.one * skin,
                    Vector3.one * 0.002f);
                int count = Physics.OverlapBoxNonAlloc(predictedCenter, halfExtents, poseOverlaps,
                    predictedTransformRotation, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore);
                for (int hitIndex = 0; hitIndex < count; hitIndex++)
                {
                    Collider hit = poseOverlaps[hitIndex];
                    if (hit == null || IsOwnCollider(hit)) continue;
                    ShopClawPrizeNetwork prize = hit.GetComponentInParent<ShopClawPrizeNetwork>();
                    if (prize != null)
                    {
                        touchedPrize = true;
                        contactedPrizeIds.Add(prize.GetInstanceID());
                        if (!stopOnPrize) continue;
                    }

                    bool candidatePenetrates = Physics.ComputePenetration(box,
                        predictedTransformPosition, predictedTransformRotation,
                        hit, hit.transform.position, hit.transform.rotation,
                        out _, out float candidateDepth);
                    if (!candidatePenetrates) continue;
                    bool currentPenetrates = Physics.ComputePenetration(box,
                        box.transform.position, box.transform.rotation,
                        hit, hit.transform.position, hit.transform.rotation,
                        out _, out float currentDepth);
                    if (currentPenetrates && candidateDepth <= currentDepth + skin) continue;
                    LastPoseBlockerName = hit.name;
                    return true;
                }
            }
            return false;
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
            ownBoxColliders.Clear();
            ownBoxColliders.AddRange(GetComponentsInChildren<BoxCollider>(true));
        }

        private bool IsOwnCollider(Collider candidate)
        {
            for (int index = 0; index < ownColliders.Count; index++)
                if (ownColliders[index] == candidate) return true;
            return false;
        }

        private void OnCollisionEnter(Collision collision) => RecordPrizeContact(collision.collider);
        private void OnCollisionStay(Collision collision) => RecordPrizeContact(collision.collider);

        private void RecordPrizeContact(Collider candidate)
        {
            ShopClawPrizeNetwork prize = candidate != null
                ? candidate.GetComponentInParent<ShopClawPrizeNetwork>()
                : null;
            if (prize != null) contactedPrizeIds.Add(prize.GetInstanceID());
        }
    }
}
