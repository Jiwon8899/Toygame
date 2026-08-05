using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    public sealed class ShopContainerSlotView : MonoBehaviour, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private ShopClawInventoryUI owner;
        private Image background;
        private RawImage icon;
        private Text label;
        private Color normalColor;

        public ShopContainerKind Container { get; private set; }
        public int SlotIndex { get; private set; }
        public bool ActiveSlot { get; private set; }
        public ShopContainerItem? Item { get; private set; }

        public void Configure(ShopClawInventoryUI ui, ShopContainerKind container, int slot,
            Image slotBackground, RawImage slotIcon, Text slotLabel)
        {
            owner = ui;
            Container = container;
            SlotIndex = slot;
            background = slotBackground;
            icon = slotIcon;
            label = slotLabel;
            normalColor = background.color;
        }

        public void Refresh(ShopContainerItem? item, bool active)
        {
            Item = item;
            ActiveSlot = active;
            gameObject.SetActive(active);
            if (!active) return;
            SetHighlight(false, false);
            if (!item.HasValue)
            {
                icon.texture = null;
                icon.color = new Color(0.16f, 0.19f, 0.25f, 1f);
                label.text = (SlotIndex + 1) + ". 빈 칸";
                return;
            }
            ShopContainerItem value = item.Value;
            ShopProductDefinition product = ShopProductVisuals.Find(value.ProductId);
            icon.texture = product != null && product.Icon != null ? product.Icon.texture : null;
            icon.color = icon.texture != null ? Color.white : new Color(0.25f, 0.28f, 0.35f, 1f);
            label.text = value.DisplayName + (value.Quantity > 1 ? " x" + value.Quantity : string.Empty);
        }

        public void SetHighlight(bool highlighted, bool valid)
        {
            if (background == null) return;
            background.color = !highlighted ? normalColor : valid
                ? new Color(0.16f, 0.46f, 0.34f, 1f)
                : new Color(0.48f, 0.18f, 0.2f, 1f);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (ActiveSlot && Item.HasValue) owner?.BeginDrag(this, eventData.position);
        }
        public void OnDrag(PointerEventData eventData) => owner?.MoveDrag(eventData.position);
        public void OnEndDrag(PointerEventData eventData) => owner?.EndDrag();
        public void OnDrop(PointerEventData eventData) => owner?.DropOn(this);
        public void OnPointerEnter(PointerEventData eventData) => owner?.Hover(this, true);
        public void OnPointerExit(PointerEventData eventData) => owner?.Hover(this, false);
    }

    [DefaultExecutionOrder(200)]
    public sealed class ShopClawInventoryUI : MonoBehaviour
    {
        private const int MaximumStorageSlots = 70;
        private const int MaximumDisplaySlots = 10;
        private static ShopClawInventoryUI instance;

        private readonly List<ShopContainerSlotView> personalSlots = new();
        private readonly List<ShopContainerSlotView> storageSlots = new();
        private readonly List<ShopContainerSlotView> displaySlots = new();
        private readonly List<ShopContainerSlotView> allSlots = new();

        private Canvas canvas;
        private GameObject panel;
        private Text personalCount;
        private Text storageCount;
        private Text displayCount;
        private Text feedback;
        private Font uiFont;
        private ShopNetworkGame observedGame;
        private ShopContainerSlotView dragSource;
        private RawImage dragIcon;
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
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 28);
            BuildUi();
            SetOpen(false);
        }

        private void OnDestroy()
        {
            DetachGame();
            ShopInputModeManager.Pop(this);
            if (instance == this) instance = null;
        }

        private void Update()
        {
            AttachGameIfNeeded();
            if (!ShopLocalPauseState.IsPaused && Keyboard.current != null &&
                Keyboard.current.iKey.wasPressedThisFrame && ShopNetworkGame.Instance != null)
                SetOpen(!isOpen);
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
            if (observedGame != null) observedGame.ItemContainers.OnListChanged += OnContainerChanged;
            dirty = true;
            if (observedGame == null && isOpen) SetOpen(false);
        }

        private void DetachGame()
        {
            if (observedGame != null) observedGame.ItemContainers.OnListChanged -= OnContainerChanged;
            observedGame = null;
        }

        private void OnContainerChanged(NetworkListEvent<ShopContainerItem> _) => dirty = true;

        private void SetOpen(bool open)
        {
            isOpen = open;
            if (panel != null) panel.SetActive(open);
            if (open)
            {
                ShopInputModeManager.Push(this, ShopInputMode.UI);
                dirty = true;
                ShopTutorialRuntime.Report(ShopTutorialAction.InventoryOpened);
            }
            else
            {
                EndDrag();
                ShopInputModeManager.Pop(this);
            }
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

            panel = CreatePanel("상품 관리", canvasObject.transform, new Vector2(1810f, 900f),
                new Color(0.025f, 0.035f, 0.065f, 0.97f));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;

            Text title = CreateText("Title", panel.transform,
                "상품 관리 · 드래그해서 개인 가방 / 공용 창고 / 공용 진열 간 이동",
                38, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.98f, 0.83f, 0.32f));
            SetRect(title.rectTransform, new Vector2(32f, -20f), new Vector2(1500f, 54f));
            Text help = CreateText("Help", panel.transform,
                "I 닫기 · 같은 상품 위에 놓으면 최대 10개까지 합치기 · 실패한 이동은 원래 위치 유지",
                21, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.68f, 0.78f, 0.9f));
            SetRect(help.rectTransform, new Vector2(34f, -72f), new Vector2(1450f, 38f));

            personalCount = CreateText("PersonalCount", panel.transform, "개인 가방 0 / 10",
                27, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetRect(personalCount.rectTransform, new Vector2(36f, -118f), new Vector2(860f, 38f));
            BuildGrid(panel.transform, personalSlots, ShopContainerKind.PersonalInventory,
                ShopNetworkGame.MaxClawInventorySlots, new Vector2(36f, -164f), 5,
                new Vector2(168f, 188f), new Vector2(180f, 200f));

            displayCount = CreateText("DisplayCount", panel.transform, "공용 진열 0 / 4",
                27, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(1f, 0.82f, 0.4f));
            SetRect(displayCount.rectTransform, new Vector2(36f, -575f), new Vector2(860f, 38f));
            BuildGrid(panel.transform, displaySlots, ShopContainerKind.SharedDisplay,
                MaximumDisplaySlots, new Vector2(36f, -622f), 5,
                new Vector2(168f, 108f), new Vector2(180f, 118f));

            storageCount = CreateText("StorageCount", panel.transform, "공용 창고 0 / 30",
                27, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.55f, 0.88f, 1f));
            SetRect(storageCount.rectTransform, new Vector2(946f, -118f), new Vector2(800f, 38f));
            BuildGrid(panel.transform, storageSlots, ShopContainerKind.SharedStorage,
                MaximumStorageSlots, new Vector2(946f, -164f), 10,
                new Vector2(74f, 84f), new Vector2(78f, 90f), true);

            feedback = CreateText("Feedback", panel.transform, string.Empty, 22, FontStyle.Bold,
                TextAnchor.MiddleLeft, new Color(0.7f, 1f, 0.82f));
            SetRect(feedback.rectTransform, new Vector2(946f, -810f), new Vector2(800f, 42f));

            dragIcon = new GameObject("DraggedIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage))
                .GetComponent<RawImage>();
            dragIcon.transform.SetParent(canvasObject.transform, false);
            dragIcon.rectTransform.sizeDelta = new Vector2(120f, 120f);
            dragIcon.raycastTarget = false;
            dragIcon.gameObject.SetActive(false);
        }

        private void BuildGrid(Transform parent, List<ShopContainerSlotView> target,
            ShopContainerKind container, int count, Vector2 origin, int columns,
            Vector2 size, Vector2 step, bool compact = false)
        {
            for (int i = 0; i < count; i++)
            {
                int row = i / columns;
                int column = i % columns;
                GameObject slot = CreatePanel(container + "_" + i, parent, size,
                    new Color(0.075f, 0.1f, 0.15f, 0.98f));
                SetRect(slot.GetComponent<RectTransform>(), origin + new Vector2(column * step.x, -row * step.y), size);
                RawImage icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage))
                    .GetComponent<RawImage>();
                icon.transform.SetParent(slot.transform, false);
                icon.raycastTarget = false;
                float padding = compact ? 5f : 8f;
                SetRect(icon.rectTransform, new Vector2(padding, -padding),
                    new Vector2(size.x - padding * 2f, size.y - (compact ? 28f : 42f) - padding),
                    new Vector2(0f, 1f), new Vector2(0f, 1f));
                Text label = CreateText("Label", slot.transform, (i + 1) + ". 빈 칸",
                    compact ? 13 : 17, FontStyle.Bold, TextAnchor.MiddleCenter,
                    new Color(0.78f, 0.82f, 0.9f));
                SetRect(label.rectTransform, new Vector2(3f, 3f), new Vector2(size.x - 6f, compact ? 25f : 38f),
                    Vector2.zero, Vector2.zero);
                ShopContainerSlotView view = slot.AddComponent<ShopContainerSlotView>();
                view.Configure(this, container, i, slot.GetComponent<Image>(), icon, label);
                target.Add(view);
                allSlots.Add(view);
            }
        }

        private void RefreshSlots()
        {
            ulong owner = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
                ? NetworkManager.Singleton.LocalClientId : 0;
            int storageCapacity = observedGame != null ? observedGame.SharedStorageCapacity : 0;
            int displayCapacity = observedGame != null ? observedGame.SharedDisplayCapacity : 0;
            RefreshContainer(personalSlots, owner, ShopContainerKind.PersonalInventory,
                ShopContainerRules.PersonalCapacity);
            RefreshContainer(storageSlots, ShopContainerRules.SharedOwner, ShopContainerKind.SharedStorage,
                storageCapacity);
            RefreshContainer(displaySlots, ShopContainerRules.SharedOwner, ShopContainerKind.SharedDisplay,
                displayCapacity);
            personalCount.text = "개인 가방 " + TotalQuantity(owner, ShopContainerKind.PersonalInventory) +
                                 "개 · " + UsedSlots(owner, ShopContainerKind.PersonalInventory) + "/10칸";
            storageCount.text = "공용 창고 " + TotalQuantity(ShopContainerRules.SharedOwner,
                ShopContainerKind.SharedStorage) + "개 · " + UsedSlots(ShopContainerRules.SharedOwner,
                ShopContainerKind.SharedStorage) + "/" + storageCapacity + "칸";
            displayCount.text = "공용 진열 " + TotalQuantity(ShopContainerRules.SharedOwner,
                ShopContainerKind.SharedDisplay) + "개 · " + UsedSlots(ShopContainerRules.SharedOwner,
                ShopContainerKind.SharedDisplay) + "/" + displayCapacity + "칸";
        }

        private void RefreshContainer(List<ShopContainerSlotView> views, ulong owner,
            ShopContainerKind container, int capacity)
        {
            for (int slot = 0; slot < views.Count; slot++)
                views[slot].Refresh(FindItem(owner, container, slot), slot < capacity);
        }

        private ShopContainerItem? FindItem(ulong owner, ShopContainerKind container, int slot)
        {
            if (observedGame == null) return null;
            for (int i = 0; i < observedGame.ItemContainers.Count; i++)
            {
                ShopContainerItem item = observedGame.ItemContainers[i];
                if (ShopContainerRules.BelongsTo(item, owner, container) && item.SlotIndex == slot)
                    return item;
            }
            return null;
        }

        private int TotalQuantity(ulong owner, ShopContainerKind container) => observedGame == null ? 0 :
            ShopContainerRules.TotalQuantity(observedGame.ItemContainers, owner, container);
        private int UsedSlots(ulong owner, ShopContainerKind container) => observedGame == null ? 0 :
            ShopContainerRules.UsedCount(observedGame.ItemContainers, owner, container);

        public void BeginDrag(ShopContainerSlotView source, Vector2 screenPosition)
        {
            if (source == null || !source.Item.HasValue) return;
            dragSource = source;
            ShopProductDefinition product = ShopProductVisuals.Find(source.Item.Value.ProductId);
            dragIcon.texture = product != null && product.Icon != null ? product.Icon.texture : null;
            dragIcon.color = dragIcon.texture != null ? new Color(1f, 1f, 1f, 0.88f) : new Color(0.5f, 0.5f, 0.5f, 0.8f);
            dragIcon.gameObject.SetActive(true);
            MoveDrag(screenPosition);
            foreach (ShopContainerSlotView slot in allSlots)
                if (slot.ActiveSlot) slot.SetHighlight(true, CanDrop(slot));
            feedback.text = "놓을 슬롯을 선택하세요.";
        }

        public void MoveDrag(Vector2 screenPosition)
        {
            if (dragSource == null || dragIcon == null) return;
            dragIcon.rectTransform.position = screenPosition;
        }

        public void DropOn(ShopContainerSlotView destination)
        {
            if (dragSource == null || destination == null || !CanDrop(destination))
            {
                feedback.text = "이 슬롯에는 놓을 수 없습니다. 원래 위치를 유지합니다.";
                return;
            }
            observedGame?.RequestContainerMove(dragSource.Container, dragSource.SlotIndex,
                destination.Container, destination.SlotIndex);
            feedback.text = "호스트가 이동을 확인하고 있습니다…";
        }

        public void EndDrag()
        {
            dragSource = null;
            if (dragIcon != null) dragIcon.gameObject.SetActive(false);
            foreach (ShopContainerSlotView slot in allSlots) slot.SetHighlight(false, false);
        }

        public void Hover(ShopContainerSlotView slot, bool entered)
        {
            if (dragSource == null || slot == null || !slot.ActiveSlot) return;
            slot.SetHighlight(entered, CanDrop(slot));
        }

        private bool CanDrop(ShopContainerSlotView destination)
        {
            if (dragSource == null || !dragSource.Item.HasValue || destination == null ||
                !destination.ActiveSlot || destination == dragSource) return false;
            if (!destination.Item.HasValue) return true;
            ShopContainerItem source = dragSource.Item.Value;
            ShopContainerItem target = destination.Item.Value;
            return source.ProductId != target.ProductId || target.Quantity < target.MaxStack;
        }

        private GameObject CreatePanel(string name, Transform parent, Vector2 size, Color color)
        {
            GameObject target = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            target.transform.SetParent(parent, false);
            target.GetComponent<RectTransform>().sizeDelta = size;
            target.GetComponent<Image>().color = color;
            return target;
        }

        private Text CreateText(string name, Transform parent, string content, int size,
            FontStyle style, TextAnchor alignment, Color color)
        {
            Text target = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text))
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
            Vector2? anchorMin = null, Vector2? anchorMax = null)
        {
            Vector2 min = anchorMin ?? new Vector2(0f, 1f);
            Vector2 max = anchorMax ?? min;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(min.x, max.y);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
