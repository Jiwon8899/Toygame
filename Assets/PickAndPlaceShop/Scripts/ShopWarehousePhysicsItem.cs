using UnityEngine;

namespace PickAndPlaceShop
{
    [DisallowMultipleComponent]
    public sealed class ShopWarehousePhysicsItem : MonoBehaviour
    {
        private Rigidbody body;
        private Transform homeRoot;
        private Vector3 homeLocalPosition;
        private Quaternion homeLocalRotation;
        private float pushImpulse;
        private float recoveryRadius;
        private float recoveryDropDistance;
        private float maximumSpeed;
        private float maximumAngularSpeed;
        private bool authoritative;

        public bool IsAuthoritative => authoritative;

        public void Configure(Transform configuredHomeRoot, bool hasAuthority,
            ShopWarehouseVisualConfig config)
        {
            homeRoot = configuredHomeRoot;
            homeLocalPosition = transform.localPosition;
            homeLocalRotation = transform.localRotation;
            authoritative = hasAuthority;
            pushImpulse = config.ControllerPushImpulse;
            recoveryRadius = config.RecoveryRadius;
            recoveryDropDistance = config.RecoveryDropDistance;
            maximumSpeed = config.MaximumLinearSpeed;
            maximumAngularSpeed = config.MaximumAngularSpeed;

            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.mass = config.ProductMass;
            body.linearDamping = config.LinearDamping;
            body.angularDamping = config.AngularDamping;
            body.maxLinearVelocity = maximumSpeed;
            body.maxAngularVelocity = maximumAngularSpeed;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.useGravity = authoritative;
            body.isKinematic = !authoritative;
            body.sleepThreshold = config.SleepThreshold;
        }

        public void ApplyControllerPush(Vector3 controllerVelocity)
        {
            if (!authoritative || body == null || body.isKinematic) return;
            Vector3 push = Vector3.ProjectOnPlane(controllerVelocity, Vector3.up);
            if (push.sqrMagnitude <= 0.01f) return;
            body.AddForce(push.normalized * pushImpulse, ForceMode.Impulse);
            body.AddTorque(Vector3.Cross(Vector3.up, push.normalized) * pushImpulse * 0.35f,
                ForceMode.Impulse);
        }

        private void FixedUpdate()
        {
            if (!authoritative || body == null || body.isKinematic || homeRoot == null) return;

            Vector3 homeWorldPosition = homeRoot.TransformPoint(homeLocalPosition);
            Vector3 flatOffset = Vector3.ProjectOnPlane(transform.position - homeWorldPosition, Vector3.up);
            if (transform.position.y < homeRoot.position.y - recoveryDropDistance ||
                flatOffset.sqrMagnitude > recoveryRadius * recoveryRadius)
            {
                ResetToHome();
                return;
            }

            if (body.linearVelocity.sqrMagnitude > maximumSpeed * maximumSpeed)
                body.linearVelocity = body.linearVelocity.normalized * maximumSpeed;
            if (body.angularVelocity.sqrMagnitude > maximumAngularSpeed * maximumAngularSpeed)
                body.angularVelocity = body.angularVelocity.normalized * maximumAngularSpeed;
        }

        private void ResetToHome()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = homeRoot.TransformPoint(homeLocalPosition);
            body.rotation = homeRoot.rotation * homeLocalRotation;
            body.Sleep();
        }
    }

    [DisallowMultipleComponent]
    public sealed class ShopWarehousePushSource : MonoBehaviour
    {
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            ShopWarehousePhysicsItem item = hit.collider != null
                ? hit.collider.GetComponentInParent<ShopWarehousePhysicsItem>()
                : null;
            if (item != null) item.ApplyControllerPush(hit.moveDirection * hit.moveLength /
                                                        Mathf.Max(Time.deltaTime, 0.001f));
        }
    }
}
