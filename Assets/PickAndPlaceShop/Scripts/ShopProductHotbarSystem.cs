using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(470)]
    public sealed class ShopProductHotbarSystem : MonoBehaviour
    {
        public static ShopProductHotbarSystem Instance { get; private set; }
        public static bool IsHoldingLocal => Instance != null && Instance.heldVisual != null;

        private readonly Image[] icons = new Image[5];
        private readonly Image[] backgrounds = new Image[5];
        private readonly Text[] labels = new Text[5];
        private Canvas canvas;
        private ShopContainerItem? assignmentCandidate;
        private ShopContainerItem heldItem;
        private GameObject heldVisual;
        private int activeSlot = -1;
        private float nextRefresh;

        public int HeldProductId => heldVisual != null ? heldItem.ProductId : -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject host = new("[Shop] Product Hotbar");
            DontDestroyOnLoad(host);
            Instance = host.AddComponent<ShopProductHotbarSystem>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            BuildUi();
        }

        private void Update()
        {
            bool gameplay = ShopNetworkGame.Instance != null;
            if (canvas != null) canvas.enabled = gameplay && !ShopLocalPauseState.IsPaused;
            if (!gameplay) return;

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.2f;
                RefreshHotbar();
            }

            UpdateHeldVisual();
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || ShopLocalPauseState.IsPaused) return;
            if (keyboard.fKey.wasPressedThisFrame && heldVisual != null)
            {
                CancelHolding();
                return;
            }
            HandleHotbarInput(keyboard);
        }

        public void SetHotbarAssignmentCandidate(ShopContainerItem item)
        {
            if (item.Container == ShopContainerKind.PersonalInventory)
                assignmentCandidate = item;
        }

        public void BeginHolding(ShopContainerKind container, int slot, ShopContainerItem item)
        {
            if (container != ShopContainerKind.PersonalInventory &&
                container != ShopContainerKind.SharedStorage) return;
            CancelHolding(false);
            Transform player = FindLocalPlayer();
            ShopProductDefinition product = ShopProductVisuals.Find(item.ProductId);
            if (player == null || product == null) return;

            heldItem = item;
            heldVisual = ShopProductVisuals.Instantiate(product, player);
            if (heldVisual == null) return;
            heldVisual.name = "Held Product";
            foreach (Collider collider in heldVisual.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Rigidbody body in heldVisual.GetComponentsInChildren<Rigidbody>(true))
                body.isKinematic = true;
            UpdateHeldVisual();
        }

        public void CancelHolding() => CancelHolding(true);

        private void CancelHolding(bool clearSelection)
        {
            if (heldVisual != null) Destroy(heldVisual);
            heldVisual = null;
            heldItem = default;
            if (!clearSelection) return;
            activeSlot = -1;
            ShopProgressionManager.Instance?.SetSelectedHotbarSlot(-1);
            RefreshHotbar();
        }

        private void HandleHotbarInput(Keyboard keyboard)
        {
            int pressed = keyboard.digit1Key.wasPressedThisFrame ? 0 :
                keyboard.digit2Key.wasPressedThisFrame ? 1 :
                keyboard.digit3Key.wasPressedThisFrame ? 2 :
                keyboard.digit4Key.wasPressedThisFrame ? 3 :
                keyboard.digit5Key.wasPressedThisFrame ? 4 : -1;
            if (pressed < 0) return;

            bool assign = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            if (assign)
            {
                if (!assignmentCandidate.HasValue) return;
                ShopProgressionManager.Instance?.SetHotbarProduct(pressed,
                    assignmentCandidate.Value.ProductId);
                RefreshHotbar();
                return;
            }
            if (!ShopInputModeManager.AllowsGameplay) return;
            SelectHotbarSlot(pressed);
        }

        private void SelectHotbarSlot(int slot)
        {
            if (activeSlot == slot && heldVisual != null)
            {
                CancelHolding();
                return;
            }

            int productId = ShopProgressionManager.Instance?.GetHotbarProductId(slot) ?? -1;
            if (productId < 0 || !TryFindPersonalProduct(productId, out ShopContainerItem item)) return;
            activeSlot = slot;
            ShopProgressionManager.Instance?.SetSelectedHotbarSlot(slot);
            BeginHolding(ShopContainerKind.PersonalInventory, item.SlotIndex, item);
            RefreshHotbar();
        }

        private bool TryFindPersonalProduct(int productId, out ShopContainerItem item)
        {
            item = default;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            NetworkManager manager = NetworkManager.Singleton;
            if (game == null || manager == null) return false;
            ulong owner = manager.LocalClientId;
            for (int i = 0; i < game.ItemContainers.Count; i++)
            {
                ShopContainerItem candidate = game.ItemContainers[i];
                if (candidate.OwnerClientId != owner ||
                    candidate.Container != ShopContainerKind.PersonalInventory ||
                    candidate.ProductId != productId || candidate.Quantity <= 0) continue;
                item = candidate;
                return true;
            }
            return false;
        }

        private void UpdateHeldVisual()
        {
            if (heldVisual == null) return;
            Transform player = FindLocalPlayer();
            if (player == null) { CancelHolding(); return; }
            Transform hand = FindRightHand(player);
            Vector3 position = hand != null
                ? hand.position + hand.forward * 0.12f + hand.up * 0.06f
                : player.position + player.forward * 0.55f + Vector3.up * 1.05f;
            Quaternion rotation = hand != null
                ? hand.rotation * Quaternion.Euler(20f, 90f, 0f)
                : player.rotation;
            heldVisual.transform.SetPositionAndRotation(position, rotation);
        }

        private void RefreshHotbar()
        {
            ShopProgressionManager manager = ShopProgressionManager.Instance;
            bool clearHeldItem = false;
            for (int i = 0; i < icons.Length; i++)
            {
                if (icons[i] == null) continue;
                int productId = manager?.GetHotbarProductId(i) ?? -1;
                if (productId >= 0 && !TryFindPersonalProduct(productId, out _))
                {
                    manager?.SetHotbarProduct(i, -1);
                    productId = -1;
                    if (activeSlot == i) clearHeldItem = true;
                }
                ShopProductDefinition product = ShopProductVisuals.Find(productId);
                icons[i].sprite = product != null ? product.Icon : null;
                icons[i].enabled = icons[i].sprite != null;
                labels[i].text = product != null ? product.DisplayName : "+";
                bool active = activeSlot == i && heldVisual != null;
                backgrounds[i].color = active ? ShopUiSkin.Teal : ShopUiSkin.CreamBackground;
                labels[i].color = active ? Color.white : ShopUiSkin.TextBody;
            }
            if (clearHeldItem)
            {
                if (heldVisual != null) Destroy(heldVisual);
                heldVisual = null;
                heldItem = default;
                activeSlot = -1;
                manager?.SetSelectedHotbarSlot(-1);
            }
        }

        private void BuildUi()
        {
            GameObject root = new("Product Hotbar Canvas", typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1450;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            Image strip = new GameObject("Hotbar", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            strip.transform.SetParent(root.transform, false);
            strip.color = ShopUiSkin.CreamCard;
            ShopUiSkin.Round(strip, 20);
            RectTransform stripRect = strip.rectTransform;
            stripRect.anchorMin = stripRect.anchorMax = new Vector2(0.5f, 0f);
            stripRect.pivot = new Vector2(0.5f, 0f);
            stripRect.anchoredPosition = new Vector2(0f, 126f);
            stripRect.sizeDelta = new Vector2(640f, 132f);

            for (int i = 0; i < 5; i++)
            {
                Image slot = new GameObject("Hotbar Slot " + (i + 1), typeof(RectTransform), typeof(Image))
                    .GetComponent<Image>();
                slot.transform.SetParent(strip.transform, false);
                backgrounds[i] = slot;
                ShopUiSkin.Round(slot, 12);
                RectTransform slotRect = slot.rectTransform;
                slotRect.anchorMin = slotRect.anchorMax = new Vector2(0f, 0.5f);
                slotRect.pivot = new Vector2(0f, 0.5f);
                slotRect.anchoredPosition = new Vector2(16f + i * 124f, 0f);
                slotRect.sizeDelta = new Vector2(112f, 104f);

                Image icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                icon.transform.SetParent(slot.transform, false);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.rectTransform.anchorMin = new Vector2(0.15f, 0.28f);
                icon.rectTransform.anchorMax = new Vector2(0.85f, 0.95f);
                icon.rectTransform.offsetMin = icon.rectTransform.offsetMax = Vector2.zero;
                icons[i] = icon;

                Text label = CreateText(slot.transform, 16, TextAnchor.LowerCenter);
                label.rectTransform.offsetMin = new Vector2(4f, 4f);
                label.rectTransform.offsetMax = new Vector2(-4f, -72f);
                labels[i] = label;

                Text number = CreateText(slot.transform, 18, TextAnchor.UpperLeft);
                number.text = (i + 1).ToString();
                number.rectTransform.offsetMin = new Vector2(8f, 70f);
                number.rectTransform.offsetMax = new Vector2(-75f, -6f);
            }
            RefreshHotbar();
        }

        private static Text CreateText(Transform parent, int size, TextAnchor anchor)
        {
            Text text = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = ShopUiFonts.Bold;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = ShopUiSkin.TextBody;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            return text;
        }

        private static Transform FindRightHand(Transform player)
        {
            Animator animator = player != null ? player.GetComponentInChildren<Animator>() : null;
            if (animator != null && animator.isHuman)
            {
                Transform bone = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (bone != null) return bone;
            }
            if (player == null) return null;
            foreach (Transform candidate in player.GetComponentsInChildren<Transform>(true))
                if (candidate.name.IndexOf("RightHand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    candidate.name.IndexOf("Hand_R", StringComparison.OrdinalIgnoreCase) >= 0)
                    return candidate;
            return null;
        }

        private static Transform FindLocalPlayer()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.LocalClient?.PlayerObject != null)
                return manager.LocalClient.PlayerObject.transform;
            foreach (ShopPlayerInteractor player in FindObjectsByType<ShopPlayerInteractor>(FindObjectsSortMode.None))
                if (player.IsOwner) return player.transform;
            return null;
        }
    }
}
