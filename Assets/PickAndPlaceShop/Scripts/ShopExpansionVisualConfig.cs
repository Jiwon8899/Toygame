using System;
using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Expansion Visual Config", fileName = "ShopExpansionVisualConfig")]
    public sealed class ShopExpansionVisualConfig : ScriptableObject
    {
        [Serializable]
        public sealed class StageRule
        {
            [Min(1)] public int minimumLevel = 1;
            public string[] activateObjectNames = Array.Empty<string>();
            public string[] deactivateObjectNames = Array.Empty<string>();
        }

        [SerializeField] private StageRule[] stageRules = Array.Empty<StageRule>();

        public StageRule[] StageRules => stageRules;
    }
}
