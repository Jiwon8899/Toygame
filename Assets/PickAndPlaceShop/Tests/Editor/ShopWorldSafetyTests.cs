using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopWorldSafetyTests
    {
        [Test]
        public void WorldConfig_UsesRequestedRecoveryAndPedestrianLimits()
        {
            ShopWorldConfig config = ShopWorldConfig.Load();
            Assert.NotNull(config);
            Assert.AreEqual(-10f, config.FallRecoveryHeight, 0.001f);
            Assert.AreEqual(6, config.MaximumPedestrians);
            Assert.Greater(config.SafetyPollInterval, 0f);
            Assert.AreEqual(0.2f, config.PedestrianSpeedVariance, 0.001f);
            Assert.Greater(config.PedestrianSpawnStagger, 0f);
            Assert.Greater(config.PedestrianPauseChance, 0f);
            Assert.Greater(config.PedestrianLaneSpacing, 1f);
        }

        [Test]
        public void SpawnPad_KeepsFunctionAndHasRuntimeVisualMarker()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Shooter/Art/Environment/SpawnPad/Pfb_SpawnPad.prefab");
            Assert.NotNull(prefab);
            Assert.NotNull(prefab.GetComponent<ShopSpawnPadMarker>());
            Assert.Greater(prefab.GetComponentsInChildren<Renderer>(true).Length, 0,
                "렌더러를 삭제하지 않고 런타임에만 숨겨야 합니다.");
        }

        [Test]
        public void ClawRails_AreVisualOnlyWhileFryingPanKeepsCompoundColliders()
        {
            string[] paths = AssetDatabase.FindAssets("t:Prefab ClawMachine_",
                    new[] { "Assets/PickAndPlaceShop/Prefabs/ClawMachines" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => System.IO.Path.GetFileNameWithoutExtension(path)
                    .StartsWith("ClawMachine_", System.StringComparison.Ordinal))
                .ToArray();
            Assert.AreEqual(5, paths.Length);
            foreach (string path in paths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Transform[] rails = prefab.GetComponentsInChildren<Transform>(true)
                    .Where(item => item.name == "HorizontalRailX" || item.name == "HorizontalRailZ")
                    .ToArray();
                Assert.AreEqual(2, rails.Length, path);
                Assert.IsTrue(rails.All(item => item.GetComponents<Collider>().Length == 0), path);
                ShopClawScoopRig scoop = prefab.GetComponentInChildren<ShopClawScoopRig>(true);
                Assert.NotNull(scoop, path);
                Collider[] colliders = scoop.GetComponentsInChildren<Collider>(true);
                Assert.AreEqual(10, colliders.Length, path);
                Assert.AreEqual(9, colliders.Count(item => item is BoxCollider), path);
                Assert.AreEqual(1, colliders.Count(item => item is CapsuleCollider), path);
                Assert.AreEqual(0, colliders.Count(item => item is MeshCollider), path);
                Collider handle = colliders.Single(item => item is CapsuleCollider);
                Assert.AreEqual(LayerMask.NameToLayer("ScoopHandle"), handle.gameObject.layer, path);
            }
        }
    }
}
