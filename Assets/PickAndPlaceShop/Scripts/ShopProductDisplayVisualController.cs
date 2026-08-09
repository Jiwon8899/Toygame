using System.Collections.Generic;
using System.Linq;
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
        public static void RequestRefresh()
        {
            if (instance != null) instance.dirty = true;
        }

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
                }
                observed = game;
                if (observed != null)
                {
                    observed.ItemContainers.OnListChanged += Changed;
                }
                dirty = true;
            }
            if (dirty && observed != null && ShopNightSalesSystem.Instance != null) Rebuild();
        }

        private void Changed(NetworkListEvent<ShopContainerItem> _) => dirty = true;

        private void Rebuild()
        {
            dirty = false;
            foreach (GameObject visual in visuals) if (visual != null) Destroy(visual);
            visuals.Clear();
            EnsureMainShelfProvider();
            ShopDisplayShelfAnchors[] providers = FindObjectsByType<ShopDisplayShelfAnchors>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (providers.Length == 0)
            {
                ShopShelfVisual shelf = FindFirstObjectByType<ShopShelfVisual>();
                if (shelf != null) providers = new[] { shelf.gameObject.AddComponent<ShopDisplayShelfAnchors>() };
            }
            if (providers.Length == 0) return;
            List<ShopDisplaySlotAnchor> anchors = new();
            foreach (ShopDisplayShelfAnchors provider in providers)
            {
                if (provider == null) continue;
                provider.EnsureAnchors();
                anchors.AddRange(provider.Anchors);
            }
            Dictionary<int, ShopDisplaySlotAnchor> anchorsBySlot = anchors
                .Where(anchor => anchor != null && anchor.gameObject.activeInHierarchy)
                .GroupBy(anchor => anchor.SlotIndex)
                .ToDictionary(group => group.Key, group => group.First());
            for (int i = 0; i < observed.ItemContainers.Count; i++)
            {
                ShopContainerItem item = observed.ItemContainers[i];
                if (item.Container != ShopContainerKind.SharedDisplay || item.Quantity <= 0) continue;
                if (!anchorsBySlot.TryGetValue(item.SlotIndex, out ShopDisplaySlotAnchor slotAnchor))
                {
                    Debug.LogWarning("[DisplayVisual] 진열 슬롯 앵커를 찾지 못했습니다. slot=" +
                                     item.SlotIndex, this);
                    continue;
                }
                ShopProductDefinition product = ShopProductVisuals.Find(item.ProductId);
                GameObject visual = ShopProductVisuals.Instantiate(product, slotAnchor.transform);
                if (visual == null) continue;
                visual.name = $"Displayed_{item.ProductId}_{item.SlotIndex:00}";
                visual.transform.SetLocalPositionAndRotation(Vector3.zero,
                    Quaternion.Euler(0f, (item.SlotIndex * 37f) % 55f - 27f, 0f));
                visuals.Add(visual);
            }
        }

        private static void EnsureMainShelfProvider()
        {
            Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null || candidate.name != "Shared Display Shelves" ||
                    !candidate.gameObject.scene.IsValid()) continue;
                ShopDisplayShelfAnchors provider = candidate.GetComponent<ShopDisplayShelfAnchors>();
                if (provider == null) provider = candidate.gameObject.AddComponent<ShopDisplayShelfAnchors>();
                provider.Configure(0, 1, true);
                provider.EnsureAnchors();
                return;
            }
        }

        private void OnDestroy()
        {
            if (observed != null)
            {
                observed.ItemContainers.OnListChanged -= Changed;
            }
            if (instance == this) instance = null;
        }
    }
}
