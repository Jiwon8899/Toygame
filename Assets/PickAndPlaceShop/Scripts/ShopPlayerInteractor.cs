using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShopPlayerInteractor : NetworkBehaviour
    {
        [SerializeField] private ShopOperationsConfig interactionConfig;
        [SerializeField] private float interactionRange = 2.5f;
        [SerializeField] private float cameraRayPadding = 1f;
        [SerializeField, Range(-1f, 1f)] private float proximityFacingThreshold = 0.05f;

        public static string LocalPrompt { get; private set; } = string.Empty;

        private Camera playerCamera;
        private ShopInteractable currentTarget;
        private readonly RaycastHit[] rayHits = new RaycastHit[32];
        private readonly Collider[] nearbyColliders = new Collider[48];

        public float EffectiveInteractionRange => interactionConfig != null
            ? interactionConfig.InteractionDistance
            : Mathf.Max(0.5f, interactionRange);

        private float EffectiveFacingThreshold => interactionConfig != null
            ? interactionConfig.InteractionFacingThreshold
            : proximityFacingThreshold;

        private void Awake()
        {
            if (interactionConfig == null) interactionConfig = ShopOperationsConfig.Load();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                LocalPrompt = string.Empty;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                LocalPrompt = string.Empty;
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }

            if (ShopLocalPauseState.IsPaused || !ShopInputModeManager.AllowsGameplay)
            {
                LocalPrompt = string.Empty;
                return;
            }

            if (playerCamera == null || !playerCamera.isActiveAndEnabled)
            {
                playerCamera = Camera.main;
            }

            UpdateTarget();
            if (currentTarget != null && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                currentTarget.Interact();
            }
        }

        private void UpdateTarget()
        {
            currentTarget = null;
            Vector3 playerCenter = transform.position + Vector3.up * 1.2f;
            Vector3 rayOrigin = playerCamera != null ? playerCamera.transform.position : playerCenter;
            Vector3 rayDirection = playerCamera != null ? playerCamera.transform.forward : transform.forward;
            float cameraToPlayer = Vector3.Distance(rayOrigin, playerCenter);
            float range = EffectiveInteractionRange;
            float rayDistance = range + cameraToPlayer + cameraRayPadding;

            int hitCount = Physics.RaycastNonAlloc(new Ray(rayOrigin, rayDirection), rayHits, rayDistance, ~0,
                QueryTriggerInteraction.Collide);
            float nearestRayHit = float.MaxValue;
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = rayHits[index];
                if (hit.collider == null || IsLocalPlayerCollider(hit.collider)) continue;
                ShopInteractable candidate = hit.collider.GetComponentInParent<ShopInteractable>();
                if (candidate == null || !IsWithinPlayerRange(candidate, playerCenter) ||
                    !IsFacing(candidate, playerCenter, rayDirection) || hit.distance >= nearestRayHit) continue;
                nearestRayHit = hit.distance;
                currentTarget = candidate;
            }

            if (currentTarget == null)
                currentTarget = FindNearbyFacingTarget(playerCenter, rayDirection);

            LocalPrompt = currentTarget != null
                ? "[E] " + currentTarget.Prompt
                : string.Empty;
        }

        private ShopInteractable FindNearbyFacingTarget(Vector3 playerCenter, Vector3 facing)
        {
            float range = EffectiveInteractionRange;
            int count = Physics.OverlapSphereNonAlloc(playerCenter, range, nearbyColliders, ~0,
                QueryTriggerInteraction.Collide);
            ShopInteractable best = null;
            float bestScore = float.MaxValue;
            Vector3 horizontalFacing = Vector3.ProjectOnPlane(facing, Vector3.up).normalized;
            if (horizontalFacing.sqrMagnitude < 0.01f) horizontalFacing = transform.forward;
            Vector3 playerFacing = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (playerFacing.sqrMagnitude < 0.01f) playerFacing = horizontalFacing;

            for (int index = 0; index < count; index++)
            {
                Collider collider = nearbyColliders[index];
                if (collider == null || IsLocalPlayerCollider(collider)) continue;
                ShopInteractable candidate = collider.GetComponentInParent<ShopInteractable>();
                if (candidate == null) continue;
                Vector3 interactionPoint = candidate.ClosestInteractionWorldPosition(playerCenter);
                Vector3 direction = Vector3.ProjectOnPlane(interactionPoint - playerCenter, Vector3.up);
                float distance = direction.magnitude;
                if (distance > range) continue;
                Vector3 normalizedDirection = distance <= 0.05f ? horizontalFacing : direction / distance;
                float facingDot = distance <= 0.05f ? 1f : Mathf.Max(
                    Vector3.Dot(horizontalFacing, normalizedDirection),
                    Vector3.Dot(playerFacing, normalizedDirection));
                if (facingDot < EffectiveFacingThreshold) continue;
                float score = distance + (1f - facingDot) * 2f;
                if (score >= bestScore) continue;
                bestScore = score;
                best = candidate;
            }

            return best;
        }

        private bool IsLocalPlayerCollider(Collider collider) =>
            collider.transform == transform || collider.transform.IsChildOf(transform);

        private bool IsWithinPlayerRange(ShopInteractable candidate, Vector3 playerCenter) =>
            candidate != null && Vector3.Distance(playerCenter,
                candidate.ClosestInteractionWorldPosition(playerCenter)) <= EffectiveInteractionRange;

        private bool IsFacing(ShopInteractable candidate, Vector3 playerCenter, Vector3 facing)
        {
            Vector3 horizontalFacing = Vector3.ProjectOnPlane(facing, Vector3.up).normalized;
            Vector3 direction = Vector3.ProjectOnPlane(
                candidate.ClosestInteractionWorldPosition(playerCenter) - playerCenter, Vector3.up).normalized;
            return direction.sqrMagnitude < 0.01f || horizontalFacing.sqrMagnitude < 0.01f ||
                   Vector3.Dot(horizontalFacing, direction) >= EffectiveFacingThreshold;
        }
    }
}
