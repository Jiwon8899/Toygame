using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [Serializable]
    public struct ShopCurationPlacement : INetworkSerializable, IEquatable<ShopCurationPlacement>
    {
        public int PlacementId;
        public int ProductId;
        public Vector3 Position;
        public Vector3 Size;
        public float Yaw;
        public ShopProductRarity Rarity;
        public ShopAppraisalGrade AppraisalGrade;
        public ulong InstanceId;
        public bool Automatic;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlacementId);
            serializer.SerializeValue(ref ProductId);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Size);
            serializer.SerializeValue(ref Yaw);
            byte rarity = (byte)Rarity;
            serializer.SerializeValue(ref rarity);
            if (serializer.IsReader) Rarity = (ShopProductRarity)rarity;
            byte grade = (byte)AppraisalGrade;
            serializer.SerializeValue(ref grade);
            if (serializer.IsReader) AppraisalGrade = (ShopAppraisalGrade)grade;
            serializer.SerializeValue(ref InstanceId);
            serializer.SerializeValue(ref Automatic);
        }

        public bool Equals(ShopCurationPlacement other) => PlacementId == other.PlacementId &&
            ProductId == other.ProductId && Position == other.Position && Size == other.Size &&
            Mathf.Approximately(Yaw, other.Yaw) && Rarity == other.Rarity &&
            AppraisalGrade == other.AppraisalGrade && InstanceId == other.InstanceId &&
            Automatic == other.Automatic;
        public override bool Equals(object obj) => obj is ShopCurationPlacement other && Equals(other);
        public override int GetHashCode() => PlacementId;
    }

    public sealed class ShopCurationPlacedView : MonoBehaviour
    {
        public int PlacementId { get; private set; }
        public void Configure(int id) => PlacementId = id;
    }

    [DefaultExecutionOrder(470)]
    public sealed class ShopCurationSystem : MonoBehaviour
    {
        public static ShopCurationSystem Instance { get; private set; }
        public static bool IsHoldingLocal => Instance != null && Instance.heldVisual != null;
        public static bool IsTargetingPlacedLocal => Instance != null && Instance.targetedPlacement != null;

        private ShopDifferentiationConfig config;
        private ShopNetworkGame observedGame;
        private ShopDisplayShelfAnchors shelf;
        private readonly List<GameObject> placementVisuals = new();
        private readonly List<Canvas> hiddenCanvases = new();
        private Text scoreText;
        private Text helpText;
        private Canvas scoreCanvas;
        private Canvas photoCanvas;
        private Text photoText;
        private bool placementsDirty = true;

        private ShopContainerKind heldContainer;
        private int heldSlot = -1;
        private ShopContainerItem heldItem;
        private GameObject heldVisual;
        private readonly Dictionary<Renderer, Color> heldOriginalColors = new();
        private Vector3 heldSize = new(0.3f, 0.3f, 0.3f);
        private float heldYaw;
        private bool ghostCanPlace;
        private Vector3 ghostPosition;
        private ShopCurationPlacedView targetedPlacement;

        private bool photoMode;
        private Camera photoCamera;
        private Vector3 savedCameraPosition;
        private Quaternion savedCameraRotation;
        private float photoPitch;
        private string lastScreenshotPath;
        private bool photoCapturePending;
        private readonly Image[] hotbarIcons = new Image[5];
        private readonly Image[] hotbarBackgrounds = new Image[5];
        private readonly Text[] hotbarLabels = new Text[5];
        private Canvas hotbarCanvas;
        private ShopContainerItem? hotbarAssignmentCandidate;
        private int activeHotbarSlot = -1;
        private float nextHotbarRefresh;
        private bool coordinatorReportOpen;
        private int coordinatorReportOpenedFrame = -1;

        public int CurrentScoreAverage => observedGame == null ? 0 : Mathf.RoundToInt(
            (observedGame.CurationClusterScore.Value + observedGame.CurationSymmetryScore.Value +
             observedGame.CurationRarityScore.Value + observedGame.CurationDensityScore.Value) / 4f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject host = new("[Shop] Curation System");
            DontDestroyOnLoad(host);
            Instance = host.AddComponent<ShopCurationSystem>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            config = ShopDifferentiationConfig.Load();
            BuildUi();
        }

        private void OnDestroy()
        {
            DetachGame();
            CloseCoordinatorReport();
            CancelHolding();
            ExitPhotoMode();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            AttachGame();
            if (observedGame == null || config == null) return;
            if (shelf == null)
            {
                shelf = FindFirstObjectByType<ShopDisplayShelfAnchors>();
                if (shelf == null)
                {
                    ShopShelfVisual legacy = FindFirstObjectByType<ShopShelfVisual>();
                    if (legacy != null) shelf = legacy.gameObject.GetComponent<ShopDisplayShelfAnchors>() ??
                                                 legacy.gameObject.AddComponent<ShopDisplayShelfAnchors>();
                }
                shelf?.EnsureAnchors();
            }
            if (placementsDirty) RebuildPlacementVisuals();
            UpdateScorePanel();
            if (Time.unscaledTime >= nextHotbarRefresh)
            {
                nextHotbarRefresh = Time.unscaledTime + 0.2f;
                RefreshHotbar();
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || ShopLocalPauseState.IsPaused) return;
            if (coordinatorReportOpen)
            {
                if (Time.frameCount > coordinatorReportOpenedFrame &&
                    (keyboard.eKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame))
                    CloseCoordinatorReport();
                return;
            }
            if (HandleHotbarInput(keyboard)) return;
            if (photoMode) { UpdatePhotoMode(keyboard); return; }
            if (keyboard.pKey.wasPressedThisFrame) { EnterPhotoMode(); return; }
            if (heldVisual != null) UpdateHolding(keyboard);
            else UpdatePlacedTarget(keyboard);
        }

        private void AttachGame()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (observedGame == game) return;
            DetachGame();
            observedGame = game;
            if (observedGame != null) observedGame.CurationPlacements.OnListChanged += PlacementChanged;
            placementsDirty = true;
        }

        private void DetachGame()
        {
            if (observedGame != null) observedGame.CurationPlacements.OnListChanged -= PlacementChanged;
            observedGame = null;
        }

        private void PlacementChanged(NetworkListEvent<ShopCurationPlacement> _) => placementsDirty = true;

        public void BeginHolding(ShopContainerKind container, int slot, ShopContainerItem item)
        {
            if (container != ShopContainerKind.PersonalInventory &&
                container != ShopContainerKind.SharedStorage) return;
            CancelHolding();
            Transform player = FindLocalPlayer();
            ShopProductDefinition product = ShopProductVisuals.Find(item.ProductId);
            if (player == null || product == null) return;
            heldContainer = container;
            heldSlot = slot;
            heldItem = item;
            heldYaw = 0f;
            heldVisual = ShopProductVisuals.Instantiate(product, player);
            if (heldVisual == null) return;
            heldVisual.name = "Curation Held Product";
            heldVisual.transform.localPosition = new Vector3(0f, 1.05f, 0.8f);
            heldVisual.transform.localRotation = Quaternion.identity;
            CaptureHeldColors();
            foreach (Collider collider in heldVisual.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            heldSize = MeasureVisual(heldVisual);
            SetVisualGhost(heldVisual, false, true);
            if (helpText != null) helpText.text = "Q/E 회전 · 선반 앞 E/Space 배치 · F 인벤토리로 되돌리기";
        }

        public void SetHotbarAssignmentCandidate(ShopContainerItem item)
        {
            if (item.Container == ShopContainerKind.PersonalInventory)
                hotbarAssignmentCandidate = item;
        }

        public void ShowCoordinatorReport()
        {
            if ((ShopProgressionManager.Instance?.ExpansionLevel ?? 1) < 4 || scoreCanvas == null) return;
            UpdateScorePanel();
            coordinatorReportOpen = true;
            coordinatorReportOpenedFrame = Time.frameCount;
            scoreCanvas.enabled = true;
            if (helpText != null) helpText.text = "진열 코디네이터: 네 가지 점수를 함께 보고 진열을 조정해 보세요. · E/Esc 닫기";
            ShopInputModeManager.Push(this, ShopInputMode.UI);
        }

        private void CloseCoordinatorReport()
        {
            if (!coordinatorReportOpen) return;
            coordinatorReportOpen = false;
            if (scoreCanvas != null) scoreCanvas.enabled = false;
            ShopInputModeManager.Pop(this);
        }

        public void CancelHolding()
        {
            if (heldVisual != null) Destroy(heldVisual);
            heldOriginalColors.Clear();
            heldVisual = null;
            heldSlot = -1;
            ghostCanPlace = false;
            if (helpText != null) helpText.text = "I 상품 선택 · P 포토 모드 · 자동 정렬은 선반 옆 버튼";
        }

        private void UpdateHolding(Keyboard keyboard)
        {
            if (!ShopInputModeManager.AllowsGameplay) return;
            float turn = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
            heldYaw = Mathf.Repeat(heldYaw + turn * config.ShelfPlacementRotationSpeed * Time.deltaTime, 360f);
            Transform player = FindLocalPlayer();
            if (player == null) { CancelHolding(); return; }
            Transform handAnchor = FindRightHand(player);
            Vector3 hand = handAnchor != null
                ? handAnchor.position + handAnchor.forward * 0.12f + handAnchor.up * 0.06f
                : player.position + player.forward * 0.55f + Vector3.up * 1.05f;
            ghostCanPlace = TryFindGhostPosition(out ghostPosition) &&
                            ServerPositionValid(ghostPosition, heldSize, heldYaw, -1, out _);
            Quaternion handRotation = handAnchor != null
                ? handAnchor.rotation * Quaternion.Euler(20f, 90f, 0f)
                : Quaternion.Euler(0f, heldYaw, 0f);
            heldVisual.transform.SetPositionAndRotation(ghostCanPlace ? ghostPosition : hand,
                ghostCanPlace ? Quaternion.Euler(0f, heldYaw, 0f) : handRotation);
            SetVisualGhost(heldVisual, ghostCanPlace, ghostCanPlace);
            bool confirm = keyboard.spaceKey.wasPressedThisFrame ||
                           keyboard.eKey.wasPressedThisFrame && ghostCanPlace;
            if (confirm && ghostCanPlace)
            {
                observedGame.RequestCurationPlacement(heldContainer, heldSlot, ghostPosition,
                    heldYaw, heldSize);
                CancelHolding();
            }
            else if (keyboard.fKey.wasPressedThisFrame) CancelHolding();
        }

        private void UpdatePlacedTarget(Keyboard keyboard)
        {
            targetedPlacement = null;
            Camera camera = Camera.main;
            if (camera == null) return;
            if (Physics.Raycast(camera.transform.position, camera.transform.forward, out RaycastHit hit, 3.5f,
                    ~0, QueryTriggerInteraction.Collide))
                targetedPlacement = hit.collider.GetComponentInParent<ShopCurationPlacedView>();
            if (targetedPlacement != null)
            {
                if (helpText != null) helpText.text = "[E] 진열 상품 다시 들기 · P 포토 모드";
                if (keyboard.eKey.wasPressedThisFrame)
                    observedGame.RequestCurationRemoval(targetedPlacement.PlacementId);
            }
            else if (helpText != null)
                helpText.text = "I 상품 선택 · P 포토 모드 · 자동 정렬은 선반 옆 버튼";
        }

        private bool TryFindGhostPosition(out Vector3 position)
        {
            position = default;
            if (!TryGetShelfBounds(out Bounds bounds, out List<float> tiers)) return false;
            Transform player = FindLocalPlayer();
            if (player == null || Vector3.Distance(player.position, bounds.center) > 6f) return false;
            Camera camera = Camera.main;
            Vector3 aim = camera != null ? camera.transform.position + camera.transform.forward * 3f
                : player.position + player.forward * 2f;
            float y = tiers[0];
            float best = float.MaxValue;
            for (int i = 0; i < tiers.Count; i++)
            {
                float distance = Mathf.Abs(tiers[i] - aim.y);
                if (distance < best) { best = distance; y = tiers[i]; }
            }
            position = new Vector3(Mathf.Clamp(aim.x, bounds.min.x + heldSize.x * 0.5f,
                bounds.max.x - heldSize.x * 0.5f), y,
                Mathf.Clamp(aim.z, bounds.min.z + heldSize.z * 0.5f,
                    bounds.max.z - heldSize.z * 0.5f));
            return true;
        }

        public bool ServerTryPlace(ulong requester, ShopContainerKind source, int sourceSlot,
            Vector3 position, float yaw, Vector3 requestedSize)
        {
            if (observedGame == null || !observedGame.IsServer || config == null) return false;
            if (observedGame.CurationPlacements.Count >= config.MaximumCurationPlacements)
            {
                observedGame.ServerSetEvent("자유 진열 한도에 도달했습니다.");
                return false;
            }
            Vector3 size = new(Mathf.Clamp(requestedSize.x, 0.08f, 1.2f),
                Mathf.Clamp(requestedSize.y, 0.08f, 1.5f), Mathf.Clamp(requestedSize.z, 0.08f, 1.2f));
            if (!ServerPositionValid(position, size, yaw, -1, out string reason))
            {
                observedGame.ServerSetEvent("배치 거부: " + reason + " 상품은 인벤토리에 유지됩니다.");
                return false;
            }
            if (!observedGame.ServerTryMoveOwnedSlotToDisplay(requester, source, sourceSlot,
                    config.MaximumCurationPlacements, out ShopContainerItem moved))
            {
                observedGame.ServerSetEvent("상품 상태가 바뀌어 배치하지 못했습니다. 인벤토리를 확인해 주세요.");
                return false;
            }
            ShopCurationPlacement placement = new()
            {
                PlacementId = observedGame.CurationNextPlacementId.Value++,
                ProductId = moved.ProductId,
                Position = position,
                Size = size,
                Yaw = yaw,
                Rarity = moved.Rarity,
                AppraisalGrade = moved.AppraisalGrade,
                InstanceId = moved.InstanceId,
                Automatic = false
            };
            observedGame.CurationPlacements.Add(placement);
            observedGame.CurationAutomatic.Value = false;
            ServerRecalculateScores();
            observedGame.ServerSetEvent(moved.DisplayName + "을(를) 자유 진열했습니다.");
            return true;
        }

        public bool ServerTryRemove(ulong requester, int placementId)
        {
            if (observedGame == null || !observedGame.IsServer) return false;
            for (int i = 0; i < observedGame.CurationPlacements.Count; i++)
            {
                ShopCurationPlacement placement = observedGame.CurationPlacements[i];
                if (placement.PlacementId != placementId) continue;
                if (!observedGame.ServerTryReturnCurationPlacement(requester, placement))
                {
                    observedGame.ServerSetEvent("가방과 창고가 가득 차 진열을 해제할 수 없습니다.");
                    return false;
                }
                observedGame.CurationPlacements.RemoveAt(i);
                observedGame.CurationAutomatic.Value = false;
                ServerRecalculateScores();
                observedGame.ServerSetEvent("진열 상품을 다시 보관했습니다.");
                return true;
            }
            return false;
        }

        public void ServerRemoveSoldPlacement(ShopContainerItem sold)
        {
            if (observedGame == null || !observedGame.IsServer) return;
            for (int i = 0; i < observedGame.CurationPlacements.Count; i++)
            {
                ShopCurationPlacement placement = observedGame.CurationPlacements[i];
                if (placement.ProductId != sold.ProductId ||
                    sold.InstanceId != 0 && placement.InstanceId != sold.InstanceId) continue;
                observedGame.CurationPlacements.RemoveAt(i);
                ServerRecalculateScores();
                return;
            }
        }

        public void ServerAutoArrange()
        {
            if (observedGame == null || !observedGame.IsServer || config == null ||
                !TryGetShelfBounds(out Bounds bounds, out List<float> tiers)) return;
            observedGame.CurationPlacements.Clear();
            int placementId = observedGame.CurationNextPlacementId.Value;
            int index = 0;
            for (int itemIndex = 0; itemIndex < observedGame.ItemContainers.Count &&
                 index < config.MaximumCurationPlacements; itemIndex++)
            {
                ShopContainerItem item = observedGame.ItemContainers[itemIndex];
                if (item.Container != ShopContainerKind.SharedDisplay || item.Quantity <= 0) continue;
                for (int quantity = 0; quantity < item.Quantity && index < config.MaximumCurationPlacements; quantity++)
                {
                    int tier = index % tiers.Count;
                    int column = index / tiers.Count;
                    float x = Mathf.Lerp(bounds.min.x + 0.18f, bounds.max.x - 0.18f,
                        (column % 6) / 5f);
                    Vector3 size = Vector3.one * 0.26f;
                    ShopCurationPlacement placement = new()
                    {
                        PlacementId = placementId++, ProductId = item.ProductId,
                        Position = new Vector3(x, tiers[tier], bounds.center.z), Size = size,
                        Yaw = 0f, Rarity = item.Rarity, AppraisalGrade = item.AppraisalGrade,
                        InstanceId = item.InstanceId, Automatic = true
                    };
                    if (ServerPositionValid(placement.Position, size, 0f, -1, out _))
                        observedGame.CurationPlacements.Add(placement);
                    index++;
                }
            }
            observedGame.CurationNextPlacementId.Value = placementId;
            observedGame.CurationAutomatic.Value = true;
            ServerRecalculateScores();
            observedGame.ServerSetEvent("자동 정렬을 적용했습니다. 세부 점수는 기준값으로 고정됩니다.");
        }

        public bool ServerPositionValid(Vector3 position, Vector3 size, float yaw,
            int ignorePlacementId, out string reason)
        {
            reason = string.Empty;
            if (!TryGetShelfBounds(out Bounds bounds, out List<float> tiers))
            { reason = "선반 가이드를 찾지 못했습니다."; return false; }
            float radians = yaw * Mathf.Deg2Rad;
            float width = Mathf.Abs(Mathf.Cos(radians)) * size.x + Mathf.Abs(Mathf.Sin(radians)) * size.z;
            float depth = Mathf.Abs(Mathf.Sin(radians)) * size.x + Mathf.Abs(Mathf.Cos(radians)) * size.z;
            if (position.x - width * 0.5f < bounds.min.x || position.x + width * 0.5f > bounds.max.x ||
                position.z - depth * 0.5f < bounds.min.z || position.z + depth * 0.5f > bounds.max.z)
            { reason = "선반 밖입니다."; return false; }
            bool tierMatch = false;
            for (int i = 0; i < tiers.Count; i++) if (Mathf.Abs(position.y - tiers[i]) <= 0.08f) tierMatch = true;
            if (!tierMatch) { reason = "선반 단 위가 아닙니다."; return false; }
            if (observedGame == null) return true;
            for (int i = 0; i < observedGame.CurationPlacements.Count; i++)
            {
                ShopCurationPlacement other = observedGame.CurationPlacements[i];
                if (other.PlacementId == ignorePlacementId || Mathf.Abs(other.Position.y - position.y) >
                    Mathf.Max(size.y, other.Size.y) * 0.65f) continue;
                float otherRadians = other.Yaw * Mathf.Deg2Rad;
                float otherWidth = Mathf.Abs(Mathf.Cos(otherRadians)) * other.Size.x +
                                   Mathf.Abs(Mathf.Sin(otherRadians)) * other.Size.z;
                float otherDepth = Mathf.Abs(Mathf.Sin(otherRadians)) * other.Size.x +
                                   Mathf.Abs(Mathf.Cos(otherRadians)) * other.Size.z;
                if (Mathf.Abs(other.Position.x - position.x) < (otherWidth + width) * 0.5f &&
                    Mathf.Abs(other.Position.z - position.z) < (otherDepth + depth) * 0.5f)
                { reason = "다른 상품과 겹칩니다."; return false; }
            }
            return true;
        }

        public void ServerRecalculateScores()
        {
            if (observedGame == null || !observedGame.IsServer || config == null) return;
            if (observedGame.CurationAutomatic.Value)
            {
                int fixedScore = config.AutomaticLayoutScore;
                observedGame.CurationClusterScore.Value = fixedScore;
                observedGame.CurationSymmetryScore.Value = fixedScore;
                observedGame.CurationRarityScore.Value = fixedScore;
                observedGame.CurationDensityScore.Value = fixedScore;
                return;
            }
            CalculateScores(observedGame.CurationPlacements, config,
                out int cluster, out int symmetry, out int rarity, out int density);
            observedGame.CurationClusterScore.Value = cluster;
            observedGame.CurationSymmetryScore.Value = symmetry;
            observedGame.CurationRarityScore.Value = rarity;
            observedGame.CurationDensityScore.Value = density;
        }

        public void CalculateScores(NetworkList<ShopCurationPlacement> placements,
            ShopDifferentiationConfig settings, out int cluster, out int symmetry,
            out int rarity, out int density)
        {
            int count = placements != null ? placements.Count : 0;
            if (count == 0) { cluster = symmetry = rarity = density = 0; return; }
            int adjacent = 0, matching = 0;
            for (int i = 0; i < count; i++)
            for (int j = i + 1; j < count; j++)
            {
                if (Vector3.Distance(placements[i].Position, placements[j].Position) > 1.15f) continue;
                adjacent++;
                ShopProductDefinition a = ShopProductVisuals.Find(placements[i].ProductId);
                ShopProductDefinition b = ShopProductVisuals.Find(placements[j].ProductId);
                if (a != null && b != null && a.Category == b.Category) matching++;
            }
            cluster = adjacent == 0 ? 35 : Mathf.RoundToInt(matching * 100f / adjacent);

            if (!TryGetShelfBounds(out Bounds bounds, out List<float> tiers))
            { symmetry = rarity = density = 0; return; }
            float symmetryError = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 mirror = placements[i].Position;
                mirror.x = bounds.center.x * 2f - mirror.x;
                float nearest = bounds.size.x;
                for (int j = 0; j < count; j++)
                    if (Mathf.Abs(placements[j].Position.y - mirror.y) < 0.1f)
                        nearest = Mathf.Min(nearest, Mathf.Abs(placements[j].Position.x - mirror.x));
                symmetryError += nearest / Mathf.Max(0.1f, bounds.size.x);
            }
            symmetry = Mathf.Clamp(Mathf.RoundToInt(100f * (1f - symmetryError / count)), 0, 100);

            int rareCount = 0;
            float rarityTotal = 0f;
            float middle = tiers[tiers.Count / 2];
            for (int i = 0; i < count; i++)
            {
                ShopCurationPlacement placement = placements[i];
                if (placement.Rarity < ShopProductRarity.Rare && !placement.AppraisalGrade.Equals(
                        ShopAppraisalGrade.A) && !placement.AppraisalGrade.Equals(ShopAppraisalGrade.S)) continue;
                rareCount++;
                float value = Mathf.Abs(placement.Position.y - middle) < 0.12f ? 100f :
                    placement.Position.y > middle ? 55f : 20f;
                value *= settings.AppraisalCurationMultiplier(placement.AppraisalGrade);
                rarityTotal += Mathf.Min(100f, value);
            }
            rarity = rareCount > 0 ? Mathf.RoundToInt(rarityTotal / rareCount) : 40;

            float occupied = 0f;
            for (int i = 0; i < count; i++) occupied += placements[i].Size.x * placements[i].Size.z;
            float available = Mathf.Max(0.01f, bounds.size.x * bounds.size.z * tiers.Count);
            float percent = occupied / available * 100f;
            Vector2 ideal = settings.IdealDensityPercent;
            density = percent >= ideal.x && percent <= ideal.y ? 100 : percent < ideal.x
                ? Mathf.RoundToInt(100f * percent / Mathf.Max(1f, ideal.x))
                : Mathf.RoundToInt(100f * Mathf.Clamp01(1f - (percent - ideal.y) /
                    Mathf.Max(1f, 100f - ideal.y)));
            density = Mathf.Clamp(density, 0, 100);
        }

        private bool TryGetShelfBounds(out Bounds bounds, out List<float> tiers)
        {
            bounds = default;
            tiers = new List<float>();
            if (shelf == null) return false;
            shelf.EnsureAnchors();
            IReadOnlyList<ShopDisplaySlotAnchor> anchors = shelf.Anchors;
            if (anchors == null || anchors.Count == 0) return false;
            Vector3 min = anchors[0].transform.position;
            Vector3 max = min;
            for (int i = 0; i < anchors.Count; i++)
            {
                Vector3 point = anchors[i].transform.position;
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
                bool exists = false;
                for (int t = 0; t < tiers.Count; t++) if (Mathf.Abs(tiers[t] - point.y) < 0.08f) exists = true;
                if (!exists) tiers.Add(point.y);
            }
            tiers.Sort();
            min += new Vector3(-0.55f, -0.05f, -0.42f);
            max += new Vector3(0.55f, 0.05f, 0.42f);
            bounds.SetMinMax(min, max);
            return true;
        }

        private void RebuildPlacementVisuals()
        {
            placementsDirty = false;
            foreach (GameObject visual in placementVisuals) if (visual != null) Destroy(visual);
            placementVisuals.Clear();
            if (observedGame == null) return;
            for (int i = 0; i < observedGame.CurationPlacements.Count; i++)
            {
                ShopCurationPlacement placement = observedGame.CurationPlacements[i];
                ShopProductDefinition product = ShopProductVisuals.Find(placement.ProductId);
                GameObject root = new("CurationPlacement_" + placement.PlacementId);
                root.transform.SetPositionAndRotation(placement.Position, Quaternion.Euler(0f, placement.Yaw, 0f));
                ShopCurationPlacedView view = root.AddComponent<ShopCurationPlacedView>();
                view.Configure(placement.PlacementId);
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.size = placement.Size;
                collider.center = Vector3.up * placement.Size.y * 0.5f;
                collider.isTrigger = true;
                GameObject visual = ShopProductVisuals.Instantiate(product, root.transform);
                if (visual != null) visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                placementVisuals.Add(root);
            }
        }

        private void UpdateScorePanel()
        {
            if (scoreText == null || observedGame == null || config == null) return;
            int average = CurrentScoreAverage;
            scoreText.text = "선반 평가 " + config.CurationGrade(average) + "등급\n" +
                             "군집 " + observedGame.CurationClusterScore.Value +
                             "  대칭 " + observedGame.CurationSymmetryScore.Value +
                             "  희귀도 노출 " + observedGame.CurationRarityScore.Value +
                             "  밀도 " + observedGame.CurationDensityScore.Value +
                             (observedGame.CurationAutomatic.Value ? "  [자동 정렬]" : "  [수동 진열]");
        }

        private void EnterPhotoMode()
        {
            if (photoMode || Camera.main == null) return;
            photoMode = true;
            photoCamera = Camera.main;
            savedCameraPosition = photoCamera.transform.position;
            savedCameraRotation = photoCamera.transform.rotation;
            photoPitch = photoCamera.transform.eulerAngles.x;
            hiddenCanvases.Clear();
            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas == photoCanvas || !canvas.enabled) continue;
                hiddenCanvases.Add(canvas);
                canvas.enabled = false;
            }
            if (photoCanvas != null) photoCanvas.enabled = true;
            if (photoText != null) photoText.text = "포토 모드 · WASD/Space/Ctrl 이동 · 마우스 시점 · Enter 촬영 · P/Esc 종료";
            ShopInputModeManager.Push(this, ShopInputMode.Photo);
        }

        private void UpdatePhotoMode(Keyboard keyboard)
        {
            if (photoCamera == null) { ExitPhotoMode(); return; }
            if (keyboard.pKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame)
            { ExitPhotoMode(); return; }
            Vector3 move = Vector3.zero;
            if (keyboard.wKey.isPressed) move += Vector3.forward;
            if (keyboard.sKey.isPressed) move += Vector3.back;
            if (keyboard.dKey.isPressed) move += Vector3.right;
            if (keyboard.aKey.isPressed) move += Vector3.left;
            if (keyboard.spaceKey.isPressed) move += Vector3.up;
            if (keyboard.leftCtrlKey.isPressed) move += Vector3.down;
            photoCamera.transform.Translate(move.normalized * 3f * Time.unscaledDeltaTime, Space.Self);
            if (Mouse.current != null)
            {
                Vector2 delta = Mouse.current.delta.ReadValue() * 0.08f;
                photoPitch = Mathf.Clamp(photoPitch - delta.y, -80f, 80f);
                float yaw = photoCamera.transform.eulerAngles.y + delta.x;
                photoCamera.transform.rotation = Quaternion.Euler(photoPitch, yaw, 0f);
            }
            if (keyboard.enterKey.wasPressedThisFrame) CapturePhoto();
        }

        private void CapturePhoto()
        {
            if (photoCapturePending) return;
            StartCoroutine(CapturePhotoRoutine());
        }

        private IEnumerator CapturePhotoRoutine()
        {
            photoCapturePending = true;
            string directory = Path.Combine(Application.persistentDataPath, "Screenshots");
            Directory.CreateDirectory(directory);
            lastScreenshotPath = Path.Combine(directory,
                "ToyGame_Curation_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            if (photoCanvas != null) photoCanvas.enabled = false;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(lastScreenshotPath);
            yield return null;
            if (photoMode && photoCanvas != null) photoCanvas.enabled = true;
            if (photoText != null) photoText.text = "저장 중: " + lastScreenshotPath + "\nEnter 다시 촬영 · P/Esc 종료";
            photoCapturePending = false;
        }

        private void ExitPhotoMode()
        {
            if (!photoMode) return;
            photoMode = false;
            if (photoCamera != null)
                photoCamera.transform.SetPositionAndRotation(savedCameraPosition, savedCameraRotation);
            foreach (Canvas canvas in hiddenCanvases) if (canvas != null) canvas.enabled = true;
            hiddenCanvases.Clear();
            if (photoCanvas != null) photoCanvas.enabled = false;
            ShopInputModeManager.Pop(this);
            if (!string.IsNullOrWhiteSpace(lastScreenshotPath))
                Debug.Log("[Curation Photo] 저장 완료: " + lastScreenshotPath);
        }

        private void BuildUi()
        {
            Font font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 24);
            GameObject canvasObject = new("Curation Score Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            scoreCanvas = canvasObject.GetComponent<Canvas>();
            scoreCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scoreCanvas.sortingOrder = 1500;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            GameObject panel = new("Shelf Evaluation", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(24f, -90f);
            rect.sizeDelta = new Vector2(690f, 105f);
            panel.GetComponent<Image>().color = new Color(0.025f, 0.055f, 0.075f, 0.88f);
            scoreText = CreateText(panel.transform, font, 24, TextAnchor.MiddleLeft);
            scoreText.rectTransform.offsetMin = new Vector2(18f, 34f);
            scoreText.rectTransform.offsetMax = new Vector2(-18f, -8f);
            helpText = CreateText(panel.transform, font, 18, TextAnchor.MiddleLeft);
            helpText.rectTransform.offsetMin = new Vector2(18f, 7f);
            helpText.rectTransform.offsetMax = new Vector2(-18f, -68f);
            scoreCanvas.enabled = false;

            GameObject photoObject = new("Photo Mode Canvas", typeof(Canvas), typeof(CanvasScaler));
            photoObject.transform.SetParent(transform, false);
            photoCanvas = photoObject.GetComponent<Canvas>();
            photoCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            photoCanvas.sortingOrder = 32700;
            photoText = CreateText(photoObject.transform, font, 22, TextAnchor.UpperCenter);
            photoText.rectTransform.anchorMin = new Vector2(0f, 1f);
            photoText.rectTransform.anchorMax = new Vector2(1f, 1f);
            photoText.rectTransform.pivot = new Vector2(0.5f, 1f);
            photoText.rectTransform.anchoredPosition = new Vector2(0f, -24f);
            photoText.rectTransform.sizeDelta = new Vector2(0f, 90f);
            photoCanvas.enabled = false;
            BuildHotbarUi(font);
        }

        private void BuildHotbarUi(Font font)
        {
            GameObject canvasObject = new("Product Hotbar Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            hotbarCanvas = canvasObject.GetComponent<Canvas>();
            hotbarCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hotbarCanvas.sortingOrder = 1450;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject strip = new("Hotbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            strip.transform.SetParent(canvasObject.transform, false);
            RectTransform stripRect = strip.GetComponent<RectTransform>();
            stripRect.anchorMin = stripRect.anchorMax = new Vector2(0.5f, 0f);
            stripRect.pivot = new Vector2(0.5f, 0f);
            stripRect.anchoredPosition = new Vector2(0f, 18f);
            stripRect.sizeDelta = new Vector2(590f, 116f);
            strip.GetComponent<Image>().color = new Color(0.02f, 0.04f, 0.06f, 0.88f);

            for (int i = 0; i < 5; i++)
            {
                GameObject slot = new("Hotbar Slot " + (i + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                slot.transform.SetParent(strip.transform, false);
                RectTransform slotRect = slot.GetComponent<RectTransform>();
                slotRect.anchorMin = slotRect.anchorMax = new Vector2(0f, 0.5f);
                slotRect.pivot = new Vector2(0f, 0.5f);
                slotRect.anchoredPosition = new Vector2(12f + i * 114f, 8f);
                slotRect.sizeDelta = new Vector2(104f, 94f);
                hotbarBackgrounds[i] = slot.GetComponent<Image>();

                Image icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
                    .GetComponent<Image>();
                icon.transform.SetParent(slot.transform, false);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.rectTransform.anchorMin = new Vector2(0.15f, 0.28f);
                icon.rectTransform.anchorMax = new Vector2(0.85f, 0.95f);
                icon.rectTransform.offsetMin = icon.rectTransform.offsetMax = Vector2.zero;
                hotbarIcons[i] = icon;

                Text label = CreateText(slot.transform, font, 16, TextAnchor.LowerCenter);
                label.rectTransform.offsetMin = new Vector2(2f, 2f);
                label.rectTransform.offsetMax = new Vector2(-2f, -68f);
                hotbarLabels[i] = label;
            }
            RefreshHotbar();
        }

        private bool HandleHotbarInput(Keyboard keyboard)
        {
            int pressed = -1;
            if (keyboard.digit1Key.wasPressedThisFrame) pressed = 0;
            else if (keyboard.digit2Key.wasPressedThisFrame) pressed = 1;
            else if (keyboard.digit3Key.wasPressedThisFrame) pressed = 2;
            else if (keyboard.digit4Key.wasPressedThisFrame) pressed = 3;
            else if (keyboard.digit5Key.wasPressedThisFrame) pressed = 4;
            if (pressed < 0) return false;

            bool assign = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            if (assign)
            {
                if (!hotbarAssignmentCandidate.HasValue) return true;
                ShopProgressionManager.Instance?.SetHotbarProduct(pressed,
                    hotbarAssignmentCandidate.Value.ProductId);
                RefreshHotbar();
                return true;
            }
            if (!ShopInputModeManager.AllowsGameplay) return false;
            SelectHotbarSlot(pressed);
            return true;
        }

        private void SelectHotbarSlot(int slot)
        {
            if (activeHotbarSlot == slot && heldVisual != null)
            {
                CancelHolding();
                activeHotbarSlot = -1;
                ShopProgressionManager.Instance?.SetSelectedHotbarSlot(-1);
                RefreshHotbar();
                return;
            }
            int productId = ShopProgressionManager.Instance?.GetHotbarProductId(slot) ?? -1;
            if (productId < 0 || !TryFindPersonalProduct(productId, out ShopContainerItem item))
            {
                if (helpText != null) helpText.text = productId < 0
                    ? "이 단축 슬롯은 비어 있습니다. 인벤토리 상품 위에서 Shift+숫자로 등록하세요."
                    : "등록한 상품이 현재 개인 인벤토리에 없습니다.";
                return;
            }
            activeHotbarSlot = slot;
            ShopProgressionManager.Instance?.SetSelectedHotbarSlot(slot);
            BeginHolding(ShopContainerKind.PersonalInventory, item.SlotIndex, item);
            RefreshHotbar();
        }

        private bool TryFindPersonalProduct(int productId, out ShopContainerItem item)
        {
            item = default;
            if (observedGame == null || NetworkManager.Singleton == null) return false;
            ulong owner = NetworkManager.Singleton.LocalClientId;
            for (int i = 0; i < observedGame.ItemContainers.Count; i++)
            {
                ShopContainerItem candidate = observedGame.ItemContainers[i];
                if (candidate.OwnerClientId != owner || candidate.Container != ShopContainerKind.PersonalInventory ||
                    candidate.ProductId != productId || candidate.Quantity <= 0) continue;
                item = candidate;
                return true;
            }
            return false;
        }

        private void RefreshHotbar()
        {
            ShopProgressionManager manager = ShopProgressionManager.Instance;
            for (int i = 0; i < hotbarIcons.Length; i++)
            {
                if (hotbarIcons[i] == null) continue;
                int productId = manager?.GetHotbarProductId(i) ?? -1;
                ShopProductDefinition product = ShopProductVisuals.Find(productId);
                hotbarIcons[i].sprite = product != null ? product.Icon : null;
                hotbarIcons[i].enabled = hotbarIcons[i].sprite != null;
                hotbarLabels[i].text = (i + 1) + (product != null ? "  " + product.DisplayName : "  빈 슬롯");
                hotbarBackgrounds[i].color = activeHotbarSlot == i && heldVisual != null
                    ? new Color(0.92f, 0.62f, 0.12f, 0.96f)
                    : new Color(0.08f, 0.13f, 0.2f, 0.96f);
            }
        }

        private static Text CreateText(Transform parent, Font font, int size, TextAnchor anchor)
        {
            Text text = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text))
                .GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            return text;
        }

        private static Transform FindRightHand(Transform player)
        {
            if (player == null) return null;
            Animator animator = player.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                Transform bone = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (bone != null) return bone;
            }
            foreach (Transform candidate in player.GetComponentsInChildren<Transform>(true))
                if (candidate.name.IndexOf("RightHand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    candidate.name.IndexOf("Hand_R", StringComparison.OrdinalIgnoreCase) >= 0)
                    return candidate;
            return null;
        }

        private static Transform FindLocalPlayer()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.LocalClient != null && manager.LocalClient.PlayerObject != null)
                return manager.LocalClient.PlayerObject.transform;
            ShopPlayerInteractor[] players = FindObjectsByType<ShopPlayerInteractor>(FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++) if (players[i].IsOwner) return players[i].transform;
            return null;
        }

        private static Vector3 MeasureVisual(GameObject visual)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return Vector3.one * 0.3f;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            Vector3 size = bounds.size;
            return new Vector3(Mathf.Clamp(size.x, 0.08f, 1.2f),
                Mathf.Clamp(size.y, 0.08f, 1.5f), Mathf.Clamp(size.z, 0.08f, 1.2f));
        }

        private void CaptureHeldColors()
        {
            heldOriginalColors.Clear();
            if (heldVisual == null) return;
            foreach (Renderer renderer in heldVisual.GetComponentsInChildren<Renderer>(true))
            {
                Material material = renderer.material;
                Color original = Color.white;
                if (material.HasProperty("_BaseColor")) original = material.GetColor("_BaseColor");
                else if (material.HasProperty("_Color")) original = material.color;
                heldOriginalColors[renderer] = original;
            }
        }

        private void SetVisualGhost(GameObject visual, bool ghost, bool valid)
        {
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                Material material = renderer.material;
                Color original = heldOriginalColors.TryGetValue(renderer, out Color saved) ? saved : Color.white;
                Color tint = ghost
                    ? Color.Lerp(original, valid ? new Color(0.25f, 1f, 0.55f, 1f) :
                        new Color(1f, 0.2f, 0.2f, 1f), 0.55f)
                    : original;
                tint.a = ghost ? 0.48f : original.a;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
                else if (material.HasProperty("_Color")) material.color = tint;
                ConfigureMaterialSurface(material, ghost);
            }
        }

        private static void ConfigureMaterialSurface(Material material, bool transparent)
        {
            if (!material.HasProperty("_Surface")) return;
            material.SetFloat("_Surface", transparent ? 1f : 0f);
            material.SetFloat("_SrcBlend", transparent ? 5f : 1f);
            material.SetFloat("_DstBlend", transparent ? 10f : 0f);
            material.SetFloat("_ZWrite", transparent ? 0f : 1f);
            material.renderQueue = transparent ? 3000 : -1;
            if (transparent) material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }
}
