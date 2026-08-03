using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(NetworkObject), typeof(Collider))]
    public sealed class ShopDistrictPortalNetwork : NetworkBehaviour
    {
        [SerializeField] private string portalName = "건물 입구";
        [SerializeField] private Transform destination;
        [SerializeField, Min(1f)] private float interactionRange = 4.5f;
        [SerializeField, Min(0.1f)] private float reuseCooldown = 0.75f;

        private readonly Dictionary<ulong, float> nextUseTimeByClient = new();

        public string InteractionPrompt => portalName + " 들어가기";
        public string PortalName => portalName;
        public Vector3 DestinationWorldPosition => destination != null ? destination.position : transform.position;
        public Vector3 InteractionWorldPosition => transform.position - transform.forward * 2.2f;

#if UNITY_EDITOR
        public void EditorConfigure(string label, Transform target, float range = 4.5f)
        {
            portalName = label;
            destination = target;
            interactionRange = range;
        }
#endif

        public void RequestUse()
        {
            if (IsSpawned) RequestUseRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestUseRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (destination == null || NetworkManager == null ||
                !NetworkManager.ConnectedClients.TryGetValue(sender, out NetworkClient client) ||
                client.PlayerObject == null) return;
            if (nextUseTimeByClient.TryGetValue(sender, out float nextUse) && Time.unscaledTime < nextUse) return;

            NetworkObject player = client.PlayerObject;
            if (Vector3.Distance(player.transform.position, transform.position) > interactionRange)
            {
                ShopNetworkGame.Instance?.ServerSetEvent("문 가까이에서 E키를 눌러 주세요.");
                return;
            }

            nextUseTimeByClient[sender] = Time.unscaledTime + reuseCooldown;
            Vector3 targetPosition = destination.position;
            Quaternion targetRotation = destination.rotation;
            player.transform.SetPositionAndRotation(targetPosition, targetRotation);
            ApplyTeleportRpc(sender, targetPosition, targetRotation);
            ShopNetworkGame.Instance?.ServerSetEvent(portalName + "으로 이동했습니다.");
        }

        [Rpc(SendTo.Everyone)]
        private void ApplyTeleportRpc(ulong targetClientId, Vector3 position, Quaternion rotation)
        {
            if (!IsClient || NetworkManager == null || NetworkManager.LocalClientId != targetClientId ||
                NetworkManager.LocalClient == null || NetworkManager.LocalClient.PlayerObject == null) return;

            NetworkObject player = NetworkManager.LocalClient.PlayerObject;
            CoreMovement movement = player.GetComponent<CoreMovement>();
            player.transform.rotation = rotation;
            if (movement != null)
            {
                movement.SetPosition(position, true);
                return;
            }

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            player.transform.SetPositionAndRotation(position, rotation);
            if (controller != null) controller.enabled = true;
        }
    }
}
