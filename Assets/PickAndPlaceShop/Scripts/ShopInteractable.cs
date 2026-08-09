using UnityEngine;

namespace PickAndPlaceShop
{
    public sealed class ShopInteractable : MonoBehaviour
    {
        [SerializeField] private ShopAction action;
        [SerializeField] private string prompt = "Interact";

        private ShopClawMachineNetwork networkClaw;
        private ShopGachaMachineNetwork gachaMachine;
        private ShopKujiStationNetwork kujiStation;
        private ShopDistrictPortalNetwork districtPortal;
        private ShopUpgradeTerminal upgradeTerminal;
        private ShopTrashSearchPoint trashSearchPoint;
        private ShopRivalShelfInteractable rivalShelf;
        private Collider[] interactionColliders;
        private bool interactionCollidersCached;

        public ShopAction Action => action;
        public Vector3 InteractionWorldPosition
        {
            get
            {
                CacheHandlers();
                if (networkClaw != null) return networkClaw.OperatorWorldPosition;
                if (gachaMachine != null) return gachaMachine.InteractionWorldPosition;
                if (kujiStation != null) return kujiStation.InteractionWorldPosition;
                if (districtPortal != null) return districtPortal.InteractionWorldPosition;
                if (action == ShopAction.OnlineOrder)
                    return transform.position + transform.right * 1f - transform.forward * 1.2f;
                return transform.position;
            }
        }

        public string Prompt
        {
            get
            {
                CacheHandlers();
                if (networkClaw != null) return networkClaw.InteractionPrompt;
                if (gachaMachine != null) return gachaMachine.InteractionPrompt;
                if (kujiStation != null) return kujiStation.InteractionPrompt;
                if (districtPortal != null) return districtPortal.InteractionPrompt;
                if (upgradeTerminal != null) return upgradeTerminal.Prompt;
                if (action == ShopAction.UpgradeShop && ShopNetworkGame.Instance != null)
                    return ShopNetworkGame.Instance.ShopUpgradePrompt;
                return prompt;
            }
        }

        public Vector3 ClosestInteractionWorldPosition(Vector3 observerPosition)
        {
            CacheHandlers();
            if (networkClaw != null || gachaMachine != null || kujiStation != null ||
                districtPortal != null || action == ShopAction.OnlineOrder)
                return InteractionWorldPosition;

            CacheInteractionColliders();
            Vector3 closest = transform.position;
            float closestSqrDistance = float.MaxValue;
            for (int index = 0; index < interactionColliders.Length; index++)
            {
                Collider candidate = interactionColliders[index];
                if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy) continue;
                Vector3 point = candidate.ClosestPoint(observerPosition);
                float sqrDistance = (point - observerPosition).sqrMagnitude;
                if (sqrDistance >= closestSqrDistance) continue;
                closestSqrDistance = sqrDistance;
                closest = point;
            }

            return closest;
        }

        public void Configure(ShopAction configuredAction, string configuredPrompt)
        {
            action = configuredAction;
            prompt = configuredPrompt;
        }

        public void Interact()
        {
            CacheHandlers();
            if (networkClaw != null)
            {
                networkClaw.RequestUse();
                return;
            }
            if (gachaMachine != null)
            {
                gachaMachine.RequestUse();
                return;
            }
            if (kujiStation != null)
            {
                kujiStation.RequestUse();
                return;
            }
            if (districtPortal != null)
            {
                districtPortal.RequestUse();
                return;
            }
            if (upgradeTerminal != null)
            {
                upgradeTerminal.Interact();
                return;
            }
            if (trashSearchPoint != null)
            {
                trashSearchPoint.Interact();
                return;
            }
            if (rivalShelf != null)
            {
                rivalShelf.Interact();
                return;
            }
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null)
            {
                Debug.LogError("[ShopInteractable] ShopNetworkGame이 없어 상호작용을 처리할 수 없습니다.", this);
                return;
            }
            game.RequestInteraction(action);
        }

        private void CacheHandlers()
        {
            if (networkClaw == null) networkClaw = GetComponent<ShopClawMachineNetwork>();
            if (gachaMachine == null) gachaMachine = GetComponent<ShopGachaMachineNetwork>();
            if (kujiStation == null) kujiStation = GetComponent<ShopKujiStationNetwork>();
            if (districtPortal == null) districtPortal = GetComponent<ShopDistrictPortalNetwork>();
            if (upgradeTerminal == null) upgradeTerminal = GetComponent<ShopUpgradeTerminal>();
            if (trashSearchPoint == null) trashSearchPoint = GetComponentInParent<ShopTrashSearchPoint>();
            if (rivalShelf == null) rivalShelf = GetComponentInParent<ShopRivalShelfInteractable>();
        }

        private void CacheInteractionColliders()
        {
            if (interactionCollidersCached) return;
            interactionColliders = GetComponentsInChildren<Collider>(true);
            interactionCollidersCached = true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 1f);
        }
    }
}
