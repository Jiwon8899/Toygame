using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    public sealed class ShopEndingController : MonoBehaviour
    {
        private void Awake()
        {
            // Network-spawned players survive the NGO scene transition and bring their UI Toolkit HUD.
            // The ending uses its own uGUI canvas, so hide gameplay documents to keep the result screen clean.
            foreach (UnityEngine.UIElements.UIDocument document in
                     FindObjectsByType<UnityEngine.UIElements.UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (document.rootVisualElement != null)
                    document.rootVisualElement.style.display = UnityEngine.UIElements.DisplayStyle.None;
                document.enabled = false;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            OnClick("BtnEndingRestart", RestartCampaign);
            OnClick("BtnEndingMenu", () => StartCoroutine(ShutdownAndGo(null)));
            OnClick("BtnEndingQuit", () => StartCoroutine(ShutdownAndGo("QUIT")));
            RenderResult();
        }

        private void Start()
        {
            Button first = Find<Button>("BtnEndingRestart");
            if (EventSystem.current != null && first != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
        }

        private void RenderResult()
        {
            ShopCampaignResultData result = ShopCampaignResultStore.HasResult
                ? ShopCampaignResultStore.Result
                : new ShopCampaignResultData { TopProductName = new Unity.Collections.FixedString64Bytes("없음"), Grade = new Unity.Collections.FixedString32Bytes("D") };
            Text stats = Find<Text>("EndingStatsText");
            Text grade = Find<Text>("EndingGradeText");
            Text evaluation = Find<Text>("EndingEvaluationText");
            if (stats != null)
            {
                stats.text = "최종 가게 자금       " + result.FinalCoins.ToString("N0") + "\n" +
                             "총매출               " + result.TotalRevenue.ToString("N0") + "\n" +
                             "판매한 상품          " + result.TotalSold + "개\n" +
                             "획득한 상품          " + result.TotalAcquired + "개\n" +
                             "최종 평판            " + result.FinalReputation + "\n" +
                             "가장 많이 판 상품    " + result.TopProductName + "\n" +
                             "구매 포기 손님       " + result.GiveUpCustomers + "명\n" +
                             "인형뽑기 성공/실패   " + result.ClawSuccesses + " / " + result.ClawFailures;
            }
            string gradeValue = result.Grade.ToString();
            if (grade != null) grade.text = "최종 등급  " + gradeValue + "   ·   운영 점수 " + result.Score;
            if (evaluation != null) evaluation.text = ShopCampaignGradeRules.Evaluation(gradeValue);

        }

        private void RestartCampaign()
        {
            ShopLaunchRequest request = new ShopLaunchRequest
            {
                Mode = ShopLaunchMode.Solo,
                MaximumPlayers = 1,
                Address = "127.0.0.1",
                Port = ShopLaunchContext.LastPort,
                PlayerName = "점장",
                ResetCampaign = true
            };
            StartCoroutine(ShutdownAndGo(request));
        }

        private IEnumerator ShutdownAndGo(object target)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null)
            {
                if (manager.IsListening || manager.ShutdownInProgress) manager.Shutdown();
                float deadline = Time.realtimeSinceStartup + 5f;
                while ((manager.IsListening || manager.ShutdownInProgress) && Time.realtimeSinceStartup < deadline) yield return null;
                Destroy(manager.gameObject);
            }
            yield return null;

            if (target is string && (string)target == "QUIT")
            {
                Debug.Log("[ShopFlow] ENDING_QUIT");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                yield break;
            }
            if (target is ShopLaunchRequest request)
            {
                ShopCampaignResultStore.Clear();
                ShopLaunchContext.SetRequest(request);
                SceneManager.LoadScene(ShopLaunchContext.CompleteFlowScene);
            }
            else
            {
                ShopCampaignResultStore.Clear();
                SceneManager.LoadScene(ShopLaunchContext.MainMenuScene);
            }
        }

        private void OnClick(string name, UnityEngine.Events.UnityAction action) { Button button = Find<Button>(name); if (button != null) button.onClick.AddListener(action); }
        private T Find<T>(string name) where T : Component => GetComponentsInChildren<T>(true).FirstOrDefault(x => x.gameObject.name == name);
    }
}
