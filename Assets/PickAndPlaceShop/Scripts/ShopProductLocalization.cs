namespace PickAndPlaceShop
{
    public static class ShopProductLocalization
    {
        public static string CategoryLabel(ShopProductCategory category) => category switch
        {
            ShopProductCategory.Plush or ShopProductCategory.Animal or
                ShopProductCategory.CatPlush => "고양이 인형",
            ShopProductCategory.CapsuleToy or ShopProductCategory.Space or
                ShopProductCategory.CatFigure => "고양이 피규어",
            ShopProductCategory.Decoration or ShopProductCategory.Other or
                ShopProductCategory.CatGoods => "냥냥 잡화",
            ShopProductCategory.Seasonal or ShopProductCategory.CatSeasonal => "계절 한정 냥이",
            ShopProductCategory.Retro or ShopProductCategory.CatRetro => "레트로 냥이",
            _ => "상품"
        };

        public static string CategoryId(ShopProductCategory category) => category switch
        {
            ShopProductCategory.CatPlush => "cat_plush",
            ShopProductCategory.CatFigure => "cat_figure",
            ShopProductCategory.CatGoods => "cat_goods",
            ShopProductCategory.CatSeasonal => "cat_seasonal",
            ShopProductCategory.CatRetro => "cat_retro",
            ShopProductCategory.Animal or ShopProductCategory.Plush => "cat_plush",
            ShopProductCategory.Space or ShopProductCategory.CapsuleToy => "cat_figure",
            ShopProductCategory.Retro => "cat_retro",
            ShopProductCategory.Seasonal => "cat_seasonal",
            _ => "cat_goods"
        };

        public static bool IsCatTheme(ShopProductCategory category) => category is
            ShopProductCategory.CatPlush or ShopProductCategory.CatFigure or
            ShopProductCategory.CatGoods or ShopProductCategory.CatSeasonal or
            ShopProductCategory.CatRetro;

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
