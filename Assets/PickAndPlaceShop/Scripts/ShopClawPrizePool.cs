using System;
using System.Collections.Generic;
using UnityEngine;

namespace PickAndPlaceShop
{
    [Serializable]
    public sealed class ShopClawPrizePoolEntry
    {
        [SerializeField] private ShopClawPrizeDefinition prize;
        [Min(1)] [SerializeField] private int rarityWeight = 10;

        public ShopClawPrizeDefinition Prize => prize;
        public int RarityWeight => Mathf.Max(1, rarityWeight);

        public ShopClawPrizePoolEntry(ShopClawPrizeDefinition definition, int weight)
        {
            prize = definition;
            rarityWeight = Mathf.Max(1, weight);
        }
    }

    [CreateAssetMenu(menuName = "Pick And Place Shop/Claw Prize Pool",
        fileName = "ClawPrizePool")]
    public sealed class ShopClawPrizePool : ScriptableObject
    {
        [SerializeField] private string poolId = "general";
        [SerializeField] private List<ShopClawPrizePoolEntry> entries = new();
        [Range(1, 24)] [SerializeField] private int maxConcurrentPrizes = 6;
        [Range(1, 30)] [SerializeField] private int spawnAttemptsPerPrize = 12;
        [Range(0f, 0.25f)] [SerializeField] private float spawnClearance = 0.035f;

        public string PoolId => poolId;
        public IReadOnlyList<ShopClawPrizePoolEntry> Entries => entries;
        public int MaxConcurrentPrizes => Mathf.Max(1, maxConcurrentPrizes);
        public int SpawnAttemptsPerPrize => Mathf.Max(1, spawnAttemptsPerPrize);
        public float SpawnClearance => Mathf.Max(0f, spawnClearance);

        public ShopClawPrizeDefinition PickWeighted(System.Random random)
        {
            if (entries == null || entries.Count == 0) return null;
            int total = 0;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i]?.Prize != null) total += entries[i].RarityWeight;
            if (total <= 0) return null;
            int roll = random != null ? random.Next(total) : UnityEngine.Random.Range(0, total);
            for (int i = 0; i < entries.Count; i++)
            {
                ShopClawPrizePoolEntry entry = entries[i];
                if (entry?.Prize == null) continue;
                roll -= entry.RarityWeight;
                if (roll < 0) return entry.Prize;
            }
            return entries[entries.Count - 1]?.Prize;
        }

        public ShopClawPrizeDefinition PickByRarity(ShopProductRarity rarity, System.Random random)
        {
            if (entries == null || entries.Count == 0) return null;
            List<ShopClawPrizePoolEntry> matches = new();
            int total = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                ShopClawPrizePoolEntry entry = entries[i];
                if (entry?.Prize?.Product == null || entry.Prize.Product.Rarity != rarity) continue;
                matches.Add(entry);
                total += entry.RarityWeight;
            }
            if (matches.Count == 0) return PickWeighted(random);
            int roll = random != null ? random.Next(total) : UnityEngine.Random.Range(0, total);
            for (int i = 0; i < matches.Count; i++)
            {
                roll -= matches[i].RarityWeight;
                if (roll < 0) return matches[i].Prize;
            }
            return matches[matches.Count - 1].Prize;
        }

#if UNITY_EDITOR
        public void EditorConfigure(string stableId, IEnumerable<ShopClawPrizePoolEntry> poolEntries,
            int maximumPrizes, int attempts, float clearance)
        {
            poolId = stableId;
            entries = poolEntries != null
                ? new List<ShopClawPrizePoolEntry>(poolEntries)
                : new List<ShopClawPrizePoolEntry>();
            maxConcurrentPrizes = Mathf.Clamp(maximumPrizes, 1, 24);
            spawnAttemptsPerPrize = Mathf.Clamp(attempts, 1, 30);
            spawnClearance = Mathf.Clamp(clearance, 0f, 0.25f);
        }
#endif
    }

    public static class ShopClawSpawnRules
    {
        public static bool CanPlace(Vector3 candidate, float radius,
            IReadOnlyList<Vector3> occupiedPositions, IReadOnlyList<float> occupiedRadii)
        {
            if (occupiedPositions == null || occupiedRadii == null ||
                occupiedPositions.Count != occupiedRadii.Count) return false;
            for (int i = 0; i < occupiedPositions.Count; i++)
            {
                Vector2 delta = new(candidate.x - occupiedPositions[i].x,
                    candidate.z - occupiedPositions[i].z);
                float required = Mathf.Max(0f, radius) + Mathf.Max(0f, occupiedRadii[i]);
                if (delta.sqrMagnitude < required * required) return false;
            }
            return true;
        }
    }
}
