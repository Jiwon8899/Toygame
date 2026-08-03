using UnityEngine;

namespace PickAndPlaceShop
{
    public sealed class ShopUpgradeTerminal : MonoBehaviour
    {
        public string Prompt => ShopNetworkGame.Instance != null
            ? ShopNetworkGame.Instance.ShopUpgradePrompt
            : "업그레이드 내역 열기";

        public void Interact()
        {
            if (ShopNetworkGame.Instance == null)
            {
                Debug.LogError("[ShopUpgradeTerminal] 상점 네트워크 상태가 준비되지 않았습니다.", this);
                return;
            }
            ShopUpgradeUI.Open();
        }
    }
}
