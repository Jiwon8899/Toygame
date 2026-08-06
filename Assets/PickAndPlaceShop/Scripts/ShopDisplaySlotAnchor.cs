using System;
using System.Collections.Generic;
using UnityEngine;

namespace PickAndPlaceShop
{
    [DisallowMultipleComponent]
    public sealed class ShopDisplaySlotAnchor : MonoBehaviour
    {
        [SerializeField] private int slotIndex;
        public int SlotIndex => slotIndex;

        public void Configure(int index) => slotIndex = Mathf.Max(0, index);
    }

    [DisallowMultipleComponent]
    public sealed class ShopDisplayShelfAnchors : MonoBehaviour
    {
        private const int AnchorsPerShelf = 3;
        private readonly List<ShopDisplaySlotAnchor> anchors = new();
        public IReadOnlyList<ShopDisplaySlotAnchor> Anchors => anchors;

        private void Awake() => EnsureAnchors();

        public void EnsureAnchors()
        {
            anchors.Clear();
            anchors.AddRange(GetComponentsInChildren<ShopDisplaySlotAnchor>(true));
            if (anchors.Count == 0) RestoreLegacyDisplayPrizeAnchors();
            if (anchors.Count == 0) BuildFromShelfSurfaces();
            anchors.Sort((left, right) => left.SlotIndex.CompareTo(right.SlotIndex));
            ShopShelfVisual legacy = GetComponent<ShopShelfVisual>();
            if (legacy != null) legacy.UseProductVisuals();
        }

        private void RestoreLegacyDisplayPrizeAnchors()
        {
            Transform[] sceneTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int index = 0; index < 8; index++)
            {
                string expectedName = $"DisplayPrize_{index}";
                Transform legacy = Array.Find(sceneTransforms, candidate =>
                    candidate != null && candidate.name == expectedName &&
                    candidate.gameObject.scene.IsValid() && candidate.gameObject.scene.isLoaded);
                if (legacy == null) continue;

                foreach (Renderer renderer in legacy.GetComponents<Renderer>()) renderer.enabled = false;
                foreach (Collider collider in legacy.GetComponents<Collider>()) collider.enabled = false;
                legacy.localScale = Vector3.one;
                legacy.gameObject.SetActive(true);
                ShopDisplaySlotAnchor anchor = legacy.GetComponent<ShopDisplaySlotAnchor>();
                if (anchor == null) anchor = legacy.gameObject.AddComponent<ShopDisplaySlotAnchor>();
                anchor.Configure(index);
                anchors.Add(anchor);
            }
        }

        private void BuildFromShelfSurfaces()
        {
            var shelves = new List<Transform>();
            foreach (Transform child in transform)
                if (child != null && child.name.StartsWith("Shelf_", StringComparison.Ordinal))
                    shelves.Add(child);
            shelves.Sort((left, right) =>
            {
                int y = left.localPosition.y.CompareTo(right.localPosition.y);
                return y != 0 ? y : left.localPosition.x.CompareTo(right.localPosition.x);
            });

            int slot = 0;
            foreach (Transform shelf in shelves)
            {
                float width = Mathf.Max(0.4f, Mathf.Abs(shelf.localScale.x));
                for (int column = 0; column < AnchorsPerShelf; column++)
                {
                    GameObject host = new($"ProductSlotAnchor_{slot:00}");
                    host.transform.SetParent(transform, false);
                    float x = Mathf.Lerp(-width * 0.34f, width * 0.34f,
                        column / (float)(AnchorsPerShelf - 1));
                    host.transform.localPosition = shelf.localPosition +
                                                   new Vector3(x, Mathf.Abs(shelf.localScale.y) * 0.5f + 0.17f, -0.08f);
                    ShopDisplaySlotAnchor anchor = host.AddComponent<ShopDisplaySlotAnchor>();
                    anchor.Configure(slot++);
                    anchors.Add(anchor);
                }
            }
        }
    }
}
