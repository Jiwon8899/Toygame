using System.Collections.Generic;
using UnityEngine;

namespace PickAndPlaceShop
{
    public static class ShopProductVisuals
    {
        private static Dictionary<int, ShopProductDefinition> byId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => byId = null;

        public static ShopProductDefinition Find(int productId)
        {
            EnsureCatalog();
            return byId.TryGetValue(productId, out ShopProductDefinition product) ? product : null;
        }

        public static ShopProductDefinition FindByName(string displayName)
        {
            EnsureCatalog();
            foreach (ShopProductDefinition product in byId.Values)
                if (product != null && product.DisplayName == displayName) return product;
            return null;
        }

        public static Sprite FindIcon(int productId)
        {
            ShopProductDefinition product = Find(productId);
            if (product != null && product.Icon != null) return product.Icon;
            return Resources.Load<Sprite>($"ProductIcons/Generated/ProductIcon_{productId:D4}");
        }

        public static GameObject Instantiate(ShopProductDefinition product, Transform parent)
        {
            if (product == null) return null;
            // Legacy catalog entries already have their real prize prefab but predate
            // the dedicated display-visual field. Reuse that canonical model before
            // falling back to a placeholder so every acquisition route looks alike.
            GameObject source = product.VisualPrefab != null ? product.VisualPrefab : product.PrizePrefab;
            GameObject visual = source != null
                ? Object.Instantiate(source, parent)
                : CreateFallbackVisual(product, parent);
            DisablePhysics(visual);
            ApplyTint(visual, product.Tint);
            return visual;
        }

        private static GameObject CreateFallbackVisual(ShopProductDefinition product, Transform parent)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = $"Fallback Product {product.ProductId}";
            visual.transform.SetParent(parent, false);
            visual.transform.localScale = new Vector3(0.22f, 0.18f, 0.22f);
            return visual;
        }

        public static void ApplyTint(GameObject root, Color tint)
        {
            if (root == null) return;
            MaterialPropertyBlock block = new();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                renderer.SetPropertyBlock(block);
            }
        }

        public static void DisablePhysics(GameObject root)
        {
            if (root == null) return;
            foreach (Collider item in root.GetComponentsInChildren<Collider>(true))
                if (Application.isPlaying) Object.Destroy(item);
                else Object.DestroyImmediate(item);
            foreach (Rigidbody item in root.GetComponentsInChildren<Rigidbody>(true))
                if (Application.isPlaying) Object.Destroy(item);
                else Object.DestroyImmediate(item);
        }

        private static void EnsureCatalog()
        {
            if (byId != null) return;
            byId = new Dictionary<int, ShopProductDefinition>();
            foreach (ShopProductDefinition product in Resources.LoadAll<ShopProductDefinition>("Products"))
                if (product != null) byId[product.ProductId] = product;
        }
    }
}
