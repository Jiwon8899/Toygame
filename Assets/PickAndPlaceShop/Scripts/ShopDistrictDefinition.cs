using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Open World/District Definition")]
    public sealed class ShopDistrictDefinition : ScriptableObject
    {
        [SerializeField] private string districtId;
        [SerializeField] private string displayName;
        [SerializeField] private string scenePath;
        [SerializeField] private int networkBitIndex;
        [SerializeField] private Vector3 worldOffset;
        [SerializeField] private bool initiallyUnlocked;
        [SerializeField] private bool transitOnly;

        public string DistrictId => districtId;
        public string DisplayName => displayName;
        public string ScenePath => scenePath;
        public string SceneName => string.IsNullOrWhiteSpace(scenePath)
            ? string.Empty
            : System.IO.Path.GetFileNameWithoutExtension(scenePath);
        public int NetworkBitIndex => networkBitIndex;
        public int NetworkBit => networkBitIndex is >= 0 and < 31 ? 1 << networkBitIndex : 0;
        public Vector3 WorldOffset => worldOffset;
        public bool InitiallyUnlocked => initiallyUnlocked;
        public bool TransitOnly => transitOnly;

#if UNITY_EDITOR
        public void EditorConfigure(string stableId, string koreanName, string path, int bitIndex,
            Vector3 offset, bool unlockedAtStart, bool requiresTransit)
        {
            districtId = stableId;
            displayName = koreanName;
            scenePath = path;
            networkBitIndex = bitIndex;
            worldOffset = offset;
            initiallyUnlocked = unlockedAtStart;
            transitOnly = requiresTransit;
        }
#endif
    }
}
