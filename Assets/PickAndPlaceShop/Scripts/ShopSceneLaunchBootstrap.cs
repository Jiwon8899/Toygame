using System.Collections;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.SinglePlayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PickAndPlaceShop
{
    public sealed class ShopSceneLaunchBootstrap : MonoBehaviour
    {
        [SerializeField] private float connectionTimeoutSeconds = 10f;
        private ShopLaunchRequest request;
        private GameNetworkManager manager;

        private IEnumerator Start()
        {
            if (!ShopLaunchContext.TryConsume(out request))
            {
                request = new ShopLaunchRequest
                {
                    Mode = ShopLaunchMode.Solo,
                    MaximumPlayers = 1,
                    Address = "127.0.0.1",
                    Port = ShopFlowRules.DefaultPort,
                    PlayerName = "점장",
                    ResetCampaign = true
                };
            }
            yield return null;

            manager = GameNetworkManager.Instance;
            if (manager == null)
            {
                yield return FailAndReturn("게임의 NetworkManager를 찾지 못했습니다.");
                yield break;
            }

            foreach (GameNetworkUI networkUi in FindObjectsByType<GameNetworkUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                UIDocument document = networkUi.GetComponent<UIDocument>();
                if (document != null) document.enabled = false;
                networkUi.enabled = false;
            }

            SinglePlayerTransport transport = manager.GetComponent<SinglePlayerTransport>();
            if (transport == null)
            {
                transport = manager.gameObject.AddComponent<SinglePlayerTransport>();
            }

            manager.PlayerName = string.IsNullOrWhiteSpace(request.PlayerName) ? "플레이어" : request.PlayerName.Trim();
            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.NetworkTransport = transport;
            manager.ConnectionApprovalCallback = ApproveConnection;
            manager.StartHostConnection();

            float deadline = Time.realtimeSinceStartup + connectionTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (manager != null && manager.IsHost && manager.IsListening &&
                    manager.LocalClient != null && manager.LocalClient.PlayerObject != null) break;
                if (manager == null || manager.NetworkState == null ||
                    manager.NetworkState.ConnectionState == GameNetworkManager.ConnectionStates.Failed)
                {
                    yield return FailAndReturn("싱글플레이 게임을 시작하지 못했습니다.");
                    yield break;
                }
                yield return null;
            }

            if (manager == null || !manager.IsHost || !manager.IsListening ||
                manager.LocalClient == null || manager.LocalClient.PlayerObject == null)
            {
                yield return FailAndReturn("싱글플레이 게임 준비 시간이 초과되었습니다.");
                yield break;
            }

            if (manager.IsServer)
            {
                float gameDeadline = Time.realtimeSinceStartup + 8f;
                while ((ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsSpawned) && Time.realtimeSinceStartup < gameDeadline)
                    yield return null;
                if (ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsSpawned)
                {
                    yield return FailAndReturn("게임 상태를 준비하지 못했습니다.");
                    yield break;
                }

                if (request.ResetCampaign)
                {
                    ShopProgressionManager.Instance?.ResetProgressionForNewProfile(true);
                    ShopNetworkGame.Instance.ServerResetCampaign();
                    ShopCampaignResultStore.Clear();
                    ShopStoreNamingSystem naming = ShopStoreNamingSystem.Instance;
                    naming.BeginNewGameNaming();
                    while (naming != null && naming.IsNaming) yield return null;
                }
                else
                {
                    ShopProgressionManager progression = ShopProgressionManager.Instance;
                    if (progression == null || !progression.LoadNow())
                    {
                        Debug.LogError("[ShopFlow] CONTINUE_RESTORE_FAILED");
                        yield return FailAndReturn("저장된 게임을 불러오지 못했습니다.");
                        yield break;
                    }
                    Debug.Log("[ShopFlow] CONTINUE_RESTORE_COMPLETE items=" +
                              ShopNetworkGame.Instance.ItemContainers.Count);
                }
            }

            Debug.Log("[ShopFlow] SOLO_STARTED");
        }

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest approvalRequest,
            NetworkManager.ConnectionApprovalResponse approvalResponse)
        {
            int currentPlayers = manager != null ? manager.ConnectedClientsIds.Count : 0;
            bool approved = currentPlayers < 1;
            approvalResponse.Approved = approved;
            approvalResponse.CreatePlayerObject = approved;
            approvalResponse.Pending = false;
            approvalResponse.Reason = approved ? string.Empty : "방의 최대 인원에 도달했습니다.";
        }

        private IEnumerator FailAndReturn(string message)
        {
            Debug.LogWarning("[ShopFlow] CONNECTION_FAILED " + message);
            ShopLaunchContext.SetError(message);
            if (manager != null)
            {
                manager.ConnectionApprovalCallback = null;
                if (manager.IsListening || manager.ShutdownInProgress) manager.Shutdown();
                float deadline = Time.realtimeSinceStartup + 5f;
                while (manager != null && (manager.IsListening || manager.ShutdownInProgress) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (manager != null) Destroy(manager.gameObject);
            }
            yield return null;
            SceneManager.LoadScene(ShopLaunchContext.MainMenuScene);
        }
    }
}
