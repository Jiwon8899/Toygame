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
        public void SoloLaunch_UsesTheInProcessSinglePlayerTransport()
        {
            const string path = "Assets/PickAndPlaceShop/Scripts/ShopSceneLaunchBootstrap.cs";
            string source = File.ReadAllText(path);
            StringAssert.Contains("Unity.Netcode.Transports.SinglePlayer", source);
            StringAssert.Contains("GetComponent<SinglePlayerTransport>()", source);
            StringAssert.Contains("NetworkConfig.NetworkTransport = transport", source);
            StringAssert.DoesNotContain("SetConnectionData", source);
        }

        [Test]
        public void WebGlTemplate_FillsTheBrowserViewport()
        {
            const string path = "Assets/WebGLTemplates/ToyGameResponsive/index.html";
            Assert.IsTrue(File.Exists(path));
            string source = File.ReadAllText(path);
            StringAssert.Contains("width: 100vw", source);
            StringAssert.Contains("height: 100vh", source);
            StringAssert.Contains("autoSyncPersistentDataPath: true", source);
            StringAssert.DoesNotContain("960px", source);
        }

        [Test]
        public void WebGlSettings_LeaveResolutionSizingToTheBrowser()
        {
            const string path = "Assets/PickAndPlaceShop/Scripts/ShopUserSettings.cs";
            string source = File.ReadAllText(path);
            StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
            StringAssert.Contains("[Settings] WEBGL_BROWSER_RESOLUTION", source);
            StringAssert.Contains("Screen.fullScreen = data.Fullscreen", source);
        }

        [Test]
        public void WebGlGameplay_ReacquiresPointerLockFromAUserGesture()
        {
            string inputModes = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopInputModeManager.cs");
            string gameManager = File.ReadAllText(
                "Assets/Core/Scripts/Runtime/Components/GameManager.cs");

            StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", inputModes);
            StringAssert.Contains("HasPointerLockGestureThisFrame", inputModes);
            StringAssert.Contains("Cursor.lockState = CursorLockMode.Locked", inputModes);
            StringAssert.Contains("pointerLockPending", inputModes);
            StringAssert.DoesNotContain("Cursor.lockState", gameManager);
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
            string hotbar = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopProductHotbarSystem.cs");
            StringAssert.Contains("!TryFindPersonalProduct(productId, out _)", hotbar);
            StringAssert.Contains("stripRect.anchoredPosition = new Vector2(0f, 126f)", hotbar);
        }

        [Test]
        public void HeldProduct_UsesFixedShelfRequestAndDisablesColliders()
        {
            string hotbar = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopProductHotbarSystem.cs");
            string interactor = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopPlayerInteractor.cs");
            StringAssert.Contains("collider.enabled = false", hotbar);
            StringAssert.Contains("RequestDisplayProduct", interactor);
            StringAssert.DoesNotContain("ghostPosition", hotbar);
        }

        [Test]
        public void FullscreenUi_HidesHotbarAndClosingAdvancesDay()
        {
            string hotbar = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopProductHotbarSystem.cs");
            StringAssert.Contains("!ShopInputModeManager.AllowsGameplay", hotbar);
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
        public void TutorialSkip_UsesDataDrivenYDoubleTapAndHidesNetworkStatus()
        {
            ShopTutorialConfig tutorial = ShopTutorialConfig.Load();
            Assert.NotNull(tutorial);
            Assert.AreEqual(0.5f, tutorial.SkipDoubleTapSeconds, 0.001f);
            float previous = float.NegativeInfinity;
            Assert.IsFalse(ShopTutorialInputRules.RegisterSkipTap(ref previous, 1f,
                tutorial.SkipDoubleTapSeconds));
            Assert.IsTrue(ShopTutorialInputRules.RegisterSkipTap(ref previous, 1.49f,
                tutorial.SkipDoubleTapSeconds));
            previous = float.NegativeInfinity;
            Assert.IsFalse(ShopTutorialInputRules.RegisterSkipTap(ref previous, 2f,
                tutorial.SkipDoubleTapSeconds));
            Assert.IsFalse(ShopTutorialInputRules.RegisterSkipTap(ref previous, 2.51f,
                tutorial.SkipDoubleTapSeconds));
            string hud = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopProgressionHUD.cs");
            StringAssert.Contains("keyboard.yKey.wasPressedThisFrame", hud);
            StringAssert.Contains("SkipDoubleTapSeconds", hud);
            string network = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopNetworkHUD.cs");
            StringAssert.Contains("networkPanel.SetActive(false)", network);
        }

        [Test]
        public void AttackAnimation_UsesCompleteImportedClipsAndExplicitStateCrossfade()
        {
            string appearance = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopPlayerAppearance.cs");
            StringAssert.Contains("CrossFadeInFixedTime", appearance);
            StringAssert.Contains("HasState(0, state)", appearance);
            StringAssert.Contains("AttackAnimationActive", appearance);

            string theft = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopPlayerTheftNetwork.cs");
            StringAssert.Contains("leftButton.wasPressedThisFrame", theft);
            StringAssert.Contains("AttackSpeedForClickInterval", theft);

            string controller = File.ReadAllText(
                "Assets/PickAndPlaceShop/GeneratedCharacters/PlayerLocomotion.controller");
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(controller,
                @"m_ExitTime: 1\r?\n  m_HasExitTime: 1").Count);
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(controller,
                @"m_SpeedParameterActive: 1[\s\S]{0,240}m_SpeedParameter: AttackSpeed").Count);
            StringAssert.Contains("m_Name: AttackSpeed", controller);
            ShopTheftConfig config = ShopTheftConfig.Load();
            Assert.NotNull(config);
            Assert.AreEqual(0.06f, config.AttackTransitionSeconds, 0.001f);
            Assert.AreEqual(1.15f, config.AttackAnimationSpeed, 0.001f);
            Assert.AreEqual(2.8f, config.AttackMaximumAnimationSpeed, 0.001f);
            Assert.AreEqual(1.05f, config.ClawImpulse, 0.001f);

            foreach (string path in new[] { "Assets/animation/attack1.fbx", "Assets/animation/attack2.fbx" })
            {
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                Assert.NotNull(importer, path);
                Assert.AreEqual(ModelImporterAnimationType.Generic, importer.animationType, path);
                AnimationClip clip = System.Array.Find(AssetDatabase.LoadAllAssetsAtPath(path),
                    asset => asset is AnimationClip && !asset.name.StartsWith("__preview__")) as AnimationClip;
                Assert.NotNull(clip, path);
                Assert.Greater(clip.length, 1f, path);
            }
        }

        [Test]
        public void TrendAnnouncement_IsVisibleForDataDrivenDuration()
        {
            ShopOperationsConfig config = ShopOperationsConfig.Load();
            Assert.NotNull(config);
            Assert.AreEqual(7f, config.TrendAnnouncementSeconds, 0.001f);
            string hud = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopProgressionHUD.cs");
            StringAssert.Contains("DayAnnouncement.Value", hud);
            StringAssert.Contains("ShowNotificationNow", hud);
        }

        [Test]
        public void DynamicShopText_UsesTheSharedEmbeddedFontUtility()
        {
            string[] paths =
            {
                "Assets/PickAndPlaceShop/Scripts/ShopCustomerDialogueBubble.cs",
                "Assets/PickAndPlaceShop/Scripts/ShopCustomerDebugView.cs",
                "Assets/PickAndPlaceShop/Scripts/ShopCustomerWaitIndicator.cs",
                "Assets/PickAndPlaceShop/Scripts/ShopExpansionVisualController.cs",
                "Assets/PickAndPlaceShop/Scripts/ShopMainMenuController.cs",
                "Assets/PickAndPlaceShop/Scripts/ShopDifferentiationController.cs"
            };
            foreach (string path in paths)
            {
                string source = File.ReadAllText(path);
                StringAssert.Contains("ShopUiFonts.Apply", source, path);
                StringAssert.DoesNotContain("CreateDynamicFontFromOSFont", source, path);
            }
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

        [Test]
        public void DayTransition_CapturesAuthoritativeFundsBeforeSaving()
        {
            string game = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopNetworkGame.cs");
            int capture = game.IndexOf("nextDayProgression?.CaptureAuthoritativeSessionState();",
                System.StringComparison.Ordinal);
            int save = game.IndexOf("nextDayProgression?.SaveNowWithFeedback();",
                System.StringComparison.Ordinal);
            Assert.That(capture, Is.GreaterThanOrEqualTo(0));
            Assert.That(save, Is.GreaterThan(capture));
        }

        [Test]
        public void PreparationSkip_ReusesTutorialDoubleTapRuleAndStartsOpenPhase()
        {
            string hud = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopProgressionHUD.cs");
            StringAssert.Contains("건너뛰기 (Y×2)", hud);
            StringAssert.Contains("ShopTutorialInputRules.RegisterSkipTap", hud);
            StringAssert.Contains("live.RequestSkipPreparation();", hud);
            string operations = File.ReadAllText(
                "Assets/PickAndPlaceShop/Scripts/ShopLiveOperationsNetwork.cs");
            StringAssert.Contains("public bool ServerSkipPreparation()", operations);
            StringAssert.Contains("ServerSetPhase(ShopPhase.Open);", operations);
            StringAssert.Contains("phaseRemaining = DurationFor(observedPhase);", operations);
        }
    }
}
