using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    /// <summary>
    /// Server-owned business access state for a building.  The zone itself does
    /// not teleport players; it only gives the automatic door a replicated lock.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShopBuildingZone : NetworkBehaviour
    {
        [SerializeField] private string buildingId = "building";
        [SerializeField] private bool startsUnlocked = true;

        public NetworkVariable<bool> IsUnlocked = new(true);
        public string BuildingId => buildingId;
        public bool CanEnter => IsUnlocked.Value;

#if UNITY_EDITOR
        public void EditorConfigure(string id, bool unlocked)
        {
            buildingId = id;
            startsUnlocked = unlocked;
            IsUnlocked.Value = unlocked;
        }
#endif

        public override void OnNetworkSpawn()
        {
            if (IsServer) IsUnlocked.Value = startsUnlocked;
        }

        public void ServerSetUnlocked(bool unlocked)
        {
            if (IsServer) IsUnlocked.Value = unlocked;
        }
    }
}
