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

        [Test]
        public void OverlayAndWorldLabels_KeepGameplayUiReadable()
        {
            string negotiation = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopNegotiationPresenter.cs");
            StringAssert.Contains("canvas.overrideSorting = true", negotiation);
            StringAssert.Contains("canvas.sortingOrder = 31050", negotiation);

            string facing = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopWorldFacingUtility.cs");
            StringAssert.DoesNotContain("FindObjectsByType<TextMesh>", facing);
            StringAssert.DoesNotContain("sceneLoaded", facing);
        }

        [Test]
        public void NewDay_RebuildsSalesLedgerAndReleasesTransientCheckoutState()
        {
            string sales = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopNightSalesSystem.cs");
            StringAssert.Contains("public void ServerPrepareForNextDay()", sales);
            StringAssert.Contains("RebuildLedgerFromNetworkStock();", sales);
            StringAssert.Contains("if (NegotiationActive.Value) ClearNegotiation(true);", sales);

            string operations = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopLiveOperationsNetwork.cs");
            StringAssert.Contains("ServerPrepareForNextDay();", operations);
        }

        [Test]
        public void Hotbar_AutoAssignsManualAcquisitionsAndClearsMissingProducts()
        {
            string game = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopNetworkGame.cs");
            StringAssert.Contains("AutoAssignHotbarProduct(product.ProductId)", game);
            string curation = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopCurationSystem.cs");
            StringAssert.Contains("!TryFindPersonalProduct(productId, out _)", curation);
            StringAssert.Contains("stripRect.anchoredPosition = new Vector2(0f, 126f)", curation);
        }

        [Test]
        public void HeldProduct_PlacementUsesShelfPlaneAndFallbackAnchors()
        {
            string curation = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopCurationSystem.cs");
            StringAssert.Contains("tierPlane.Raycast(aimRay", curation);
            StringAssert.Contains("nearestValidIndex", curation);
            StringAssert.Contains("collider.enabled = false", curation);
        }

        [Test]
        public void FullscreenUi_HidesHotbarAndClosingAdvancesDay()
        {
            string curation = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopCurationSystem.cs");
            StringAssert.Contains("gameplayActive && !ShopInputModeManager.IsUiOpen", curation);
            string summary = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopClosingSummaryPresenter.cs");
            StringAssert.Contains("RequestInteraction(ShopAction.EndDay)", summary);
        }

        [Test]
        public void TutorialSkip_HasConfirmationAndPersistentCompletionPath()
        {
            string hud = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopProgressionHUD.cs");
            StringAssert.Contains("튜토리얼을 건너뛸까요?", hud);
            StringAssert.Contains("ConfirmTutorialSkip", hud);
            string progression = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopProgressionManager.cs");
            StringAssert.Contains("public void SkipTutorial()", progression);
            StringAssert.Contains("SaveNow();", progression);
        }

        [Test]
        public void Negotiation_IsButtonChoiceWithDataDrivenRates()
        {
            ShopOperationsConfig config = ShopOperationsConfig.Load();
            Assert.NotNull(config);
            Assert.AreEqual(3, config.NegotiationOfferCount);
            float[] expectedBonus = { 0.10f, 0.20f, 0.30f };
            float[] expectedChance = { 0.80f, 0.55f, 0.30f };
            for (int option = 0; option < 3; option++)
            {
                ShopNegotiationOffer offer = config.NegotiationOfferAt(option);
                Assert.AreEqual(expectedBonus[option], offer.PriceBonus, 0.0001f);
                Assert.AreEqual(expectedChance[option], offer.SuccessChance, 0.0001f);
                int successes = 0;
                for (int sample = 0; sample < 1000; sample++)
                    if (ShopNegotiationRules.Succeeds((sample + 0.5f) / 1000f,
                            offer.SuccessChance)) successes++;
                Assert.AreEqual(Mathf.RoundToInt(offer.SuccessChance * 1000f), successes);
            }
            string presenter = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopNegotiationPresenter.cs");
            StringAssert.Contains("ChooseOffer(0)", presenter);
            StringAssert.Contains("button.onClick.AddListener", presenter);
            StringAssert.DoesNotContain("PingPong", presenter);
            StringAssert.DoesNotContain("SuccessBand", presenter);
        }

        [Test]
        public void TitlePresentation_ReferencesBuildIncludedArtwork()
        {
            ShopTitlePresentationConfig config = ShopTitlePresentationConfig.Load();
            Assert.NotNull(config);
            Assert.NotNull(config.Background);
            Assert.NotNull(config.Logo);
            Assert.AreEqual(4f, config.IdleAmplitudePixels, 0.001f);
            Assert.AreEqual(2.6f, config.IdlePeriodSeconds, 0.001f);
        }
    }
}
