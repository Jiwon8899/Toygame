using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopInputInteractionRegressionTests
    {
        [Test]
        public void InteractionConfig_UsesShortRangeAndFacingGate()
        {
            ShopOperationsConfig config = ShopOperationsConfig.Load();
            Assert.NotNull(config);
            Assert.AreEqual(2.5f, config.InteractionDistance, 0.001f);
            Assert.Greater(config.InteractionFacingThreshold, 0f);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/PickAndPlaceShop/Prefabs/PickAndPlacePlayer.prefab");
            Assert.NotNull(prefab);
            ShopPlayerInteractor interactor = prefab.GetComponent<ShopPlayerInteractor>();
            Assert.NotNull(interactor);
            Assert.AreEqual(2.5f, interactor.EffectiveInteractionRange, 0.001f);
        }

        [Test]
        public void MainMenu_UsesSharedMenuInputMode()
        {
            const string path = "Assets/PickAndPlaceShop/Scripts/ShopMainMenuController.cs";
            string source = File.ReadAllText(path);
            StringAssert.Contains("ShopInputModeManager.Push(this, ShopInputMode.Menu)", source);
            StringAssert.Contains("ShopInputModeManager.Pop(this)", source);
        }

        [Test]
        public void ClawExit_HasOneCleanupFunctionForEveryExitPath()
        {
            const string path = "Assets/PickAndPlaceShop/Scripts/ShopClawMachineNetwork.cs";
            string source = File.ReadAllText(path);
            StringAssert.Contains("private void ExitLocalMode()", source);
            StringAssert.Contains("ShopInputModeManager.Pop(this)", source);
            StringAssert.Contains("ExitLocalMode();\n            if (aimGroundMarker", source.Replace("\r\n", "\n"));
        }
    }
}
