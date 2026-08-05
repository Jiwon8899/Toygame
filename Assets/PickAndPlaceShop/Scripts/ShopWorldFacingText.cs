using UnityEngine;

namespace PickAndPlaceShop
{
    public sealed class ShopWorldFacingText : MonoBehaviour
    {
        private void LateUpdate() => ShopWorldFacingUtility.FaceCamera(transform, Camera.main);
    }
}
