using System.Collections.Generic;
using UnityEngine;

namespace PickAndPlaceShop
{
    /// <summary>
    /// Records real collision contacts for one physical finger. It never attaches or moves a prize.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopClawFingerContactSensor : MonoBehaviour
    {
        private readonly Dictionary<int, ContactRecord> contacts = new();

        public bool HasRecentContact
        {
            get
            {
                PruneExpired();
                return contacts.Count > 0;
            }
        }

        public bool IsTouching(ShopClawPrizeNetwork prize)
        {
            if (prize == null) return false;
            PruneExpired();
            return contacts.ContainsKey(prize.GetInstanceID());
        }

        public void CollectRecentPrizes(List<ShopClawPrizeNetwork> destination)
        {
            if (destination == null) return;
            PruneExpired();
            foreach (ContactRecord record in contacts.Values)
                if (record.Prize != null && !destination.Contains(record.Prize))
                    destination.Add(record.Prize);
        }

        private void OnCollisionEnter(Collision collision) => Record(collision);
        private void OnCollisionStay(Collision collision) => Record(collision);

        private void Record(Collision collision)
        {
            if (collision == null) return;
            ShopClawPrizeNetwork prize = collision.collider.GetComponentInParent<ShopClawPrizeNetwork>();
            if (prize == null) return;
            contacts[prize.GetInstanceID()] = new ContactRecord(prize, Time.fixedTime);
        }

        private void PruneExpired()
        {
            if (contacts.Count == 0) return;
            float cutoff = Time.fixedTime - Mathf.Max(0.08f, Time.fixedDeltaTime * 3.5f);
            var expired = new List<int>();
            foreach (KeyValuePair<int, ContactRecord> pair in contacts)
                if (pair.Value.Prize == null || pair.Value.LastSeen < cutoff)
                    expired.Add(pair.Key);
            foreach (int id in expired) contacts.Remove(id);
        }

        private readonly struct ContactRecord
        {
            public readonly ShopClawPrizeNetwork Prize;
            public readonly float LastSeen;

            public ContactRecord(ShopClawPrizeNetwork prize, float lastSeen)
            {
                Prize = prize;
                LastSeen = lastSeen;
            }
        }
    }
}
