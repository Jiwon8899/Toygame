using UnityEngine;

namespace PickAndPlaceShop
{
    public sealed class ShopShelfVisual : MonoBehaviour
    {
        [SerializeField] private GameObject[] itemVisuals;

        public void Configure(GameObject[] visuals)
        {
            itemVisuals = visuals;
        }

        private void Update()
        {
            int activeCount = ShopNetworkGame.Instance != null ? ShopNetworkGame.Instance.Displayed.Value : 0;
            if (itemVisuals == null)
            {
                return;
            }

            for (int i = 0; i < itemVisuals.Length; i++)
            {
                if (itemVisuals[i] != null)
                {
                    itemVisuals[i].SetActive(i < activeCount);
                }
            }
        }
    }
}
