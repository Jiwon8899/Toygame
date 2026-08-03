using UnityEngine;

namespace PickAndPlaceShop
{
    [CreateAssetMenu(menuName = "Pick And Place Shop/Customer Archetype", fileName = "Customer")]
    public sealed class ShopCustomerArchetypeDefinition : ScriptableObject
    {
        [SerializeField] private ShopCustomerType customerType;
        [Min(1)] [SerializeField] private int budgetMin = 80;
        [Min(1)] [SerializeField] private int budgetMax = 180;
        [Min(1)] [SerializeField] private int preferredPrice = 100;
        [Range(0f, 2f)] [SerializeField] private float priceSensitivity = 1f;
        [Min(3f)] [SerializeField] private float patienceSeconds = 15f;
        [Min(0.01f)] [SerializeField] private float spawnWeight = 1f;
        [SerializeField] private ShopProductCategory preferredCategory;
        [Range(0f, 2f)] [SerializeField] private float rarityPreference;
        [Range(0f, 2f)] [SerializeField] private float conditionPreference;
        [Range(0f, 2f)] [SerializeField] private float giftPreference;
        [Range(1f, 5f)] [SerializeField] private float movementSpeed = 2.4f;

        public ShopCustomerType CustomerType => customerType;
        public int BudgetMin => budgetMin;
        public int BudgetMax => Mathf.Max(budgetMin, budgetMax);
        public int PreferredPrice => preferredPrice;
        public float PriceSensitivity => priceSensitivity;
        public float PatienceSeconds => patienceSeconds;
        public float SpawnWeight => spawnWeight;
        public ShopProductCategory PreferredCategory => preferredCategory;
        public float RarityPreference => rarityPreference;
        public float ConditionPreference => conditionPreference;
        public float GiftPreference => giftPreference;
        public float MovementSpeed => movementSpeed;

#if UNITY_EDITOR
        public void EditorConfigure(ShopCustomerType type, int minBudget, int maxBudget, int idealPrice,
            float sensitivity, float patience, float weight, ShopProductCategory category,
            float rarityWeight, float conditionWeight, float giftWeight, float speed)
        {
            customerType = type;
            budgetMin = minBudget;
            budgetMax = maxBudget;
            preferredPrice = idealPrice;
            priceSensitivity = sensitivity;
            patienceSeconds = patience;
            spawnWeight = weight;
            preferredCategory = category;
            rarityPreference = rarityWeight;
            conditionPreference = conditionWeight;
            giftPreference = giftWeight;
            movementSpeed = speed;
        }
#endif
    }
}
