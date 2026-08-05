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
        [SerializeField, Min(0.5f)] private float revealDuration = 1.5f;
        [SerializeField] private Color firstRoomFloorColor = new(0.56f, 0.34f, 0.22f);
        [SerializeField] private Color secondRoomFloorColor = new(0.25f, 0.46f, 0.48f);
        [SerializeField] private Color roomWallColor = new(0.19f, 0.12f, 0.18f);
        [Header("Bundled expansion wings")]
        [SerializeField] private Vector3 level3ZoneCenter = new(11.7f, 0f, 3.2f);
        [SerializeField] private Vector3 level4ZoneCenter = new(11.7f, 0f, 0.2f);
        [SerializeField] private Vector3 level5ZoneCenter = new(11.7f, 0f, 6.1f);
        [SerializeField] private Vector2 zoneFloorSize = new(6f, 2.6f);

        public StageRule[] StageRules => stageRules;
        public float RevealDuration => revealDuration;
        public Color FirstRoomFloorColor => firstRoomFloorColor;
        public Color SecondRoomFloorColor => secondRoomFloorColor;
        public Color RoomWallColor => roomWallColor;
        public Vector3 Level3ZoneCenter => level3ZoneCenter;
        public Vector3 Level4ZoneCenter => level4ZoneCenter;
        public Vector3 Level5ZoneCenter => level5ZoneCenter;
        public Vector2 ZoneFloorSize => new(Mathf.Max(3f, zoneFloorSize.x), Mathf.Max(2f, zoneFloorSize.y));
    }
}
