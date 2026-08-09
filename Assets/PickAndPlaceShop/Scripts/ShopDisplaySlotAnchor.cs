using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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
        [SerializeField, Min(0)] private int firstSlotIndex;
        [SerializeField, Min(1)] private int anchorsPerShelf = 1;
        [SerializeField] private bool addBaseOverflowAnchor = true;
        private readonly List<ShopDisplaySlotAnchor> anchors = new();
        public IReadOnlyList<ShopDisplaySlotAnchor> Anchors => anchors;

        private void Awake() => EnsureAnchors();

        public void Configure(int firstSlot, int perShelf, bool addOverflow)
        {
            firstSlotIndex = Mathf.Max(0, firstSlot);
            anchorsPerShelf = Mathf.Max(1, perShelf);
            addBaseOverflowAnchor = addOverflow;
        }

        public void EnsureAnchors()
        {
            anchors.Clear();
            // DisplayPrize objects are the old placeholder meshes. ShopShelfVisual
            // disables those objects, so using them as anchors also disables every
            // real product instantiated below them. Only independent anchors are
            // reusable; otherwise rebuild anchors from the actual shelf surfaces.
            foreach (ShopDisplaySlotAnchor anchor in GetComponentsInChildren<ShopDisplaySlotAnchor>(true))
                if (anchor != null && !anchor.name.StartsWith("DisplayPrize_", StringComparison.Ordinal))
                    anchors.Add(anchor);
            if (anchors.Count == 0) BuildFromShelfSurfaces();
            if (anchors.Count == 0) RestoreLegacyDisplayPrizeAnchors();
            anchors.Sort((left, right) => left.SlotIndex.CompareTo(right.SlotIndex));
            ShopShelfVisual legacy = GetComponent<ShopShelfVisual>();
            if (legacy != null) legacy.UseProductVisuals();
            EnsureStructureBlocking();
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
                GameObject host = new($"ProductSlotAnchor_{index:00}");
                host.transform.SetParent(transform, true);
                host.transform.SetPositionAndRotation(legacy.position, legacy.rotation);
                host.transform.localScale = Vector3.one;
                ShopDisplaySlotAnchor anchor = host.AddComponent<ShopDisplaySlotAnchor>();
                anchor.Configure(index);
                anchors.Add(anchor);
            }
        }

        private void BuildFromShelfSurfaces()
        {
            var shelves = new List<Transform>();
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
                if (child != transform && IsShelfSurface(child.name))
                    shelves.Add(child);
            shelves.Sort((left, right) =>
            {
                int y = left.localPosition.y.CompareTo(right.localPosition.y);
                return y != 0 ? y : left.localPosition.x.CompareTo(right.localPosition.x);
            });

            int slot = firstSlotIndex;
            foreach (Transform shelf in shelves)
            {
                float width = Mathf.Max(0.4f, Mathf.Abs(shelf.lossyScale.x));
                for (int column = 0; column < anchorsPerShelf; column++)
                {
                    GameObject host = new($"ProductSlotAnchor_{slot:00}");
                    host.transform.SetParent(transform, true);
                    float x = anchorsPerShelf == 1 ? 0f : Mathf.Lerp(-width * 0.28f, width * 0.28f,
                        column / (float)(anchorsPerShelf - 1));
                    host.transform.SetPositionAndRotation(
                        shelf.position + shelf.right * x + shelf.up *
                        (Mathf.Abs(shelf.lossyScale.y) * 0.5f + 0.17f) - shelf.forward * 0.08f,
                        shelf.rotation);
                    host.transform.localScale = Vector3.one;
                    ShopDisplaySlotAnchor anchor = host.AddComponent<ShopDisplaySlotAnchor>();
                    anchor.Configure(slot++);
                    anchors.Add(anchor);
                }
            }
            if (addBaseOverflowAnchor && shelves.Count > 0)
            {
                Transform shelf = shelves[shelves.Count / 2];
                GameObject host = new($"ProductSlotAnchor_{slot:00}");
                host.transform.SetParent(transform, true);
                host.transform.SetPositionAndRotation(shelf.position + shelf.right * 0.48f + shelf.up *
                    (Mathf.Abs(shelf.lossyScale.y) * 0.5f + 0.17f) - shelf.forward * 0.08f, shelf.rotation);
                host.transform.localScale = Vector3.one;
                ShopDisplaySlotAnchor anchor = host.AddComponent<ShopDisplaySlotAnchor>();
                anchor.Configure(slot);
                anchors.Add(anchor);
            }
        }

        private static bool IsShelfSurface(string objectName) =>
            objectName.StartsWith("Shelf_", StringComparison.Ordinal) ||
            objectName == "Shelf0" || objectName == "Shelf1" || objectName == "Shelf2";

        private void EnsureStructureBlocking()
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child == transform || !IsShelfSurface(child.name)) continue;
                BoxCollider collider = child.GetComponent<BoxCollider>();
                if (collider == null) collider = child.gameObject.AddComponent<BoxCollider>();
                collider.isTrigger = false;
                NavMeshObstacle obstacle = child.GetComponent<NavMeshObstacle>();
                if (obstacle == null) obstacle = child.gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
            }
        }
    }
}
