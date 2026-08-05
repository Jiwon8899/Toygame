using UnityEngine;

namespace PickAndPlaceShop
{
    /// <summary>Keeps world-space labels readable without exposing their back face.</summary>
    public static class ShopWorldFacingUtility
    {
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
        }

        public static bool FaceCamera(Transform target, Camera camera)
        {
            if (target == null || camera == null) return false;
            Vector3 toCamera = camera.transform.position - target.position;
            if (toCamera.sqrMagnitude < 0.0001f) return false;
            target.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
            return true;
        }
    }
}
