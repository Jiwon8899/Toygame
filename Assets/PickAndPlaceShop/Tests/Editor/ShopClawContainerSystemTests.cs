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
            Assert.AreEqual((6, 50), (catalog.ExpansionTiers[1].DisplaySlots,
                catalog.ExpansionTiers[1].StorageSlots));
            Assert.AreEqual((10, 80), (catalog.ExpansionTiers[2].DisplaySlots,
                catalog.ExpansionTiers[2].StorageSlots));
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
        public void PhysicalClaw_PrefabsHaveThreeTorqueFingersAndRealChuteFloor()
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
                    Assert.AreEqual(3,
                        root.GetComponentsInChildren<ShopClawFingerContactSensor>(true).Length, path);
                    Assert.AreEqual(3,
                        root.GetComponentsInChildren<HingeJoint>(true).Length, path);
                    Assert.AreEqual(3,
                        root.GetComponentsInChildren<Rigidbody>(true)
                            .Count(body => body.name.StartsWith("집게발_")), path);
                    Rigidbody carriage = root.GetComponentsInChildren<Rigidbody>(true)
                        .FirstOrDefault(body => body.name == "PhysicalClawCarriage");
                    Assert.NotNull(carriage, path);
                    Assert.IsTrue(carriage.isKinematic, path);
                    ConfigurableJoint suspension = root.GetComponentsInChildren<ConfigurableJoint>(true)
                        .FirstOrDefault();
                    Assert.NotNull(suspension, path);
                    Assert.AreSame(carriage, suspension.connectedBody, path);
                    Rigidbody head = suspension.GetComponent<Rigidbody>();
                    Assert.NotNull(head, path);
                    Assert.IsTrue(root.GetComponentsInChildren<HingeJoint>(true)
                        .All(hinge => hinge.connectedBody == head && hinge.axis == Vector3.right), path);
                    Transform sharedRig = root.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(item => item.name == "SharedPhysicalClawRig");
                    Assert.NotNull(sharedRig, path);
                    Assert.AreEqual(
                        "Assets/PickAndPlaceShop/Prefabs/ClawMachines/SharedPhysicalClawRig.prefab",
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sharedRig.gameObject), path);
                    Assert.AreEqual(9,
                        root.GetComponentsInChildren<CapsuleCollider>(true)
                            .Count(collider => collider.name.StartsWith("FingerCollider_")), path);
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
        public void PhysicalClaw_NormalMapsAreImportedCorrectly()
        {
            foreach (string path in new[]
                     {
                         "Assets/외형들모음/Textures/ClawFinger_2.png",
                         "Assets/외형들모음/Textures/ClawHousing_2.png",
                         "Assets/외형들모음/Textures/CableConnector_2.png"
                     })
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.NotNull(importer, path);
                Assert.AreEqual(TextureImporterType.NormalMap, importer.textureType, path);
                Assert.IsFalse(importer.sRGBTexture, path);
            }
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
        public void PhysicalClaw_PresetsUseTorqueAndDifferByMachine()
        {
            ShopClawMachineConfig[] configs = AssetDatabase.FindAssets("t:ShopClawMachineConfig",
                    new[] { "Assets/PickAndPlaceShop/Data" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopClawMachineConfig>)
                .Where(config => config != null && config.MachineId >= 101 && config.MachineId <= 105)
                .ToArray();
            Assert.GreaterOrEqual(configs.Length, 5);
            Assert.IsTrue(configs.All(config => config.CloseMotorTorque > 0f));
            Assert.IsTrue(configs.All(config => config.AscentGripTorqueMultiplier <= 1f));
            Assert.IsTrue(configs.Any(config => config.AscentGripTorqueMultiplier < 1f));
            Assert.Greater(configs.Select(config => config.CloseMotorTorque).Distinct().Count(), 1);
            Assert.Greater(configs.Select(config => config.AscentGripTorqueMultiplier).Distinct().Count(), 1);
            Assert.IsTrue(configs.All(config => config.OpenFingerAngle > config.ClosedFingerAngle));
            Assert.IsTrue(configs.All(config => config.ClosedFingerClearanceAngle >= 8f));
        }

        [Test]
        public void PhysicalClaw_SharedRigUsesExactSymmetricAutoLayout()
        {
            const string path =
                "Assets/PickAndPlaceShop/Prefabs/ClawMachines/SharedPhysicalClawRig.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ShopClawFingerAutoLayout layout =
                    root.GetComponentInChildren<ShopClawFingerAutoLayout>(true);
                Assert.NotNull(layout);
                Assert.AreEqual(3, layout.Fingers.Length);
                Assert.AreEqual(0.69f, layout.Radius, 0.0001f);
                Assert.AreEqual(-0.38f, layout.Height, 0.0001f);
                Assert.AreEqual(120f, layout.TiltAngle, 0.001f);

                for (int index = 0; index < layout.Fingers.Length; index++)
                {
                    float angle = 120f * index;
                    Vector3 expectedPosition = new(
                        Mathf.Sin(angle * Mathf.Deg2Rad) * 0.69f,
                        -0.38f,
                        Mathf.Cos(angle * Mathf.Deg2Rad) * 0.69f);
                    Quaternion expectedRotation = Quaternion.Euler(120f, angle, 0f);
                    Assert.Less(Vector3.Distance(expectedPosition,
                        layout.transform.InverseTransformPoint(layout.Fingers[index].position)),
                        0.0001f, "finger " + (index + 1) + " position");
                    Assert.Less(Quaternion.Angle(expectedRotation,
                        Quaternion.Inverse(layout.transform.rotation) *
                        layout.Fingers[index].rotation), 0.001f,
                        "finger " + (index + 1) + " rotation");
                }
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
    }
}
