using UnityEngine;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(LineRenderer))]
    public sealed class ShopClawCableVisual : MonoBehaviour
    {
        [SerializeField] private Transform ceilingAnchor;
        [SerializeField] private Transform clawHead;
        private LineRenderer line;

        public void Configure(Transform top, Transform head)
        {
            ceilingAnchor = top;
            clawHead = head;
        }

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
        }

        private void LateUpdate()
        {
            if (line == null || ceilingAnchor == null || clawHead == null) return;
            Vector3 top = ceilingAnchor.position;
            top.x = clawHead.position.x;
            top.z = clawHead.position.z;
            line.SetPosition(0, top);
            line.SetPosition(1, clawHead.position);
        }
    }
}
