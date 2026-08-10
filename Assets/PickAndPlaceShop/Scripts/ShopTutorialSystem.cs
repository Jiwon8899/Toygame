using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    public enum ShopTutorialAction
    {
        MachineEntered,
        PrizeAcquired,
        InventoryOpened,
        ProductDisplayed,
        ProductSold,
        DayClosed
    }

    [CreateAssetMenu(menuName = "Pick And Place Shop/Tutorial Config", fileName = "ShopTutorialConfig")]
    public sealed class ShopTutorialConfig : ScriptableObject
    {
        [SerializeField, Min(0.1f)] private float movementDistance = 2.5f;
        [SerializeField, Min(0)] private int completionReward = 200;
        [SerializeField, Min(0)] private int skipReward = 3000;
        [SerializeField, Range(0.2f, 1f)] private float skipDoubleTapSeconds = 0.5f;
        [SerializeField, Range(0.2f, 0.3f)] private float objectiveToggleSeconds = 0.25f;

        public float MovementDistance => movementDistance;
        public int CompletionReward => completionReward;
        public int SkipReward => Mathf.Max(0, skipReward);
        public float SkipDoubleTapSeconds => Mathf.Clamp(skipDoubleTapSeconds, 0.2f, 1f);
        public float ObjectiveToggleSeconds => Mathf.Clamp(objectiveToggleSeconds, 0.2f, 0.3f);

        public static ShopTutorialConfig Load() =>
            Resources.Load<ShopTutorialConfig>("Progression/ShopTutorialConfig");
    }

    public static class ShopTutorialInputRules
    {
        public static bool RegisterSkipTap(ref float previousTapTime, float currentTime, float window)
        {
            if (currentTime - previousTapTime <= Mathf.Max(0f, window))
            {
                previousTapTime = float.NegativeInfinity;
                return true;
            }
            previousTapTime = currentTime;
            return false;
        }
    }

    [DefaultExecutionOrder(350)]
    public sealed class ShopTutorialRuntime : MonoBehaviour
    {
        public const int StepCount = 7;
        private static readonly string[] Labels =
        {
            "이동해 보세요 (WASD)",
            "뽑기 기계 앞에서 E를 누르세요",
            "캡슐을 하나 떠 보세요 (이번 판 무료)",
            "I를 눌러 보관함에서 상품을 확인하세요",
            "진열대에 상품을 올려 보세요",
            "E로 영업을 시작하고 상품을 1개 판매하세요",
            "마감 정산을 해 보세요"
        };

        private static ShopTutorialRuntime instance;
        private ShopTutorialConfig config;
        private Transform observedPlayer;
        private Vector3 previousPosition;
        private float movedDistance;
        private ShopInteractable closingInteractable;
        private GameObject closingDestinationMarker;
        private float nextClosingSearchAt;

        public static bool IsActive => ShopProgressionManager.Instance != null &&
            !ShopProgressionManager.Instance.TutorialCompleted;
        public static bool FreeScoopAttempt => IsActive &&
            ShopProgressionManager.Instance.TutorialStep == 2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject root = new("[Global] Shop Tutorial");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<ShopTutorialRuntime>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            config = ShopTutorialConfig.Load();
        }

        private void Update()
        {
            UpdateClosingDestinationMarker();
            if (!IsHostAuthority() || !IsActive || config == null) return;
            ShopProgressionManager progression = ShopProgressionManager.Instance;
            if (progression == null || progression.TutorialStep != 0) return;
            Transform player = NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null
                ? NetworkManager.Singleton.LocalClient.PlayerObject?.transform
                : null;
            if (player == null) return;
            if (observedPlayer != player)
            {
                observedPlayer = player;
                previousPosition = player.position;
                movedDistance = 0f;
                return;
            }
            Vector3 delta = player.position - previousPosition;
            delta.y = 0f;
            movedDistance += delta.magnitude;
            previousPosition = player.position;
            if (movedDistance >= config.MovementDistance) CompleteCurrentStep();
        }

        private void OnDestroy()
        {
            if (closingDestinationMarker != null) Destroy(closingDestinationMarker);
            if (instance == this) instance = null;
        }

        private void UpdateClosingDestinationMarker()
        {
            ShopProgressionManager progression = ShopProgressionManager.Instance;
            bool shouldShow = progression != null && !progression.TutorialCompleted &&
                              progression.TutorialStep == 6;
            if (!shouldShow)
            {
                if (closingDestinationMarker != null) Destroy(closingDestinationMarker);
                closingDestinationMarker = null;
                closingInteractable = null;
                return;
            }

            if (closingInteractable == null && Time.unscaledTime >= nextClosingSearchAt)
            {
                nextClosingSearchAt = Time.unscaledTime + 0.5f;
                ShopInteractable[] interactables = FindObjectsByType<ShopInteractable>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < interactables.Length; i++)
                {
                    if (interactables[i] == null || interactables[i].Action != ShopAction.EndDay) continue;
                    closingInteractable = interactables[i];
                    break;
                }
            }
            if (closingInteractable == null) return;

            if (closingDestinationMarker == null)
            {
                closingDestinationMarker = new GameObject("TutorialClosingDestinationMarker");
                TextMesh marker = closingDestinationMarker.AddComponent<TextMesh>();
                marker.text = "!";
                marker.anchor = TextAnchor.MiddleCenter;
                marker.alignment = TextAlignment.Center;
                marker.fontSize = 96;
                marker.characterSize = 0.12f;
                marker.color = Color.red;
                closingDestinationMarker.AddComponent<ShopWorldTextBillboard>();
            }
            closingDestinationMarker.transform.position = closingInteractable.InteractionWorldPosition +
                                                          Vector3.up * 2.4f;
        }

        public static void Report(ShopTutorialAction action)
        {
            if (instance == null || !IsActive || !instance.IsHostAuthority()) return;
            int expected = action switch
            {
                ShopTutorialAction.MachineEntered => 1,
                ShopTutorialAction.PrizeAcquired => 2,
                ShopTutorialAction.InventoryOpened => 3,
                ShopTutorialAction.ProductDisplayed => 4,
                ShopTutorialAction.ProductSold => 5,
                ShopTutorialAction.DayClosed => 6,
                _ => -1
            };
            if (ShopProgressionManager.Instance != null &&
                ShopProgressionManager.Instance.TutorialStep == expected)
                instance.CompleteCurrentStep();
        }

        public static bool TryGetDisplay(out string label, out int current, out int target)
        {
            label = string.Empty;
            current = 0;
            target = 1;
            ShopProgressionManager progression = ShopProgressionManager.Instance;
            if (progression == null || progression.TutorialCompleted) return false;
            int step = Mathf.Clamp(progression.TutorialStep, 0, StepCount - 1);
            label = "튜토리얼 " + (step + 1) + "/" + StepCount + "  " + Labels[step];
            if (step == 0 && instance != null && instance.config != null)
            {
                current = Mathf.FloorToInt(instance.movedDistance * 10f);
                target = Mathf.CeilToInt(instance.config.MovementDistance * 10f);
            }
            return true;
        }

        private void CompleteCurrentStep()
        {
            ShopProgressionManager progression = ShopProgressionManager.Instance;
            if (progression == null || config == null) return;
            progression.AdvanceTutorial(config.CompletionReward);
            movedDistance = 0f;
        }

        private bool IsHostAuthority()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager == null || !manager.IsListening || manager.IsServer;
        }
    }
}
