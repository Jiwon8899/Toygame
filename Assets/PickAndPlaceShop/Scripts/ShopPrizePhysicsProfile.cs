using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Prize Physics Profile",
        fileName = "PrizePhysicsProfile")]
    public sealed class ShopPrizePhysicsProfile : ScriptableObject
    {
        [Min(0.1f)] [SerializeField] private float mass = 0.75f;
        [Min(0.2f)] [SerializeField] private float visualSize = 0.65f;
        [Range(0f, 2f)] [SerializeField] private float gripDifficulty = 0.55f;
        [Range(-20f, 20f)] [SerializeField] private float gripScoreModifier;
        [Range(0f, 1f)] [SerializeField] private float friction = 0.58f;
        [Range(0f, 0.25f)] [SerializeField] private float bounciness = 0.02f;
        [Min(0f)] [SerializeField] private float linearDamping = 0.4f;
        [Min(0f)] [SerializeField] private float angularDamping = 0.75f;
        [SerializeField] private bool articulated;
        [SerializeField] private PhysicsMaterial surfaceMaterial;

        public float Mass => mass;
        public float VisualSize => visualSize;
        public float GripDifficulty => gripDifficulty;
        public float GripScoreModifier => gripScoreModifier;
        public float Friction => friction;
        public float Bounciness => bounciness;
        public float LinearDamping => linearDamping;
        public float AngularDamping => angularDamping;
        public bool Articulated => articulated;
        public PhysicsMaterial SurfaceMaterial => surfaceMaterial;

#if UNITY_EDITOR
        public void EditorConfigure(float bodyMass, float size, float difficulty,
            float scoreModifier, float surfaceFriction, float bounce, float linearDrag,
            float angularDrag, bool useArticulation, PhysicsMaterial material)
        {
            mass = Mathf.Max(0.1f, bodyMass);
            visualSize = Mathf.Max(0.2f, size);
            gripDifficulty = Mathf.Clamp(difficulty, 0f, 2f);
            gripScoreModifier = Mathf.Clamp(scoreModifier, -20f, 20f);
            friction = Mathf.Clamp01(surfaceFriction);
            bounciness = Mathf.Clamp(bounce, 0f, 0.25f);
            linearDamping = Mathf.Max(0f, linearDrag);
            angularDamping = Mathf.Max(0f, angularDrag);
            articulated = useArticulation;
            surfaceMaterial = material;
        }
#endif
    }
}
