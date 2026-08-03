using System;
using System.Collections.Generic;
using UnityEngine;

namespace PickAndPlaceShop
{
    public static class ShopProgressionRules
    {
        public static bool AreAllConditionsMet(IReadOnlyList<ShopProgressCondition> conditions,
            Func<ShopProgressCondition, int> currentValue)
        {
            if (conditions == null || conditions.Count == 0) return true;
            if (currentValue == null) return false;
            for (int i = 0; i < conditions.Count; i++)
            {
                ShopProgressCondition condition = conditions[i];
                if (condition == null || currentValue(condition) < condition.Target) return false;
            }
            return true;
        }

        public static int CollectionPercent(int ownedCount, int registeredCount)
        {
            if (registeredCount <= 0) return 0;
            return Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0, ownedCount) * 100f / registeredCount), 0, 100);
        }

        public static List<ShopCollectionMilestone> FindNewCollectionMilestones(int currentPercent,
            IReadOnlyList<ShopCollectionMilestone> milestones, ISet<int> granted)
        {
            List<ShopCollectionMilestone> result = new();
            if (milestones == null) return result;
            for (int i = 0; i < milestones.Count; i++)
            {
                ShopCollectionMilestone milestone = milestones[i];
                if (milestone == null) continue;
                int threshold = milestone.Percent;
                if (currentPercent >= threshold && (granted == null || !granted.Contains(threshold)))
                    result.Add(milestone);
            }
            return result;
        }

        public static ShopMasteryTier FindMasteryTier(int successes,
            IReadOnlyList<ShopMasteryTier> tiers)
        {
            ShopMasteryTier result = null;
            if (tiers == null) return null;
            for (int i = 0; i < tiers.Count; i++)
            {
                ShopMasteryTier tier = tiers[i];
                if (tier != null && successes >= tier.Successes &&
                    (result == null || tier.Successes >= result.Successes))
                    result = tier;
            }
            return result;
        }

        public static int DeterministicGoalTarget(ShopGoalDefinition definition, int cycle)
        {
            if (definition == null) return 1;
            int minimum = definition.MinimumTarget;
            int maximum = definition.MaximumTarget;
            if (maximum <= minimum) return minimum;
            int range = maximum - minimum + 1;
            int hash = StableHash(definition.GoalId);
            int positive = (hash ^ (cycle * 397)) & int.MaxValue;
            return minimum + positive % range;
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                if (!string.IsNullOrEmpty(value))
                    for (int i = 0; i < value.Length; i++)
                        hash = hash * 31 + value[i];
                return hash;
            }
        }
    }
}
