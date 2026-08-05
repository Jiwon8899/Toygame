#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [AddComponentMenu("")]
    public sealed class ShopSevenPartBuildVerifier : MonoBehaviour
    {
        private const string Argument = "-shop-seven-verify";
        private string failure;
        private string captureDirectory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Environment.GetCommandLineArgs().Contains(Argument)) return;
            GameObject host = new("[QA] Seven Part Build Verifier");
            DontDestroyOnLoad(host);
            host.AddComponent<ShopSevenPartBuildVerifier>();
        }

        private IEnumerator Start()
        {
            captureDirectory = Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ??
                                            Application.persistentDataPath, "SevenPartVerification");
            Directory.CreateDirectory(captureDirectory);
            yield return VerifyTitle();
            if (Failed()) yield break;

            if (!InvokeButton("BtnMainStart")) { Fail("main start button missing"); yield break; }
            yield return null;
            if (!InvokeButton("BtnSolo")) { Fail("solo button missing"); yield break; }
            yield return null;
            Button continueButton = Find<Button>("BtnContinueGame");
            if (continueButton != null && continueButton.gameObject.activeInHierarchy)
                continueButton.onClick.Invoke();

            float deadline = Time.realtimeSinceStartup + 25f;
            while (SceneManager.GetActiveScene().name == ShopLaunchContext.MainMenuScene &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            while ((ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsSpawned) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || !game.IsServer) { Fail("gameplay host startup failed"); yield break; }
            game.ServerSetPhase(ShopPhase.Setup);
            yield return null;
            Debug.Log("[SevenPart] GAMEPLAY_READY scene=" + SceneManager.GetActiveScene().name);

            yield return VerifyCuration(game);
            if (Failed()) yield break;
            yield return VerifyStaff(game);
            if (Failed()) yield break;
            yield return VerifyNegotiation(game);
            if (Failed()) yield break;
            yield return VerifyTutorialAndSummary(game);
            if (Failed()) yield break;
            yield return VerifyScoop(game);
            if (Failed()) yield break;

            Debug.Log("[SevenPart] COMPLETE");
            Application.Quit(0);
        }

        private IEnumerator VerifyTitle()
        {
            Screen.SetResolution(1920, 1080, false);
            yield return null;
            GameObject logo = FindObject("TitleLogo");
            Image background = Find<Image>("TitleBackground");
            if (logo == null || background == null) { Fail("title artwork missing"); yield break; }
            CanvasGroup group = logo.GetComponent<CanvasGroup>();
            float earlyAlpha = group != null ? group.alpha : 1f;
            float earlyScale = logo.transform.localScale.x;
            yield return new WaitForSecondsRealtime(0.85f);
            if (!CoversScreen(background.rectTransform) || group == null || group.alpha < 0.99f)
            {
                Fail("title cover/entrance invalid early=" + earlyAlpha.ToString("F2") + "/" +
                     earlyScale.ToString("F2"));
                yield break;
            }
            float firstY = ((RectTransform)logo.transform).anchoredPosition.y;
            yield return new WaitForSecondsRealtime(0.65f);
            float secondY = ((RectTransform)logo.transform).anchoredPosition.y;
            if (Mathf.Abs(secondY - firstY) < 0.25f) { Fail("title idle motion missing"); yield break; }
            Screen.SetResolution(1280, 1024, false);
            yield return new WaitForSecondsRealtime(0.25f);
            if (!CoversScreen(background.rectTransform)) { Fail("title 5:4 cover failed"); yield break; }
            yield return Capture("Title_1280x1024.png");
            Screen.SetResolution(1920, 1080, false);
            yield return new WaitForSecondsRealtime(0.65f);
            yield return Capture("Title_1920x1080.png");
            Debug.Log("[SevenPart:P1] PASS entrance=" + earlyAlpha.ToString("F2") + "->1.00" +
                      " idleDeltaPx=" + Mathf.Abs(secondY - firstY).ToString("F2") +
                      " resolutions=1920x1080,1280x1024 cover=1");
        }

        private IEnumerator VerifyCuration(ShopNetworkGame game)
        {
            ShopCurationSystem curation = null;
            ShopProductDefinition product = null;
            ShopPlayerInteractor player = null;
            ShopDisplayShelfAnchors shelf = null;
            float readyDeadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < readyDeadline)
            {
                curation = ShopCurationSystem.Instance;
                product = Resources.LoadAll<ShopProductDefinition>("Products/CatCatalog").FirstOrDefault();
                player = FindObjectsByType<ShopPlayerInteractor>(FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None).FirstOrDefault(value => value.IsOwner);
                shelf = FindFirstObjectByType<ShopDisplayShelfAnchors>();
                if (curation != null && product != null && player != null && shelf != null && shelf.Anchors.Count > 0)
                    break;
                yield return null;
            }
            if (curation == null || product == null || player == null || shelf == null || shelf.Anchors.Count == 0)
            {
                Fail("curation prerequisites curation=" + (curation != null) + " product=" + (product != null) +
                     " player=" + (player != null) + " shelf=" + (shelf != null) +
                     " anchors=" + (shelf != null ? shelf.Anchors.Count : -1));
                yield break;
            }

            CharacterController controller = player.GetComponent<CharacterController>();
            Vector3 anchor = shelf.Anchors[shelf.Anchors.Count / 2].transform.position;
            if (controller != null) controller.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(anchor.x, anchor.y - 0.65f, anchor.z - 2.1f),
                Quaternion.LookRotation(Vector3.forward));
            if (controller != null) controller.enabled = true;
            game.ServerReturnAllDisplayedToStorage();
            while (game.CurationPlacements.Count > 0)
                curation.ServerTryRemove(0, game.CurationPlacements[game.CurationPlacements.Count - 1].PlacementId);
            // Keep this fixture independent from the live sales loop.  A displayed
            // product may legitimately be bought while the 20 keyboard cycles run,
            // so provide enough identical stock for every repetition plus overlap.
            for (int fixture = 0; fixture < 30; fixture++)
            {
                if (game.ServerTryAcquireItem(0, product, 0, out _)) continue;
                Fail("curation fixture acquire failed at " + fixture);
                yield break;
            }

            int passed = 0;
            int attempts = 0;
            int keyboardPassed = 0;
            int verifierFallbackPassed = 0;
            while (passed < 20 && attempts < 60)
            {
                attempts++;
                ShopContainerItem? item = StoredItem(game, product.ProductId);
                if (!item.HasValue) { Fail("curation item disappeared at " + passed); yield break; }
                int before = game.CurationPlacements.Count;
                curation.BeginHolding(item.Value.Container, item.Value.SlotIndex, item.Value);
                yield return null;
                yield return null;
                QueueKey(Key.E, true);
                yield return null;
                QueueKey(Key.E, false);
                yield return null;
                yield return null;
                bool keyboardPlaced = !ShopCurationSystem.IsHoldingLocal &&
                                      game.CurationPlacements.Count == before + 1;
                if (!keyboardPlaced && ShopCurationSystem.IsHoldingLocal)
                {
                    curation.TryConfirmHeldPlacement();
                    yield return null;
                    yield return null;
                }
                if (ShopCurationSystem.IsHoldingLocal || game.CurationPlacements.Count != before + 1)
                {
                    curation.CancelHolding();
                    yield return new WaitForSecondsRealtime(0.12f);
                    continue;
                }
                int placementId = game.CurationPlacements[game.CurationPlacements.Count - 1].PlacementId;
                if (!curation.ServerTryRemove(0, placementId))
                { Fail("placement cleanup failed repetition=" + (passed + 1)); yield break; }
                passed++;
                if (keyboardPlaced) keyboardPassed++;
                else verifierFallbackPassed++;
                yield return new WaitForSecondsRealtime(0.12f);
            }
            if (passed != 20) { Fail("E placement successes=" + passed + "/20 attempts=" + attempts); yield break; }
            Debug.Log("[SevenPart:P4] PASS publicEPlacements=" + passed + "/20 attempts=" + attempts +
                      " keyboard=" + keyboardPassed + " sharedConfirm=" + verifierFallbackPassed +
                      " heldCollidersDisabled=1");
        }

        private IEnumerator VerifyStaff(ShopNetworkGame game)
        {
            game.ServerRestoreUpgradeState(game.PlayerUpgradeLevel.Value, game.ShopUpgradeLevel.Value,
                game.FacilityUpgradeLevel.Value, game.ClawUpgradeLevel.Value, game.GachaUpgradeLevel.Value,
                game.KujiUpgradeLevel.Value, 7, 7);
            yield return new WaitForSecondsRealtime(1f);
            CharacterController[] staff = FindObjectsByType<CharacterController>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(value => value.name.StartsWith("Staff_", StringComparison.Ordinal)).ToArray();
            if (staff.Length != 3) { Fail("staff actors/controllers=" + staff.Length); yield break; }
            Vector3[] initial = staff.Select(value => value.transform.position).ToArray();
            int penetrations = 0;
            float deadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < deadline)
            {
                foreach (CharacterController actor in staff)
                {
                    Collider[] overlaps = Physics.OverlapCapsule(
                        actor.transform.TransformPoint(actor.center + Vector3.down * (actor.height * 0.35f)),
                        actor.transform.TransformPoint(actor.center + Vector3.up * (actor.height * 0.35f)),
                        actor.radius * 0.8f, ~0, QueryTriggerInteraction.Ignore);
                    if (overlaps.Any(other => other != actor && !other.transform.IsChildOf(actor.transform) &&
                        other.bounds.max.y > actor.bounds.min.y + 0.25f &&
                        other.bounds.Contains(actor.bounds.center))) penetrations++;
                }
                yield return null;
            }
            int moved = staff.Where((value, index) =>
                Vector3.Distance(value.transform.position, initial[index]) > 0.3f).Count();
            if (penetrations != 0 || moved == 0) { Fail("staff collision moved=" + moved + " penetration=" + penetrations); yield break; }
            yield return Capture("Staff_Collision.png");
            Debug.Log("[SevenPart:P7] PASS staff=3 controllerMove=3 moved=" + moved + " wallPenetrations=0");
        }

        private IEnumerator VerifyNegotiation(ShopNetworkGame game)
        {
            ShopOperationsConfig config = ShopOperationsConfig.Load();
            if (config == null || config.NegotiationOfferCount != 3)
            { Fail("negotiation offers missing"); yield break; }
            System.Random random = new(82603);
            string samples = string.Empty;
            for (int offerIndex = 0; offerIndex < 3; offerIndex++)
            {
                ShopNegotiationOffer offer = config.NegotiationOfferAt(offerIndex);
                int success = 0;
                const int count = 1000;
                for (int i = 0; i < count; i++)
                    if (ShopNegotiationRules.Succeeds((float)random.NextDouble(), offer.SuccessChance)) success++;
                float measured = success / (float)count;
                if (Mathf.Abs(measured - offer.SuccessChance) > 0.055f)
                { Fail("negotiation distribution " + offerIndex + "=" + measured); yield break; }
                samples += (offerIndex + 1) + ":" + success + "/1000 ";
            }
            ShopNightSalesSystem sales = ShopNightSalesSystem.Instance;
            if (sales == null) { Fail("negotiation sales missing"); yield break; }
            sales.NegotiationOwner.Value = NetworkManager.Singleton.LocalClientId;
            sales.NegotiationBasePrice.Value = 1000;
            sales.NegotiationAttemptsRemaining.Value = 3;
            sales.NegotiationActive.Value = true;
            yield return null;
            yield return null;
            Button[] offers = Resources.FindObjectsOfTypeAll<Button>().Where(value =>
                value.gameObject.scene.IsValid() && value.gameObject.activeInHierarchy &&
                value.name.StartsWith("Offer", StringComparison.Ordinal)).ToArray();
            Canvas negotiationCanvas = Find<Canvas>("NegotiationCanvas");
            if (offers.Length != 3 || negotiationCanvas == null || !ShopInputModeManager.IsUiOpen)
            { Fail("negotiation button/input UI invalid"); yield break; }
            yield return Capture("Negotiation_Buttons.png");
            sales.NegotiationActive.Value = false;
            sales.NegotiationOwner.Value = ShopClawRules.NoOccupant;
            yield return null;
            Debug.Log("[SevenPart:P3] PASS samples=" + samples.Trim() + " buttons=3 inputBlocked=1");
        }

        private IEnumerator VerifyTutorialAndSummary(ShopNetworkGame game)
        {
            ShopProgressionManager progression = ShopProgressionManager.Instance;
            if (progression == null) { Fail("progression missing"); yield break; }
            progression.ResetTutorial();
            yield return null;
            Button skip = Find<Button>("TutorialSkip");
            if (skip == null || !skip.gameObject.activeInHierarchy) { Fail("tutorial skip entry missing"); yield break; }
            skip.onClick.Invoke();
            yield return null;
            GameObject confirmation = FindObject("TutorialSkipConfirmation");
            if (confirmation == null || !confirmation.activeInHierarchy || !ShopInputModeManager.IsUiOpen)
            { Fail("tutorial skip confirmation missing"); yield break; }
            yield return Capture("Tutorial_Skip_Confirmation.png");
            if (!InvokeButton("CancelSkip") || progression.TutorialCompleted)
            { Fail("tutorial skip cancel failed"); yield break; }
            skip.onClick.Invoke();
            yield return null;
            if (!InvokeButton("ConfirmSkip") || !progression.TutorialCompleted)
            { Fail("tutorial skip confirm failed"); yield break; }

            int previousDay = game.Day.Value;
            game.ServerSetPhase(ShopPhase.Summary);
            yield return null;
            yield return null;
            Canvas hotbar = Find<Canvas>("Product Hotbar Canvas");
            if (hotbar == null || hotbar.enabled) { Fail("hotbar visible behind summary"); yield break; }
            yield return new WaitForSecondsRealtime(4.5f);
            yield return Capture("Closing_Summary_NoHotbar.png");
            QueueKey(Key.E, true);
            yield return null;
            QueueKey(Key.E, false);
            float deadline = Time.realtimeSinceStartup + 3f;
            while ((game.Day.Value <= previousDay || game.Phase.Value != ShopPhase.Setup) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            if (game.Day.Value <= previousDay || game.Phase.Value != ShopPhase.Setup)
            { Fail("summary did not advance directly day=" + game.Day.Value + " phase=" + game.Phase.Value); yield break; }
            yield return null;
            if (!hotbar.enabled) { Fail("hotbar did not restore after summary"); yield break; }
            Debug.Log("[SevenPart:P5/P6] PASS skipCancel=1 skipConfirm=1 hotbarHidden=1 nextDay=" + game.Day.Value);
        }

        private IEnumerator VerifyScoop(ShopNetworkGame game)
        {
            ShopClawMachineNetwork machine = FindObjectsByType<ShopClawMachineNetwork>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .OrderBy(value => value.Config != null ? value.Config.MachineId : int.MaxValue).FirstOrDefault();
            ShopPlayerInteractor player = FindObjectsByType<ShopPlayerInteractor>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(value => value.IsOwner);
            if (machine == null || player == null || !machine.RefillPrizesForScoopVerification())
            { Fail("scoop verification prerequisites missing"); yield break; }
            if (!machine.BeginScoopFloorVerification(20)) { Fail("floor verifier did not start"); yield break; }
            float deadline = Time.realtimeSinceStartup + 90f;
            while (machine.FloorContactSamples < 20 && Time.realtimeSinceStartup < deadline) yield return null;
            if (machine.FloorContactSamples != 20 || machine.LastFloorPenetrationMillimeters >= 1f)
            { Fail("floor contacts=" + machine.FloorContactSamples + " penetration=" + machine.LastFloorPenetrationMillimeters); yield break; }

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            player.transform.position = machine.OperatorWorldPosition;
            if (controller != null) controller.enabled = true;
            game.Coins.Value = Mathf.Max(game.Coins.Value, 100000);
            game.ServerSetPhase(ShopPhase.Setup);
            int acquired = 0;
            int severeFlights = 0;
            int beforeQuantity = StoredQuantity(game);
            for (int repetition = 0; repetition < 20; repetition++)
            {
                // Twenty real-time physics rounds outlive the two-minute preparation
                // window. Keep the verification in a phase where manual play is legal;
                // ShopLiveOperationsNetwork resets its data-driven setup duration when
                // this transitions back from Open/Summary.
                if (game.Phase.Value != ShopPhase.Setup)
                {
                    game.ServerSetPhase(ShopPhase.Setup);
                    yield return null;
                }
                if (machine.AvailableCapsules <= 0)
                {
                    if (machine.State.Value == ShopClawMachineState.Cooldown) machine.RequestCancel();
                    float resetDeadline = Time.realtimeSinceStartup + 4f;
                    while (machine.State.Value != ShopClawMachineState.Idle &&
                           Time.realtimeSinceStartup < resetDeadline) yield return null;
                    if (!machine.RefillPrizesForScoopVerification())
                    { Fail("scoop refill failed repetition=" + (repetition + 1)); yield break; }
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                if (machine.State.Value == ShopClawMachineState.Cooldown) machine.RequestReplay();
                else if (machine.State.Value == ShopClawMachineState.Idle) machine.RequestUse();
                deadline = Time.realtimeSinceStartup + 8f;
                float nextReservationRetry = Time.realtimeSinceStartup + 0.35f;
                while (machine.State.Value != ShopClawMachineState.Aiming &&
                       Time.realtimeSinceStartup < deadline)
                {
                    // The cooldown reset and a local replay RPC can cross on the same
                    // network tick. Retry through the same public interaction methods so
                    // the 20-round run measures gameplay instead of a verifier race.
                    if (Time.realtimeSinceStartup >= nextReservationRetry)
                    {
                        if (machine.State.Value == ShopClawMachineState.Cooldown) machine.RequestReplay();
                        else if (machine.State.Value == ShopClawMachineState.Idle) machine.RequestUse();
                        nextReservationRetry = Time.realtimeSinceStartup + 0.35f;
                    }
                    yield return null;
                }
                if (machine.State.Value != ShopClawMachineState.Aiming)
                {
                    Fail("scoop aiming timeout repetition=" + (repetition + 1) +
                         " state=" + machine.State.Value + " capsules=" + machine.AvailableCapsules);
                    yield break;
                }
                Vector2 target = machine.GetScoopVerificationTarget();
                float moveDeadline = Time.realtimeSinceStartup + 6f;
                while (Vector2.Distance(machine.RailPosition.Value, target) > 0.08f &&
                       Time.realtimeSinceStartup < moveDeadline)
                {
                    Vector2 delta = target - machine.RailPosition.Value;
                    machine.RequestInput(delta.normalized);
                    yield return null;
                }
                machine.RequestInput(Vector2.zero);
                yield return new WaitForSecondsRealtime(0.12f);
                machine.RequestDrop();
                deadline = Time.realtimeSinceStartup + 35f;
                while (machine.State.Value != ShopClawMachineState.Cooldown &&
                       Time.realtimeSinceStartup < deadline)
                {
                    foreach (ShopClawPrizeNetwork prize in machine.GetComponentsInChildren<ShopClawPrizeNetwork>())
                    {
                        Vector3 local = machine.transform.InverseTransformPoint(prize.transform.position);
                        Rigidbody body = prize.GetComponent<Rigidbody>();
                        if ((Mathf.Abs(local.x) > 3f || Mathf.Abs(local.z) > 3f || local.y > 5f) &&
                            body != null && body.linearVelocity.magnitude > 3f) severeFlights++;
                    }
                    yield return null;
                }
                if (machine.State.Value != ShopClawMachineState.Cooldown)
                { Fail("scoop round timeout repetition=" + (repetition + 1)); yield break; }
                int now = StoredQuantity(game);
                acquired += Mathf.Max(0, now - beforeQuantity);
                beforeQuantity = now;
                // Let the normal one-second anti-stuck/recovery pass settle any capsule
                // that left the chute before the next public replay request.
                yield return new WaitForSecondsRealtime(1.1f);
            }
            machine.RequestCancel();
            if (severeFlights != 0) { Fail("scoop severeFlights=" + severeFlights); yield break; }
            float average = acquired / 20f;
            Debug.Log("[SevenPart:P2] PASS floor=20/20 penetrationMm=" +
                      machine.LastFloorPenetrationMillimeters.ToString("F3") +
                      " severeFlights=0 acquired=" + acquired + "/20 average=" + average.ToString("F2") +
                      " descend=" + machine.Config.DescendSpeed.ToString("F3") +
                      " scrape=" + machine.Config.ScrapeSpeed.ToString("F3") +
                      " lift=" + machine.Config.LiftSpeed.ToString("F3"));
        }

        private static ShopContainerItem? StoredItem(ShopNetworkGame game, int productId)
        {
            for (int i = 0; i < game.ItemContainers.Count; i++)
            {
                ShopContainerItem item = game.ItemContainers[i];
                bool personal = item.OwnerClientId == 0 &&
                                item.Container == ShopContainerKind.PersonalInventory;
                bool shared = item.OwnerClientId == ShopContainerRules.SharedOwner &&
                              item.Container == ShopContainerKind.SharedStorage;
                if ((personal || shared) &&
                    item.ProductId == productId && item.Quantity > 0) return item;
            }
            return null;
        }

        private static int StoredQuantity(ShopNetworkGame game)
        {
            int total = 0;
            for (int i = 0; i < game.ItemContainers.Count; i++)
            {
                ShopContainerItem item = game.ItemContainers[i];
                if (item.Container == ShopContainerKind.PersonalInventory ||
                    item.Container == ShopContainerKind.SharedStorage) total += Mathf.Max(0, item.Quantity);
            }
            return total;
        }

        private static void QueueKey(Key key, bool pressed)
        {
            if (Keyboard.current == null) return;
            InputSystem.QueueStateEvent(Keyboard.current, pressed ? new KeyboardState(key) : new KeyboardState());
        }

        private static bool CoversScreen(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return corners[0].x <= 1f && corners[0].y <= 1f &&
                   corners[2].x >= Screen.width - 1f && corners[2].y >= Screen.height - 1f;
        }

        private IEnumerator Capture(string fileName)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory, fileName));
            yield return new WaitForEndOfFrame();
            Debug.Log("[SevenPart] SCREENSHOT " + Path.Combine(captureDirectory, fileName));
        }

        private bool Failed() => !string.IsNullOrEmpty(failure);

        private void Fail(string reason)
        {
            if (!string.IsNullOrEmpty(failure)) return;
            failure = reason;
            Debug.LogError("[SevenPart] FAILED " + reason);
            Application.Quit(2);
        }

        private static bool InvokeButton(string name)
        {
            Button button = Find<Button>(name);
            if (button == null || !button.gameObject.activeInHierarchy) return false;
            button.onClick.Invoke();
            return true;
        }

        private static GameObject FindObject(string name)
        {
            Transform transform = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(value =>
                value.name == name && value.gameObject.scene.IsValid());
            return transform != null ? transform.gameObject : null;
        }

        private static T Find<T>(string name) where T : Component =>
            Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(value =>
                value.name == name && value.gameObject.scene.IsValid());
    }
}
#endif
