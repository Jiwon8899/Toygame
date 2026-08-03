using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(200)]
    public sealed class ShopClawInventoryUI : MonoBehaviour
    {
        private const int PreviewLayer = 31;
        private static ShopClawInventoryUI instance;

        private readonly List<RawImage> previewImages = new();
        private readonly List<Text> slotLabels = new();
        private readonly List<RenderTexture> renderTextures = new();
        private readonly List<GameObject> previewObjects = new();

        private Canvas canvas;
        private GameObject panel;
        private Text countText;
        private Text storageText;
        private Text displayText;
        private Font uiFont;
        private ShopNetworkGame observedGame;
        private bool isOpen;
        private bool dirty = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject root = new("ClawInventoryUI");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<ShopClawInventoryUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 32);
            BuildUi();
            SetOpen(false);
        }

        private void OnDestroy()
        {
            DetachGame();
            ClearPreviews();
            if (instance == this) instance = null;
        }

        private void Update()
        {
            AttachGameIfNeeded();
            if (!ShopLocalPauseState.IsPaused && Keyboard.current != null &&
                Keyboard.current.iKey.wasPressedThisFrame &&
                ShopNetworkGame.Instance != null)
            {
                SetOpen(!isOpen);
            }

            if (isOpen && dirty)
            {
                dirty = false;
                RefreshSlots();
            }
        }

        private void AttachGameIfNeeded()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (observedGame == game) return;
            DetachGame();
            observedGame = game;
            if (observedGame != null)
                observedGame.ItemContainers.OnListChanged += OnContainerChanged;
            dirty = true;
            if (observedGame == null && isOpen) SetOpen(false);
        }

        private void DetachGame()
        {
            if (observedGame != null)
                observedGame.ItemContainers.OnListChanged -= OnContainerChanged;
            observedGame = null;
        }

        private void OnContainerChanged(NetworkListEvent<ShopContainerItem> changeEvent)
        {
            dirty = true;
        }

        private void SetOpen(bool open)
        {
            isOpen = open;
            if (panel != null) panel.SetActive(open);
            if (open) dirty = true;
            else ClearPreviews();
        }

        private void BuildUi()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            panel = CreatePanel("상품 컨테이너", canvasObject.transform,
                new Vector2(1500f, 680f), new Color(0.025f, 0.035f, 0.065f, 0.96f));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;

            Text title = CreateText("Title", panel.transform, "상품 관리 · 개인 인벤토리 / 공용 창고 / 공용 진열",
                42, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.95f, 0.83f, 0.35f));
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            SetRect(title.rectTransform, new Vector2(42f, -30f), new Vector2(650f, 58f),
                new Vector2(0f, 1f), new Vector2(0f, 1f));

            countText = CreateText("Count", panel.transform, "개인 0 / 10",
                30, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetRect(countText.rectTransform, new Vector2(42f, -88f), new Vector2(600f, 48f),
                new Vector2(0f, 1f), new Vector2(0f, 1f));

            Text help = CreateText("Help", panel.transform, "I 닫기 · 진열대에서 E: 개인 인벤토리 우선, 없으면 공용 창고에서 진열",
                22, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.67f, 0.76f, 0.88f));
            SetRect(help.rectTransform, new Vector2(44f, 24f), new Vector2(850f, 40f),
                new Vector2(0f, 0f), new Vector2(0f, 0f));

            for (int i = 0; i < ShopNetworkGame.MaxClawInventorySlots; i++)
            {
                int row = i / 5;
                int column = i % 5;
                GameObject slot = CreatePanel("Slot_" + (i + 1), panel.transform,
                    new Vector2(178f, 204f), new Color(0.08f, 0.105f, 0.16f, 0.98f));
                RectTransform slotRect = slot.GetComponent<RectTransform>();
                slotRect.anchorMin = slotRect.anchorMax = new Vector2(0f, 1f);
                slotRect.pivot = new Vector2(0f, 1f);
                slotRect.anchoredPosition = new Vector2(42f + column * 197f, -142f - row * 222f);

                RawImage preview = new GameObject("Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage))
                    .GetComponent<RawImage>();
                preview.transform.SetParent(slot.transform, false);
                preview.color = Color.white;
                SetRect(preview.rectTransform, new Vector2(9f, -8f), new Vector2(160f, 150f),
                    new Vector2(0f, 1f), new Vector2(0f, 1f));
                previewImages.Add(preview);

                Text label = CreateText("Label", slot.transform, "빈 칸",
                    19, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.76f, 0.8f, 0.88f));
                SetRect(label.rectTransform, new Vector2(7f, 7f), new Vector2(164f, 42f),
                    new Vector2(0f, 0f), new Vector2(0f, 0f));
                slotLabels.Add(label);
            }

            GameObject storagePanel = CreatePanel("SharedStorage", panel.transform,
                new Vector2(410f, 250f), new Color(0.06f, 0.085f, 0.13f, 0.98f));
            SetRect(storagePanel.GetComponent<RectTransform>(), new Vector2(-42f, -106f),
                new Vector2(410f, 250f), Vector2.one, Vector2.one);
            storageText = CreateText("StorageText", storagePanel.transform, "공용 창고 0 / 0",
                22, FontStyle.Bold, TextAnchor.UpperLeft, new Color(0.58f, 0.88f, 1f));
            SetRect(storageText.rectTransform, new Vector2(18f, -14f), new Vector2(374f, 220f),
                new Vector2(0f, 1f), new Vector2(0f, 1f));

            GameObject displayPanel = CreatePanel("SharedDisplay", panel.transform,
                new Vector2(410f, 250f), new Color(0.08f, 0.105f, 0.12f, 0.98f));
            SetRect(displayPanel.GetComponent<RectTransform>(), new Vector2(-42f, -382f),
                new Vector2(410f, 250f), Vector2.one, Vector2.one);
            displayText = CreateText("DisplayText", displayPanel.transform, "공용 진열 0 / 0",
                22, FontStyle.Bold, TextAnchor.UpperLeft, new Color(1f, 0.82f, 0.42f));
            SetRect(displayText.rectTransform, new Vector2(18f, -14f), new Vector2(374f, 220f),
                new Vector2(0f, 1f), new Vector2(0f, 1f));
        }

        private void RefreshSlots()
        {
            ClearPreviews();
            ulong localOwner = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
                ? NetworkManager.Singleton.LocalClientId
                : 0;
            var personal = new List<ShopContainerItem>();
            var storage = new List<ShopContainerItem>();
            var display = new List<ShopContainerItem>();
            if (observedGame != null)
            {
                for (int i = 0; i < observedGame.ItemContainers.Count; i++)
                {
                    ShopContainerItem item = observedGame.ItemContainers[i];
                    if (item.Container == ShopContainerKind.PersonalInventory &&
                        item.OwnerClientId == localOwner) personal.Add(item);
                    else if (item.Container == ShopContainerKind.SharedStorage) storage.Add(item);
                    else if (item.Container == ShopContainerKind.SharedDisplay) display.Add(item);
                }
            }
            int count = 0;
            foreach (ShopContainerItem item in personal) count += item.Quantity;
            countText.text = "개인 인벤토리 " + count + " / " + ShopContainerRules.PersonalCapacity;
            storageText.text = BuildContainerText("공용 창고", storage,
                observedGame != null ? observedGame.SharedStorageCapacity : 0);
            displayText.text = BuildContainerText("공용 진열", display,
                observedGame != null ? observedGame.SharedDisplayCapacity : 0);

            for (int i = 0; i < ShopNetworkGame.MaxClawInventorySlots; i++)
            {
                ShopContainerItem? slotItem = null;
                foreach (ShopContainerItem item in personal)
                    if (item.SlotIndex == i)
                    {
                        slotItem = item;
                        break;
                    }
                if (!slotItem.HasValue)
                {
                    previewImages[i].texture = null;
                    previewImages[i].color = new Color(0.12f, 0.15f, 0.21f, 1f);
                    slotLabels[i].text = (i + 1) + ". 빈 칸";
                    continue;
                }

                ShopContainerItem current = slotItem.Value;
                int visualIndex = current.VisualPrefabIndex;
                GameObject prefab = ShopClawPrizeNetwork.GetCatalogPrefab(visualIndex);
                slotLabels[i].text = (i + 1) + ". " + current.DisplayName +
                                     (current.Quantity > 1 ? " x" + current.Quantity : "");
                if (prefab != null) CreatePreview(i, prefab);
            }
        }

        private static string BuildContainerText(string title, List<ShopContainerItem> items, int capacity)
        {
            int used = 0;
            foreach (ShopContainerItem item in items) used += item.Quantity;
            System.Text.StringBuilder builder = new();
            builder.Append(title).Append(' ').Append(used).Append(" / ").Append(capacity).Append('\n');
            if (items.Count == 0) return builder.Append("\n비어 있음").ToString();
            int lines = Mathf.Min(7, items.Count);
            for (int i = 0; i < lines; i++)
                builder.Append("\n• ").Append(items[i].DisplayName)
                    .Append(items[i].Quantity > 1 ? " x" + items[i].Quantity : "");
            if (items.Count > lines) builder.Append("\n외 ").Append(items.Count - lines).Append("종");
            return builder.ToString();
        }

        private void CreatePreview(int slotIndex, GameObject prefab)
        {
            Vector3 stage = new(slotIndex * 6f, -1000f, -1000f);
            GameObject previewRoot = new("InventoryPreview_" + slotIndex);
            previewRoot.transform.position = stage;
            previewObjects.Add(previewRoot);

            GameObject model = Instantiate(prefab, previewRoot.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, 28f, 0f);
            SetLayerRecursively(model, PreviewLayer);
            foreach (Collider targetCollider in model.GetComponentsInChildren<Collider>(true))
                targetCollider.enabled = false;
            foreach (Rigidbody targetBody in model.GetComponentsInChildren<Rigidbody>(true))
            {
                targetBody.isKinematic = true;
                targetBody.detectCollisions = false;
            }
            foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            model.transform.position += stage - bounds.center;
            bounds.center = stage;

            GameObject lightObject = new("PreviewLight", typeof(Light));
            lightObject.transform.SetParent(previewRoot.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(36f, -32f, 0f);
            Light previewLight = lightObject.GetComponent<Light>();
            previewLight.type = LightType.Directional;
            previewLight.intensity = 1.35f;
            previewLight.color = new Color(1f, 0.94f, 0.82f);
            previewLight.cullingMask = 1 << PreviewLayer;

            GameObject cameraObject = new("PreviewCamera", typeof(Camera));
            cameraObject.transform.SetParent(previewRoot.transform, false);
            Camera previewCamera = cameraObject.GetComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.28f;
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 50f;
            previewCamera.transform.position = stage + new Vector3(1.8f, 1.25f, -3.6f);
            previewCamera.transform.LookAt(stage + Vector3.up * bounds.extents.y * 0.05f);

            RenderTexture texture = new(256, 256, 24, RenderTextureFormat.ARGB32)
            {
                name = "InventorySlot_" + slotIndex,
                antiAliasing = 4
            };
            texture.Create();
            renderTextures.Add(texture);
            previewCamera.targetTexture = texture;
            RenderTexture previousActive = RenderTexture.active;
            previewCamera.Render();
            RenderTexture.active = previousActive;
            previewCamera.enabled = false;
            previewImages[slotIndex].texture = texture;
            previewImages[slotIndex].color = Color.white;
        }

        private void ClearPreviews()
        {
            foreach (RawImage image in previewImages)
            {
                if (image != null) image.texture = null;
            }
            foreach (RenderTexture texture in renderTextures)
            {
                if (texture == null) continue;
                texture.Release();
                Destroy(texture);
            }
            renderTextures.Clear();
            foreach (GameObject previewObject in previewObjects)
                if (previewObject != null) Destroy(previewObject);
            previewObjects.Clear();
        }

        private GameObject CreatePanel(string objectName, Transform parent, Vector2 size, Color color)
        {
            GameObject target = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            target.transform.SetParent(parent, false);
            target.GetComponent<RectTransform>().sizeDelta = size;
            target.GetComponent<Image>().color = color;
            return target;
        }

        private Text CreateText(string objectName, Transform parent, string content, int size,
            FontStyle style, TextAnchor alignment, Color color)
        {
            Text target = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text))
                .GetComponent<Text>();
            target.transform.SetParent(parent, false);
            target.font = uiFont;
            target.fontSize = size;
            target.fontStyle = style;
            target.alignment = alignment;
            target.color = color;
            target.text = content;
            target.horizontalOverflow = HorizontalWrapMode.Wrap;
            target.verticalOverflow = VerticalWrapMode.Truncate;
            target.raycastTarget = false;
            return target;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(anchorMin.x, anchorMax.y);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;
            foreach (Transform child in target.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
    }
}
