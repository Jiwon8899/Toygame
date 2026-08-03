using UnityEngine;

namespace PickAndPlaceShop
{
    public sealed class ShopDistrictFlowGuide : MonoBehaviour
    {
        [SerializeField] private TextMesh guideText;

#if UNITY_EDITOR
        public void EditorConfigure(TextMesh text) => guideText = text;
#endif

        private void Update()
        {
            if (guideText == null || ShopNetworkGame.Instance == null) return;
            ShopPhase phase = ShopNetworkGame.Instance.Phase.Value;
            guideText.text = phase switch
            {
                ShopPhase.PrizeHunt => "낮 · 골목의 뽑기 건물에서 상품을 모으세요\n상품 획득 후 옆의 우리 뽑기 가게로 이동",
                ShopPhase.Setup => "저녁 준비 · 우리 뽑기 가게에서 진열대를 보충하고 계산대에서 영업 시작",
                ShopPhase.Open => "밤 영업 중 · 손님을 맞이하고 계산대에서 E키로 결제",
                ShopPhase.Summary => "오늘 영업 정산 중 · 판매 결과와 평판을 확인하세요",
                _ => "골목 상점가 · 뽑기와 가게 운영이 이어지는 하루"
            };
        }
    }
}
