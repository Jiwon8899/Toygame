using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopClawContainerSystemTests
    {
        [Test]
        public void ProductCatalog_CoversEveryBuiltInPrefab()
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab",
                new[] { "Assets/Low-Poly_Objects_Pack/Prefabs/Built-in" });
            ShopProductDefinition[] products = AssetDatabase.FindAssets("t:ShopProductDefinition",
                    new[] { "Assets/PickAndPlaceShop/Resources/Products/Generated" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopProductDefinition>)
                .Where(product => product != null)
                .ToArray();

            Assert.AreEqual(32, prefabGuids.Length);
            Assert.AreEqual(prefabGuids.Length, products.Length);
            Assert.AreEqual(prefabGuids.Length, products.Select(product => product.PrizePrefab).Distinct().Count());
            Assert.IsTrue(products.All(product => product.PhysicsProfile != null));
            Assert.IsTrue(products.All(product => !string.IsNullOrWhiteSpace(product.StableItemId)));
        }

        [Test]
        public void MachinePools_AreDistinctWeightedAndCapped()
        {
            ShopClawPrizePool[] pools = AssetDatabase.FindAssets("t:ShopClawPrizePool",
                    new[] { "Assets/PickAndPlaceShop/Data/Generated/PrizePools" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopClawPrizePool>)
                .Where(pool => pool != null)
                .ToArray();

            Assert.AreEqual(3, pools.Length);
            Assert.AreEqual(3, pools.Select(pool => pool.PoolId).Distinct().Count());
            Assert.IsTrue(pools.All(pool => pool.Entries.Count >= 10));
            Assert.IsTrue(pools.All(pool => pool.MaxConcurrentPrizes <= 7));
            Assert.IsTrue(pools.All(pool => pool.Entries.Select(entry => entry.RarityWeight).Distinct().Count() > 1));
            Assert.IsTrue(pools.All(pool => pool.PickWeighted(new System.Random(41)) != null));
        }

        [Test]
        public void SpawnRules_RejectOverlapAndAcceptClearPosition()
        {
            var positions = new List<Vector3> { Vector3.zero, new(2f, 0f, 0f) };
            var radii = new List<float> { 0.5f, 0.5f };
            Assert.IsFalse(ShopClawSpawnRules.CanPlace(new Vector3(0.7f, 0f, 0f), 0.3f,
                positions, radii));
            Assert.IsTrue(ShopClawSpawnRules.CanPlace(new Vector3(1f, 0f, 1.2f), 0.3f,
                positions, radii));
        }

        [TestCase(9, 10, true)]
        [TestCase(10, 10, false)]
        [TestCase(30, 30, false)]
        public void CapacityBoundary_IsExact(int used, int capacity, bool expected)
        {
            Assert.AreEqual(expected, ShopContainerRules.CanAccept(used, capacity));
        }

        [Test]
        public void AtomicMove_RequiresSourceAndDestinationCapacity()
        {
            Assert.IsTrue(ShopContainerRules.CanMoveAtomic(1, 9, 10));
            Assert.IsFalse(ShopContainerRules.CanMoveAtomic(0, 9, 10));
            Assert.IsFalse(ShopContainerRules.CanMoveAtomic(1, 10, 10));
        }

        [Test]
        public void ExpansionCapacity_MatchesCanonicalValues()
        {
            ShopProgressionCatalog catalog = AssetDatabase.LoadAssetAtPath<ShopProgressionCatalog>(
                "Assets/PickAndPlaceShop/Resources/Progression/ShopProgressionCatalog.asset");
            Assert.NotNull(catalog);
            Assert.AreEqual((4, 30), (catalog.ExpansionTiers[0].DisplaySlots,
                catalog.ExpansionTiers[0].StorageSlots));
            Assert.AreEqual((6, 30), (catalog.ExpansionTiers[1].DisplaySlots,
                catalog.ExpansionTiers[1].StorageSlots));
            Assert.AreEqual((8, 30), (catalog.ExpansionTiers[2].DisplaySlots,
                catalog.ExpansionTiers[2].StorageSlots));
            Assert.AreEqual((10, 30), (catalog.ExpansionTiers[3].DisplaySlots,
                catalog.ExpansionTiers[3].StorageSlots));
            Assert.AreEqual((10, 50), (catalog.ExpansionTiers[4].DisplaySlots,
                catalog.ExpansionTiers[4].StorageSlots));
            Assert.AreEqual((10, 70), (catalog.ExpansionTiers[5].DisplaySlots,
                catalog.ExpansionTiers[5].StorageSlots));
        }

        [Test]
        public void SaveData_VersionThreeRoundTripsContainersAndMachinePrizes()
        {
            ShopProgressionSaveData source = new();
            source.containerItems.Add(new ShopContainerItemSave
            {
                ownerClientId = 7,
                container = (int)ShopContainerKind.SharedDisplay,
                slotIndex = 2,
                productId = 1012,
                visualPrefabIndex = 12,
                quantity = 3,
                maxStack = 5,
                unitPrice = 240,
                rarity = (int)ShopProductRarity.Uncommon,
                displayName = "roundtrip"
            });
            source.clawMachines.Add(new ShopClawMachineSave
            {
                machineId = 101,
                prizes = new List<ShopClawPrizeSave>
                {
                    new()
                    {
                        productId = 1012,
                        visualPrefabIndex = 12,
                        localPosition = new Vector3(0.4f, 1.1f, -0.3f),
                        localRotation = Quaternion.Euler(0f, 35f, 0f)
                    }
                }
            });
            string json = JsonUtility.ToJson(source);
            ShopProgressionSaveData restored = JsonUtility.FromJson<ShopProgressionSaveData>(json);
            Assert.AreEqual(ShopProgressionSaveStore.CurrentVersion, restored.version);
            Assert.AreEqual(1, restored.containerItems.Count);
            Assert.AreEqual(3, restored.containerItems[0].quantity);
            Assert.AreEqual("roundtrip", restored.containerItems[0].displayName);
            Assert.AreEqual(1, restored.clawMachines.Count);
            Assert.AreEqual(101, restored.clawMachines[0].machineId);
            Assert.AreEqual(1012, restored.clawMachines[0].prizes[0].productId);
        }

        [Test]
        public void PhysicalClaw_DoesNotExposeCodeAttachmentPath()
        {
            Assert.IsNull(typeof(ShopClawPrizeNetwork).GetMethod("ServerAttach"));
            Assert.IsFalse(typeof(ShopClawPrizeNetwork)
                .GetFields(System.Reflection.BindingFlags.Instance |
                           System.Reflection.BindingFlags.NonPublic)
                .Any(field => field.FieldType == typeof(ConfigurableJoint)));
        }

        [Test]
        public void Scoop_PrefabsUseKinematicCompoundRigAndRealChuteFloor()
        {
            string[] paths = AssetDatabase.FindAssets("t:Prefab",
                    new[] { "Assets/PickAndPlaceShop/Prefabs/ClawMachines" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => System.IO.Path.GetFileNameWithoutExtension(path)
                    .StartsWith("ClawMachine_", System.StringComparison.Ordinal))
                .ToArray();
            Assert.AreEqual(5, paths.Length);

            foreach (string path in paths)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Assert.AreEqual(0, root.GetComponentsInChildren<HingeJoint>(true).Length, path);
                    Rigidbody carriage = root.GetComponentsInChildren<Rigidbody>(true)
                        .FirstOrDefault(body => body.name == "ScoopRailCarriage");
                    Assert.NotNull(carriage, path);
                    Assert.IsTrue(carriage.isKinematic, path);
                    Assert.AreEqual(0, root.GetComponentsInChildren<ConfigurableJoint>(true).Length, path);
                    ShopClawScoopRig scoop = root.GetComponentInChildren<ShopClawScoopRig>(true);
                    Assert.NotNull(scoop, path);
                    Assert.NotNull(scoop.Body, path);
                    Assert.IsTrue(scoop.Body.isKinematic, path);
                    Assert.AreEqual(CollisionDetectionMode.ContinuousSpeculative,
                        scoop.Body.collisionDetectionMode, path);
                    Assert.AreEqual(9, scoop.CompoundColliderCount, path);
                    Assert.IsTrue(scoop.RimColliders.All(collider => collider is BoxCollider), path);
                    Assert.AreEqual(0, root.GetComponentsInChildren<MeshCollider>(true).Length, path);
                    Transform floor = root.transform.Find("PhysicalFloorWithChute");
                    Assert.NotNull(floor, path);
                    BoxCollider[] floorParts = floor.GetComponentsInChildren<BoxCollider>(true);
                    Assert.AreEqual(3, floorParts.Count(item => item.name.StartsWith("Floor_")), path);
                    Assert.AreEqual(3, floorParts.Count(item => item.name.StartsWith("Base_")), path);
                    Assert.NotNull(floorParts.FirstOrDefault(item => item.name == "ChuteCatchFloor"), path);
                    Assert.AreEqual(11, floorParts.Length, path);
                    BoxCollider chute = root.GetComponentsInChildren<BoxCollider>(true)
                        .FirstOrDefault(collider => collider.name == "PrizeAwardTrigger");
                    Assert.NotNull(chute, path);
                    Assert.IsTrue(chute.isTrigger, path);
                    Assert.GreaterOrEqual(chute.size.x, 1.1f, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        [Test]
        public void Scoop_MaterialsAreAuthoredAndLegacyRigIsExplicitlyDeprecated()
        {
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/PickAndPlaceShop/Materials/Scoop/ScoopPan.mat"));
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/PickAndPlaceShop/Materials/Scoop/ScoopEdge.mat"));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/PickAndPlaceShop/Prefabs/ClawMachines/SharedPhysicalClawRig.prefab"));
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/PickAndPlaceShop/Deprecated/Claw/SharedPhysicalClawRig.prefab"));
        }

        [Test]
        public void ClawPrize_UsesOneDataDrivenCapsuleColliderWithoutDirectProductVisual()
        {
            const string path = "Assets/PickAndPlaceShop/Prefabs/ClawPrize_Network.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                Assert.AreEqual(1, colliders.Length);
                Assert.IsInstanceOf<SphereCollider>(colliders[0]);
                Assert.NotNull(root.transform.Find("CapsuleShell"));
                Assert.NotNull(root.transform.Find("CapsuleSeam"));
                Assert.IsFalse(root.GetComponentsInChildren<Transform>(true)
                    .Any(item => item.name.StartsWith("상품_") || item.name.StartsWith("PrizeGrip_")));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void Scoop_PresetsAreDataDrivenAndAwardEverythingPouredIntoChute()
        {
            ShopClawMachineConfig[] configs = AssetDatabase.FindAssets("t:ShopClawMachineConfig",
                    new[] { "Assets/PickAndPlaceShop/Data" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopClawMachineConfig>)
                .Where(config => config != null && config.MachineId >= 101 && config.MachineId <= 105)
                .ToArray();
            Assert.GreaterOrEqual(configs.Length, 5);
            Assert.IsTrue(configs.All(config => config.ScoopDiameter > 0.8f));
            Assert.IsTrue(configs.All(config => config.ScoopRimHeight >= 0.08f));
            Assert.IsTrue(configs.All(config => config.ScrapeDistance > 0.25f));
            Assert.IsTrue(configs.All(config => config.ScrapeSpeed > 0.2f));
            Assert.IsTrue(configs.All(config => config.LiftSpeed > 0f));
            Assert.IsTrue(configs.All(config => config.MultiPrizePolicy == ShopMultiPrizePolicy.AwardAll));
            Assert.Greater(configs.Select(config => config.ScoopDiameter).Distinct().Count(), 1);
            Assert.Greater(configs.Select(config => config.ScrapeDistance).Distinct().Count(), 1);
            Assert.IsTrue(configs.All(config =>
                config.GetCapsuleMass(ShopProductRarity.UltraRare) >
                config.GetCapsuleMass(ShopProductRarity.Common)));
        }

        [Test]
        public void Scoop_RimUsesEightSegmentsWithLowFrontLip()
        {
            const string path = "Assets/PickAndPlaceShop/Prefabs/ClawMachines/ClawMachine_101.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ShopClawScoopRig rig = root.GetComponentInChildren<ShopClawScoopRig>(true);
                Assert.NotNull(rig);
                Assert.AreEqual(8, rig.RimColliders.Count);
                Assert.Less(rig.RimColliders[0].size.y, rig.RimColliders[1].size.y);
                Assert.NotNull(rig.VisualRoot.Find("ScoopPanVisual"));
                Assert.NotNull(rig.VisualRoot.Find("ScoopHandle"));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void ChuteSettlement_RequiresLowLinearAndAngularVelocity()
        {
            Assert.IsTrue(ShopClawRules.IsChuteSettled(
                new Vector3(0.02f, 0f, 0.01f), new Vector3(0f, 0.1f, 0f), 0.18f));
            Assert.IsFalse(ShopClawRules.IsChuteSettled(
                new Vector3(0.3f, 0f, 0f), Vector3.zero, 0.18f));
            Assert.IsFalse(ShopClawRules.IsChuteSettled(
                Vector3.zero, new Vector3(0f, 2f, 0f), 0.18f));
        }

        [Test]
        public void ChuteAwardWindow_IncludesCooldownForLateSleepingBodies()
        {
            Assert.IsTrue(ShopClawRules.CanAwardChutePrize(ShopClawMachineState.Release));
            Assert.IsTrue(ShopClawRules.CanAwardChutePrize(ShopClawMachineState.Judge));
            Assert.IsTrue(ShopClawRules.CanAwardChutePrize(ShopClawMachineState.Cooldown));
            Assert.IsFalse(ShopClawRules.CanAwardChutePrize(ShopClawMachineState.Aiming));
        }

        [Test]
        public void ScoopDescent_RejectsHighSideWallButAcceptsFloorContact()
        {
            Assert.IsFalse(ShopClawRules.IsDescentTerminalContact(3.1675f, 0.98f, 1.535f,
                Vector3.right, Vector3.up), "High cabinet glass must not start the scoop.");
            Assert.IsTrue(ShopClawRules.IsDescentTerminalContact(1.26f, 0.98f, 1.535f,
                Vector3.right, Vector3.up), "A low funnel contact is a safe descent limit.");
            Assert.IsTrue(ShopClawRules.IsDescentTerminalContact(2.2f, 0.98f, 1.535f,
                Vector3.up, Vector3.up), "An upward-facing floor surface is terminal.");
        }

        [Test]
        public void DisplayShelfAnchors_AreGeneratedFromShelfSurfacesNotWorldFloor()
        {
            GameObject root = new("Shared Display Shelves");
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    GameObject shelf = new("Shelf_0_" + i);
                    shelf.transform.SetParent(root.transform, false);
                    shelf.transform.localPosition = new Vector3(0f, 0.45f + i * 0.9f, 0f);
                    shelf.transform.localScale = new Vector3(2f, 0.14f, 1.25f);
                }
                ShopDisplayShelfAnchors provider = root.AddComponent<ShopDisplayShelfAnchors>();
                provider.EnsureAnchors();
                Assert.AreEqual(6, provider.Anchors.Count);
                Assert.IsTrue(provider.Anchors.All(anchor =>
                    anchor.transform.parent == root.transform &&
                    anchor.transform.localPosition.y >= 0.69f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
