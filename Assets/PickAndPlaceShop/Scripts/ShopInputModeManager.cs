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
        private int sequence;
        private ShopInputMode appliedMode = ShopInputMode.Gameplay;
        private bool appliedOnce;
        private int suppressLookFrames;

        public static ShopInputMode CurrentMode =>
            instance != null ? instance.ResolveMode() : ShopInputMode.Gameplay;
        public static bool AllowsGameplay => CurrentMode == ShopInputMode.Gameplay;
        public static bool AllowsClaw => CurrentMode == ShopInputMode.Claw;
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

        private void Update()
        {
            int previousCount = stack.Count;
            stack.RemoveAll(entry => entry.Owner == null);
            if (stack.Count != previousCount) ApplyResolvedMode();
            else ApplyLocalPlayerInput(appliedMode);
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
#if UNITY_WEBGL && !UNITY_EDITOR
            bool returningToLockedPointer = false;
#else
            bool returningToLockedPointer = !pointerFree;
#endif
            appliedMode = next;
            appliedOnce = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = pointerFree;
#else
            Cursor.lockState = pointerFree ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = pointerFree;
#endif
            if (returningToLockedPointer) suppressLookFrames = Mathf.Max(suppressLookFrames, 1);
            ApplyLocalPlayerInput(next);
        }

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
