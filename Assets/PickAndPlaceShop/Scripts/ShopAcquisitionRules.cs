using System;
using System.Collections.Generic;

namespace PickAndPlaceShop
{
    public enum ShopGachaRarity
    {
        Common,
        Uncommon,
        Rare
    }

    public enum ShopGachaState
    {
        Idle,
        InsertingCoin,
        TurningHandle,
        DispensingCapsule,
        OpeningCapsule,
        Result,
        Cooldown
    }

    public enum ShopKujiRank
    {
        S,
        A,
        B,
        C,
        D
    }

    public enum ShopKujiState
    {
        Idle,
        DrawingTicket,
        AwaitingScratch,
        Scratching,
        RevealingTicket,
        Result,
        Cooldown
    }

    [Serializable]
    public struct ShopKujiStock
    {
        public int S;
        public int A;
        public int B;
        public int C;
        public int D;

        public ShopKujiStock(int s, int a, int b, int c, int d)
        {
            S = Math.Max(0, s);
            A = Math.Max(0, a);
            B = Math.Max(0, b);
            C = Math.Max(0, c);
            D = Math.Max(0, d);
        }

        public int Total => Math.Max(0, S) + Math.Max(0, A) + Math.Max(0, B) + Math.Max(0, C) + Math.Max(0, D);

        public int Get(ShopKujiRank rank) => rank switch
        {
            ShopKujiRank.S => S,
            ShopKujiRank.A => A,
            ShopKujiRank.B => B,
            ShopKujiRank.C => C,
            _ => D
        };

        public bool TryTake(ShopKujiRank rank)
        {
            if (Get(rank) <= 0) return false;
            switch (rank)
            {
                case ShopKujiRank.S: S--; break;
                case ShopKujiRank.A: A--; break;
                case ShopKujiRank.B: B--; break;
                case ShopKujiRank.C: C--; break;
                case ShopKujiRank.D: D--; break;
            }
            return true;
        }
    }

    public static class ShopAcquisitionRules
    {
        public static ShopGachaRarity SelectGachaRarity(float roll, float uncommonChance, float rareChance)
        {
            float rare = Clamp01(rareChance);
            float uncommon = Math.Min(Clamp01(uncommonChance), 1f - rare);
            float value = Clamp01(roll);
            if (value < rare) return ShopGachaRarity.Rare;
            if (value < rare + uncommon) return ShopGachaRarity.Uncommon;
            return ShopGachaRarity.Common;
        }

        public static ShopKujiRank SelectKujiRank(int zeroBasedRoll, ShopKujiStock stock)
        {
            if (stock.Total <= 0) throw new InvalidOperationException("쿠지 재고가 없습니다.");
            int roll = Math.Max(0, Math.Min(stock.Total - 1, zeroBasedRoll));
            if (roll < stock.S) return ShopKujiRank.S;
            roll -= stock.S;
            if (roll < stock.A) return ShopKujiRank.A;
            roll -= stock.A;
            if (roll < stock.B) return ShopKujiRank.B;
            roll -= stock.B;
            if (roll < stock.C) return ShopKujiRank.C;
            return ShopKujiRank.D;
        }

        public static bool ShouldAwardLastPrize(int remainingAfterDraw, bool alreadyAwarded) =>
            remainingAfterDraw == 0 && !alreadyAwarded;

        public static bool ShouldAwardCeilingPrize(int drawsSinceCeiling, int ceilingDraws) =>
            ceilingDraws > 0 && Math.Max(0, drawsSinceCeiling) + 1 >= ceilingDraws;

        public static int KujiRewardCount(bool lastPrize, bool ceilingPrize) =>
            1 + (lastPrize ? 1 : 0) + (ceilingPrize ? 1 : 0);

        public static float ClampServerScratchProgress(float current, float proposed, float timeAllowance)
        {
            float safeCurrent = Clamp01(current);
            float safeProposed = Clamp01(proposed);
            float safeAllowance = Clamp01(timeAllowance);
            return Math.Max(safeCurrent, Math.Min(safeProposed, safeAllowance));
        }

        public static bool IsRareKujiReward(ShopKujiRank rank, bool lastPrize) =>
            lastPrize || rank == ShopKujiRank.S || rank == ShopKujiRank.A;

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }

    public sealed class ShopAcquisitionAwardLedger
    {
        private readonly HashSet<int> awardedAttempts = new();

        public bool TryRecord(int attemptId) => attemptId > 0 && awardedAttempts.Add(attemptId);
    }
}
