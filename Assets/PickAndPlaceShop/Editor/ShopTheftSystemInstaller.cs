#if UNITY_EDITOR
using System;
using System.Linq;
using PickAndPlaceShop;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PickAndPlaceShop.Editor
{
    public static class ShopTheftSystemInstaller
    {
        private const string PlayerPrefabPath = "Assets/PickAndPlaceShop/Prefabs/PickAndPlacePlayer.prefab";
        private const string ControllerPath = "Assets/PickAndPlaceShop/GeneratedCharacters/PlayerLocomotion.controller";
        private const string Attack1Path = "Assets/animation/attack1.fbx";
        private const string Attack2Path = "Assets/animation/attack2.fbx";
        private const string ResourceFolder = "Assets/PickAndPlaceShop/Resources";
        private const string ConfigPath = ResourceFolder + "/ShopTheftConfig.asset";
        private const string PoliceAppearancePath =
            "Assets/PickAndPlaceShop/GeneratedCharacters/CustomerPerson6.prefab";

        [MenuItem("Tools/Pick And Place Shop/Install Theft System")]
        public static void Install()
        {
            EnsureFolder(ResourceFolder);
            ShopTheftConfig config = AssetDatabase.LoadAssetAtPath<ShopTheftConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ShopTheftConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            config.EditorSetPoliceAppearance(AssetDatabase.LoadAssetAtPath<GameObject>(PoliceAppearancePath));
            EditorUtility.SetDirty(config);

            ConfigureAttackModel(Attack1Path);
            ConfigureAttackModel(Attack2Path);
            ConfigureAnimator();
            ConfigurePlayerPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TheftInstaller] COMPLETE config, player NetworkBehaviour, attack1/attack2 states installed.");
        }

        private static void ConfigureAttackModel(string path)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException(path + " 모델 임포터를 찾지 못했습니다.");
            // The visible player model is imported as a Generic rig. Humanoid-only
            // attack clips advance in the Animator but cannot bind to that rig,
            // which leaves the rendered character frozen on the first pose.
            bool changed = importer.animationType != ModelImporterAnimationType.Generic ||
                           !importer.importAnimation;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            if (changed) importer.SaveAndReimport();
        }

        private static AnimationClip LoadAttackClip(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
        }

        private static void ConfigureAnimator()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) throw new InvalidOperationException("PlayerLocomotion.controller가 없습니다.");
            EnsureTrigger(controller, "Attack1");
            EnsureTrigger(controller, "Attack2");
            EnsureFloat(controller, "AttackSpeed", 1f);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idle = machine.states.Select(child => child.state)
                .FirstOrDefault(state => state.name == "Idle");
            if (idle == null) throw new InvalidOperationException("PlayerLocomotion Idle 상태가 없습니다.");
            ConfigureAttackState(machine, idle, "Attack1", LoadAttackClip(Attack1Path), new Vector3(450f, -20f));
            ConfigureAttackState(machine, idle, "Attack2", LoadAttackClip(Attack2Path), new Vector3(450f, 80f));
            EditorUtility.SetDirty(controller);
        }

        private static void EnsureTrigger(AnimatorController controller, string name)
        {
            if (controller.parameters.Any(parameter => parameter.name == name)) return;
            controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
        }

        private static void EnsureFloat(AnimatorController controller, string name, float defaultValue)
        {
            if (controller.parameters.Any(parameter => parameter.name == name)) return;
            controller.AddParameter(name, AnimatorControllerParameterType.Float);
            controller.parameters.First(parameter => parameter.name == name).defaultFloat = defaultValue;
        }

        private static void ConfigureAttackState(AnimatorStateMachine machine, AnimatorState idle,
            string name, AnimationClip clip, Vector3 position)
        {
            if (clip == null) throw new InvalidOperationException(name + " 애니메이션 클립을 찾지 못했습니다.");
            AnimatorState state = machine.states.Select(child => child.state)
                .FirstOrDefault(candidate => candidate.name == name);
            if (state == null) state = machine.AddState(name, position);
            state.motion = clip;
            state.speed = 1f;
            state.speedParameterActive = true;
            state.speedParameter = "AttackSpeed";
            foreach (AnimatorStateTransition transition in machine.anyStateTransitions
                         .Where(item => item.destinationState == state).ToArray())
                machine.RemoveAnyStateTransition(transition);
            AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0f, name);
            enter.hasExitTime = false;
            enter.duration = 0.08f;
            foreach (AnimatorStateTransition transition in state.transitions.ToArray())
                state.RemoveTransition(transition);
            AnimatorStateTransition exit = state.AddTransition(idle);
            exit.hasExitTime = true;
            exit.exitTime = 1f;
            exit.duration = 0.06f;
        }

        private static void ConfigurePlayerPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                if (root.GetComponent<ShopPlayerTheftNetwork>() == null)
                    root.AddComponent<ShopPlayerTheftNetwork>();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EnsureFolder(string path)
        {
            string[] pieces = path.Split('/');
            string current = pieces[0];
            for (int i = 1; i < pieces.Length; i++)
            {
                string next = current + "/" + pieces[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, pieces[i]);
                current = next;
            }
        }
    }
}
#endif
