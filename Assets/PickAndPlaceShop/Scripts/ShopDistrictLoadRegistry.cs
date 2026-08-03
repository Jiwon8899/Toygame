using System.Collections.Generic;

namespace PickAndPlaceShop
{
    public sealed class ShopDistrictLoadRegistry
    {
        private readonly HashSet<string> requested = new();
        private readonly HashSet<string> loaded = new();

        public int LoadedMask { get; private set; }

        public bool TryBeginRequest(string districtId)
        {
            return !string.IsNullOrWhiteSpace(districtId) && !loaded.Contains(districtId) && requested.Add(districtId);
        }

        public void CancelRequest(string districtId)
        {
            if (!string.IsNullOrWhiteSpace(districtId)) requested.Remove(districtId);
        }

        public bool Complete(string districtId, int bit)
        {
            if (string.IsNullOrWhiteSpace(districtId)) return false;
            requested.Remove(districtId);
            bool added = loaded.Add(districtId);
            if (added) LoadedMask |= bit;
            return added;
        }

        public bool IsRequested(string districtId) => requested.Contains(districtId);
        public bool IsLoaded(string districtId) => loaded.Contains(districtId);
    }
}
