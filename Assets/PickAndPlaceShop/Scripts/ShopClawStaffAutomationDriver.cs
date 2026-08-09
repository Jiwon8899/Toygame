using UnityEngine;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(30)]
    public sealed class ShopClawStaffAutomationDriver : MonoBehaviour
    {
        private ShopClawMachineNetwork machine;

        public void Configure(ShopClawMachineNetwork target) => machine = target;

        private void FixedUpdate()
        {
            if (machine != null && machine.IsServer)
                machine.ServerTickStaffAutomation(Time.fixedDeltaTime);
        }
    }
}
