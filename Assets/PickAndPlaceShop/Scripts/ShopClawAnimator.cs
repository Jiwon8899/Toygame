using UnityEngine;

namespace PickAndPlaceShop
{
    public sealed class ShopClawAnimator : MonoBehaviour
    {
        [SerializeField] private Transform clawHead;
        [SerializeField] private Transform prizeBin;
        private Vector3 clawStart;

        public void Configure(Transform claw, Transform prizes)
        {
            clawHead = claw;
            prizeBin = prizes;
            clawStart = claw != null ? claw.localPosition : Vector3.zero;
        }

        private void Awake()
        {
            if (clawHead != null)
            {
                clawStart = clawHead.localPosition;
            }
        }

        private void Update()
        {
            if (clawHead != null)
            {
                clawHead.localPosition = clawStart + Vector3.down * (0.12f + Mathf.Sin(Time.time * 1.8f) * 0.08f);
                clawHead.localRotation = Quaternion.Euler(0f, Time.time * 18f, 0f);
            }

            if (prizeBin != null)
            {
                prizeBin.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.4f) * 3f, 0f);
            }
        }
    }
}
