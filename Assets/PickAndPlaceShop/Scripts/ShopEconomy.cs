using UnityEngine;

namespace PickAndPlaceShop
{
    public static class ShopEconomy
    {
        public const int ClawCost = 120;
        public const int CapsuleCost = 60;
        public const int BaseSalePrice = 180;
        public const int BaseRent = 220;

        public static bool TrySpend(ref int balance, int amount)
        {
            if (amount < 0 || balance < amount)
            {
                return false;
            }

            balance -= amount;
            return true;
        }

        public static int CalculateSalePrice(int trendPercent, bool rare)
        {
            float rarityMultiplier = rare ? 1.75f : 1f;
            float trendMultiplier = 1f + Mathf.Clamp(trendPercent, -50, 100) / 100f;
            return Mathf.Max(1, Mathf.RoundToInt(BaseSalePrice * rarityMultiplier * trendMultiplier));
        }

        public static int CalculateRent(int day)
        {
            return BaseRent + Mathf.Max(0, day - 1) * 25;
        }

        public static int CalculateReputation(int sold, int rareSold)
        {
            return Mathf.Max(0, sold * 2 + rareSold * 3);
        }

        public static int CalculateDayScore(int coinsAfterRent, int sold, int reputation)
        {
            return Mathf.Max(0, coinsAfterRent + sold * 75 + reputation * 20);
        }
    }
}
