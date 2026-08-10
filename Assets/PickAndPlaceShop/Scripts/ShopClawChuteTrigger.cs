using UnityEngine;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(Collider))]
    public sealed class ShopClawChuteTrigger : MonoBehaviour
    {
        [SerializeField] private ShopClawMachineNetwork machine;
        private Collider triggerVolume;

        public void Configure(ShopClawMachineNetwork target) => machine = target;

        private void Awake()
        {
            triggerVolume = GetComponent<Collider>();
        }

        private void OnTriggerEnter(Collider other)
        {
            Enter(other);
        }

        private void OnTriggerStay(Collider other)
        {
            Enter(other);
        }

        private void OnTriggerExit(Collider other)
        {
            ShopClawPrizeNetwork prize = other.GetComponentInParent<ShopClawPrizeNetwork>();
            if (prize != null && machine != null)
                machine.ServerForgetChutePrize(prize);
        }

        private void Enter(Collider other)
        {
            ShopClawPrizeNetwork prize = other.GetComponentInParent<ShopClawPrizeNetwork>();
            if (prize != null && machine != null)
                machine.ServerEnterChutePrize(prize);
        }
    }
}
