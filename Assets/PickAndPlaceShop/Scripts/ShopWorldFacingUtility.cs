using UnityEngine;

namespace PickAndPlaceShop
{
    /// <summary>Keeps world-space labels readable without exposing their back face.</summary>
    public static class ShopWorldFacingUtility
    {
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
