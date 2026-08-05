using UnityEngine;
using UnityEngine.SceneManagement;

namespace PickAndPlaceShop
{
    /// <summary>Keeps world-space labels readable without exposing their back face.</summary>
    public static class ShopWorldFacingUtility
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AttachToSceneLabels()
        {
            TextMesh[] labels = Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < labels.Length; i++)
            {
                TextMesh label = labels[i];
                if (label == null || label.GetComponent<ShopWorldFacingText>() != null) continue;
                label.gameObject.AddComponent<ShopWorldFacingText>();
            }

            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || canvas.renderMode != RenderMode.WorldSpace ||
                    canvas.GetComponent<ShopWorldFacingText>() != null) continue;
                canvas.gameObject.AddComponent<ShopWorldFacingText>();
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachToSceneLabels();

        public static bool FaceCamera(Transform target, Camera camera)
        {
            if (target == null || camera == null) return false;
            // TextMesh and world-space Canvas content face their local -Z axis.
            // Copying the camera rotation keeps that front face pointed at the viewer
            // and avoids the mirrored back-face produced by LookRotation(toCamera).
            target.rotation = camera.transform.rotation;
            return true;
        }
    }
}
