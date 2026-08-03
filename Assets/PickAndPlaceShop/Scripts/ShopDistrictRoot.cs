using UnityEngine;

namespace PickAndPlaceShop
{
    public sealed class ShopDistrictRoot : MonoBehaviour
    {
        [SerializeField] private ShopDistrictDefinition definition;

        public ShopDistrictDefinition Definition => definition;
        public string DistrictId => definition != null ? definition.DistrictId : string.Empty;

#if UNITY_EDITOR
        public void EditorConfigure(ShopDistrictDefinition districtDefinition)
        {
            definition = districtDefinition;
            if (definition != null) transform.position = definition.WorldOffset;
        }
#endif
    }
}
