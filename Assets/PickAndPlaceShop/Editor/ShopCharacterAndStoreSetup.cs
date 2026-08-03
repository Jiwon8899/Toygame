using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PickAndPlaceShop;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PickAndPlaceShopEditor
{
    public static class ShopCharacterAndStoreSetup
    {
        private const string GeneratedFolder = "Assets/PickAndPlaceShop/GeneratedCharacters";
        private const string ExtractedTexturesFolder =
            "Assets/PickAndPlaceShop/GeneratedCharacters/ExtractedTextures";
        private const string CharacterMaterialsFolder =
            "Assets/PickAndPlaceShop/GeneratedCharacters/Materials";
        private const string CustomerPrefabPath = "Assets/PickAndPlaceShop/Prefabs/ShopCustomer_Network.prefab";
        private const string PlayerPrefabPath = "Assets/PickAndPlaceShop/Prefabs/PickAndPlacePlayer.prefab";

        private static readonly string[] CustomerIdlePaths =
        {
            "Assets/외형들모음/NPC모음/Person1/Idle.fbx",
            "Assets/외형들모음/NPC모음/Person2/Idle.fbx",
            "Assets/외형들모음/NPC모음/Person3/Idle.fbx",
            "Assets/외형들모음/NPC모음/Person4/Idle.fbx",
            "Assets/외형들모음/NPC모음/Person5/Idle (1).fbx",
            "Assets/외형들모음/NPC모음/Person6/Idle.fbx"
        };

        private static readonly string[] CustomerWalkPaths =
        {
            "Assets/외형들모음/NPC모음/Person1/Walking (1).fbx",
            "Assets/외형들모음/NPC모음/Person2/Walking (1).fbx",
            "Assets/외형들모음/NPC모음/Person3/Walking (1).fbx",
            "Assets/외형들모음/NPC모음/Person4/Walking (1).fbx",
            "Assets/외형들모음/NPC모음/Person5/Walking (2).fbx",
            "Assets/외형들모음/NPC모음/Person6/Walking (1).fbx"
        };

        [MenuItem("Tools/Pick And Place Shop/Apply Character And Store Separation")]
        public static void ApplyAll()
        {
            EnsureGeneratedFolder();
            EnsureCharacterTextures();

            foreach (string path in CustomerIdlePaths.Concat(CustomerWalkPaths))
                ConfigureLoopingClip(path);

            string playerIdle = "Assets/외형들모음/플레이어/Idle.fbx";
            string playerWalk = "Assets/외형들모음/플레이어/Walking (1).fbx";
            string playerRun = "Assets/외형들모음/플레이어/Fast Run.fbx";
            ConfigureLoopingClip(playerIdle);
            ConfigureLoopingClip(playerWalk);
            ConfigureLoopingClip(playerRun);

            var customerVisuals = new GameObject[6];
            for (int i = 0; i < customerVisuals.Length; i++)
            {
                string controllerPath = $"{GeneratedFolder}/CustomerPerson{i + 1}.controller";
                AnimatorController controller = CreateLocomotionController(
                    controllerPath,
                    LoadClip(CustomerIdlePaths[i]),
                    LoadClip(CustomerWalkPaths[i]),
                    null);
                string prefabPath = $"{GeneratedFolder}/CustomerPerson{i + 1}.prefab";
                customerVisuals[i] = CreateVisualPrefab(
                    CustomerWalkPaths[i],
                    prefabPath,
                    $"CustomerPerson{i + 1}",
                    controller,
                    0.9f);
            }

            string playerControllerPath = $"{GeneratedFolder}/PlayerLocomotion.controller";
            AnimatorController playerController = CreateLocomotionController(
                playerControllerPath,
                LoadClip(playerIdle),
                LoadClip(playerWalk),
                LoadClip(playerRun));
            GameObject playerVisual = CreateVisualPrefab(
                playerWalk,
                $"{GeneratedFolder}/PlayerAppearance.prefab",
                "PlayerAppearance",
                playerController,
                0.44f);

            ConfigureCustomerPrefab(customerVisuals);
            ConfigurePlayerPrefab(playerVisual);
            SeparateGachaAndKujiStores();
            FixMirroredStreetSigns();
            ConfigureRoadsideCustomersAndUpgradeStation();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ShopCharacterAndStoreSetup] 외형, 애니메이션, 고객 충돌, 가챠/쿠지 분리 구성을 완료했습니다.");
        }

        private static void EnsureGeneratedFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/PickAndPlaceShop"))
                throw new InvalidOperationException("Assets/PickAndPlaceShop 폴더를 찾을 수 없습니다.");
            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
                AssetDatabase.CreateFolder("Assets/PickAndPlaceShop", "GeneratedCharacters");
            if (!AssetDatabase.IsValidFolder(ExtractedTexturesFolder))
                AssetDatabase.CreateFolder(GeneratedFolder, "ExtractedTextures");
            if (!AssetDatabase.IsValidFolder(CharacterMaterialsFolder))
                AssetDatabase.CreateFolder(GeneratedFolder, "Materials");
        }

        private static void EnsureCharacterTextures()
        {
            var sources = new[]
            {
                ("Player", "Assets/외형들모음/플레이어/Idle.fbx"),
                ("Person1", CustomerIdlePaths[0]),
                ("Person2", CustomerIdlePaths[1]),
                ("Person3", CustomerIdlePaths[2]),
                ("Person4", CustomerIdlePaths[3]),
                ("Person5", CustomerIdlePaths[4]),
                ("Person6", CustomerIdlePaths[5])
            };

            foreach ((string folderName, string modelPath) in sources)
            {
                string outputFolder = $"{ExtractedTexturesFolder}/{folderName}";
                if (!AssetDatabase.IsValidFolder(outputFolder))
                    AssetDatabase.CreateFolder(ExtractedTexturesFolder, folderName);
                if (AssetDatabase.FindAssets("t:Texture", new[] { outputFolder }).Length > 0)
                    continue;

                ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (importer == null || !importer.ExtractTextures(outputFolder))
                    throw new InvalidOperationException($"내장 텍스처 추출에 실패했습니다: {modelPath}");
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        private static void ConfigureLoopingClip(string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException($"ModelImporter를 찾을 수 없습니다: {modelPath}");

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
                throw new InvalidOperationException($"애니메이션 클립이 없습니다: {modelPath}");

            bool changed = false;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                if (!clip.loopTime || !clip.loopPose || !clip.lockRootPositionXZ || !clip.lockRootHeightY)
                    changed = true;
                clip.loopTime = true;
                clip.loopPose = true;
                clip.lockRootPositionXZ = true;
                clip.lockRootHeightY = true;
                clip.lockRootRotation = true;
                clip.keepOriginalPositionXZ = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalOrientation = true;
            }

            if (!changed && importer.clipAnimations.Length > 0) return;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static AnimationClip LoadClip(string modelPath)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(candidate => !candidate.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null)
                throw new InvalidOperationException($"AnimationClip을 찾을 수 없습니다: {modelPath}");
            return clip;
        }

        private static AnimatorController CreateLocomotionController(
            string path,
            AnimationClip idleClip,
            AnimationClip walkClip,
            AnimationClip runClip)
        {
            if (idleClip == null || walkClip == null)
                throw new InvalidOperationException($"Idle/Walk 클립이 누락되었습니다: {path}");

            AssetDatabase.DeleteAsset(path);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            if (runClip != null)
            {
                controller.AddParameter("Running", AnimatorControllerParameterType.Bool);
                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            }

            AnimatorState idle = controller.AddMotion(idleClip);
            idle.name = "Idle";
            AnimatorState walk = controller.AddMotion(walkClip);
            walk.name = "Walk";
            controller.layers[0].stateMachine.defaultState = idle;

            if (runClip == null)
            {
                AddTransition(idle, walk, AnimatorConditionMode.If, "Moving");
                AddTransition(walk, idle, AnimatorConditionMode.IfNot, "Moving");
            }
            else
            {
                AnimatorState run = controller.AddMotion(runClip);
                run.name = "Run";

                AddTransition(idle, walk,
                    (AnimatorConditionMode.If, "Moving"),
                    (AnimatorConditionMode.IfNot, "Running"));
                AddTransition(idle, run, AnimatorConditionMode.If, "Running");
                AddTransition(walk, idle, AnimatorConditionMode.IfNot, "Moving");
                AddTransition(walk, run, AnimatorConditionMode.If, "Running");
                AddTransition(run, idle, AnimatorConditionMode.IfNot, "Moving");
                AddTransition(run, walk,
                    (AnimatorConditionMode.If, "Moving"),
                    (AnimatorConditionMode.IfNot, "Running"));
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void AddTransition(
            AnimatorState from,
            AnimatorState to,
            AnimatorConditionMode mode,
            string parameter)
        {
            AddTransition(from, to, (mode, parameter));
        }

        private static void AddTransition(
            AnimatorState from,
            AnimatorState to,
            params (AnimatorConditionMode mode, string parameter)[] conditions)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.12f;
            transition.canTransitionToSelf = false;
            foreach ((AnimatorConditionMode mode, string parameter) in conditions)
                transition.AddCondition(mode, 0f, parameter);
        }

        private static GameObject CreateVisualPrefab(
            string modelPath,
            string prefabPath,
            string objectName,
            RuntimeAnimatorController controller,
            float uniformScale)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
                throw new InvalidOperationException($"모델을 찾을 수 없습니다: {modelPath}");

            GameObject instance = UnityEngine.Object.Instantiate(model);
            try
            {
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                {
                    PrefabUtility.UnpackPrefabInstance(
                        instance,
                        PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);
                }

                instance.name = objectName;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one * uniformScale;

                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);
                foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
                    UnityEngine.Object.DestroyImmediate(body);

                ApplyCharacterMaterials(instance);

                Animator animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.Normal;

                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);

                GameObject savedRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    Animator savedAnimator = savedRoot.GetComponent<Animator>();
                    if (savedAnimator == null) savedAnimator = savedRoot.AddComponent<Animator>();
                    savedAnimator.runtimeAnimatorController = controller;
                    savedAnimator.applyRootMotion = false;
                    savedAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    EditorUtility.SetDirty(savedAnimator);
                    PrefabUtility.SaveAsPrefabAsset(savedRoot, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(savedRoot);
                }

                AssetDatabase.SaveAssetIfDirty(controller);
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
                return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void ApplyCharacterMaterials(GameObject instance)
        {
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material source = materials[index];
                    if (source == null) continue;
                    string texturePrefix = TexturePrefixForMaterial(source.name);
                    if (string.IsNullOrEmpty(texturePrefix)) continue;

                    materials[index] = CreateOrUpdateCharacterMaterial(source.name, texturePrefix);
                    changed = true;
                }

                if (changed) renderer.sharedMaterials = materials;
            }
        }

        private static string TexturePrefixForMaterial(string materialName)
        {
            switch (materialName)
            {
                case "Bodymat":
                    return $"{ExtractedTexturesFolder}/Player/Remy_Body";
                case "Bottommat":
                    return $"{ExtractedTexturesFolder}/Player/Remy_Bottom";
                case "Eyelashmat":
                case "Hairmat":
                    return $"{ExtractedTexturesFolder}/Player/Remy_Hair";
                case "Shoesmat":
                    return $"{ExtractedTexturesFolder}/Player/Remy_Shoes";
                case "Topmat":
                    return $"{ExtractedTexturesFolder}/Player/Remy_Top";
                case "Ch07_body":
                    return $"{ExtractedTexturesFolder}/Person1/Ch07_1001";
                case "Ch07_hair":
                    return $"{ExtractedTexturesFolder}/Person1/Ch07_1002";
                case "Ch24_Body":
                    return $"{ExtractedTexturesFolder}/Person2/Ch24_1001";
                case "Ch06_body":
                case "Ch06_eyelashes":
                    return $"{ExtractedTexturesFolder}/Person3/Ch06_1001";
                case "Ch06_body1":
                    return $"{ExtractedTexturesFolder}/Person3/Ch06_1002";
                case "Ch31_body":
                    return $"{ExtractedTexturesFolder}/Person4/Ch31_1001";
                case "Ch31_hair":
                    return $"{ExtractedTexturesFolder}/Person4/Ch31_1002";
                case "Ch21_body":
                    return $"{ExtractedTexturesFolder}/Person5/Ch21_1001";
                case "Ch21_hair":
                    return $"{ExtractedTexturesFolder}/Person5/Ch21_1002";
                case "Paladin_MAT":
                    return $"{ExtractedTexturesFolder}/Person6/Paladin";
                default:
                    return null;
            }
        }

        private static Material CreateOrUpdateCharacterMaterial(string sourceName, string texturePrefix)
        {
            string safeName = sourceName.Replace('/', '_').Replace('\\', '_');
            string materialPath = $"{CharacterMaterialsFolder}/{safeName}_Colored.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("URP Lit 셰이더를 찾을 수 없습니다.");

            if (material == null)
            {
                material = new Material(shader) { name = $"{safeName}_Colored" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.shader = shader;
            }

            Texture2D diffuse = LoadCharacterTexture(texturePrefix, "Diffuse", "diffuse");
            Texture2D normal = LoadCharacterTexture(texturePrefix, "Normal", "normal");
            Texture2D specular = LoadCharacterTexture(texturePrefix, "Specular", "specular");
            if (diffuse == null)
                throw new InvalidOperationException($"Diffuse 텍스처를 찾을 수 없습니다: {texturePrefix}");

            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", diffuse);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.38f);

            if (normal != null)
            {
                ConfigureNormalMap(AssetDatabase.GetAssetPath(normal));
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(normal));
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.SetTexture("_BumpMap", null);
                material.DisableKeyword("_NORMALMAP");
            }

            if (specular != null)
            {
                material.SetFloat("_WorkflowMode", 0f);
                material.SetTexture("_SpecGlossMap", specular);
                material.EnableKeyword("_SPECGLOSSMAP");
            }
            else
            {
                material.SetTexture("_SpecGlossMap", null);
                material.DisableKeyword("_SPECGLOSSMAP");
            }

            TextureImporter diffuseImporter =
                AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(diffuse)) as TextureImporter;
            string lowerName = sourceName.ToLowerInvariant();
            bool isTransparentPart = lowerName.Contains("hair") || lowerName.Contains("eyelash");
            bool alphaCutout = isTransparentPart && diffuseImporter != null &&
                               diffuseImporter.DoesSourceTextureHaveAlpha();
            material.SetFloat("_AlphaClip", alphaCutout ? 1f : 0f);
            material.SetFloat("_Cutoff", 0.35f);
            if (alphaCutout)
            {
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            else
            {
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = -1;
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static Texture2D LoadCharacterTexture(string prefix, params string[] suffixes)
        {
            foreach (string suffix in suffixes)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{prefix}_{suffix}.png");
                if (texture != null) return texture;
            }

            return null;
        }

        private static void ConfigureNormalMap(string texturePath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null || importer.textureType == TextureImporterType.NormalMap) return;
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }

        private static void ConfigureCustomerPrefab(GameObject[] visualPrefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CustomerPrefabPath);
            try
            {
                ShopCustomerNetwork customer = root.GetComponent<ShopCustomerNetwork>();
                if (customer == null)
                    throw new InvalidOperationException("ShopCustomer_Network.prefab에 ShopCustomerNetwork가 없습니다.");

                foreach (CapsuleCollider capsule in root.GetComponents<CapsuleCollider>())
                    UnityEngine.Object.DestroyImmediate(capsule);

                CharacterController controller = root.GetComponent<CharacterController>();
                if (controller == null) controller = root.AddComponent<CharacterController>();
                controller.center = new Vector3(0f, 0.95f, 0f);
                controller.height = 1.9f;
                controller.radius = 0.34f;
                controller.skinWidth = 0.06f;
                controller.stepOffset = 0.25f;
                controller.slopeLimit = 45f;
                controller.minMoveDistance = 0f;

                Transform appearanceRoot = root.transform.Find("AppearanceRoot");
                if (appearanceRoot == null)
                {
                    var appearanceObject = new GameObject("AppearanceRoot");
                    appearanceRoot = appearanceObject.transform;
                    appearanceRoot.SetParent(root.transform, false);
                }
                appearanceRoot.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                appearanceRoot.localScale = Vector3.one;
                for (int i = appearanceRoot.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(appearanceRoot.GetChild(i).gameObject);

                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name == "몸" || renderer.name == "머리")
                        renderer.gameObject.SetActive(false);
                }

                customer.EditorConfigureAppearance(appearanceRoot, visualPrefabs, controller);
                PrefabUtility.SaveAsPrefabAsset(root, CustomerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigurePlayerPrefab(GameObject playerVisualPrefab)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name == "Armature_Mesh") renderer.gameObject.SetActive(false);
                }

                Transform[] staleVisuals = root.GetComponentsInChildren<Transform>(true)
                    .Where(item => item != root.transform &&
                                   (item.name == "CustomPlayerAppearance" || item.name == "PlayerAppearance"))
                    .ToArray();
                foreach (Transform staleVisual in staleVisuals)
                {
                    if (staleVisual == null) continue;
                    bool hasStaleAncestor = staleVisuals.Any(other =>
                        other != null && other != staleVisual && staleVisual.IsChildOf(other));
                    if (!hasStaleAncestor) UnityEngine.Object.DestroyImmediate(staleVisual.gameObject);
                }

                RuntimeAnimatorController locomotionController =
                    playerVisualPrefab.GetComponent<Animator>().runtimeAnimatorController;
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(playerVisualPrefab, root.transform);
                PrefabUtility.UnpackPrefabInstance(
                    visual,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                visual.name = "CustomPlayerAppearance";
                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                visual.transform.localScale = playerVisualPrefab.transform.localScale;

                Animator animator = visual.GetComponent<Animator>();
                if (animator == null)
                    throw new InvalidOperationException("PlayerAppearance.prefab에 Animator가 없습니다.");
                animator.runtimeAnimatorController = locomotionController;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                ShopPlayerAppearance appearance = root.GetComponent<ShopPlayerAppearance>();
                if (appearance == null) appearance = root.AddComponent<ShopPlayerAppearance>();
                appearance.EditorConfigure(animator, 3.8f);
                if (root.GetComponent<ShopPlayerUpgradeApplier>() == null)
                    root.AddComponent<ShopPlayerUpgradeApplier>();
                Blocks.Gameplay.Core.JumpAbility jump =
                    root.GetComponent<Blocks.Gameplay.Core.JumpAbility>();
                if (jump != null) UnityEngine.Object.DestroyImmediate(jump);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void SeparateGachaAndKujiStores()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path !=
                "Assets/PickAndPlaceShop/Scenes/PickAndPlaceShop_MainStreetSlice_Multiplayer.unity")
                throw new InvalidOperationException("메인 거리 씬을 연 상태에서 실행해야 합니다.");

            GameObject building = GameObject.Find("MainStreetSlice_World/가챠_쿠지_전문점");
            if (building == null)
                throw new InvalidOperationException("가챠_쿠지_전문점 오브젝트를 찾을 수 없습니다.");

            Transform previousGenerated = building.transform.Find("가챠_쿠지_분리구조");
            if (previousGenerated != null) UnityEngine.Object.DestroyImmediate(previousGenerated.gameObject);

            var generated = new GameObject("가챠_쿠지_분리구조");
            generated.transform.SetParent(building.transform, false);

            Renderer referenceFloorRenderer = GameObject.Find("ShopFloor")?.GetComponent<Renderer>();
            Material floorMaterial = referenceFloorRenderer != null
                ? referenceFloorRenderer.sharedMaterial
                : null;

            CreateVisibleFloor(generated.transform, "GachaShop_WalkableFloor",
                new Vector3(60f, -0.05f, 12.6f), new Vector3(14f, 0.1f, 10.2f), floorMaterial);
            CreateVisibleFloor(generated.transform, "KujiShop_WalkableFloor",
                new Vector3(72.5f, -0.05f, 12.6f), new Vector3(11f, 0.1f, 10.2f), floorMaterial);

            Renderer wallRenderer = FindChild(building.transform, "가챠_쿠지_전문점_왼쪽벽")
                ?.GetComponent<Renderer>();
            Material wallMaterial = wallRenderer != null ? wallRenderer.sharedMaterial : null;

            CreateCube(generated.transform, "중앙_분리벽",
                new Vector3(67f, 2.7f, 12.7f), new Vector3(0.28f, 5.4f, 10.25f), wallMaterial);

            foreach (string doorName in new[]
                     {
                         "가챠_쿠지_전문점_자동문",
                         "가챠샵_자동문",
                         "쿠지샵_자동문"
                     })
            {
                Transform door = FindChild(building.transform, doorName);
                if (door != null) UnityEngine.Object.DestroyImmediate(door.gameObject);
            }

            Transform originalSign = FindChild(building.transform, "가챠 · 쿠지 전문점");
            if (originalSign != null)
            {
                originalSign.gameObject.SetActive(false);
                CreateSign(originalSign.gameObject, generated.transform, "가챠샵_간판",
                    "별빛 가챠샵", new Vector3(60.5f, 5.95f, 7.25f));
                CreateSign(originalSign.gameObject, generated.transform, "쿠지샵_간판",
                    "달토끼 쿠지샵", new Vector3(72f, 5.95f, 7.25f));
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureDoor(GameObject doorObject, string name, Vector3 worldPosition, string buildingId)
        {
            doorObject.name = name;
            doorObject.transform.position = worldPosition;
            doorObject.transform.rotation = Quaternion.identity;
            doorObject.transform.localScale = Vector3.one;

            Transform left = doorObject.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item != doorObject.transform && item.name.Contains("왼쪽문"));
            Transform right = doorObject.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item != doorObject.transform && item.name.Contains("오른쪽문"));
            if (left == null || right == null)
                throw new InvalidOperationException($"{name}의 좌우 문 패널을 찾을 수 없습니다.");

            left.localPosition = new Vector3(-0.85f, 1.35f, 0f);
            right.localPosition = new Vector3(0.85f, 1.35f, 0f);
            left.localRotation = Quaternion.identity;
            right.localRotation = Quaternion.identity;

            ShopBuildingZone zone = doorObject.GetComponent<ShopBuildingZone>();
            ShopAutomaticDoorNetwork door = doorObject.GetComponent<ShopAutomaticDoorNetwork>();
            Unity.Netcode.NetworkObject networkObject = doorObject.GetComponent<Unity.Netcode.NetworkObject>();
            if (zone == null || door == null || networkObject == null)
                throw new InvalidOperationException($"{name}에 자동문 구성요소가 없습니다.");

            zone.EditorConfigure(buildingId, true);
            door.EditorConfigure(zone, left, right, new Vector3(-0.9f, 0f, 0f),
                new Vector3(0.9f, 0f, 0f), 0.35f);
            typeof(Unity.Netcode.NetworkObject)
                .GetMethod("OnValidate",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(networkObject, null);
            EditorUtility.SetDirty(networkObject);

            ShopDoorPresenceSensor sensor = doorObject.GetComponentInChildren<ShopDoorPresenceSensor>(true);
            if (sensor == null)
                throw new InvalidOperationException($"{name}에 감지영역이 없습니다.");
            sensor.transform.localPosition = Vector3.zero;
            sensor.EditorConfigure(door);
        }

        private static void CreateSign(
            GameObject source,
            Transform parent,
            string name,
            string text,
            Vector3 worldPosition)
        {
            GameObject sign = UnityEngine.Object.Instantiate(source, parent);
            sign.name = name;
            sign.SetActive(true);
            sign.transform.position = worldPosition;
            sign.transform.rotation = Quaternion.identity;
            TMP_Text label = sign.GetComponent<TMP_Text>();
            if (label != null) label.text = text;
            TextMesh legacyLabel = sign.GetComponent<TextMesh>();
            if (legacyLabel != null)
            {
                legacyLabel.text = text;
                Renderer labelRenderer = legacyLabel.GetComponent<Renderer>();
                if (labelRenderer != null && legacyLabel.font != null)
                    labelRenderer.sharedMaterial = legacyLabel.font.material;
            }
        }

        private static void FixMirroredStreetSigns()
        {
            string[] signNames =
            {
                "포근한 인형뽑기",
                "오늘의 인기 뽑기",
                "별빛 프리미엄",
                "인형뽑기 아케이드",
                "가챠 · 쿠지 전문점",
                "평판으로 열리는 상점",
                "가챠샵_간판",
                "쿠지샵_간판"
            };

            Scene scene = SceneManager.GetActiveScene();
            foreach (string signName in signNames)
            {
                TextMesh sign = Resources.FindObjectsOfTypeAll<TextMesh>()
                    .FirstOrDefault(item =>
                        item.gameObject.scene == scene &&
                        item.gameObject.name == signName);
                if (sign == null) continue;

                sign.transform.rotation = Quaternion.identity;
                if (signName == "인형뽑기 아케이드")
                    sign.transform.position = new Vector3(30f, 6.45f, 7.25f);
                Renderer signRenderer = sign.GetComponent<Renderer>();
                if (signRenderer != null && sign.font != null)
                {
                    signRenderer.sharedMaterial = sign.font.material;
                    EditorUtility.SetDirty(signRenderer);
                }
                EditorUtility.SetDirty(sign.transform);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureRoadsideCustomersAndUpgradeStation()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path !=
                "Assets/PickAndPlaceShop/Scenes/PickAndPlaceShop_MainStreetSlice_Multiplayer.unity")
                throw new InvalidOperationException("메인 거리 씬을 연 상태에서 실행해야 합니다.");

            ShopNightSalesSystem nightSales = Resources.FindObjectsOfTypeAll<ShopNightSalesSystem>()
                .FirstOrDefault(item => item.gameObject.scene == scene);
            if (nightSales == null)
                throw new InvalidOperationException("ShopNightSalesSystem을 찾을 수 없습니다.");

            GameObject previous = GameObject.Find("ShopGameplayEnhancements");
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);

            var generated = new GameObject("ShopGameplayEnhancements");

            Transform entry = CreateRoutePoint(generated.transform, "RoadsideShopEntry",
                new Vector3(14.15f, 0.15f, 0f));
            Transform[] spawnPoints =
            {
                CreateRoutePoint(generated.transform, "RoadsideSpawn_SouthNear",
                    new Vector3(18f, 0.2f, -6.1f)),
                CreateRoutePoint(generated.transform, "RoadsideSpawn_SouthFar",
                    new Vector3(23f, 0.2f, -6.1f)),
                CreateRoutePoint(generated.transform, "RoadsideSpawn_NorthNear",
                    new Vector3(18f, 0.2f, 6.1f)),
                CreateRoutePoint(generated.transform, "RoadsideSpawn_NorthFar",
                    new Vector3(23f, 0.2f, 6.1f))
            };
            Transform exit = CreateRoutePoint(generated.transform, "RoadsideShopExit",
                new Vector3(17.4f, 0.2f, 0f));
            nightSales.EditorConfigureRoadsideRoute(entry, spawnPoints, exit);
            EditorUtility.SetDirty(nightSales);

            Renderer counterRenderer = GameObject.Find("Counter")?.GetComponent<Renderer>();
            Renderer topRenderer = GameObject.Find("CounterTop")?.GetComponent<Renderer>();
            Renderer screenRenderer = GameObject.Find("RegisterScreen")?.GetComponent<Renderer>();
            Material bodyMaterial = counterRenderer != null ? counterRenderer.sharedMaterial : null;
            Material trimMaterial = topRenderer != null ? topRenderer.sharedMaterial : null;
            Material screenMaterial = screenRenderer != null ? screenRenderer.sharedMaterial : null;

            var terminal = new GameObject("Shop Upgrade Terminal");
            terminal.transform.SetParent(generated.transform, true);
            terminal.transform.position = new Vector3(5.15f, 0f, -4.5f);
            ShopInteractable interactable = terminal.AddComponent<ShopInteractable>();
            interactable.Configure(ShopAction.UpgradeShop, "상점 업그레이드");
            terminal.AddComponent<ShopUpgradeTerminal>();
            BoxCollider terminalCollider = terminal.AddComponent<BoxCollider>();
            terminalCollider.center = new Vector3(0f, 0.9f, 0f);
            terminalCollider.size = new Vector3(1.35f, 1.8f, 1.2f);

            CreateDecorCube(terminal.transform, "UpgradeCabinet",
                new Vector3(5.15f, 0.72f, -4.5f), new Vector3(1.15f, 1.44f, 1f), bodyMaterial);
            GameObject trim = CreateDecorCube(terminal.transform, "UpgradeTrim",
                new Vector3(5.15f, 1.48f, -4.5f), new Vector3(1.3f, 0.14f, 1.12f), trimMaterial);
            GameObject screen = CreateDecorCube(terminal.transform, "UpgradeScreen",
                new Vector3(5.15f, 1.08f, -5.03f), new Vector3(0.82f, 0.54f, 0.08f), screenMaterial);

            TextMesh referenceLabel = GameObject.Find("RegisterLabel")?.GetComponent<TextMesh>();
            TextMesh summary = null;
            if (referenceLabel != null)
            {
                CreateTerminalLabel(referenceLabel, terminal.transform, "UpgradeTitle",
                    "업그레이드 내역", new Vector3(5.15f, 1.95f, -4.5f), 0.78f);
                summary = CreateTerminalLabel(referenceLabel, terminal.transform, "UpgradeSummary",
                    "업그레이드 0/12\nE로 내역 열기", new Vector3(5.15f, 1.11f, -5.09f), 0.34f);
            }

            var indicatorRenderers = new List<Renderer>();
            for (int i = 0; i < 6; i++)
            {
                GameObject indicator = CreateDecorCube(terminal.transform, "UpgradeIndicator_" + (i + 1),
                    new Vector3(4.77f + i * 0.15f, 0.42f, -5.02f),
                    new Vector3(0.1f, 0.1f, 0.08f), screenMaterial);
                indicatorRenderers.Add(indicator.GetComponent<Renderer>());
            }

            var lightingTier = new GameObject("FacilityLightingTier");
            lightingTier.transform.SetParent(generated.transform, true);
            foreach (Vector3 position in new[]
                     {
                         new Vector3(3.2f, 4.25f, -4.4f),
                         new Vector3(7.2f, 4.25f, -4.4f),
                         new Vector3(11.2f, 4.25f, -4.4f),
                         new Vector3(3.2f, 4.25f, 2.5f),
                         new Vector3(7.2f, 4.25f, 2.5f),
                         new Vector3(11.2f, 4.25f, 2.5f)
                     })
                CreateUpgradeCeilingLight(lightingTier.transform, position, screenMaterial);
            lightingTier.SetActive(false);

            var decorationTier = new GameObject("FacilityDecorationTier");
            decorationTier.transform.SetParent(generated.transform, true);
            CreateDecorCube(decorationTier.transform, "CounterRenewalGoldTrim",
                new Vector3(7.8f, 1.36f, -5.48f), new Vector3(3.5f, 0.09f, 0.08f), screenMaterial);
            CreateDecorCube(decorationTier.transform, "UpgradeStationGoldTrim",
                new Vector3(5.15f, 1.48f, -5.06f), new Vector3(1.25f, 0.08f, 0.08f), screenMaterial);
            CreateUpgradePlant(decorationTier.transform, "RenewalPlant_Left",
                new Vector3(3.5f, 0f, -5.8f), bodyMaterial, trimMaterial);
            CreateUpgradePlant(decorationTier.transform, "RenewalPlant_Right",
                new Vector3(12.8f, 0f, -5.8f), bodyMaterial, trimMaterial);
            if (referenceLabel != null)
                CreateTerminalLabel(referenceLabel, decorationTier.transform, "RenewalWallSign",
                    "우리 소품샵 · 리뉴얼", new Vector3(7.2f, 3.45f, -9.15f), 1.05f);
            decorationTier.SetActive(false);

            ShopUpgradeVisualController visualController = terminal.AddComponent<ShopUpgradeVisualController>();
            visualController.EditorConfigure(
                lightingTier,
                decorationTier,
                indicatorRenderers.ToArray(),
                new[] { trim.GetComponent<Renderer>(), screen.GetComponent<Renderer>() },
                summary);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static Transform CreateRoutePoint(Transform parent, string name, Vector3 worldPosition)
        {
            var point = new GameObject(name);
            point.transform.SetParent(parent, true);
            point.transform.position = worldPosition;
            return point.transform;
        }

        private static GameObject CreateDecorCube(
            Transform parent,
            string name,
            Vector3 worldPosition,
            Vector3 worldScale,
            Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, true);
            cube.transform.position = worldPosition;
            cube.transform.rotation = Quaternion.identity;
            cube.transform.localScale = worldScale;
            if (material != null) cube.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }

        private static TextMesh CreateTerminalLabel(
            TextMesh reference,
            Transform parent,
            string name,
            string text,
            Vector3 worldPosition,
            float scale)
        {
            GameObject labelObject = UnityEngine.Object.Instantiate(reference.gameObject, parent);
            labelObject.name = name;
            labelObject.transform.position = worldPosition;
            labelObject.transform.rotation = Quaternion.identity;
            labelObject.transform.localScale = Vector3.one * scale;
            TextMesh label = labelObject.GetComponent<TextMesh>();
            label.text = text;
            Renderer labelRenderer = label.GetComponent<Renderer>();
            if (labelRenderer != null && label.font != null)
                labelRenderer.sharedMaterial = label.font.material;
            return label;
        }

        private static void CreateUpgradeCeilingLight(
            Transform parent,
            Vector3 worldPosition,
            Material fixtureMaterial)
        {
            CreateDecorCube(parent, "WarmLightFixture", worldPosition,
                new Vector3(0.72f, 0.14f, 0.72f), fixtureMaterial);
            var lightObject = new GameObject("WarmShopLight", typeof(Light));
            lightObject.transform.SetParent(parent, true);
            lightObject.transform.position = worldPosition + Vector3.down * 0.22f;
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.78f, 0.5f);
            light.intensity = 1.35f;
            light.range = 6.5f;
            light.shadows = LightShadows.None;
        }

        private static void CreateUpgradePlant(
            Transform parent,
            string name,
            Vector3 worldPosition,
            Material potMaterial,
            Material leafMaterial)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, true);
            GameObject pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pot.name = "Pot";
            pot.transform.SetParent(root.transform, true);
            pot.transform.position = worldPosition + Vector3.up * 0.35f;
            pot.transform.localScale = new Vector3(0.42f, 0.35f, 0.42f);
            if (potMaterial != null) pot.GetComponent<Renderer>().sharedMaterial = potMaterial;
            UnityEngine.Object.DestroyImmediate(pot.GetComponent<Collider>());

            foreach (Vector3 offset in new[]
                     {
                         new Vector3(-0.24f, 0.95f, 0f),
                         new Vector3(0.22f, 1.02f, 0.05f),
                         new Vector3(0f, 1.25f, -0.04f)
                     })
            {
                GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                leaf.name = "Leaf";
                leaf.transform.SetParent(root.transform, true);
                leaf.transform.position = worldPosition + offset;
                leaf.transform.localScale = new Vector3(0.48f, 0.7f, 0.48f);
                if (leafMaterial != null) leaf.GetComponent<Renderer>().sharedMaterial = leafMaterial;
                UnityEngine.Object.DestroyImmediate(leaf.GetComponent<Collider>());
            }
        }

        private static void CreateCube(
            Transform parent,
            string name,
            Vector3 worldPosition,
            Vector3 worldScale,
            Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, true);
            cube.transform.position = worldPosition;
            cube.transform.rotation = Quaternion.identity;
            cube.transform.localScale = worldScale;
            if (material != null) cube.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Transform FindChild(Transform root, string exactName)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == exactName);
        }

        private static void CreateVisibleFloor(
            Transform parent,
            string name,
            Vector3 worldPosition,
            Vector3 worldScale,
            Material material)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = name;
            floor.transform.SetParent(parent, true);
            floor.transform.position = worldPosition;
            floor.transform.rotation = Quaternion.identity;
            floor.transform.localScale = worldScale;
            if (material != null) floor.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
