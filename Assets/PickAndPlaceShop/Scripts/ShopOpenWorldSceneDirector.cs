using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShopOpenWorldSceneDirector : NetworkBehaviour
    {
        public static ShopOpenWorldSceneDirector Instance { get; private set; }

        [SerializeField] private ShopDistrictDefinition[] districts;
        [SerializeField] private string initialDistrictId = "main_street";

        public NetworkVariable<int> LoadedDistrictMask = new(0);
        public NetworkVariable<FixedString64Bytes> LastLoadedDistrictId =
            new(new FixedString64Bytes(string.Empty));

        private readonly ShopDistrictLoadRegistry registry = new();

        public IReadOnlyList<ShopDistrictDefinition> Districts => districts;

#if UNITY_EDITOR
        public void EditorConfigure(ShopDistrictDefinition[] definitions, string initialId)
        {
            districts = definitions;
            initialDistrictId = initialId;
        }
#endif

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;
            NetworkManager.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
            ServerEnsureDistrictLoaded(initialDistrictId);
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null && NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            if (Instance == this) Instance = null;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public bool ServerEnsureDistrictLoaded(string districtId)
        {
            if (!IsServer || NetworkManager == null || NetworkManager.SceneManager == null) return false;
            ShopDistrictDefinition definition = FindDefinition(districtId);
            if (definition == null || string.IsNullOrWhiteSpace(definition.SceneName)) return false;

            Scene existing = SceneManager.GetSceneByPath(definition.ScenePath);
            if (existing.IsValid() && existing.isLoaded)
            {
                CompleteDistrict(definition);
                return false;
            }

            if (!registry.TryBeginRequest(districtId)) return false;
            SceneEventProgressStatus status = NetworkManager.SceneManager.LoadScene(
                definition.SceneName, LoadSceneMode.Additive);
            if (status == SceneEventProgressStatus.Started)
            {
                Debug.Log("[OpenWorld] DISTRICT_LOAD_STARTED id=" + districtId + " scene=" + definition.SceneName);
                return true;
            }

            registry.CancelRequest(districtId);
            Debug.LogError("[OpenWorld] DISTRICT_LOAD_FAILED id=" + districtId + " status=" + status);
            return false;
        }

        private void HandleLoadEventCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (!IsServer || mode != LoadSceneMode.Additive) return;
            ShopDistrictDefinition definition = FindDefinitionBySceneName(sceneName);
            if (definition == null) return;
            if (clientsTimedOut != null && clientsTimedOut.Count > 0)
            {
                registry.CancelRequest(definition.DistrictId);
                Debug.LogError("[OpenWorld] DISTRICT_LOAD_TIMEOUT id=" + definition.DistrictId +
                               " timedOut=" + clientsTimedOut.Count);
                return;
            }

            CompleteDistrict(definition);
            Debug.Log("[OpenWorld] DISTRICT_LOAD_COMPLETED id=" + definition.DistrictId +
                      " clients=" + (clientsCompleted != null ? clientsCompleted.Count : 0));
        }

        private void CompleteDistrict(ShopDistrictDefinition definition)
        {
            if (!registry.Complete(definition.DistrictId, definition.NetworkBit)) return;
            LoadedDistrictMask.Value = registry.LoadedMask;
            LastLoadedDistrictId.Value = new FixedString64Bytes(definition.DistrictId);
            if (definition.DistrictId == initialDistrictId && ShopNetworkGame.Instance != null)
                ShopNetworkGame.Instance.ServerSetEvent("메인 상점가가 준비되었습니다. 판매할 상품을 조달하세요.");
        }

        private ShopDistrictDefinition FindDefinition(string districtId)
        {
            if (districts == null) return null;
            foreach (ShopDistrictDefinition definition in districts)
                if (definition != null && string.Equals(definition.DistrictId, districtId,
                        StringComparison.Ordinal)) return definition;
            return null;
        }

        private ShopDistrictDefinition FindDefinitionBySceneName(string sceneName)
        {
            if (districts == null) return null;
            foreach (ShopDistrictDefinition definition in districts)
                if (definition != null && string.Equals(definition.SceneName, sceneName,
                        StringComparison.Ordinal)) return definition;
            return null;
        }
    }
}
