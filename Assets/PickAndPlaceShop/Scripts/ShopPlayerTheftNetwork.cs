using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShopPlayerTheftNetwork : NetworkBehaviour
    {
        private readonly Collider[] hitBuffer = new Collider[64];
        private readonly HashSet<int> candidateIds = new();

        public NetworkVariable<float> Alert = new(0f);
        public NetworkVariable<bool> PoliceActive = new(false);
        public NetworkVariable<Vector3> PolicePosition = new(Vector3.zero);
        public NetworkVariable<int> ArrestSequence = new(0);
        public NetworkVariable<FixedString128Bytes> PersonalStatus =
            new(new FixedString128Bytes(string.Empty));

        private ShopTheftConfig config;
        private ShopPlayerAppearance appearance;
        private CoreMovement movement;
        private float localNextAttack;
        private float serverNextAttack;
        private float lastLocalAttackClick = float.NegativeInfinity;
        private float slowUntil;
        private float baseMoveSpeed;
        private float baseSprintSpeed;
        private int nextCombo;
        private float lastTheftTime = float.NegativeInfinity;
        private float chaseElapsed;
        private float arrestElapsed;
        private float nextPoliceTargetRefresh;
        private GameObject policeVisual;
        private NavMeshAgent policeAgent;
        private CharacterController policeController;
        private Animator policeAnimator;
        private Vector3 policeActualVelocity;
        private float policeAvoidanceTimer;
        private float policeAvoidanceSign = 1f;
        private ShopTheftHud hud;

        public float AlertNormalized => config == null ? 0f : Mathf.Clamp01(Alert.Value / config.MaximumAlert);
        public float AlertHudFadeSeconds => config != null ? config.AlertHudFadeSeconds : 0.45f;
        public int ServerAcceptedAttackCount { get; private set; }
        public float LastAcceptedAttackSpeed { get; private set; }

        private void Awake()
        {
            config = ShopTheftConfig.Load();
            appearance = GetComponent<ShopPlayerAppearance>();
            movement = GetComponent<CoreMovement>();
            if (movement != null)
            {
                baseMoveSpeed = movement.moveSpeed;
                baseSprintSpeed = movement.sprintSpeed;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (config == null)
            {
                Debug.LogError("[Theft] Resources/ShopTheftConfig 데이터가 없습니다.", this);
                enabled = false;
                return;
            }
            if (IsServer)
            {
                Alert.Value = 0f;
                PoliceActive.Value = false;
                PolicePosition.Value = transform.position;
            }
            if (IsOwner) hud = ShopTheftHud.Create(this);
        }

        public override void OnNetworkDespawn()
        {
            RestoreMovementSpeed();
            if (policeVisual != null) Destroy(policeVisual);
            if (hud != null) Destroy(hud.gameObject);
        }

        private void Update()
        {
            if (!IsSpawned || config == null) return;
            if (IsOwner) UpdateOwnerInput();
            UpdateMovementSlow();
            UpdatePoliceVisual();
        }

        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || config == null) return;
            ServerUpdateAlert();
            ServerUpdatePolice();
        }

        private void UpdateOwnerInput()
        {
            if (ShopLocalPauseState.IsPaused || !ShopInputModeManager.AllowsGameplay ||
                Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;

            float now = Time.unscaledTime;
            float clickInterval = float.IsNegativeInfinity(lastLocalAttackClick)
                ? config.AttackReferenceClickInterval
                : Mathf.Max(0f, now - lastLocalAttackClick);
            lastLocalAttackClick = now;
            if (now < localNextAttack) return;

            int combo = nextCombo;
            nextCombo = 1 - nextCombo;
            localNextAttack = now + config.AttackMinimumClickInterval;
            ApplyMovementSlow();
            RequestAttackRpc(combo, transform.forward, clickInterval);
        }

        [Rpc(SendTo.Server)]
        private void RequestAttackRpc(int combo, Vector3 requestedForward, float clickInterval,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || Time.time < serverNextAttack) return;
            serverNextAttack = Time.time + config.AttackMinimumClickInterval;
            Vector3 forward = Vector3.ProjectOnPlane(requestedForward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = transform.forward;
            float playbackSpeed = config.AttackSpeedForClickInterval(clickInterval);
            ServerAcceptedAttackCount++;
            LastAcceptedAttackSpeed = playbackSpeed;
            PlayAttackRpc(Mathf.Abs(combo) % 2, playbackSpeed);
            ServerResolveHit(forward);
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void PlayAttackRpc(int combo, float playbackSpeed)
        {
            if (appearance == null) appearance = GetComponent<ShopPlayerAppearance>();
            if (appearance == null || config == null) return;
            appearance.PlayAttack(combo, config.AttackTransitionSeconds,
                Mathf.Min(playbackSpeed, config.AttackMaximumAnimationSpeed));
            if (IsOwner) ApplyMovementSlow();
        }

        private void ServerResolveHit(Vector3 forward)
        {
            Vector3 origin = transform.position + Vector3.up + forward * config.HitForwardOffset;
            int count = Physics.OverlapSphereNonAlloc(origin, config.HitRadius, hitBuffer, ~0,
                QueryTriggerInteraction.Collide);
            candidateIds.Clear();
            Component best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider hit = hitBuffer[i];
                if (hit == null || hit.transform.IsChildOf(transform)) continue;
                Component candidate = hit.GetComponentInParent<ShopClawMachineNetwork>();
                candidate ??= hit.GetComponentInParent<ShopGachaMachineNetwork>();
                candidate ??= hit.GetComponentInParent<ShopKujiStationNetwork>();
                candidate ??= hit.GetComponentInParent<ShopCustomerNetwork>();
                candidate ??= hit.GetComponentInParent<ShopTrashSearchPoint>();
                if (candidate == null) continue;
                Vector3 closest = hit.ClosestPoint(transform.position + Vector3.up);
                Vector3 delta = closest - transform.position;
                if (!ShopTheftRules.IsInsideAttackArc(forward, delta, config.HitRadius, config.HitAngle)) continue;
                if (!candidateIds.Add(candidate.GetInstanceID())) continue;
                float distance = delta.sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = candidate;
            }

            bool reacted = best switch
            {
                ShopClawMachineNetwork claw => claw.ServerApplyTheftImpulse(OwnerClientId, config),
                ShopGachaMachineNetwork gacha => gacha.ServerApplyTheftHit(OwnerClientId,
                    config.AttackDamage, config),
                ShopKujiStationNetwork kuji => kuji.ServerApplyTheftHit(OwnerClientId,
                    config.AttackDamage, config),
                ShopCustomerNetwork customer => customer.ServerApplyPlayerAttack(OwnerClientId, forward),
                ShopTrashSearchPoint trash => trash.ServerApplyAttack(OwnerClientId),
                _ => false
            };
            PersonalStatus.Value = new FixedString128Bytes(reacted
                ? "기계에 공격이 적중했습니다. 주변을 조심하세요."
                : "공격이 허공을 갈랐습니다.");
        }

        public static void ServerReportTheftSuccess(ulong playerClientId, ShopTheftAction action)
        {
            foreach (ShopPlayerTheftNetwork player in
                     FindObjectsByType<ShopPlayerTheftNetwork>(FindObjectsSortMode.None))
            {
                if (player == null || !player.IsServer || player.OwnerClientId != playerClientId) continue;
                player.ServerAddAlert(action);
                return;
            }
        }

        public static void ServerAddExternalAlert(ulong playerClientId, float amount, string status)
        {
            foreach (ShopPlayerTheftNetwork player in
                     FindObjectsByType<ShopPlayerTheftNetwork>(FindObjectsSortMode.None))
            {
                if (player == null || !player.IsServer || player.OwnerClientId != playerClientId) continue;
                player.ServerAddAlertAmount(amount, status);
                return;
            }
        }

        private void ServerAddAlertAmount(float amount, string status)
        {
            if (!IsServer || config == null || amount <= 0f) return;
            lastTheftTime = Time.time;
            Alert.Value = Mathf.Clamp(Alert.Value + amount, 0f, config.MaximumAlert);
            if (!string.IsNullOrWhiteSpace(status))
                PersonalStatus.Value = new FixedString128Bytes(status);
            if (Alert.Value >= config.MaximumAlert && !PoliceActive.Value) ServerStartPolice();
        }

        private void ServerAddAlert(ShopTheftAction action)
        {
            lastTheftTime = Time.time;
            Alert.Value = Mathf.Clamp(Alert.Value + config.AlertFor(action), 0f, config.MaximumAlert);
            PersonalStatus.Value = new FixedString128Bytes(action switch
            {
                ShopTheftAction.ClawChute => "강탈 상품을 획득했습니다. 경고가 올라갑니다.",
                ShopTheftAction.GachaBreak => "가챠 기계 파손 보상을 강탈했습니다.",
                _ => "쿠지 기계 파손 보상을 강탈했습니다."
            });
            if (Alert.Value >= config.MaximumAlert && !PoliceActive.Value) ServerStartPolice();
        }

        private void ServerUpdateAlert()
        {
            if (PoliceActive.Value || Alert.Value <= 0f ||
                Time.time - lastTheftTime < config.AlertDecayDelay) return;
            float decay = IsInsideShop() ? config.InsideShopDecayPerSecond : config.OutsideShopDecayPerSecond;
            Alert.Value = Mathf.Max(0f, Alert.Value - decay * Time.fixedDeltaTime);
        }

        private bool IsInsideShop()
        {
            ShopInteractable[] interactables = FindObjectsByType<ShopInteractable>(FindObjectsSortMode.None);
            float radiusSqr = config.ShopSafeRadius * config.ShopSafeRadius;
            for (int i = 0; i < interactables.Length; i++)
            {
                if (interactables[i] != null && interactables[i].Action == ShopAction.Register &&
                    (interactables[i].transform.position - transform.position).sqrMagnitude <= radiusSqr)
                    return true;
            }
            return false;
        }

        private void ServerStartPolice()
        {
            PoliceActive.Value = true;
            chaseElapsed = 0f;
            arrestElapsed = 0f;
            Vector3 desired = transform.position - transform.forward * config.PoliceSpawnDistance;
            PolicePosition.Value = SampleNavMesh(desired, out Vector3 sampled) ? sampled : desired;
            EnsurePoliceVisual(true);
            if (policeAgent != null && policeAgent.isOnNavMesh) policeAgent.Warp(PolicePosition.Value);
            PersonalStatus.Value = new FixedString128Bytes("경찰이 출동했습니다! " +
                Mathf.CeilToInt(config.ChaseTimeoutSeconds) + "초 동안 도망치세요.");
        }

        private void ServerUpdatePolice()
        {
            if (!PoliceActive.Value)
            {
                if (policeVisual != null) Destroy(policeVisual);
                return;
            }
            EnsurePoliceVisual(true);
            chaseElapsed += Time.fixedDeltaTime;
            if (chaseElapsed >= config.ChaseTimeoutSeconds)
            {
                PoliceActive.Value = false;
                PersonalStatus.Value = new FixedString128Bytes("경찰이 추격을 포기했습니다. 경고는 매장 안에서 감소합니다.");
                return;
            }

            if (Time.time >= nextPoliceTargetRefresh)
            {
                nextPoliceTargetRefresh = Time.time + config.PoliceTargetRefreshSeconds;
                if (policeAgent != null && policeAgent.isOnNavMesh &&
                    SampleNavMesh(transform.position, out Vector3 destination))
                    policeAgent.SetDestination(destination);
            }
            MovePoliceAuthority();

            float distance = Vector3.Distance(PolicePosition.Value, transform.position);
            arrestElapsed = distance <= config.ArrestDistance
                ? arrestElapsed + Time.fixedDeltaTime
                : 0f;
            if (arrestElapsed >= config.ArrestHoldSeconds) ServerArrest();
        }

        private void ServerArrest()
        {
            PoliceActive.Value = false;
            Alert.Value = config.AlertAfterArrest;
            ArrestSequence.Value++;
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game != null)
            {
                int paid = Mathf.Min(game.Coins.Value, config.ArrestFine);
                game.Coins.Value = Mathf.Max(0, game.Coins.Value - paid);
                PersonalStatus.Value = new FixedString128Bytes("체포되었습니다. 벌금 " + paid +
                                                               "원을 내고 매장으로 돌아갑니다.");
            }
            Vector3 safe = FindArrestReturnPoint();
            Teleport(safe);
            TeleportOwnerRpc(safe);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void TeleportOwnerRpc(Vector3 position) => Teleport(position);

        private void Teleport(Vector3 position)
        {
            CharacterController controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            transform.position = position;
            if (controller != null) controller.enabled = true;
        }

        private Vector3 FindArrestReturnPoint()
        {
            ShopSpawnPadMarker[] pads = FindObjectsByType<ShopSpawnPadMarker>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (pads.Length == 0) return transform.position;
            ShopSpawnPadMarker nearest = pads[0];
            float best = float.MaxValue;
            foreach (ShopSpawnPadMarker pad in pads)
            {
                float distance = (pad.SafePosition - transform.position).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = pad;
            }
            return nearest.SafePosition + Vector3.up * config.ArrestTeleportHeightOffset;
        }

        private void UpdatePoliceVisual()
        {
            if (!PoliceActive.Value)
            {
                if (policeVisual != null) Destroy(policeVisual);
                return;
            }
            EnsurePoliceVisual(IsServer);
            Vector3 facingDirection = Vector3.zero;
            if (!IsServer && policeVisual != null)
            {
                Vector3 delta = PolicePosition.Value - policeVisual.transform.position;
                policeVisual.transform.position = Vector3.Lerp(policeVisual.transform.position,
                    PolicePosition.Value, config.PoliceProxyLerpSpeed * Time.deltaTime);
                facingDirection = delta;
            }
            else if (policeVisual != null)
            {
                facingDirection = policeActualVelocity.sqrMagnitude > 0.01f
                    ? policeActualVelocity
                    : transform.position - policeVisual.transform.position;
            }
            RotatePoliceTowards(facingDirection);
            if (policeAnimator != null)
            {
                policeAnimator.SetBool("Moving", PoliceActive.Value);
                policeAnimator.SetFloat("Speed", config.PoliceSpeed);
            }
        }

        private void RotatePoliceTowards(Vector3 direction)
        {
            if (policeVisual == null) return;
            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (direction.sqrMagnitude <= 0.01f) return;
            Quaternion target = Quaternion.LookRotation(direction.normalized, Vector3.up) *
                                Quaternion.Euler(0f, config.PoliceVisualYawOffset, 0f);
            policeVisual.transform.rotation = Quaternion.RotateTowards(policeVisual.transform.rotation,
                target, config.PoliceAngularSpeed * Time.deltaTime);
        }

        private void MovePoliceAuthority()
        {
            if (policeVisual == null || policeController == null || !policeController.enabled) return;
            Vector3 desired = policeAgent != null && policeAgent.enabled && policeAgent.isOnNavMesh &&
                              policeAgent.desiredVelocity.sqrMagnitude > 0.01f
                ? policeAgent.desiredVelocity
                : transform.position - policeVisual.transform.position;
            desired = Vector3.ProjectOnPlane(desired, Vector3.up);
            if (desired.sqrMagnitude <= 0.001f)
            {
                policeActualVelocity = Vector3.zero;
                return;
            }

            if (policeAvoidanceTimer > 0f)
            {
                policeAvoidanceTimer -= Time.fixedDeltaTime;
                desired = Quaternion.Euler(0f,
                    config.PoliceObstacleAvoidanceAngle * policeAvoidanceSign, 0f) * desired;
            }

            Vector3 before = policeVisual.transform.position;
            CollisionFlags collision = policeController.Move(desired.normalized *
                (config.PoliceSpeed * Time.fixedDeltaTime) + Vector3.down * (2f * Time.fixedDeltaTime));
            if ((collision & CollisionFlags.Sides) != 0 && policeAvoidanceTimer <= 0f)
            {
                policeAvoidanceSign *= -1f;
                policeAvoidanceTimer = config.PoliceObstacleAvoidanceSeconds;
            }

            Vector3 actual = policeVisual.transform.position - before;
            actual.y = 0f;
            policeActualVelocity = Time.fixedDeltaTime > 0f ? actual / Time.fixedDeltaTime : Vector3.zero;
            if (policeAgent != null && policeAgent.enabled && policeAgent.isOnNavMesh)
                policeAgent.nextPosition = policeVisual.transform.position;
            PolicePosition.Value = policeVisual.transform.position;
        }

        private void EnsurePoliceVisual(bool authoritative)
        {
            if (policeVisual != null) return;
            policeVisual = config.PoliceAppearancePrefab != null
                ? Instantiate(config.PoliceAppearancePrefab, PolicePosition.Value, Quaternion.identity)
                : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            policeVisual.name = authoritative ? "Police_Authority" : "Police_Proxy";
            foreach (Collider item in policeVisual.GetComponentsInChildren<Collider>(true)) item.enabled = false;
            policeAnimator = policeVisual.GetComponentInChildren<Animator>(true);
            if (!authoritative) return;
            policeController = policeVisual.GetComponent<CharacterController>();
            if (policeController == null) policeController = policeVisual.AddComponent<CharacterController>();
            policeController.radius = config.PoliceCollisionRadius;
            policeController.height = config.PoliceCollisionHeight;
            policeController.center = Vector3.up * (config.PoliceCollisionHeight * 0.5f);
            policeController.stepOffset = Mathf.Min(0.3f, config.PoliceCollisionHeight * 0.2f);
            policeController.slopeLimit = 50f;
            policeController.enabled = true;
            policeAgent = policeVisual.GetComponent<NavMeshAgent>();
            if (policeAgent == null) policeAgent = policeVisual.AddComponent<NavMeshAgent>();
            policeAgent.speed = config.PoliceSpeed;
            policeAgent.acceleration = config.PoliceSpeed * config.PoliceAccelerationMultiplier;
            policeAgent.angularSpeed = config.PoliceAngularSpeed;
            policeAgent.radius = config.PoliceCollisionRadius;
            policeAgent.height = config.PoliceCollisionHeight;
            policeAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            policeAgent.avoidancePriority = 20;
            policeAgent.updatePosition = false;
            policeAgent.updateRotation = false;
            policeAgent.stoppingDistance = config.ArrestDistance * config.PoliceStoppingDistanceMultiplier;
            if (SampleNavMesh(PolicePosition.Value, out Vector3 sampled)) policeAgent.Warp(sampled);
            else policeAgent.enabled = false;
        }

        private bool SampleNavMesh(Vector3 position, out Vector3 sampled)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit,
                    config.PoliceNavMeshSampleRadius, NavMesh.AllAreas))
            {
                sampled = hit.position;
                return true;
            }
            sampled = position;
            return false;
        }

        private void ApplyMovementSlow()
        {
            if (movement == null) return;
            slowUntil = Mathf.Max(slowUntil, Time.unscaledTime + config.AttackMoveSlowSeconds);
            movement.moveSpeed = baseMoveSpeed * config.AttackMoveMultiplier;
            movement.sprintSpeed = baseSprintSpeed * config.AttackMoveMultiplier;
        }

        private void UpdateMovementSlow()
        {
            if (movement != null && Time.unscaledTime >= slowUntil) RestoreMovementSpeed();
        }

        private void RestoreMovementSpeed()
        {
            if (movement == null || baseMoveSpeed <= 0f) return;
            movement.moveSpeed = baseMoveSpeed;
            movement.sprintSpeed = baseSprintSpeed;
        }
    }

    public sealed class ShopTheftHud : MonoBehaviour
    {
        private ShopPlayerTheftNetwork owner;
        private Image fill;
        private Text label;
        private Text status;
        private CanvasGroup group;
        private int observedArrestSequence;
        private float statusVisibleUntil;

        public static ShopTheftHud Create(ShopPlayerTheftNetwork target)
        {
            GameObject root = new("PersonalAlertHUD", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(ShopTheftHud));
            DontDestroyOnLoad(root);
            ShopTheftHud hud = root.GetComponent<ShopTheftHud>();
            hud.owner = target;
            hud.Build();
            return hud;
        }

        private void Build()
        {
            Canvas canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32500;
            CanvasScaler scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            Image panel = CreateImage("AlertPanel", transform, new Color(0.06f, 0.035f, 0.03f, 0.9f));
            RectTransform panelRect = panel.rectTransform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -92f);
            panelRect.sizeDelta = new Vector2(460f, 66f);
            ShopUiSkin.Round(panel, 20);

            Image track = CreateImage("Track", panel.transform, new Color(0.2f, 0.14f, 0.12f, 1f));
            RectTransform trackRect = track.rectTransform;
            trackRect.anchorMin = new Vector2(0.04f, 0.18f);
            trackRect.anchorMax = new Vector2(0.96f, 0.5f);
            trackRect.offsetMin = trackRect.offsetMax = Vector2.zero;
            ShopUiSkin.Pill(track);
            fill = CreateImage("Fill", track.transform, ShopUiSkin.Danger);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            RectTransform fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;

            label = CreateText("Label", panel.transform, "경고 0%", 24, TextAnchor.MiddleCenter);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -2f);
            labelRect.sizeDelta = new Vector2(430f, 40f);
            label.verticalOverflow = VerticalWrapMode.Overflow;

            status = CreateText("Status", transform, string.Empty, 24, TextAnchor.MiddleCenter);
            RectTransform statusRect = status.rectTransform;
            statusRect.anchorMin = statusRect.anchorMax = new Vector2(0.5f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -168f);
            statusRect.sizeDelta = new Vector2(920f, 48f);
            status.color = new Color(1f, 0.86f, 0.55f, 1f);
        }

        private void Update()
        {
            if (owner == null) { Destroy(gameObject); return; }
            float value = owner.AlertNormalized;
            fill.fillAmount = value;
            label.text = owner.PoliceActive.Value
                ? "경찰 추격 중  ·  경고 " + Mathf.RoundToInt(value * 100f) + "%"
                : "경고 " + Mathf.RoundToInt(value * 100f) + "%";
            string message = owner.PersonalStatus.Value.ToString();
            if (!string.IsNullOrWhiteSpace(message) && status.text != message)
            {
                status.text = message;
                statusVisibleUntil = Time.unscaledTime + 4f;
            }
            if (owner.ArrestSequence.Value != observedArrestSequence)
            {
                observedArrestSequence = owner.ArrestSequence.Value;
                statusVisibleUntil = Time.unscaledTime + 6f;
            }
            status.enabled = Time.unscaledTime < statusVisibleUntil;
            bool shouldShow = value > 0.001f || owner.PoliceActive.Value || status.enabled;
            float fadeSeconds = owner.AlertHudFadeSeconds;
            group.alpha = Mathf.MoveTowards(group.alpha, shouldShow ? 1f : 0f,
                Time.unscaledDeltaTime / fadeSeconds);
        }

        private static Image CreateImage(string objectName, Transform parent, Color color)
        {
            Image image = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
                .GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(string objectName, Transform parent, string content,
            int size, TextAnchor anchor)
        {
            Text text = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text))
                .GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = ShopUiFonts.Bold;
            text.fontStyle = FontStyle.Normal;
            text.text = content;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }
}
