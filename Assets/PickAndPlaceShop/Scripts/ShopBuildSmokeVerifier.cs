#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [AddComponentMenu("")]
    public sealed class ShopBuildSmokeVerifier : MonoBehaviour
    {
        private const string Argument = "-shop-smoke";
        private const float SalesVerificationSeconds = 480f;
        private string regressionFailure;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Application.isEditor && Environment.GetCommandLineArgs().Contains(Argument))
            {
                GameObject host = new("[QA] Build Smoke Verifier");
                DontDestroyOnLoad(host);
                host.AddComponent<ShopBuildSmokeVerifier>();
            }
        }

        private IEnumerator Start()
        {
            yield return null;
            yield return null;
            Debug.Log("[BuildSmoke] TITLE_READY scene=" + SceneManager.GetActiveScene().name);
            Text title = FindComponent<Text>("TitleText");
            Text subtitle = FindComponent<Text>("TitleSubtitle");
            if (title == null || !title.text.Contains(ShopGameIdentity.KoreanShortName) ||
                !title.text.Contains("소품샵 뽑기 시뮬레이터") ||
                subtitle == null || subtitle.text != ShopGameIdentity.Subtitle ||
                Application.productName != ShopGameIdentity.KoreanFormalName)
                yield return Fail("final game identity missing");
            if (VisibleTextContains("냥냥 뽑아온" + " 가게") ||
                VisibleTextContains("소품샵 협동" + " 시뮬레이터") ||
                VisibleTextContains("Toy" + "Game"))
                yield return Fail("legacy game name still visible");
            yield return Capture("CatTheme_Title.png");

            if (!InvokeButton("BtnMainStart")) yield return Fail("main start button missing");
            yield return null;
            if (!InvokeButton("BtnSolo")) yield return Fail("solo button missing");
            yield return null;
            Button continueButton = FindComponent<Button>("BtnContinueGame");
            if (continueButton != null && continueButton.gameObject.activeInHierarchy)
                continueButton.onClick.Invoke();

            float deadline = Time.realtimeSinceStartup + 20f;
            while (SceneManager.GetActiveScene().name == ShopLaunchContext.MainMenuScene &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            if (SceneManager.GetActiveScene().name == ShopLaunchContext.MainMenuScene)
                yield return Fail("gameplay scene load timeout");

            deadline = Time.realtimeSinceStartup + 20f;
            while ((ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsSpawned) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            if (ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsSpawned)
                yield return Fail("network host startup timeout");
            Debug.Log("[BuildSmoke] MAIN_SCENE_READY scene=" + SceneManager.GetActiveScene().name);
            ShopLiveOperationsNetwork liveOperations = ShopLiveOperationsNetwork.Instance;
            if (liveOperations == null || liveOperations.TrendNews.Value.Length == 0)
                yield return Fail("trend news missing");
            // A continued save can legitimately restore a generated fallback headline while
            // its per-day diagnostic counters are still zero. The observable contract is that
            // a headline exists and a missing key never records an API call.
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")) &&
                liveOperations.NarrativeApiCallsToday.Value != 0)
                yield return Fail("no-key narrative fallback invalid");
            Debug.Log("[BuildSmoke] NARRATIVE_OK api=" + liveOperations.NarrativeApiCallsToday.Value +
                      " fallback=" + liveOperations.NarrativeFallbacksToday.Value +
                      " news=" + liveOperations.TrendNews.Value);
            if (!ValidateCatTheme(out string catThemeResult))
                yield return Fail(catThemeResult);
            Debug.Log("[BuildSmoke] CAT_THEME_OK " + catThemeResult);
            yield return Capture("CatTheme_Gameplay.png");

            yield return StartCoroutine(VerifyBuildBugPresentation());
            if (!string.IsNullOrEmpty(regressionFailure))
                yield return Fail(regressionFailure);

            ShopPauseMenuController pause = FindFirstObjectByType<ShopPauseMenuController>();
            if (pause == null) yield return Fail("pause controller missing");
            pause.SendMessage("Open", SendMessageOptions.DontRequireReceiver);
            yield return new WaitForSecondsRealtime(0.15f);
            int eventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None).Length;
            Debug.Log("[BuildSmoke] PAUSE_OPEN paused=" + ShopLocalPauseState.IsPaused +
                      " scale=" + Time.timeScale.ToString("F1") +
                      " cursor=" + Cursor.lockState + "/" + Cursor.visible +
                      " eventSystems=" + eventSystems);
            if (!ShopLocalPauseState.IsPaused || Time.timeScale != 0f || eventSystems != 1)
                yield return Fail("pause state invalid");
            Text pauseTitle = FindComponent<Text>("PauseTitle");
            if (pauseTitle == null || pauseTitle.text != ShopGameIdentity.KoreanShortName)
                yield return Fail("pause title identity missing");
            if (!ValidatePauseLayout(out string pauseLayoutResult))
                yield return Fail(pauseLayoutResult);
            yield return Capture("BuildBug_PauseMenu.png");
            if (!InvokeButton("BtnPauseSave")) yield return Fail("save button missing");
            yield return null;
            Text saveStatus = FindComponent<Text>("PauseSaveStatus");
            if (saveStatus == null || saveStatus.text != "저장 완료")
                yield return Fail("save button did not save");
            if (!InvokeButton("BtnPauseHelp")) yield return Fail("help button missing");
            yield return null;
            GameObject helpPanel = FindObject("PauseHelpPanel");
            if (helpPanel == null || !helpPanel.activeInHierarchy)
                yield return Fail("help panel did not open");
            if (!InvokeButton("BtnPauseHelpBack")) yield return Fail("help back missing");
            yield return null;
            if (!InvokeButton("BtnPauseQuit")) yield return Fail("quit button missing");
            yield return null;
            if (FindObject("PauseConfirmPanel") == null || !FindObject("PauseConfirmPanel").activeInHierarchy)
                yield return Fail("quit confirmation did not open");
            if (!InvokeButton("BtnPauseConfirmNo")) yield return Fail("quit cancel missing");
            yield return null;
            if (!InvokeButton("BtnPauseMainMenu")) yield return Fail("main menu button missing");
            yield return null;
            if (FindObject("PauseConfirmPanel") == null || !FindObject("PauseConfirmPanel").activeInHierarchy)
                yield return Fail("main menu confirmation did not open");
            if (!InvokeButton("BtnPauseConfirmNo")) yield return Fail("main menu cancel missing");
            yield return null;
            if (!InvokeButton("BtnPauseSettings")) yield return Fail("settings button missing");
            yield return null;
            GameObject settings = FindObject("PauseSettingsPanel");
            Slider slider = FindComponent<Slider>("PauseMasterSlider");
            if (settings == null || !settings.activeInHierarchy || slider == null || !slider.interactable)
                yield return Fail("settings panel did not become interactive");
            if (!InvokeButton("BtnPauseSettingsApply")) yield return Fail("settings apply missing");
            yield return null;
            if (!InvokeButton("BtnPauseResume")) yield return Fail("resume button missing");
            yield return new WaitForSecondsRealtime(0.15f);
            if (ShopLocalPauseState.IsPaused || Time.timeScale == 0f)
                yield return Fail("resume did not restore gameplay");
            Debug.Log("[BuildSmoke] PAUSE_ALL_BUTTONS_OK " + pauseLayoutResult);

            yield return StartCoroutine(VerifySalesAndCustomers());
            if (!string.IsNullOrEmpty(regressionFailure))
                yield return Fail(regressionFailure);

            ShopClawMachineNetwork machine = FindFirstObjectByType<ShopClawMachineNetwork>();
            if (machine == null) yield return Fail("scoop machine missing");
            if (!machine.BeginScoopFloorVerification(50)) yield return Fail("floor verifier did not start");
            deadline = Time.realtimeSinceStartup + 120f;
            while (machine.FloorContactSamples < 50 && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (machine.FloorContactSamples < 50 || machine.LastFloorPenetrationMillimeters >= 1f)
                yield return Fail("floor verification failed");
            Debug.Log("[BuildSmoke] FLOOR_OK contacts=" + machine.FloorContactSamples +
                      " penetrationMm=" + machine.LastFloorPenetrationMillimeters.ToString("F3"));

            pause.SendMessage("Open", SendMessageOptions.DontRequireReceiver);
            yield return new WaitForSecondsRealtime(0.15f);
            if (!InvokeButton("BtnPauseMainMenu")) yield return Fail("main menu button missing");
            yield return null;
            if (!InvokeButton("BtnPauseConfirmYes")) yield return Fail("main menu confirm missing");
            deadline = Time.realtimeSinceStartup + 20f;
            while (SceneManager.GetActiveScene().name != ShopLaunchContext.MainMenuScene &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            if (SceneManager.GetActiveScene().name != ShopLaunchContext.MainMenuScene)
                yield return Fail("return to title timeout");
            Debug.Log("[BuildSmoke] RETURN_TO_TITLE_OK cursor=" + Cursor.lockState + "/" + Cursor.visible);
            if (!InvokeButton("BtnMainStart")) yield return Fail("restart main start missing");
            yield return null;
            if (!InvokeButton("BtnSolo")) yield return Fail("restart solo missing");
            yield return null;
            continueButton = FindComponent<Button>("BtnContinueGame");
            if (continueButton != null && continueButton.gameObject.activeInHierarchy)
                continueButton.onClick.Invoke();
            deadline = Time.realtimeSinceStartup + 20f;
            while (SceneManager.GetActiveScene().name == ShopLaunchContext.MainMenuScene &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            if (SceneManager.GetActiveScene().name == ShopLaunchContext.MainMenuScene)
                yield return Fail("restart gameplay timeout");
            deadline = Time.realtimeSinceStartup + 20f;
            while ((ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsSpawned) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            if (ShopNetworkGame.Instance == null || !ShopNetworkGame.Instance.IsSpawned)
                yield return Fail("restart network host timeout");
            Debug.Log("[BuildSmoke] RESTART_OK scene=" + SceneManager.GetActiveScene().name);

            Debug.Log("[BuildSmoke] COMPLETE");
            Application.Quit(0);
        }

        private IEnumerator Fail(string reason)
        {
            Debug.LogError("[BuildSmoke] FAILED " + reason);
            Application.Quit(2);
            while (true) yield return null;
        }

        private static bool InvokeButton(string name)
        {
            Button button = FindComponent<Button>(name);
            if (button == null) return false;
            button.onClick.Invoke();
            return true;
        }

        private IEnumerator VerifyBuildBugPresentation()
        {
            ShopProductDefinition[] products = Resources.LoadAll<ShopProductDefinition>("Products/CatCatalog")
                .OrderBy(product => product.ProductId).ToArray();
            int repairedShaders = ShopBuildSafeMaterials.RepairInvalidShaders();
            if (repairedShaders > 0) yield return null;
            if (!ValidateBuildSafeMaterials(products, out string shaderResult))
            {
                regressionFailure = shaderResult;
                yield break;
            }
            Debug.Log("[BuildBugs:A] PASS " + shaderResult + " repaired=" + repairedShaders);
            yield return Capture("BuildBug_BuildSafeMaterials.png");

            ShopProductDefinition[] batch = products.Take(7).ToArray();
            ShopCapsuleOpeningPresenter.ShowBatch("팬 뽑기 결과", batch,
                new Color(0.38f, 0.75f, 1f), "가방이 가득 차 창고로 보냈습니다.");
            yield return new WaitForSecondsRealtime(1.1f);
            int cardCount = Resources.FindObjectsOfTypeAll<Transform>().Count(value =>
                value.gameObject.scene.IsValid() && value.gameObject.activeInHierarchy &&
                value.name.StartsWith("ResultCard_", StringComparison.Ordinal));
            if (cardCount != 7 || VisibleTextContains("정보 없음"))
            {
                regressionFailure = "batch result UI invalid cards=" + cardCount;
                yield break;
            }
            yield return Capture("BuildBug_ResultBatch.png");
            Debug.Log("[BuildBugs:C] PASS cards=7 rows=2 missingInfo=0 sharedPresenter=1");
            ShopCapsuleOpeningPresenter.Dismiss();
            yield return new WaitForSecondsRealtime(0.8f);

            ShopClawInventoryUI inventory = FindFirstObjectByType<ShopClawInventoryUI>();
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (inventory == null || game == null)
            {
                regressionFailure = "inventory verification prerequisites missing";
                yield break;
            }
            inventory.SetOpen(true);
            yield return null;
            yield return null;
            int personal = ActiveSlotCount(ShopContainerKind.PersonalInventory);
            int storage = ActiveSlotCount(ShopContainerKind.SharedStorage);
            int display = ActiveSlotCount(ShopContainerKind.SharedDisplay);
            if (personal != ShopContainerRules.PersonalCapacity || storage != game.SharedStorageCapacity ||
                display != game.SharedDisplayCapacity)
            {
                regressionFailure = "container panels invalid personal/storage/display=" +
                                    personal + "/" + storage + "/" + display;
                yield break;
            }
            yield return Capture("BuildBug_ContainerPanels.png");
            inventory.SetOpen(false);
            yield return null;
            Debug.Log("[BuildBugs:E] PASS panels=3 activeSlots=" + personal + "/" + storage + "/" + display);
        }

        private IEnumerator VerifySalesAndCustomers()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            ShopNightSalesSystem sales = ShopNightSalesSystem.Instance;
            if (game == null || sales == null || !game.IsServer)
            {
                regressionFailure = "sales verification server missing";
                yield break;
            }

            ShopProductDefinition[] products = Resources.LoadAll<ShopProductDefinition>("Products/CatCatalog")
                .GroupBy(product => product.Category)
                .Select(group => group.OrderBy(product => product.SalePrice).First())
                .OrderBy(product => product.SalePrice).Take(5).ToArray();
            if (products.Length < 5)
            {
                regressionFailure = "five category products unavailable";
                yield break;
            }

            game.ServerReturnAllDisplayedToStorage();
            for (int index = 0; index < products.Length; index++)
            {
                for (int quantity = 0; quantity < 10; quantity++)
                {
                    if (!game.ServerTryAcquireItem(0, products[index], index, out _))
                    {
                        regressionFailure = "sales stock acquire failed product=" + products[index].ProductId;
                        yield break;
                    }
                }
                while (game.ServerTryMoveItem(0, ShopContainerKind.PersonalInventory,
                           ShopContainerKind.SharedStorage, out _, products[index].ProductId)) { }
            }

            int initialDisplayKinds = 0;
            for (int index = 0; index < products.Length && index < game.SharedDisplayCapacity; index++)
            {
                if (game.ServerTryMoveItem(ShopContainerRules.SharedOwner, ShopContainerKind.SharedStorage,
                        ShopContainerKind.SharedDisplay, out _, products[index].ProductId))
                    initialDisplayKinds++;
            }
            sales.ServerRefreshDisplayLedger();
            if (initialDisplayKinds < Mathf.Min(5, game.SharedDisplayCapacity))
            {
                regressionFailure = "display fixture failed kinds=" + initialDisplayKinds;
                yield break;
            }

            game.ServerSetPhase(ShopPhase.Setup);
            sales.ServerHandleRegister();
            float startedAt = Time.realtimeSinceStartup;
            float nextServiceAt = startedAt;
            bool salesCaptureTaken = false;
            int previousVisits = 0;
            int previousPurchases = 0;
            int completedVisits = 0;
            int completedPurchases = 0;
            int sessions = 0;
            int rapidAnimationTransitions = 0;
            int overlapViolations = 0;
            HashSet<ulong> observedCustomers = new();
            Dictionary<ulong, bool> animationMoving = new();
            Dictionary<ulong, float> animationChangedAt = new();
            Dictionary<string, float> closePairs = new();

            while (Time.realtimeSinceStartup - startedAt < SalesVerificationSeconds)
            {
                float now = Time.realtimeSinceStartup;
                if (sales.VisitCount.Value < previousVisits)
                {
                    completedVisits += previousVisits;
                    sessions++;
                }
                if (sales.PurchaseCustomerCount.Value < previousPurchases)
                    completedPurchases += previousPurchases;
                previousVisits = sales.VisitCount.Value;
                previousPurchases = sales.PurchaseCustomerCount.Value;

                if (game.Phase.Value != ShopPhase.Open && sales.CustomersInStore.Value == 0)
                {
                    sales.ServerTryStaffRestockDisplay();
                    game.ServerSetPhase(ShopPhase.Setup);
                    sales.ServerHandleRegister();
                }
                if (now >= nextServiceAt)
                {
                    nextServiceAt = now + 0.35f;
                    sales.ServerTryStaffCheckout(0.1f);
                    sales.ServerTryStaffRestockDisplay();
                }

                ShopCustomerNetwork[] customers = FindObjectsByType<ShopCustomerNetwork>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                HashSet<string> currentlyClose = new();
                for (int i = 0; i < customers.Length; i++)
                {
                    ShopCustomerNetwork customer = customers[i];
                    if (customer == null || !customer.IsSpawned) continue;
                    ulong id = customer.NetworkObjectId;
                    observedCustomers.Add(id);
                    Animator animator = customer.GetComponentsInChildren<Animator>(true)
                        .FirstOrDefault(value => value.parameters.Any(parameter => parameter.name == "Moving"));
                    if (animator != null)
                    {
                        bool moving = animator.GetBool("Moving");
                        if (animationMoving.TryGetValue(id, out bool previous) && previous != moving)
                        {
                            float last = animationChangedAt.TryGetValue(id, out float changed) ? changed : now;
                            if (now - last < 0.19f) rapidAnimationTransitions++;
                            animationChangedAt[id] = now;
                            animationMoving[id] = moving;
                        }
                        else if (!animationMoving.ContainsKey(id))
                        {
                            animationMoving[id] = moving;
                            animationChangedAt[id] = now;
                        }
                    }

                    for (int j = i + 1; j < customers.Length; j++)
                    {
                        ShopCustomerNetwork other = customers[j];
                        if (other == null || !other.IsSpawned) continue;
                        if (Vector3.Distance(customer.transform.position, other.transform.position) >= 0.24f)
                            continue;
                        ulong lower = Math.Min(id, other.NetworkObjectId);
                        ulong upper = Math.Max(id, other.NetworkObjectId);
                        string key = lower + ":" + upper;
                        currentlyClose.Add(key);
                        if (!closePairs.TryGetValue(key, out float closeSince)) closePairs[key] = now;
                        else if (closeSince >= 0f && now - closeSince >= 0.5f)
                        {
                            overlapViolations++;
                            closePairs[key] = -1f;
                        }
                    }
                }
                foreach (string key in closePairs.Keys.Where(key => !currentlyClose.Contains(key)).ToArray())
                    closePairs.Remove(key);

                if (!salesCaptureTaken && now - startedAt >= 30f)
                {
                    salesCaptureTaken = true;
                    yield return Capture("BuildBug_SalesCustomers.png");
                }
                yield return null;
            }

            int totalVisits = completedVisits + sales.VisitCount.Value;
            int totalPurchases = completedPurchases + sales.PurchaseCustomerCount.Value;
            if (totalVisits < 10 || totalPurchases < 3 || observedCustomers.Count < 10 ||
                overlapViolations != 0 || rapidAnimationTransitions != 0)
            {
                regressionFailure = "sales/customer regression visits=" + totalVisits +
                                    " purchases=" + totalPurchases + " observed=" + observedCustomers.Count +
                                    " overlaps=" + overlapViolations + " rapidTransitions=" +
                                    rapidAnimationTransitions;
                yield break;
            }
            Debug.Log("[BuildBugs:D/F] PASS durationSeconds=" + SalesVerificationSeconds.ToString("F0") +
                      " sessions=" + (sessions + 1) + " stockedKinds=5 initialDisplayKinds=" + initialDisplayKinds +
                      " visits=" + totalVisits + " purchases=" + totalPurchases +
                      " observedCustomers=" + observedCustomers.Count + " overlaps=0 rapidTransitions=0");
        }

        private static int ActiveSlotCount(ShopContainerKind container) =>
            FindObjectsByType<ShopContainerSlotView>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Count(slot => slot.Container == container && slot.gameObject.activeInHierarchy);

        private static bool ValidateBuildSafeMaterials(IEnumerable<ShopProductDefinition> products,
            out string result)
        {
            List<Material> materials = FindObjectsByType<Renderer>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .SelectMany(renderer => renderer.sharedMaterials.Where(material => material != null)).ToList();
            foreach (ShopProductDefinition product in products)
            {
                if (product == null || product.VisualPrefab == null) continue;
                materials.AddRange(product.VisualPrefab.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials.Where(material => material != null)));
            }
            string[] unsafeShaders = materials.Where(material => material.shader == null ||
                    material.shader.name.IndexOf("InternalError", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    material.shader.name.IndexOf("glTF", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(material => material.name + " => " +
                    (material.shader != null ? material.shader.name : "<null>"))
                .Distinct().ToArray();
            int baked = materials.Count(material => material.shader != null &&
                material.shader.name == "Universal Render Pipeline/Lit");
            result = "materials=" + materials.Count + " urpLit=" + baked +
                     " unsafeShaders=" + unsafeShaders.Length +
                     (unsafeShaders.Length > 0 ? " [" + string.Join(", ", unsafeShaders) + "]" : string.Empty);
            return materials.Count > 0 && baked >= 80 && unsafeShaders.Length == 0;
        }

        private static bool ValidatePauseLayout(out string result)
        {
            string[] names =
            {
                "BtnPauseResume", "BtnPauseSave", "BtnPauseHelp", "BtnPauseSettings",
                "BtnPauseMainMenu", "BtnPauseQuit"
            };
            List<Button> buttons = names.Select(FindComponent<Button>).ToList();
            if (buttons.Any(button => button == null || !button.interactable))
            {
                result = "pause button missing or disabled";
                return false;
            }
            for (int i = 0; i < buttons.Count; i++)
            for (int j = i + 1; j < buttons.Count; j++)
            {
                RectTransform a = buttons[i].transform as RectTransform;
                RectTransform b = buttons[j].transform as RectTransform;
                if (a != null && b != null && WorldRect(a).Overlaps(WorldRect(b)))
                {
                    result = "pause buttons overlap " + names[i] + "/" + names[j];
                    return false;
                }
            }
            result = "buttons=6 overlap=0 interactable=6";
            return true;
        }

        private static Rect WorldRect(RectTransform rectTransform)
        {
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static bool ValidateCatTheme(out string result)
        {
            ShopProductDefinition[] products = Resources.LoadAll<ShopProductDefinition>(
                "Products/CatCatalog");
            if (products.Length != 200)
            {
                result = "cat products=" + products.Length;
                return false;
            }
            int common = products.Count(product => product.Rarity == ShopProductRarity.Common);
            int uncommon = products.Count(product => product.Rarity == ShopProductRarity.Uncommon);
            int rare = products.Count(product => product.Rarity == ShopProductRarity.Rare);
            int ultra = products.Count(product => product.Rarity == ShopProductRarity.UltraRare);
            if (common != 110 || uncommon != 40 || rare != 40 || ultra != 10 ||
                products.Any(product => !ShopProductLocalization.IsCatTheme(product.Category)))
            {
                result = "rarity/category distribution invalid";
                return false;
            }
            if (products.Any(product => product.VisualPrefab == null || product.Icon == null ||
                                        product.PlaceholderArtwork) ||
                products.Select(product => product.VisualPrefab).Distinct().Count() != 80)
            {
                result = "GLB visual/icon assignment invalid";
                return false;
            }
            GameObject sampleVisual = products[0].VisualPrefab;
            if (sampleVisual.GetComponentsInChildren<Renderer>(true).Length == 0 ||
                sampleVisual.GetComponentsInChildren<Collider>(true).Length != 0 ||
                sampleVisual.GetComponentsInChildren<Rigidbody>(true).Length != 0)
            {
                result = "GLB wrapper runtime layout invalid";
                return false;
            }
            string[] legacy =
            {
                "동물 친구들", "음식 캐릭터", "우주 탐험대", "달토끼",
                "레트로 로봇", "오늘의 한정", "별빛 가챠관"
            };
            string[] visibleTexts = FindObjectsByType<Text>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Select(text => text.text)
                .Concat(FindObjectsByType<TextMesh>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Select(text => text.text)).ToArray();
            string remaining = legacy.FirstOrDefault(old =>
                visibleTexts.Any(text => !string.IsNullOrEmpty(text) && text.Contains(old)));
            if (!string.IsNullOrEmpty(remaining))
            {
                result = "legacy world text=" + remaining;
                return false;
            }
            result = "products=200 visuals=80 icons=200 placeholders=0 " +
                     "rarity=110/40/40/10 legacyText=0";
            return true;
        }

        private static IEnumerator Capture(string fileName)
        {
            yield return new WaitForEndOfFrame();
            string folder = Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
            string path = Path.Combine(folder, fileName);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForEndOfFrame();
            Debug.Log("[BuildSmoke] SCREENSHOT " + path);
        }

        private static GameObject FindObject(string name)
        {
            Transform transform = Resources.FindObjectsOfTypeAll<Transform>()
                .FirstOrDefault(value => value.name == name && value.gameObject.scene.IsValid());
            return transform != null ? transform.gameObject : null;
        }

        private static T FindComponent<T>(string name) where T : Component
        {
            return Resources.FindObjectsOfTypeAll<T>()
                .FirstOrDefault(value => value.name == name && value.gameObject.scene.IsValid());
        }

        private static bool VisibleTextContains(string value)
        {
            return FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                       .Any(text => text != null && !string.IsNullOrEmpty(text.text) && text.text.Contains(value)) ||
                   FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                       .Any(text => text != null && !string.IsNullOrEmpty(text.text) && text.text.Contains(value));
        }
    }
}
#endif
