using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PickAndPlaceShop.Tests.Editor
{
    public sealed class ShopTheftSystemTests
    {
        private const string ConfigPath = "Assets/PickAndPlaceShop/Resources/ShopTheftConfig.asset";

        [Test]
        public void TheftConfig_IsDataDrivenAndEconomicallyWorseThanPaidGacha()
        {
            ShopTheftConfig theft = AssetDatabase.LoadAssetAtPath<ShopTheftConfig>(ConfigPath);
            Assert.NotNull(theft);
            ShopGachaMachineConfig paid = AssetDatabase.FindAssets("t:ShopGachaMachineConfig")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopGachaMachineConfig>)
                .FirstOrDefault(item => item != null);
            Assert.NotNull(paid);
            Assert.That(theft.TheftRareChance, Is.LessThanOrEqualTo(paid.RareChance / 3f + 0.0001f));
            Assert.That(theft.BrokenRecoverySeconds, Is.GreaterThan(0f));
            Assert.That(theft.ArrestFine, Is.GreaterThan(0));
        }

        [Test]
        public void AttackArc_RejectsTargetsBehindPlayer()
        {
            Assert.IsTrue(ShopTheftRules.IsInsideAttackArc(Vector3.forward,
                Vector3.forward * 1.5f, 2f, 80f));
            Assert.IsFalse(ShopTheftRules.IsInsideAttackArc(Vector3.forward,
                Vector3.back * 1.5f, 2f, 80f));
            Assert.IsFalse(ShopTheftRules.IsInsideAttackArc(Vector3.forward,
                Vector3.forward * 3f, 2f, 80f));
        }

        [Test]
        public void TheftRarity_NeverProducesUltraRare()
        {
            ShopTheftConfig config = AssetDatabase.LoadAssetAtPath<ShopTheftConfig>(ConfigPath);
            Assert.NotNull(config);
            for (int i = 0; i <= 1000; i++)
            {
                ShopGachaRarity rarity = ShopTheftRules.SelectTheftGacha(i / 1000f, config);
                CollectionAssert.Contains(new[] { ShopGachaRarity.Common,
                    ShopGachaRarity.Uncommon, ShopGachaRarity.Rare }, rarity);
                ShopKujiRank rank = ShopTheftRules.SelectTheftKuji(i / 1000f, config);
                CollectionAssert.Contains(new[] { ShopKujiRank.A, ShopKujiRank.B,
                    ShopKujiRank.C, ShopKujiRank.D }, rank);
            }
        }

        [Test]
        public void PlayerPrefab_HasTheftNetworkBehaviourAndAttackStates()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/PickAndPlaceShop/Prefabs/PickAndPlacePlayer.prefab");
            Assert.NotNull(player);
            Assert.NotNull(player.GetComponent<ShopPlayerTheftNetwork>());
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                "Assets/PickAndPlaceShop/GeneratedCharacters/PlayerLocomotion.controller");
            Assert.NotNull(controller);
            string[] stateNames = controller.layers[0].stateMachine.states
                .Select(item => item.state.name).ToArray();
            CollectionAssert.Contains(stateNames, "Attack1");
            CollectionAssert.Contains(stateNames, "Attack2");
            CollectionAssert.Contains(controller.parameters.Select(item => item.name).ToArray(), "Attack1");
            CollectionAssert.Contains(controller.parameters.Select(item => item.name).ToArray(), "Attack2");
        }

        [Test]
        public void MachineTheftEntryPoints_AreServerMethodsAndUseUnifiedAcquisitionSource()
        {
            Assert.NotNull(typeof(ShopClawMachineNetwork).GetMethod("ServerApplyTheftImpulse"));
            Assert.NotNull(typeof(ShopGachaMachineNetwork).GetMethod("ServerApplyTheftHit"));
            Assert.NotNull(typeof(ShopKujiStationNetwork).GetMethod("ServerApplyTheftHit"));
            Assert.That((int)ShopAcquisitionSource.Theft,
                Is.GreaterThan((int)ShopAcquisitionSource.Consignment));
        }
    }
}
