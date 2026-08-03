using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(1000)]
    public sealed class ShopCameraGuard : NetworkBehaviour
    {
        private const float MinimumPitch = -20f;
        private const float MaximumPitch = 55f;
        private const float InitialPitch = 10f;

        private Component cameraController;
        private Transform lookTarget;
        private FieldInfo verticalAngleField;
        private FieldInfo horizontalAngleField;

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                return;
            }

            lookTarget = transform.Find("PlayerCameraRoot");
            foreach (Component component in GetComponents<Component>())
            {
                if (component != null && component.GetType().FullName == "Blocks.Gameplay.Core.CoreCameraController")
                {
                    cameraController = component;
                    break;
                }
            }

            if (cameraController == null)
            {
                return;
            }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            verticalAngleField = cameraController.GetType().GetField("m_CurrentVerticalLookAngle", flags);
            horizontalAngleField = cameraController.GetType().GetField("m_CurrentHorizontalLookAngle", flags);
            verticalAngleField?.SetValue(cameraController, InitialPitch);
        }

        private void LateUpdate()
        {
            if (!IsSpawned || !IsOwner || cameraController == null || verticalAngleField == null)
            {
                return;
            }
            if (!ShopInputModeManager.AllowsGameplay || ShopInputModeManager.SuppressLookThisFrame)
                return;

            float pitch = (float)verticalAngleField.GetValue(cameraController);
            float clampedPitch = Mathf.Clamp(pitch, MinimumPitch, MaximumPitch);
            if (!Mathf.Approximately(pitch, clampedPitch))
            {
                verticalAngleField.SetValue(cameraController, clampedPitch);
            }

            if (lookTarget != null)
            {
                float yaw = horizontalAngleField != null
                    ? (float)horizontalAngleField.GetValue(cameraController)
                    : lookTarget.eulerAngles.y;
                lookTarget.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(clampedPitch, 0f, 0f);
            }
        }
    }
}
