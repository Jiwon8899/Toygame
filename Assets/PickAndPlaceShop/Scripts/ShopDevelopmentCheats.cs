#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(900)]
    public sealed class ShopDevelopmentCheats : MonoBehaviour
    {
        private const int FundsIncrease = 1000;
        private const int ReputationIncrease = 10;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            GameObject host = new("[Development] Shop Cheats");
            DontDestroyOnLoad(host);
            host.AddComponent<ShopDevelopmentCheats>();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (keyboard == null || game == null || !game.IsServer || IsUiFocused()) return;

            if (keyboard.digit5Key.wasPressedThisFrame)
            {
                game.Coins.Value += FundsIncrease;
                game.ServerSetEvent("개발 치트: 공동 자금 +" + FundsIncrease + "원");
            }
            else if (keyboard.digit6Key.wasPressedThisFrame)
            {
                game.Reputation.Value += ReputationIncrease;
                game.ServerSetEvent("개발 치트: 평판 +" + ReputationIncrease);
            }
            else if (keyboard.digit7Key.wasPressedThisFrame)
            {
                ShopPhase next = game.Phase.Value switch
                {
                    ShopPhase.PrizeHunt => ShopPhase.Setup,
                    ShopPhase.Setup => ShopPhase.Open,
                    ShopPhase.Open => ShopPhase.Summary,
                    _ => ShopPhase.PrizeHunt
                };
                game.ServerSetPhase(next);
                game.ServerSetEvent("개발 치트: 시간대 전환 → " + next);
            }
            else if (keyboard.digit8Key.wasPressedThisFrame)
            {
                FillPersonalInventory(game);
            }
        }

        private static bool IsUiFocused()
        {
            return ShopInputModeManager.CurrentMode != ShopInputMode.Gameplay ||
                   EventSystem.current != null &&
                   EventSystem.current.currentSelectedGameObject != null;
        }

        private static void FillPersonalInventory(ShopNetworkGame game)
        {
            ulong owner = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClientId
                : 0;
            ShopProductDefinition[] products = Resources.LoadAll<ShopProductDefinition>("Products");
            if (products.Length == 0)
            {
                game.ServerSetEvent("개발 치트: 채울 상품 데이터가 없습니다.");
                return;
            }

            int added = 0;
            while (!game.GetContainerSnapshot(owner, ShopContainerKind.PersonalInventory).IsFull)
            {
                ShopProductDefinition product = products[added % products.Length];
                int visualIndex = product != null
                    ? ShopClawPrizeNetwork.FindCatalogIndex(product.PrizePrefab)
                    : -1;
                if (!game.ServerTryAcquireItem(owner, product, visualIndex, out _)) break;
                added++;
            }
            game.ServerSetEvent("개발 치트: 개인 인벤토리 " + added + "개 채움");
        }
    }
}
#endif
