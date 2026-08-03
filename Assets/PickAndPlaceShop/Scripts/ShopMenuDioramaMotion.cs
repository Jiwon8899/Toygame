using UnityEngine;

namespace PickAndPlaceShop
{
    public sealed class ShopMenuDioramaMotion : MonoBehaviour
    {
        [SerializeField] private Transform lookTarget;
        [SerializeField] private Transform claw;
        [SerializeField] private float cameraDrift = 0.16f;
        private Vector3 startPosition;
        private Vector3 clawStart;

#if UNITY_EDITOR
        public void EditorConfigure(Transform target, Transform clawTransform)
        {
            lookTarget = target;
            claw = clawTransform;
        }
#endif

        private void Awake()
        {
            startPosition = transform.position;
            if (claw != null) clawStart = claw.localPosition;
        }

        private void Update()
        {
            float t = Time.unscaledTime;
            transform.position = startPosition + new Vector3(Mathf.Sin(t * 0.09f), Mathf.Sin(t * 0.07f) * 0.25f, 0f) * cameraDrift;
            if (lookTarget != null) transform.LookAt(lookTarget);
            if (claw != null) claw.localPosition = clawStart + Vector3.down * (0.08f + Mathf.Sin(t * 0.65f) * 0.05f);
        }
    }
}
