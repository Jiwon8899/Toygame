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
                if (observed != null) observed.ItemContainers.OnListChanged -= Changed;
                observed = game;
                if (observed != null) observed.ItemContainers.OnListChanged += Changed;
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
            ShopProductVisualConfig config = Resources.Load<ShopProductVisualConfig>(
                "Products/ShopProductVisualConfig");
            float spacing = config != null ? config.ShelfSpacing : 0.42f;
            int columns = config != null ? config.ShelfColumns : 5;
            Vector3 offset = config != null ? config.ShelfOffset : new Vector3(0f, 0.78f, 0f);
            Vector3 rotation = config != null ? config.ShelfRotation : new Vector3(0f, 24f, 0f);
            Vector3 anchor = ShopNightSalesSystem.Instance.DisplayWorkPosition + offset;
            int index = 0;
            for (int i = 0; i < observed.ItemContainers.Count && index < 10; i++)
            {
                ShopContainerItem item = observed.ItemContainers[i];
                if (item.Container != ShopContainerKind.SharedDisplay || item.Quantity <= 0) continue;
                ShopProductDefinition product = ShopProductVisuals.Find(item.ProductId);
                GameObject visual = ShopProductVisuals.Instantiate(product, transform);
                if (visual == null) continue;
                int row = index / columns;
                int column = index % columns;
                visual.transform.position = anchor + new Vector3(
                    (column - (columns - 1) * 0.5f) * spacing, row * 0.34f, 0f);
                visual.transform.rotation = Quaternion.Euler(rotation + new Vector3(0f, index * 11f, 0f));
                visuals.Add(visual);
                index++;
            }
        }

        private void OnDestroy()
        {
            if (observed != null) observed.ItemContainers.OnListChanged -= Changed;
            if (instance == this) instance = null;
        }
    }
}
