using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShopCampaignFlowController : NetworkBehaviour
    {
        [SerializeField] private ShopCampaignGradeConfig gradeConfig;
        private bool transitionStarted;

#if UNITY_EDITOR
        public void EditorConfigure(ShopCampaignGradeConfig config) => gradeConfig = config;
#endif

        private void Update()
        {
            if (!IsSpawned || !IsServer || transitionStarted) return;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || game.Phase.Value != ShopPhase.Complete) return;
            transitionStarted = true;
            ShopCampaignResultData result = game.ServerCreateCampaignResult(gradeConfig);
            ShopCampaignResultStore.Set(result);
            ReceiveFinalResultRpc(result);
            StartCoroutine(ServerLoadEnding());
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void ReceiveFinalResultRpc(ShopCampaignResultData result)
        {
            ShopCampaignResultStore.Set(result);
            Debug.Log("[ShopFlow] CAMPAIGN_RESULT grade=" + result.Grade + " revenue=" + result.TotalRevenue +
                      " sold=" + result.TotalSold + " coins=" + result.FinalCoins);
        }

        private IEnumerator ServerLoadEnding()
        {
            yield return new WaitForSecondsRealtime(0.35f);
            if (NetworkManager != null && NetworkManager.SceneManager != null)
            {
                SceneEventProgressStatus status = NetworkManager.SceneManager.LoadScene(
                    ShopLaunchContext.EndingScene, LoadSceneMode.Single);
                Debug.Log("[ShopFlow] ENDING_LOAD status=" + status);
            }
            else
            {
                SceneManager.LoadScene(ShopLaunchContext.EndingScene);
            }
        }
    }
}
