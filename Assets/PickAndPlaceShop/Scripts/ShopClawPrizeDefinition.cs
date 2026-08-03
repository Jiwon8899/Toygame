using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Claw Prize", fileName = "ClawPrize")]
    public sealed class ShopClawPrizeDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "포근한 인형";
        [SerializeField] private ShopProductDefinition product;
        [Min(0.1f)] [SerializeField] private float weight = 0.8f;
        [Min(0.2f)] [SerializeField] private float size = 0.65f;
        [Range(0f, 2f)] [SerializeField] private float gripDifficulty = 0.55f;
        [Range(-20f, 20f)] [SerializeField] private float gripScoreModifier;
        [Range(0f, 1f)] [SerializeField] private float friction = 0.55f;
        [Range(0.5f, 1.5f)] [SerializeField] private float capsuleMassMultiplier = 0.72f;
        [SerializeField] private Color color = new(0.95f, 0.55f, 0.68f);

        public string DisplayName => displayName;
        public ShopProductDefinition Product => product;
        public float Weight => product != null && product.PhysicsProfile != null
            ? product.PhysicsProfile.Mass
            : weight;
        public float Size => product != null && product.PhysicsProfile != null
            ? product.PhysicsProfile.VisualSize
            : size;
        public float GripDifficulty => product != null && product.PhysicsProfile != null
            ? product.PhysicsProfile.GripDifficulty
            : gripDifficulty;
        public float GripScoreModifier => product != null && product.PhysicsProfile != null
            ? product.PhysicsProfile.GripScoreModifier
            : gripScoreModifier;
        public float Friction => product != null && product.PhysicsProfile != null
            ? product.PhysicsProfile.Friction
            : friction;
        public float CapsuleMassMultiplier => Mathf.Clamp(capsuleMassMultiplier, 0.5f, 1.5f);
        public float Bounciness => product != null && product.PhysicsProfile != null
            ? product.PhysicsProfile.Bounciness
            : 0.02f;
        public float LinearDamping => product != null && product.PhysicsProfile != null
            ? product.PhysicsProfile.LinearDamping
            : 0.35f;
        public float AngularDamping => product != null && product.PhysicsProfile != null
            ? product.PhysicsProfile.AngularDamping
            : 0.65f;
        public bool Articulated => product != null && product.PhysicsProfile != null &&
                                   product.PhysicsProfile.Articulated;
        public PhysicsMaterial SurfaceMaterial => product != null && product.PhysicsProfile != null
            ? product.PhysicsProfile.SurfaceMaterial
            : null;
        public Color Color => color;

#if UNITY_EDITOR
        public void EditorConfigure(string label, ShopProductDefinition linkedProduct, float mass,
            float visualSize, float difficulty, float scoreModifier, float surfaceFriction, Color tint)
        {
            displayName = label;
            product = linkedProduct;
            weight = mass;
            size = visualSize;
            gripDifficulty = difficulty;
            gripScoreModifier = scoreModifier;
            friction = surfaceFriction;
            color = tint;
        }
#endif
    }
}
