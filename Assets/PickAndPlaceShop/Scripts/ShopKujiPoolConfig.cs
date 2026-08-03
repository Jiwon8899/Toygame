using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Kuji Pool", fileName = "KujiPool")]
    public sealed class ShopKujiPoolConfig : ScriptableObject
    {
        [SerializeField] private string poolId = "kuji_default";
        [SerializeField] private string displayName = "달토끼 쿠지";
        [SerializeField, Min(1)] private int ticketPrice = 110;
        [SerializeField] private ShopKujiStock initialStock = new(1, 2, 4, 6, 9);
        [SerializeField] private string sPrize = "달토끼 대형 인형";
        [SerializeField] private string aPrize = "달빛 쿠션";
        [SerializeField] private string bPrize = "별무늬 피규어";
        [SerializeField] private string cPrize = "아크릴 장식";
        [SerializeField] private string dPrize = "미니 배지";
        [SerializeField] private string lastPrize = "마지막상 달토끼 한정판";
        [SerializeField, Min(1)] private int ceilingDraws = 7;
        [SerializeField] private string ceilingPrize = "천장 보너스 한정 굿즈";
        [SerializeField] private ShopProductDefinition sPrizeDefinition;
        [SerializeField] private ShopProductDefinition aPrizeDefinition;
        [SerializeField] private ShopProductDefinition bPrizeDefinition;
        [SerializeField] private ShopProductDefinition cPrizeDefinition;
        [SerializeField] private ShopProductDefinition dPrizeDefinition;
        [SerializeField] private ShopProductDefinition lastPrizeDefinition;
        [SerializeField] private ShopProductDefinition ceilingPrizeDefinition;

        public string PoolId => poolId;
        public string DisplayName => displayName;
        public int TicketPrice => ticketPrice;
        public ShopKujiStock InitialStock => initialStock;
        public string LastPrize => lastPrize;
        public int CeilingDraws => Mathf.Max(1, ceilingDraws);
        public string CeilingPrize => ceilingPrize;
        public ShopProductDefinition LastPrizeDefinition => lastPrizeDefinition;
        public ShopProductDefinition CeilingPrizeDefinition => ceilingPrizeDefinition;

        public string PrizeFor(ShopKujiRank rank) => rank switch
        {
            ShopKujiRank.S => sPrize,
            ShopKujiRank.A => aPrize,
            ShopKujiRank.B => bPrize,
            ShopKujiRank.C => cPrize,
            _ => dPrize
        };

        public ShopProductDefinition PrizeDefinitionFor(ShopKujiRank rank) => rank switch
        {
            ShopKujiRank.S => sPrizeDefinition,
            ShopKujiRank.A => aPrizeDefinition,
            ShopKujiRank.B => bPrizeDefinition,
            ShopKujiRank.C => cPrizeDefinition,
            _ => dPrizeDefinition
        };

#if UNITY_EDITOR
        public void EditorConfigure(string id, string label, int price, ShopKujiStock stock,
            string s, string a, string b, string c, string d, string last, int ceiling, string ceilingReward)
        {
            poolId = id;
            displayName = label;
            ticketPrice = Mathf.Max(1, price);
            initialStock = stock;
            sPrize = s;
            aPrize = a;
            bPrize = b;
            cPrize = c;
            dPrize = d;
            lastPrize = last;
            ceilingDraws = Mathf.Max(1, ceiling);
            ceilingPrize = ceilingReward;
        }

        public void EditorConfigureProducts(ShopProductDefinition s, ShopProductDefinition a,
            ShopProductDefinition b, ShopProductDefinition c, ShopProductDefinition d,
            ShopProductDefinition last, ShopProductDefinition ceiling)
        {
            sPrizeDefinition = s;
            aPrizeDefinition = a;
            bPrizeDefinition = b;
            cPrizeDefinition = c;
            dPrizeDefinition = d;
            lastPrizeDefinition = last;
            ceilingPrizeDefinition = ceiling;
            sPrize = s != null ? s.DisplayName : string.Empty;
            aPrize = a != null ? a.DisplayName : string.Empty;
            bPrize = b != null ? b.DisplayName : string.Empty;
            cPrize = c != null ? c.DisplayName : string.Empty;
            dPrize = d != null ? d.DisplayName : string.Empty;
            lastPrize = last != null ? last.DisplayName : string.Empty;
            ceilingPrize = ceiling != null ? ceiling.DisplayName : string.Empty;
        }
#endif
    }
}
