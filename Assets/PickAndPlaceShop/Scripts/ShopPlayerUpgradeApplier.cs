using Blocks.Gameplay.Core;
using UnityEngine;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(CoreMovement))]
    public sealed class ShopPlayerUpgradeApplier : MonoBehaviour
    {
        private CoreMovement movement;
        private float baseMoveSpeed;
        private float baseSprintSpeed;
        private int appliedLevel = -1;

        public float AppliedMoveSpeed => movement != null ? movement.moveSpeed : 0f;
        public float AppliedSprintSpeed => movement != null ? movement.sprintSpeed : 0f;

        private void Awake()
        {
            movement = GetComponent<CoreMovement>();
            if (movement == null)
            {
                Debug.LogError("[ShopPlayerUpgradeApplier] CoreMovement가 없습니다.", this);
                enabled = false;
                return;
            }
            baseMoveSpeed = movement.moveSpeed;
            baseSprintSpeed = movement.sprintSpeed;
        }

        private void Update()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            int level = game != null ? game.PlayerUpgradeLevel.Value : 0;
            if (level == appliedLevel) return;
            appliedLevel = level;
            movement.moveSpeed = baseMoveSpeed * (game != null ? game.PlayerMoveSpeedMultiplier : 1f);
            movement.sprintSpeed = baseSprintSpeed * (game != null ? game.PlayerSprintSpeedMultiplier : 1f);
        }
    }
}
