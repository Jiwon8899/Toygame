using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
    public sealed class ShopClawPrizeNetwork : NetworkBehaviour
    {
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private GameObject[] visualPrefabs;

        public NetworkVariable<ulong> MachineNetworkObjectId = new(0);
        public NetworkVariable<int> DefinitionIndex = new(0);
        public NetworkVariable<int> ProductId = new(0);
        public NetworkVariable<int> SpawnedRarity = new((int)ShopProductRarity.Common);
        public NetworkVariable<bool> Awarded = new(false);
        public NetworkVariable<float> PrizeWeight = new(0.8f);
        public NetworkVariable<float> GripDifficulty = new(0.5f);
        public NetworkVariable<float> GripModifier = new(0f);
        public NetworkVariable<float> SurfaceFriction = new(0.55f);
        public NetworkVariable<float> VisualSize = new(0.65f);
        public NetworkVariable<Color> VisualColor = new(Color.white);
        public NetworkVariable<int> VisualPrefabIndex = new(-1);
        public NetworkVariable<FixedString64Bytes> VisualDisplayName = new(new FixedString64Bytes(""));

        private Rigidbody body;
        private AudioSource collisionAudio;
        private AudioClip collisionClip;
        private static GameObject[] catalogPrefabs;

        public Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();
        public int VisualPrefabCount => visualPrefabs != null ? visualPrefabs.Length : 0;

        public static GameObject GetCatalogPrefab(int index)
        {
            return catalogPrefabs != null && index >= 0 && index < catalogPrefabs.Length
                ? catalogPrefabs[index]
                : null;
        }

        public static string GetCatalogName(int index)
        {
            GameObject prefab = GetCatalogPrefab(index);
            return prefab != null ? prefab.name : "상품";
        }

        public static int FindCatalogIndex(GameObject prefab)
        {
            if (prefab == null || catalogPrefabs == null) return -1;
            for (int i = 0; i < catalogPrefabs.Length; i++)
                if (catalogPrefabs[i] == prefab) return i;
            return -1;
        }

        public void Configure(Renderer renderer) => bodyRenderer = renderer;

#if UNITY_EDITOR
        public void EditorConfigureVisualPrefabs(GameObject[] prefabs)
        {
            visualPrefabs = prefabs;
        }
#endif

        public override void OnNetworkSpawn()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = !IsServer;
            EnsureSingleCapsuleCollider();
            collisionAudio = GetComponent<AudioSource>();
            if (collisionAudio == null) collisionAudio = gameObject.AddComponent<AudioSource>();
            collisionAudio.spatialBlend = 0.9f;
            collisionAudio.volume = 0.28f;
            collisionClip = CreateCollisionClip();
            if (visualPrefabs != null && visualPrefabs.Length > 0) catalogPrefabs = visualPrefabs;
            ApplyVisuals();
            VisualSize.OnValueChanged += OnSizeChanged;
            VisualColor.OnValueChanged += OnColorChanged;
            VisualPrefabIndex.OnValueChanged += OnVisualPrefabChanged;
        }

        public override void OnNetworkDespawn()
        {
            VisualSize.OnValueChanged -= OnSizeChanged;
            VisualColor.OnValueChanged -= OnColorChanged;
            VisualPrefabIndex.OnValueChanged -= OnVisualPrefabChanged;
        }

        public void ServerInitialize(ulong machineId, int definitionIndex, ShopClawPrizeDefinition definition,
            Vector3 position, Quaternion rotation, int visualPrefabIndex = -1,
            ShopProductRarity? spawnedRarity = null, float maximumDepenetrationVelocity = 1.4f)
        {
            if (!IsServer || definition == null) return;
            if (NetworkManager.NetworkConfig.NetworkTopology == NetworkTopologyTypes.DistributedAuthority)
                NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.SessionOwner, true);
            MachineNetworkObjectId.Value = machineId;
            DefinitionIndex.Value = definitionIndex;
            ProductId.Value = definition.Product != null ? definition.Product.ProductId : 0;
            ShopProductRarity rarity = spawnedRarity ?? (definition.Product != null
                ? definition.Product.Rarity
                : ShopProductRarity.Common);
            SpawnedRarity.Value = (int)rarity;
            PrizeWeight.Value = definition.Weight;
            GripDifficulty.Value = definition.GripDifficulty;
            GripModifier.Value = definition.GripScoreModifier;
            SurfaceFriction.Value = definition.Friction;
            VisualSize.Value = definition.Size;
            VisualColor.Value = RarityColor(rarity);
            VisualPrefabIndex.Value = VisualPrefabCount > 0
                ? Mathf.Abs(visualPrefabIndex) % VisualPrefabCount
                : -1;
            VisualDisplayName.Value = new FixedString64Bytes(
                ShopProductLocalization.RarityLabel(rarity) + " 미개봉 캡슐");
            Awarded.Value = false;
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = Vector3.one * definition.Size;
            Body.mass = definition.Weight * definition.CapsuleMassMultiplier;
            Body.linearDamping = definition.LinearDamping;
            Body.angularDamping = definition.AngularDamping;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.constraints = RigidbodyConstraints.None;
            Body.maxAngularVelocity = 12f;
            Body.maxDepenetrationVelocity = Mathf.Max(0.1f, maximumDepenetrationVelocity);
            Body.solverIterations = 12;
            Body.solverVelocityIterations = 6;
            Body.isKinematic = false;
            SphereCollider mainBody = EnsureSingleCapsuleCollider();
            mainBody.center = Vector3.zero;
            mainBody.radius = 0.43f;
            PhysicsMaterial surface = definition.SurfaceMaterial;
            if (surface == null)
            {
                surface = new PhysicsMaterial("ClawPrizeSurface")
                {
                    dynamicFriction = definition.Friction,
                    staticFriction = Mathf.Clamp01(definition.Friction + 0.12f),
                    frictionCombine = PhysicsMaterialCombine.Average,
                    bounciness = definition.Bounciness,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
            }
            mainBody.material = surface;
            ApplyVisuals();
        }

        public bool ServerMarkAwarded()
        {
            if (!IsServer || Awarded.Value) return false;
            Awarded.Value = true;
            return true;
        }

        public void ServerReturnToField(Vector3 position, Quaternion rotation)
        {
            if (!IsServer || Awarded.Value) return;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(position, rotation);
            Body.WakeUp();
        }

        private void ApplyVisuals()
        {
            transform.localScale = Vector3.one * VisualSize.Value;
            if (bodyRenderer != null)
            {
                bodyRenderer.forceRenderingOff = false;
                MaterialPropertyBlock block = new();
                bodyRenderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", VisualColor.Value);
                block.SetColor("_Color", VisualColor.Value);
                block.SetFloat("_Smoothness", SpawnedRarity.Value >= (int)ShopProductRarity.Rare ? 0.86f : 0.42f);
                bodyRenderer.SetPropertyBlock(block);
            }
            EnsureRarityMarkers();
        }

        private void OnSizeChanged(float previous, float current) => ApplyVisuals();
        private void OnColorChanged(Color previous, Color current) => ApplyVisuals();
        private void OnVisualPrefabChanged(int previous, int current) => ApplyVisuals();

        private SphereCollider EnsureSingleCapsuleCollider()
        {
            SphereCollider selected = GetComponent<SphereCollider>();
            foreach (Collider prizeCollider in GetComponentsInChildren<Collider>(true))
            {
                if (prizeCollider == selected) continue;
                prizeCollider.enabled = false;
                if (Application.isPlaying) Destroy(prizeCollider);
            }
            if (selected == null) selected = gameObject.AddComponent<SphereCollider>();
            selected.isTrigger = false;
            return selected;
        }

        private static Color RarityColor(ShopProductRarity rarity) => rarity switch
        {
            ShopProductRarity.UltraRare => new Color(1f, 0.72f, 0.08f),
            ShopProductRarity.Rare => new Color(0.63f, 0.28f, 0.92f),
            ShopProductRarity.Uncommon => new Color(0.18f, 0.52f, 1f),
            _ => new Color(0.96f, 0.96f, 0.96f)
        };

        private void EnsureRarityMarkers()
        {
            Transform markerRoot = transform.Find("RuntimeRarityMarkers");
            if (markerRoot == null)
            {
                markerRoot = new GameObject("RuntimeRarityMarkers").transform;
                markerRoot.SetParent(transform, false);
            }
            for (int i = markerRoot.childCount - 1; i >= 0; i--)
                Destroy(markerRoot.GetChild(i).gameObject);

            ShopProductRarity rarity = (ShopProductRarity)Mathf.Clamp(SpawnedRarity.Value, 0, 3);
            if (rarity == ShopProductRarity.Common) return;
            Color markerColor = rarity == ShopProductRarity.UltraRare
                ? new Color(1f, 0.95f, 0.45f)
                : Color.white;
            CreateRing(markerRoot, "SeamSolid", 0f, markerColor);
            if (rarity >= ShopProductRarity.Rare)
                CreateRing(markerRoot, "SeamDouble", 0.055f, markerColor);
            if (rarity == ShopProductRarity.UltraRare)
                CreateStar(markerRoot, markerColor);
        }

        private static void CreateRing(Transform parent, string objectName, float height, Color color)
        {
            const int points = 33;
            GameObject ring = new(objectName, typeof(LineRenderer));
            ring.transform.SetParent(parent, false);
            LineRenderer line = ring.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = points - 1;
            line.widthMultiplier = 0.026f;
            line.startColor = line.endColor = color;
            line.material = new Material(Shader.Find("Sprites/Default"));
            for (int i = 0; i < points - 1; i++)
            {
                float angle = i / (float)(points - 1) * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * 0.445f, height,
                    Mathf.Sin(angle) * 0.445f));
            }
        }

        private static void CreateStar(Transform parent, Color color)
        {
            GameObject star = new("UltraRareStar", typeof(LineRenderer));
            star.transform.SetParent(parent, false);
            LineRenderer line = star.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 10;
            line.widthMultiplier = 0.035f;
            line.startColor = line.endColor = color;
            line.material = new Material(Shader.Find("Sprites/Default"));
            for (int i = 0; i < 10; i++)
            {
                float angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float radius = (i & 1) == 0 ? 0.24f : 0.1f;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.31f,
                    Mathf.Sin(angle) * radius));
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collisionAudio == null || collisionClip == null || collision.relativeVelocity.magnitude < 0.75f) return;
            collisionAudio.pitch = Random.Range(0.88f, 1.12f);
            collisionAudio.PlayOneShot(collisionClip, Mathf.Clamp01(collision.relativeVelocity.magnitude / 5f));
        }

        private static AudioClip CreateCollisionClip()
        {
            const int sampleRate = 22050;
            const int length = 1800;
            float[] data = new float[length];
            for (int i = 0; i < length; i++)
            {
                float envelope = 1f - i / (float)length;
                float noise = Random.Range(-1f, 1f) * 0.22f;
                data[i] = (Mathf.Sin(i * 0.085f) * 0.35f + noise) * envelope;
            }
            AudioClip clip = AudioClip.Create("Claw_PrizeImpact", length, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
