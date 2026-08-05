using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(450)]
    public sealed class ShopDifferentiationController : MonoBehaviour
    {
        public static ShopDifferentiationController Instance { get; private set; }

        private ShopDifferentiationConfig config;
        private GameObject facilitiesRoot;
        private readonly List<GameObject> upcycleDecorations = new();

        public int EmptyCapsuleCount
        {
            get
            {
                ShopNetworkGame game = ShopNetworkGame.Instance;
                ShopProductDefinition empty = config != null ? config.EmptyCapsuleProduct : null;
                if (game == null || empty == null) return 0;
                int total = 0;
                for (int i = 0; i < game.ItemContainers.Count; i++)
                {
                    ShopContainerItem item = game.ItemContainers[i];
                    if (item.Container == ShopContainerKind.CapsuleRecycler && item.ProductId == empty.ProductId)
                        total += Mathf.Max(0, item.Quantity);
                }
                return total;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject host = new("[Shop] Differentiation Systems");
            DontDestroyOnLoad(host);
            Instance = host.AddComponent<ShopDifferentiationController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            config = ShopDifferentiationConfig.Load();
        }

        private void Update()
        {
            if (config == null) config = ShopDifferentiationConfig.Load();
            if (config == null || ShopNetworkGame.Instance == null) return;
            if (facilitiesRoot == null) BuildFacilities();
            RefreshUpcycleDecorations();
        }

        public bool ServerCollectEmptyCapsule()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsServer || config == null || config.EmptyCapsuleProduct == null)
                return false;
            bool stored = game.ServerTryAcquireSharedContainer(config.EmptyCapsuleProduct, -1,
                ShopContainerKind.CapsuleRecycler, config.CapsuleRecyclerSlots);
            if (!stored)
                game.ServerSetEvent("빈 캡슐 회수함이 가득 찼습니다. 이번 캡슐 껍데기는 안전하게 폐기했습니다.");
            return stored;
        }

        public void ServerHandleInteraction(ShopAction action, ulong requester)
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsServer) return;
            if (action == ShopAction.CapsuleRecycler) ServerCraftNextDecoration(game);
        }

        private void ServerCraftNextDecoration(ShopNetworkGame game)
        {
            int[] thresholds = config.UpcycleThresholds;
            int mask = game.UpcycleDecorMask.Value;
            for (int index = 0; index < thresholds.Length; index++)
            {
                if ((mask & (1 << index)) != 0) continue;
                int required = Mathf.Max(1, thresholds[index]);
                int productId = config.EmptyCapsuleProduct != null ? config.EmptyCapsuleProduct.ProductId : int.MinValue;
                if (!game.ServerTryConsumeContainerProductFrom(ShopContainerKind.CapsuleRecycler,
                        productId, required))
                {
                    game.ServerSetEvent("빈 캡슐 " + EmptyCapsuleCount + "개 보관 중 · 다음 업사이클 장식은 " +
                                        required + "개가 필요합니다.");
                    return;
                }
                game.UpcycleDecorMask.Value = mask | (1 << index);
                game.ServerSetEvent("빈 캡슐 " + required + "개로 매장 장식 " + (index + 1) + "을 제작했습니다!");
                ShopProgressionManager.Instance?.SaveNow();
                return;
            }
            game.ServerSetEvent("모든 빈 캡슐 업사이클 장식을 제작했습니다. 현재 보관량 " + EmptyCapsuleCount + "개");
        }

        private void BuildFacilities()
        {
            facilitiesRoot = new GameObject("Differentiation Facilities");
            BuildFacility("빈 캡슐 회수함", config.CapsuleRecyclerPosition,
                new Color(0.12f, 0.62f, 0.58f), ShopAction.CapsuleRecycler,
                "빈 캡슐 회수함 / 업사이클 장식 제작");
        }

        private void BuildFacility(string objectName, Vector3 position, Color color,
            ShopAction action, string prompt)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = objectName;
            root.transform.SetParent(facilitiesRoot.transform, false);
            root.transform.position = position + Vector3.up * 0.65f;
            root.transform.localScale = new Vector3(1.35f, 1.3f, 0.75f);
            Renderer renderer = root.GetComponent<Renderer>();
            renderer.material.color = color;
            root.AddComponent<ShopInteractable>().Configure(action, prompt);

            GameObject labelHost = new(objectName + "_Label");
            labelHost.transform.SetParent(root.transform, false);
            labelHost.transform.localPosition = new Vector3(0f, 0.8f, -0.52f);
            labelHost.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            TextMesh label = labelHost.AddComponent<TextMesh>();
            label.text = objectName;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.12f;
            label.fontSize = 48;
            label.color = Color.white;
        }

        private void RefreshUpcycleDecorations()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || facilitiesRoot == null) return;
            int mask = game.UpcycleDecorMask.Value;
            int count = config.UpcycleThresholds.Length;
            while (upcycleDecorations.Count < count)
            {
                int index = upcycleDecorations.Count;
                GameObject decor = GameObject.CreatePrimitive(index == 0 ? PrimitiveType.Quad :
                    index == 1 ? PrimitiveType.Sphere : PrimitiveType.Cube);
                decor.name = "UpcycleDecor_" + index;
                decor.transform.SetParent(facilitiesRoot.transform, false);
                decor.transform.position = index switch
                {
                    0 => new Vector3(4.2f, 2.2f, 7.6f),
                    1 => new Vector3(10.8f, 3.2f, 3.8f),
                    _ => new Vector3(1.4f, 2.1f, 1.8f)
                };
                decor.transform.localScale = index == 0 ? new Vector3(1.8f, 1.1f, 1f) : Vector3.one * 0.55f;
                Collider collider = decor.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                Renderer renderer = decor.GetComponent<Renderer>();
                renderer.material.color = index == 0 ? new Color(0.85f, 0.38f, 0.58f) :
                    index == 1 ? new Color(1f, 0.75f, 0.25f) : new Color(0.45f, 0.8f, 1f);
                upcycleDecorations.Add(decor);
            }
            for (int i = 0; i < upcycleDecorations.Count; i++)
                upcycleDecorations[i].SetActive((mask & (1 << i)) != 0);
        }
    }
}
