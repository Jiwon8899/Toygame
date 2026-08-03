using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(370)]
    public sealed class ShopExpansionVisualController : MonoBehaviour
    {
        private static ShopExpansionVisualController instance;
        private readonly List<GameObject> generated = new();
        private int appliedLevel;
        private float nextPoll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[Progression] Store Expansion Visuals");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopExpansionVisualController>();
        }

        private void Awake() => SceneManager.sceneLoaded += HandleSceneLoaded;
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (instance == this) instance = null;
        }

        private void HandleSceneLoaded(Scene _, LoadSceneMode __)
        {
            appliedLevel = 0;
            ClearGenerated();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.5f;
            int level = ShopProgressionManager.Instance?.ExpansionLevel ?? 1;
            if (ShopNightSalesSystem.Instance == null)
            {
                if (generated.Count > 0) ClearGenerated();
                appliedLevel = 0;
                return;
            }
            if (level == appliedLevel) return;
            appliedLevel = level;
            Rebuild(level);
        }

        private void Rebuild(int level)
        {
            ClearGenerated();
            Vector3 origin = ShopNightSalesSystem.Instance.DisplayWorkPosition;
            for (int tier = 2; tier <= Mathf.Min(4, level); tier++)
                CreateShelf(origin + new Vector3((tier - 3) * 2.1f, 0f, 2.1f), tier);
            if (level >= 5) CreateRoomExtension(origin, 1);
            if (level >= 6) CreateRoomExtension(origin + Vector3.right * 5f, 2);
        }

        private void CreateShelf(Vector3 position, int tier)
        {
            GameObject root = new("ExpandedDisplayShelf_L" + tier);
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 1f, -0.35f);
            trigger.size = new Vector3(2f, 2f, 1.5f);
            root.AddComponent<ShopInteractable>().Configure(ShopAction.DisplayShelf,
                "[E] 공용 창고 상품 진열");
            Color wood = new(0.38f, 0.18f, 0.09f);
            CreatePart(root.transform, "SideL", new Vector3(-0.9f, 1f, 0f), new Vector3(0.16f, 2f, 0.65f), wood, false);
            CreatePart(root.transform, "SideR", new Vector3(0.9f, 1f, 0f), new Vector3(0.16f, 2f, 0.65f), wood, false);
            for (int i = 0; i < 3; i++)
                CreatePart(root.transform, "Shelf" + i, new Vector3(0f, 0.25f + i * 0.72f, 0f),
                    new Vector3(1.95f, 0.12f, 0.72f), new Color(0.82f, 0.55f, 0.28f), false);
            generated.Add(root);
        }

        private void CreateRoomExtension(Vector3 origin, int index)
        {
            GameObject root = new("StoreRoomExtension_" + index);
            root.transform.SetParent(transform, false);
            root.transform.position = origin + new Vector3(index == 1 ? -2.5f : 2.5f, -0.08f, 4.8f);
            Color floor = new(0.55f, 0.36f, 0.23f);
            Color wall = new(0.19f, 0.12f, 0.18f);
            CreatePart(root.transform, "Floor", Vector3.zero, new Vector3(5f, 0.16f, 4.5f), floor, true);
            CreatePart(root.transform, "BackWall", new Vector3(0f, 1.6f, 2.2f), new Vector3(5f, 3.2f, 0.18f), wall, true);
            CreatePart(root.transform, "SideWall", new Vector3(index == 1 ? -2.4f : 2.4f, 1.6f, 0f),
                new Vector3(0.18f, 3.2f, 4.5f), wall, true);
            generated.Add(root);
        }

        private static void CreatePart(Transform parent, string name, Vector3 localPosition,
            Vector3 scale, Color color, bool keepCollider)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().material.color = color;
            if (!keepCollider)
            {
                Collider collider = part.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
            }
            part.isStatic = true;
        }

        private void ClearGenerated()
        {
            for (int i = 0; i < generated.Count; i++) if (generated[i] != null) Destroy(generated[i]);
            generated.Clear();
        }
    }
}
