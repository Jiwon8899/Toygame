using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(400)]
    public sealed class ShopWorldSafetyController : MonoBehaviour
    {
        private static ShopWorldSafetyController instance;
        private readonly List<ShopSpawnPadMarker> safePoints = new();
        private ShopWorldConfig config;
        private float nextPoll;
        private CanvasGroup fade;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[World] Safety Controller");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopWorldSafetyController>();
        }

        private void Awake()
        {
            config = ShopWorldConfig.Load();
            if (config == null)
            {
                Debug.LogError("[WorldSafety] Resources/" + ShopWorldConfig.ResourcePath +
                               " 설정을 찾지 못했습니다.", this);
                enabled = false;
                return;
            }
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void Start() => RefreshSafePoints();

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (instance == this) instance = null;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => RefreshSafePoints();

        private void Update()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + config.SafetyPollInterval;
            if (safePoints.Count == 0) RefreshSafePoints();
            if (safePoints.Count == 0) return;

            NetworkManager network = NetworkManager.Singleton;
            NetworkObject localPlayer = network != null && network.LocalClient != null
                ? network.LocalClient.PlayerObject
                : null;
            if (localPlayer != null && localPlayer.transform.position.y < config.FallRecoveryHeight)
            {
                RecoverTransform(localPlayer.transform, FindNearestSafePoint(localPlayer.transform.position));
                StartCoroutine(PlayRecoveryFade());
            }

            if (network == null || !network.IsServer) return;
            foreach (ShopCustomerNetwork customer in
                     FindObjectsByType<ShopCustomerNetwork>(FindObjectsSortMode.None))
            {
                if (customer != null && customer.IsSpawned &&
                    customer.transform.position.y < config.FallRecoveryHeight)
                    customer.ServerRecoverTo(FindNearestSafePoint(customer.transform.position));
            }

            foreach (ShopWorldSafetyAgent agent in
                     FindObjectsByType<ShopWorldSafetyAgent>(FindObjectsSortMode.None))
            {
                if (agent != null && agent.transform.position.y < config.FallRecoveryHeight)
                    agent.RecoverTo(FindNearestSafePoint(agent.transform.position));
            }
        }

        private void RefreshSafePoints()
        {
            safePoints.Clear();
            foreach (ShopSpawnPadMarker marker in
                     FindObjectsByType<ShopSpawnPadMarker>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (marker == null || !marker.gameObject.scene.IsValid()) continue;
                marker.HideRuntimeVisuals();
                safePoints.Add(marker);
            }
        }

        private Vector3 FindNearestSafePoint(Vector3 origin)
        {
            ShopSpawnPadMarker nearest = safePoints[0];
            float best = float.MaxValue;
            for (int i = 0; i < safePoints.Count; i++)
            {
                Vector3 delta = safePoints[i].SafePosition - origin;
                delta.y = 0f;
                if (delta.sqrMagnitude >= best) continue;
                best = delta.sqrMagnitude;
                nearest = safePoints[i];
            }
            return nearest.SafePosition + Vector3.up * config.SafePointHeightOffset;
        }

        private static void RecoverTransform(Transform target, Vector3 safePosition)
        {
            CharacterController controller = target.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            target.position = safePosition;
            if (controller != null) controller.enabled = true;
            Rigidbody body = target.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private IEnumerator PlayRecoveryFade()
        {
            EnsureFade();
            float duration = config.RecoveryFadeSeconds;
            if (duration <= 0f) yield break;
            fade.alpha = 0.8f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                fade.alpha = Mathf.Lerp(0.8f, 0f, elapsed / duration);
                yield return null;
            }
            fade.alpha = 0f;
        }

        private void EnsureFade()
        {
            if (fade != null) return;
            GameObject canvasObject = new("RecoveryFade", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            fade = canvasObject.GetComponent<CanvasGroup>();
            fade.blocksRaycasts = false;
            fade.interactable = false;
            Image image = new GameObject("Fade", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
                .GetComponent<Image>();
            image.transform.SetParent(canvasObject.transform, false);
            image.color = Color.black;
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            fade.alpha = 0f;
        }
    }

    public sealed class ShopWorldSafetyAgent : MonoBehaviour
    {
        public void RecoverTo(Vector3 safePosition)
        {
            CharacterController controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            transform.position = safePosition;
            if (controller != null) controller.enabled = true;
            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null) return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
