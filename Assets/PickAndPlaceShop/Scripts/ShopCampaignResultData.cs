using System;
using Unity.Collections;
using Unity.Netcode;

namespace PickAndPlaceShop
{
    [Serializable]
    public struct ShopCampaignResultData : INetworkSerializable, IEquatable<ShopCampaignResultData>
    {
        public int FinalCoins;
        public int TotalRevenue;
        public int TotalSold;
        public int TotalAcquired;
        public int FinalReputation;
        public int AverageSatisfaction;
        public int GiveUpCustomers;
        public int ClawSuccesses;
        public int ClawFailures;
        public int Score;
        public FixedString64Bytes TopProductName;
        public FixedString32Bytes Grade;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref FinalCoins);
            serializer.SerializeValue(ref TotalRevenue);
            serializer.SerializeValue(ref TotalSold);
            serializer.SerializeValue(ref TotalAcquired);
            serializer.SerializeValue(ref FinalReputation);
            serializer.SerializeValue(ref AverageSatisfaction);
            serializer.SerializeValue(ref GiveUpCustomers);
            serializer.SerializeValue(ref ClawSuccesses);
            serializer.SerializeValue(ref ClawFailures);
            serializer.SerializeValue(ref Score);
            serializer.SerializeValue(ref TopProductName);
            serializer.SerializeValue(ref Grade);
        }

        public bool Equals(ShopCampaignResultData other)
        {
            return FinalCoins == other.FinalCoins && TotalRevenue == other.TotalRevenue &&
                   TotalSold == other.TotalSold && TotalAcquired == other.TotalAcquired &&
                   FinalReputation == other.FinalReputation && AverageSatisfaction == other.AverageSatisfaction &&
                   GiveUpCustomers == other.GiveUpCustomers && ClawSuccesses == other.ClawSuccesses &&
                   ClawFailures == other.ClawFailures && Score == other.Score &&
                   TopProductName.Equals(other.TopProductName) && Grade.Equals(other.Grade);
        }
    }

    public static class ShopCampaignResultStore
    {
        public static bool HasResult { get; private set; }
        public static ShopCampaignResultData Result { get; private set; }

        public static void Set(ShopCampaignResultData result)
        {
            Result = result;
            HasResult = true;
        }

        public static void Clear()
        {
            Result = default;
            HasResult = false;
        }
    }
}
