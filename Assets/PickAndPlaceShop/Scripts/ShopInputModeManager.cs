using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PickAndPlaceShop
{
    public enum ShopInputMode
    {
        Gameplay,
        Claw,
        UI,
        Photo,
        Menu
    }

    [DefaultExecutionOrder(-900)]
    public sealed class ShopInputModeManager : MonoBehaviour
    {
        private sealed class Entry
        {
            public Object Owner;
            public ShopInputMode Mode;
            public int Sequence;
        }

        private static ShopInputModeManager instance;
        private readonly List<Entry> stack = new();
        private readonly HashSet<Object> gameplayHudSuppressors = new();
        private int sequence;
        private ShopInputMode appliedMode = ShopInputMode.Gameplay;
        private bool appliedOnce;
        private int suppressLookFrames;
        private bool pointerLockPending;

        public static ShopInputMode CurrentMode =>
            instance != null ? instance.ResolveMode() : ShopInputMode.Gameplay;
        public static bool AllowsGameplay => CurrentMode == ShopInputMode.Gameplay;
        public static bool AllowsClaw => CurrentMode == ShopInputMode.Claw;
        public static bool ShowsGameplayHud =>
            (CurrentMode == ShopInputMode.Gameplay || CurrentMode == ShopInputMode.Claw) &&
            (instance == null || instance.gameplayHudSuppressors.Count == 0);
        public static bool IsUiOpen => IsPointerFreeMode(CurrentMode);
        public static bool SuppressLookThisFrame => instance != null && instance.suppressLookFrames > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[Input] Mode Manager");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopInputModeManager>();
        }

        public static void Push(Object owner, ShopInputMode mode)
        {
            if (owner == null) return;
            if (instance == null) Bootstrap();
            instance.stack.RemoveAll(entry => entry.Owner == null || entry.Owner == owner);
            instance.stack.Add(new Entry { Owner = owner, Mode = mode, Sequence = ++instance.sequence });
            instance.ApplyResolvedMode();
        }

        public static void Pop(Object owner)
        {
            if (instance == null || owner == null) return;
            instance.stack.RemoveAll(entry => entry.Owner == null || entry.Owner == owner);
            instance.ApplyResolvedMode();
        }

        /// <summary>
        /// Re-applies the resolved input mode after a player/session transition.
        /// Cursor ownership stays centralized here instead of being duplicated by scene code.
        /// </summary>
        public static void RefreshInputState()
        {
            if (instance == null) Bootstrap();
            instance.ApplyResolvedMode(true);
        }

        public static void SetGameplayHudSuppressed(Object owner, bool suppressed)
        {
            if (owner == null) return;
            // Scene teardown can destroy the persistent manager before modal owners run
            // OnDestroy. Removing a stale suppression must not recreate the manager.
            if (instance == null)
            {
                if (!suppressed) return;
                Bootstrap();
            }
            if (suppressed) instance.gameplayHudSuppressors.Add(owner);
            else instance.gameplayHudSuppressors.Remove(owner);
        }

        private void Update()
        {
            int previousCount = stack.Count;
            stack.RemoveAll(entry => entry.Owner == null);
            gameplayHudSuppressors.RemoveWhere(owner => owner == null);
            if (stack.Count != previousCount) ApplyResolvedMode();
            else ApplyLocalPlayerInput(appliedMode);
#if UNITY_WEBGL && !UNITY_EDITOR
            if (pointerLockPending && !IsPointerFreeMode(appliedMode) && HasPointerLockGestureThisFrame())
                ApplyCursorState(appliedMode);
#endif
            if (suppressLookFrames > 0) suppressLookFrames--;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused)
            {
                suppressLookFrames = Mathf.Max(suppressLookFrames, 1);
                ApplyResolvedMode(true);
            }
        }

        private ShopInputMode ResolveMode()
        {
            Entry selected = null;
            for (int index = 0; index < stack.Count; index++)
            {
                Entry candidate = stack[index];
                if (candidate.Owner == null) continue;
                if (selected == null || candidate.Mode > selected.Mode ||
                    candidate.Mode == selected.Mode && candidate.Sequence > selected.Sequence)
                    selected = candidate;
            }
            return selected != null ? selected.Mode : ShopInputMode.Gameplay;
        }

        private void ApplyResolvedMode(bool force = false)
        {
            ShopInputMode next = ResolveMode();
            if (!force && appliedOnce && next == appliedMode)
            {
                ApplyLocalPlayerInput(next);
                return;
            }

            bool pointerFree = IsPointerFreeMode(next);
            bool returningToLockedPointer = !pointerFree;
            appliedMode = next;
            appliedOnce = true;
            ApplyCursorState(next);
            if (returningToLockedPointer) suppressLookFrames = Mathf.Max(suppressLookFrames, 1);
            ApplyLocalPlayerInput(next);
        }

        private void ApplyCursorState(ShopInputMode mode)
        {
            bool pointerFree = IsPointerFreeMode(mode);
            if (pointerFree)
            {
                pointerLockPending = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            Cursor.visible = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browsers only grant pointer lock while processing a user gesture. UI close
            // buttons and keyboard close actions both satisfy this check; scene loads do not.
            pointerLockPending = Cursor.lockState != CursorLockMode.Locked;
            if (pointerLockPending && !HasPointerLockGestureThisFrame()) return;
#endif
            Cursor.lockState = CursorLockMode.Locked;
            pointerLockPending = false;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static bool HasPointerLockGestureThisFrame()
        {
            bool mouseGesture = Mouse.current != null &&
                (Mouse.current.leftButton.wasPressedThisFrame ||
                 Mouse.current.rightButton.wasPressedThisFrame ||
                 Mouse.current.middleButton.wasPressedThisFrame);
            bool keyboardGesture = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
            return mouseGesture || keyboardGesture;
        }
#endif

        private void ApplyLocalPlayerInput(ShopInputMode mode)
        {
            NetworkManager network = NetworkManager.Singleton;
            NetworkObject player = network != null && network.LocalClient != null
                ? network.LocalClient.PlayerObject
                : null;
            if (player == null) return;

            bool gameplay = mode == ShopInputMode.Gameplay;
            CoreInputHandler input = player.GetComponent<CoreInputHandler>();
            CoreMovement movement = player.GetComponent<CoreMovement>();
            CoreCameraController cameraController = player.GetComponent<CoreCameraController>();
            ShopPlayerInteractor interactor = player.GetComponent<ShopPlayerInteractor>();

            if (input != null) input.enabled = gameplay;
            if (movement != null)
            {
                movement.IsMovementEnabled = gameplay;
                if (!gameplay)
                {
                    movement.SetMoveInput(Vector2.zero);
                    movement.SetSprintState(false);
                }
            }
            if (cameraController != null)
            {
                bool enableCamera = gameplay && suppressLookFrames <= 0;
                if (!enableCamera) cameraController.SetLookInput(Vector2.zero);
                cameraController.enabled = enableCamera;
            }
            if (interactor != null) interactor.enabled = gameplay;
        }

        private static bool IsPointerFreeMode(ShopInputMode mode) =>
            mode == ShopInputMode.UI || mode == ShopInputMode.Menu;
    }
}
