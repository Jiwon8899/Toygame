using UnityEngine;

namespace PickAndPlaceShop
{
    public readonly struct ShopProductOffer
    {
        public readonly int ProductId;
        public readonly ShopProductCategory Category;
        public readonly int Price;
        public readonly ShopProductRarity Rarity;
        public readonly ShopProductCondition Condition;
        public readonly bool GiftWrappable;
        public readonly bool Available;

        public ShopProductOffer(int productId, ShopProductCategory category, int price,
            ShopProductRarity rarity, ShopProductCondition condition, bool giftWrappable, bool available)
        {
            ProductId = productId;
            Category = category;
            Price = price;
            Rarity = rarity;
            Condition = condition;
            GiftWrappable = giftWrappable;
            Available = available;
        }
    }

    public readonly struct ShopCustomerPreference
    {
        public readonly int Budget;
        public readonly int PreferredPrice;
        public readonly float PriceSensitivity;
        public readonly ShopProductCategory PreferredCategory;
        public readonly float RarityPreference;
        public readonly float ConditionPreference;
        public readonly float GiftPreference;

        public ShopCustomerPreference(int budget, int preferredPrice, float priceSensitivity,
            ShopProductCategory preferredCategory, float rarityPreference, float conditionPreference,
            float giftPreference)
        {
            Budget = budget;
            PreferredPrice = preferredPrice;
            PriceSensitivity = priceSensitivity;
            PreferredCategory = preferredCategory;
            RarityPreference = rarityPreference;
            ConditionPreference = conditionPreference;
            GiftPreference = giftPreference;
        }
    }

    public static class ShopProductScoring
    {
        public static bool TryScore(in ShopProductOffer offer, in ShopCustomerPreference customer, out float score)
        {
            score = float.NegativeInfinity;
            if (!offer.Available || offer.Price <= 0 || offer.Price > customer.Budget)
            {
                return false;
            }

            float priceDistance = Mathf.Abs(offer.Price - customer.PreferredPrice) /
                                  Mathf.Max(1f, customer.PreferredPrice);
            float priceFit = Mathf.Clamp01(1f - priceDistance * Mathf.Max(0.1f, customer.PriceSensitivity));
            float categoryFit = offer.Category == customer.PreferredCategory ? 1f : 0.25f;
            float rarityFit = ((int)offer.Rarity / 3f) * customer.RarityPreference;
            float conditionFit = ((int)offer.Condition / 2f) * customer.ConditionPreference;
            float giftFit = offer.GiftWrappable ? customer.GiftPreference : 0f;
            float budgetComfort = Mathf.Clamp01((customer.Budget - offer.Price) / (float)Mathf.Max(1, customer.Budget));

            score = priceFit * 45f + categoryFit * 25f + rarityFit * 12f +
                    conditionFit * 10f + giftFit * 8f + budgetComfort * 5f;
            return true;
        }

        public static bool CanSpawn(ShopPhase phase, bool spawnEnabled, float remainingSeconds, int active, int maximum)
        {
            return phase == ShopPhase.Open && spawnEnabled && remainingSeconds > 0f && active < maximum;
        }
    }
}
