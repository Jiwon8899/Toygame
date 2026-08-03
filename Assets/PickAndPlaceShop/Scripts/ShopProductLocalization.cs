namespace PickAndPlaceShop
{
    public static class ShopProductLocalization
    {
        public static string CategoryLabel(ShopProductCategory category) => category switch
        {
            ShopProductCategory.Plush => "인형",
            ShopProductCategory.CapsuleToy => "캡슐 토이",
            ShopProductCategory.Decoration => "장식품",
            ShopProductCategory.Animal => "동물",
            ShopProductCategory.Space => "우주",
            ShopProductCategory.Retro => "레트로",
            ShopProductCategory.Seasonal => "계절",
            ShopProductCategory.Other => "기타",
            _ => "상품"
        };

        public static string RarityLabel(ShopProductRarity rarity) => rarity switch
        {
            ShopProductRarity.UltraRare => "초희귀",
            ShopProductRarity.Rare => "희귀",
            ShopProductRarity.Uncommon => "고급",
            _ => "일반"
        };

        public static string ConditionLabel(ShopProductCondition condition) => condition switch
        {
            ShopProductCondition.Mint => "최상",
            ShopProductCondition.Good => "양호",
            _ => "사용감 있음"
        };
    }
}
