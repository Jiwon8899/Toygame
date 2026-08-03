using System;
using System.Net;
using UnityEngine;

namespace PickAndPlaceShop
{
    public static class ShopFlowRules
    {
        public const ushort DefaultPort = 7777;

        public static bool TryParsePort(string value, out ushort port, out string error)
        {
            error = string.Empty;
            if (!ushort.TryParse(value, out port) || port == 0)
            {
                port = 0;
                error = "포트는 1부터 65535 사이의 숫자여야 합니다.";
                return false;
            }

            return true;
        }

        public static bool IsValidDirectAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (string.Equals(value.Trim(), "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return IPAddress.TryParse(value.Trim(), out _);
        }

        public static bool CanSpawnCustomer(bool openPhase, bool spawnEnabled)
        {
            return openPhase && spawnEnabled;
        }

        public static bool IsSoloPause(bool isHost, int connectedPlayerCount)
        {
            return isHost && connectedPlayerCount <= 1;
        }
    }

    public static class ShopCampaignGradeRules
    {
        public static int CalculateScore(ShopCampaignResultData result, ShopCampaignGradeConfig config)
        {
            int targetCoins = config != null ? Mathf.Max(1, config.targetCoins) : 10000;
            int targetReputation = config != null ? Mathf.Max(1, config.targetReputation) : 100;
            int targetSold = config != null ? Mathf.Max(1, config.targetSold) : 50;
            float score = Mathf.Clamp01(result.FinalCoins / (float)targetCoins) * 25f;
            score += Mathf.Clamp01(result.FinalReputation / (float)targetReputation) * 25f;
            score += Mathf.Clamp01(result.AverageSatisfaction / 100f) * 30f;
            score += Mathf.Clamp01(result.TotalSold / (float)targetSold) * 20f;
            return Mathf.Clamp(Mathf.RoundToInt(score), 0, 100);
        }

        public static string CalculateGrade(ShopCampaignResultData result, ShopCampaignGradeConfig config)
        {
            int score = CalculateScore(result, config);
            int s = config != null ? config.sThreshold : 85;
            int a = config != null ? config.aThreshold : 70;
            int b = config != null ? config.bThreshold : 50;
            int c = config != null ? config.cThreshold : 30;
            if (score >= s) return "S";
            if (score >= a) return "A";
            if (score >= b) return "B";
            if (score >= c) return "C";
            return "D";
        }

        public static string Evaluation(string grade)
        {
            return grade switch
            {
                "S" => "모두가 다시 찾고 싶은 최고의 소품샵이 되었습니다!",
                "A" => "손님과 친구 모두가 만족한 멋진 일주일이었습니다.",
                "B" => "안정적인 운영으로 가게의 가능성을 증명했습니다.",
                "C" => "아슬아슬했지만 가게 문을 끝까지 지켜냈습니다.",
                _ => "어려운 한 주였습니다. 다음 운영에서는 더 좋은 선택을 해보세요."
            };
        }
    }
}
