using System;

namespace PickAndPlaceShop
{
    public enum ShopLaunchMode
    {
        None,
        Solo
    }

    [Serializable]
    public struct ShopLaunchRequest
    {
        public ShopLaunchMode Mode;
        public int MaximumPlayers;
        public string Address;
        public ushort Port;
        public string PlayerName;
        public bool ResetCampaign;
    }

    public static class ShopLaunchContext
    {
        public const string MainMenuScene = "PickAndPlaceShop_MainMenu";
        public const string FullFlowScene = "PickAndPlaceShop_FullFlow_Multiplayer";
        public const string MainStreetSliceScene = "PickAndPlaceShop_MainStreetSlice_Multiplayer";
        public const string CompleteFlowScene = MainStreetSliceScene;
        public const string DistrictScene = "PickAndPlaceShop_District_Multiplayer";
        public const string EndingScene = "PickAndPlaceShop_Ending";

        private static ShopLaunchRequest pendingRequest;
        private static bool hasPendingRequest;
        private static string pendingError;

        public static ShopLaunchMode LastMode { get; private set; } = ShopLaunchMode.Solo;
        public static int LastMaximumPlayers { get; private set; } = 1;
        public static ushort LastPort { get; private set; } = ShopFlowRules.DefaultPort;

        public static void SetRequest(ShopLaunchRequest request)
        {
            request.Mode = ShopLaunchMode.Solo;
            request.MaximumPlayers = 1;
            request.Address = "127.0.0.1";
            request.Port = request.Port == 0 ? ShopFlowRules.DefaultPort : request.Port;
            pendingRequest = request;
            hasPendingRequest = true;
            LastMode = ShopLaunchMode.Solo;
            LastMaximumPlayers = 1;
            LastPort = request.Port;
        }

        public static bool TryConsume(out ShopLaunchRequest request)
        {
            request = pendingRequest;
            if (!hasPendingRequest) return false;
            hasPendingRequest = false;
            return true;
        }

        public static void SetError(string message)
        {
            pendingError = message;
        }

        public static string ConsumeError()
        {
            string result = pendingError;
            pendingError = null;
            return result;
        }

        public static void Clear()
        {
            hasPendingRequest = false;
            pendingError = null;
        }

        public static bool TryCreateQaRequest(out ShopLaunchRequest request)
        {
            request = default;
            string[] args = Environment.GetCommandLineArgs();
            if (!Contains(args, "-shopQaAutoStart")) return false;

            request.Mode = ShopLaunchMode.Solo;
            request.MaximumPlayers = 1;
            request.Address = "127.0.0.1";
            request.PlayerName = "QA Player";
            request.ResetCampaign = true;
            string portText = ValueAfter(args, "-shopPort", ShopFlowRules.DefaultPort.ToString());
            request.Port = ShopFlowRules.TryParsePort(portText, out ushort port, out _) ? port : ShopFlowRules.DefaultPort;
            return true;
        }

        public static string ResolveGameplayScene()
        {
            return Contains(Environment.GetCommandLineArgs(), "-shopDistrict")
                ? DistrictScene
                : CompleteFlowScene;
        }

        private static bool Contains(string[] args, string key)
        {
            foreach (string value in args)
                if (string.Equals(value, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ValueAfter(string[] args, string key, string fallback)
        {
            for (int index = 0; index < args.Length - 1; index++)
                if (string.Equals(args[index], key, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
            return fallback;
        }
    }
}
