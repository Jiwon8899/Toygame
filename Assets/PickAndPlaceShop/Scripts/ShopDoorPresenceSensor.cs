using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    /// <summary>
    /// Server-only occupancy sensor.  It reports player presence to the door and
    /// never changes player position, so walking through remains continuous.
    /// </summary>
    public sealed class ShopDoorPresenceSensor : MonoBehaviour
    {
        [SerializeField] private ShopAutomaticDoorNetwork door;

#if UNITY_EDITOR
        public void EditorConfigure(ShopAutomaticDoorNetwork targetDoor)
        {
            door = targetDoor;
        }
#endif

        private void OnTriggerEnter(Collider other)
        {
            SetPresence(other, true);
        }

        private void OnTriggerExit(Collider other)
        {
            SetPresence(other, false);
        }

        private void OnTriggerStay(Collider other)
        {
            SetPresence(other, true);
        }

        private void SetPresence(Collider other, bool present)
        {
            if (door == null || !door.IsServer) return;
            NetworkObject networkObject = other.GetComponentInParent<NetworkObject>();
            if (networkObject == null || !networkObject.IsPlayerObject || !networkObject.IsSpawned) return;
            door.ServerSetPlayerPresence(networkObject.OwnerClientId, present);
        }
    }
}
