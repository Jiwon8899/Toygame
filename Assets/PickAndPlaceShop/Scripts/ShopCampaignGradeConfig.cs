using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(fileName = "ShopCampaignGradeConfig", menuName = "Pick And Place Shop/Campaign Grade Config")]
    public sealed class ShopCampaignGradeConfig : ScriptableObject
    {
        [Range(0, 100)] public int sThreshold = 85;
        [Range(0, 100)] public int aThreshold = 70;
        [Range(0, 100)] public int bThreshold = 50;
        [Range(0, 100)] public int cThreshold = 30;
        public int targetCoins = 10000;
        public int targetReputation = 100;
        public int targetSold = 50;
    }
}
