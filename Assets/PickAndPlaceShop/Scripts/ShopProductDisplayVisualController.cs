using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(420)]
    public sealed class ShopProductDisplayVisualController : MonoBehaviour
    {
        private static ShopProductDisplayVisualController instance;
        private readonly List<GameObject> visuals = new();
        private ShopNetworkGame observed;
        private bool dirty = true;
        public static int ActiveVisualCount => instance != null ? instance.visuals.Count : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[Shop] Product Display Visuals");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopProductDisplayVisualController>();
        }

        private void Update()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (observed != game)
            {
                if (observed != null)
                {
                    observed.ItemContainers.OnListChanged -= Changed;
                    observed.CurationPlacements.OnListChanged -= PlacementChanged;
                }
                observed = game;
                if (observed != null)
                {
                    observed.ItemContainers.OnListChanged += Changed;
                    observed.CurationPlacements.OnListChanged += PlacementChanged;
                }
                dirty = true;
            }
            if (dirty && observed != null && ShopNightSalesSystem.Instance != null) Rebuild();
        }

        private void Changed(NetworkListEvent<ShopContainerItem> _) => dirty = true;
        private void PlacementChanged(NetworkListEvent<ShopCurationPlacement> _) => dirty = true;

        private void Rebuild()
        {
            dirty = false;
            foreach (GameObject visual in visuals) if (visual != null) Destroy(visual);
            visuals.Clear();
            if (observed != null && observed.CurationPlacements.Count > 0) return;
            ShopDisplayShelfAnchors provider = FindFirstObjectByType<ShopDisplayShelfAnchors>();
            if (provider == null)
            {
                ShopShelfVisual shelf = FindFirstObjectByType<ShopShelfVisual>();
                if (shelf != null) provider = shelf.gameObject.AddComponent<ShopDisplayShelfAnchors>();
            }
            if (provider == null) return;
            provider.EnsureAnchors();
            IReadOnlyList<ShopDisplaySlotAnchor> anchors = provider.Anchors;
            int index = 0;
            for (int i = 0; i < observed.ItemContainers.Count && index < anchors.Count; i++)
            {
                ShopContainerItem item = observed.ItemContainers[i];
                if (item.Container != ShopContainerKind.SharedDisplay || item.Quantity <= 0) continue;
                ShopProductDefinition product = ShopProductVisuals.Find(item.ProductId);
                for (int quantity = 0; quantity < item.Quantity && index < anchors.Count; quantity++)
                {
                    Transform anchor = anchors[index].transform;
                    GameObject visual = ShopProductVisuals.Instantiate(product, anchor);
                    if (visual == null) break;
                    visual.name = $"Displayed_{item.ProductId}_{index:00}";
                    visual.transform.SetLocalPositionAndRotation(Vector3.zero,
                        Quaternion.Euler(0f, (index * 37f) % 55f - 27f, 0f));
                    visuals.Add(visual);
                    index++;
                }
            }
        }

        private void OnDestroy()
        {
            if (observed != null)
            {
                observed.ItemContainers.OnListChanged -= Changed;
                observed.CurationPlacements.OnListChanged -= PlacementChanged;
            }
            if (instance == this) instance = null;
        }
    }
}
