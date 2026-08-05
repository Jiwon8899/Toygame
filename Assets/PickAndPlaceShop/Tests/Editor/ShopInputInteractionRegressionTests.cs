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
        public void WideInteractable_UsesColliderSurfaceInsteadOfTransformCenter()
        {
            GameObject root = new GameObject("Wide Register");
            try
            {
                root.transform.position = new Vector3(7.8f, 0f, -4.5f);
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 0.85f, 0f);
                collider.size = new Vector3(3.4f, 1.8f, 1.8f);
                ShopInteractable interactable = root.AddComponent<ShopInteractable>();
                Vector3 playerCenter = new Vector3(9.5f, 1.33f, -6.6f);

                Assert.Greater(Vector3.Distance(playerCenter, root.transform.position), 2.5f);
                Assert.Less(Vector3.Distance(playerCenter,
                    interactable.ClosestInteractionWorldPosition(playerCenter)), 2.5f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OnlineOrderStation_UsesDedicatedOperatorPoint()
        {
            GameObject station = new GameObject("OnlineOrderPackingStation");
            try
            {
                station.transform.position = new Vector3(9.4f, 0f, -4.5f);
                ShopInteractable interactable = station.AddComponent<ShopInteractable>();
                interactable.Configure(ShopAction.OnlineOrder, "온라인 주문 포장/발송");

                Assert.AreEqual(new Vector3(10.4f, 0f, -5.7f), interactable.InteractionWorldPosition);
                Assert.AreEqual(interactable.InteractionWorldPosition,
                    interactable.ClosestInteractionWorldPosition(new Vector3(10.4f, 1.2f, -6f)));
            }
            finally
            {
                Object.DestroyImmediate(station);
            }
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
