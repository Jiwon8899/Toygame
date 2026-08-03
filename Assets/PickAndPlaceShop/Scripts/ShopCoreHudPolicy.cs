using Blocks.Gameplay.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace PickAndPlaceShop
{
    /// <summary>
    /// Keeps the shared gameplay-kit HUD appropriate for a non-combat shop game.
    /// Health and stamina have no player-facing meaning in the shop HUD.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class ShopCoreHudPolicy : MonoBehaviour
    {
        private CoreHUD boundHud;
        private float nextLookup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            GameObject host = new("[Shop UI] Core HUD Policy");
            DontDestroyOnLoad(host);
            host.AddComponent<ShopCoreHudPolicy>();
        }

        private void Update()
        {
            if (boundHud == null && Time.unscaledTime >= nextLookup)
            {
                nextLookup = Time.unscaledTime + 0.5f;
                BindLocalHud();
            }
        }

        private void BindLocalHud()
        {
            CoreHUD[] huds = FindObjectsByType<CoreHUD>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < huds.Length; i++)
            {
                UIDocument document = huds[i].GetComponent<UIDocument>();
                if (document == null || document.rootVisualElement == null) continue;
                VisualElement root = document.rootVisualElement;
                ProgressBar health = root.Q<ProgressBar>("player-health-bar");
                ProgressBar stamina = root.Q<ProgressBar>("player-stamina-bar");
                if (health == null && stamina == null) continue;

                health?.RemoveFromHierarchy();
                stamina?.RemoveFromHierarchy();

                VisualElement container = root.Q<VisualElement>("health-info-container");
                if (container != null && container.childCount == 0)
                    container.RemoveFromHierarchy();
                boundHud = huds[i];
                return;
            }
        }
    }
}
