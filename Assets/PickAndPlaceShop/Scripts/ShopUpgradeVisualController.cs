using UnityEngine;

namespace PickAndPlaceShop
{
    public sealed class ShopUpgradeVisualController : MonoBehaviour
    {
        [SerializeField] private GameObject lightingTier;
        [SerializeField] private GameObject decorationTier;
        [SerializeField] private Renderer[] terminalIndicators;
        [SerializeField] private Renderer[] terminalAccents;
        [SerializeField] private TextMesh terminalSummary;

        private int appliedFacilityLevel = -1;
        private int appliedTotalLevel = -1;

#if UNITY_EDITOR
        public void EditorConfigure(GameObject lights, GameObject decorations,
            Renderer[] indicators, Renderer[] accents, TextMesh summary)
        {
            lightingTier = lights;
            decorationTier = decorations;
            terminalIndicators = indicators;
            terminalAccents = accents;
            terminalSummary = summary;
        }
#endif

        private void Update()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            int facilityLevel = game != null ? game.FacilityUpgradeLevel.Value : 0;
            int totalLevel = game != null ? game.TotalUpgradeLevel : 0;
            if (facilityLevel != appliedFacilityLevel)
            {
                appliedFacilityLevel = facilityLevel;
                if (lightingTier != null) lightingTier.SetActive(facilityLevel >= 1);
                if (decorationTier != null) decorationTier.SetActive(facilityLevel >= 2);
                Color accent = facilityLevel switch
                {
                    1 => new Color(1f, 0.72f, 0.25f),
                    2 => new Color(0.28f, 1f, 0.78f),
                    _ => new Color(0.25f, 0.7f, 0.72f)
                };
                ApplyColor(terminalAccents, accent, facilityLevel > 0);
            }

            if (totalLevel == appliedTotalLevel) return;
            appliedTotalLevel = totalLevel;
            if (terminalSummary != null)
                terminalSummary.text = "업그레이드 " + totalLevel + "/" +
                                       ShopNetworkGame.TotalSupportedUpgradeLevels + "\nE로 내역 열기";
            if (terminalIndicators == null) return;
            int litIndicators = Mathf.CeilToInt(totalLevel /
                (float)ShopNetworkGame.TotalSupportedUpgradeLevels * terminalIndicators.Length);
            for (int i = 0; i < terminalIndicators.Length; i++)
                ApplyColor(new[] { terminalIndicators[i] },
                    i < litIndicators ? new Color(0.25f, 1f, 0.72f) : new Color(0.12f, 0.18f, 0.2f),
                    i < litIndicators);
        }

        private static void ApplyColor(Renderer[] renderers, Color color, bool emissive)
        {
            if (renderers == null) return;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                MaterialPropertyBlock block = new();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_EmissionColor", emissive ? color * 1.2f : Color.black);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
