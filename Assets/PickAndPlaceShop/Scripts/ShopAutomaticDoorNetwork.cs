using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    /// <summary>
    /// A directly walkable sliding door.  The server decides whether it is open;
    /// every peer animates the same local panel transforms from the replicated state.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShopAutomaticDoorNetwork : NetworkBehaviour
    {
        [SerializeField] private ShopBuildingZone buildingZone;
        [SerializeField] private Transform leftPanel;
        [SerializeField] private Transform rightPanel;
        [SerializeField] private Vector3 leftOpenOffset = new(-0.9f, 0f, 0f);
        [SerializeField] private Vector3 rightOpenOffset = new(0.9f, 0f, 0f);
        [SerializeField, Min(0.05f)] private float animationSeconds = 0.35f;

        public NetworkVariable<bool> IsOpen = new(false);

        private readonly HashSet<ulong> presentPlayers = new();
        private Vector3 leftClosedPosition;
        private Vector3 rightClosedPosition;
        private bool cachedPanelPositions;

#if UNITY_EDITOR
        public void EditorConfigure(ShopBuildingZone zone, Transform left, Transform right,
            Vector3 leftOffset, Vector3 rightOffset, float seconds)
        {
            buildingZone = zone;
            leftPanel = left;
            rightPanel = right;
            leftOpenOffset = leftOffset;
            rightOpenOffset = rightOffset;
            animationSeconds = Mathf.Max(0.05f, seconds);
        }
#endif

        private void Awake()
        {
            CachePanelPositions();
        }

        public override void OnNetworkSpawn()
        {
            CachePanelPositions();
            if (IsServer) IsOpen.Value = false;
        }

        private void Update()
        {
            AnimatePanel(leftPanel, IsOpen.Value ? leftClosedPosition + leftOpenOffset : leftClosedPosition);
            AnimatePanel(rightPanel, IsOpen.Value ? rightClosedPosition + rightOpenOffset : rightClosedPosition);

            if (!IsServer) return;
            RemoveDisconnectedPlayers();
            bool shouldOpen = buildingZone == null || buildingZone.CanEnter;
            shouldOpen &= presentPlayers.Count > 0;
            if (IsOpen.Value != shouldOpen) IsOpen.Value = shouldOpen;
        }

        public void ServerSetPlayerPresence(ulong clientId, bool present)
        {
            if (!IsServer) return;
            if (present) presentPlayers.Add(clientId);
            else presentPlayers.Remove(clientId);
        }

        public int ServerPresenceCount => presentPlayers.Count;

        private void RemoveDisconnectedPlayers()
        {
            if (NetworkManager == null) return;
            presentPlayers.RemoveWhere(clientId => !NetworkManager.ConnectedClients.ContainsKey(clientId));
        }

        private void CachePanelPositions()
        {
            if (cachedPanelPositions) return;
            if (leftPanel != null) leftClosedPosition = leftPanel.localPosition;
            if (rightPanel != null) rightClosedPosition = rightPanel.localPosition;
            cachedPanelPositions = leftPanel != null || rightPanel != null;
        }

        private void AnimatePanel(Transform panel, Vector3 target)
        {
            if (panel == null) return;
            float blend = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.05f, animationSeconds));
            panel.localPosition = Vector3.Lerp(panel.localPosition, target, blend);
        }
    }
}
