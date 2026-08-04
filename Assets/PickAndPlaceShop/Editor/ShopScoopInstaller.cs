#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PickAndPlaceShop.Editor
{
    public static class ShopScoopInstaller
    {
        private const string PrefabFolder = "Assets/PickAndPlaceShop/Prefabs/ClawMachines";
        private const string MaterialFolder = "Assets/PickAndPlaceShop/Materials/Scoop";
        private const string DeprecatedFolder = "Assets/PickAndPlaceShop/Deprecated/Claw";

        [MenuItem("Tools/Pick And Place Shop/Install Scoop Machines")]
        public static void Install()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("삽형 팬 설치는 Edit Mode에서 실행해야 합니다.");

            EnsureFolder(MaterialFolder);
            EnsureFolder(DeprecatedFolder);
            EnsurePhysicsLayers(out int prizeLayer, out int handleLayer);
            Material panMaterial = CreateMaterial(MaterialFolder + "/FryingPanBody.mat",
                new Color(0.055f, 0.062f, 0.07f), 0.08f, 0.22f);
            Material edgeMaterial = CreateMaterial(MaterialFolder + "/FryingPanEdge.mat",
                new Color(0.085f, 0.092f, 0.1f), 0.1f, 0.26f);

            string[] prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileNameWithoutExtension(path)
                    .StartsWith("ClawMachine_", StringComparison.Ordinal))
                .OrderBy(path => path)
                .ToArray();
            foreach (string path in prefabPaths)
                UpgradePrefab(path, panMaterial, edgeMaterial, handleLayer);
            if (prizeLayer >= 0 && handleLayer >= 0)
                Physics.IgnoreLayerCollision(prizeLayer, handleLayer, true);

            ConfigurePresets();
            MoveLegacyAsset(PrefabFolder + "/SharedPhysicalClawRig.prefab",
                DeprecatedFolder + "/SharedPhysicalClawRig.prefab");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
            {
                foreach (ShopClawMachineNetwork machine in UnityEngine.Object.FindObjectsByType<ShopClawMachineNetwork>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    ShopClawScoopRig rig = machine.GetComponentInChildren<ShopClawScoopRig>(true);
                    if (rig == null) continue;
                    Rigidbody carriage = machine.GetComponentsInChildren<Rigidbody>(true)
                        .FirstOrDefault(body => body.name == "ScoopRailCarriage");
                    machine.EditorConfigureScoopRig(carriage, rig);
                    EditorUtility.SetDirty(machine);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log("[ScoopInstaller] INSTALLED prefabs=" + prefabPaths.Length +
                      " policy=AwardAll compoundColliders=9");
        }

        private static void UpgradePrefab(string path, Material panMaterial, Material edgeMaterial,
            int handleLayer)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ShopClawMachineNetwork machine = root.GetComponent<ShopClawMachineNetwork>();
                if (machine == null) throw new InvalidOperationException(path + ": machine component missing");
                Transform head = root.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(item => item.name == "ClawHead");
                if (head == null) throw new InvalidOperationException(path + ": ClawHead missing");

                foreach (Transform child in head.Cast<Transform>().ToArray())
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                foreach (Collider collider in head.GetComponents<Collider>())
                    UnityEngine.Object.DestroyImmediate(collider);
                foreach (ConfigurableJoint joint in head.GetComponents<ConfigurableJoint>())
                    UnityEngine.Object.DestroyImmediate(joint);
                foreach (HingeJoint hinge in root.GetComponentsInChildren<HingeJoint>(true))
                    UnityEngine.Object.DestroyImmediate(hinge);
                ShopClawMachineConfig config = machine.Config;
                float diameter = config != null ? config.ScoopDiameter : 1.24f;
                float thickness = config != null ? config.ScoopBottomThickness : 0.065f;
                float rimHeight = config != null ? config.ScoopRimHeight : 0.19f;
                float pivotHeight = config != null ? config.ScoopPivotHeight : 0.55f;
                BuildScoop(head, diameter, thickness, rimHeight, pivotHeight,
                    panMaterial, edgeMaterial, handleLayer,
                    out ShopClawScoopRig scoopRig);

                Rigidbody headBody = head.GetComponent<Rigidbody>();
                if (headBody == null) headBody = head.gameObject.AddComponent<Rigidbody>();
                headBody.isKinematic = true;
                headBody.useGravity = false;
                headBody.interpolation = RigidbodyInterpolation.Interpolate;
                headBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                Transform carriage = root.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(item => item.name == "PhysicalClawCarriage" ||
                                            item.name == "ScoopRailCarriage");
                if (carriage == null)
                {
                    carriage = new GameObject("ScoopRailCarriage").transform;
                    carriage.SetParent(root.transform, false);
                }
                carriage.name = "ScoopRailCarriage";
                Rigidbody carriageBody = carriage.GetComponent<Rigidbody>();
                if (carriageBody == null) carriageBody = carriage.gameObject.AddComponent<Rigidbody>();
                carriageBody.isKinematic = true;
                carriageBody.useGravity = false;
                carriageBody.interpolation = RigidbodyInterpolation.Interpolate;

                scoopRig.EditorConfigure(headBody,
                    head.Find("ScoopRig/ScoopBottomCollider").GetComponent<BoxCollider>(),
                    head.Find("ScoopRig/Rims").GetComponentsInChildren<BoxCollider>(true),
                    head.Find("ScoopRig/Visuals"), head.Find("ScoopRig/CurlPivot"),
                    head.Find("ScoopRig/ScoopHandleCollider").GetComponent<CapsuleCollider>());
                foreach (Collider collider in scoopRig.GetComponentsInChildren<Collider>(true))
                    if (config != null && config.ScoopPhysicsMaterial != null)
                        collider.material = config.ScoopPhysicsMaterial;
                Transform floorRoot = root.transform.Find("PhysicalFloorWithChute");
                if (floorRoot != null && config != null && config.MachineFloorMaterial != null)
                    foreach (Collider collider in floorRoot.GetComponentsInChildren<Collider>(true))
                        collider.material = config.MachineFloorMaterial;
                machine.EditorConfigureScoopRig(carriageBody, scoopRig);
                EnsureChuteTriggerClearance(root.transform, diameter);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void BuildScoop(Transform head, float diameter, float thickness,
            float rimHeight, float pivotHeight, Material panMaterial, Material edgeMaterial,
            int handleLayer, out ShopClawScoopRig rig)
        {
            GameObject rigObject = new("ScoopRig");
            rigObject.transform.SetParent(head, false);
            rigObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            Transform visuals = new GameObject("Visuals").transform;
            visuals.SetParent(rigObject.transform, false);
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "ScoopPanVisual";
            disc.transform.SetParent(visuals, false);
            disc.transform.localPosition = Vector3.up * (thickness * 0.14f);
            disc.transform.localScale = new Vector3(diameter * 0.5f, thickness * 0.5f, diameter * 0.5f);
            UnityEngine.Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.GetComponent<Renderer>().sharedMaterial = panMaterial;

            GameObject bottom = new("ScoopBottomCollider");
            bottom.transform.SetParent(rigObject.transform, false);
            BoxCollider bottomCollider = bottom.AddComponent<BoxCollider>();
            bottomCollider.size = new Vector3(diameter * 0.88f, thickness, diameter * 0.88f);

            Transform rims = new GameObject("Rims").transform;
            rims.SetParent(rigObject.transform, false);
            var rimColliders = new List<BoxCollider>(8);
            float radius = diameter * 0.45f;
            float arcLength = 2f * Mathf.PI * radius / 8f * 1.08f;
            for (int index = 0; index < 8; index++)
            {
                float angle = index * 45f;
                float radians = angle * Mathf.Deg2Rad;
                float height = index == 0 || index == 4 ? Mathf.Min(0.04f, rimHeight) : rimHeight;
                GameObject rim = new("ScoopRim_" + index);
                rim.transform.SetParent(rims, false);
                rim.transform.localPosition = new Vector3(Mathf.Sin(radians) * radius,
                    thickness * 0.5f + height * 0.5f, Mathf.Cos(radians) * radius);
                rim.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
                BoxCollider collider = rim.AddComponent<BoxCollider>();
                collider.size = new Vector3(arcLength, height, 0.065f);
                rimColliders.Add(collider);

                GameObject rimVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rimVisual.name = "RimVisual";
                rimVisual.transform.SetParent(rim.transform, false);
                rimVisual.transform.localPosition = new Vector3(0f, height * 0.04f, 0.012f);
                rimVisual.transform.localRotation = Quaternion.Euler(-7f, 0f, 0f);
                rimVisual.transform.localScale = new Vector3(collider.size.x * 1.04f,
                    collider.size.y, collider.size.z * 1.35f);
                UnityEngine.Object.DestroyImmediate(rimVisual.GetComponent<Collider>());
                rimVisual.GetComponent<Renderer>().sharedMaterial = edgeMaterial;
            }

            float handleLength = Mathf.Max(0.62f, diameter * 0.62f);
            Vector3 handleStart = new(0f, thickness + rimHeight * 0.62f,
                -diameter * 0.43f);
            Vector3 handleEnd = new(0f, Mathf.Max(pivotHeight, handleStart.y + 0.14f),
                -diameter * 0.43f - handleLength);
            GameObject handle = CreateCylinderBetween("ScoopHandle", visuals,
                handleStart, handleEnd, 0.075f, edgeMaterial);
            UnityEngine.Object.DestroyImmediate(handle.GetComponent<Collider>());

            Transform curlPivot = new GameObject("CurlPivot").transform;
            curlPivot.SetParent(rigObject.transform, false);
            curlPivot.localPosition = handleEnd;

            GameObject handleColliderObject = new("ScoopHandleCollider");
            handleColliderObject.transform.SetParent(rigObject.transform, false);
            ConfigureBetween(handleColliderObject.transform, handleStart, handleEnd);
            if (handleLayer >= 0) handleColliderObject.layer = handleLayer;
            CapsuleCollider handleCollider = handleColliderObject.AddComponent<CapsuleCollider>();
            handleCollider.direction = 1;
            handleCollider.radius = 0.082f;
            handleCollider.height = Vector3.Distance(handleStart, handleEnd);

            rig = head.GetComponent<ShopClawScoopRig>();
            if (rig == null) rig = head.gameObject.AddComponent<ShopClawScoopRig>();
            rig.EditorConfigure(head.GetComponent<Rigidbody>(), bottomCollider,
                rimColliders.ToArray(), visuals, curlPivot, handleCollider);
        }

        private static GameObject CreateCylinderBetween(string name, Transform parent,
            Vector3 start, Vector3 end, float radius, Material material)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.SetParent(parent, false);
            ConfigureBetween(cylinder.transform, start, end);
            cylinder.transform.localScale = new Vector3(radius,
                Vector3.Distance(start, end) * 0.5f, radius);
            cylinder.GetComponent<Renderer>().sharedMaterial = material;
            return cylinder;
        }

        private static void EnsureChuteTriggerClearance(Transform root, float scoopDiameter)
        {
            BoxCollider trigger = root.GetComponentsInChildren<BoxCollider>(true)
                .FirstOrDefault(item => item.name == "PrizeAwardTrigger" && item.isTrigger);
            if (trigger == null) return;
            float horizontalSize = Mathf.Max(1.34f, scoopDiameter * 0.9f);
            Vector3 size = trigger.size;
            size.x = Mathf.Max(size.x, horizontalSize);
            size.y = Mathf.Max(size.y, 1.3f);
            size.z = Mathf.Max(size.z, horizontalSize);
            trigger.size = size;
        }

        private static void ConfigureBetween(Transform target, Vector3 start, Vector3 end)
        {
            Vector3 direction = end - start;
            target.localPosition = (start + end) * 0.5f;
            target.localRotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
        }

        private static void ConfigurePresets()
        {
            ShopClawMachineConfig[] configs = AssetDatabase.FindAssets("t:ShopClawMachineConfig",
                    new[] { "Assets/PickAndPlaceShop/Data/ClawGallery" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopClawMachineConfig>)
                .Where(config => config != null)
                .OrderBy(config => config.MachineId)
                .ToArray();
            for (int index = 0; index < configs.Length; index++)
            {
                ShopClawMachineConfig config = configs[index];
                float diameter = 1.52f + index * 0.015f;
                float rim = 0.56f + index * 0.008f;
                float scrape = 0.78f + index * 0.02f;
                float lift = 1.25f;
                config.EditorConfigureScoop(diameter, 0.065f, rim, scrape, 0.48f,
                    8f, 62f, 0.006f, 0.004f, 0.76f,
                    new Vector4(0.34f, 0.40f, 0.48f, 0.58f));
                config.EditorConfigureRarity(config.RarityWeights, ShopMultiPrizePolicy.AwardAll);
                float tunedDescend = config.MachineId == 101 ? 0.525f :
                    config.MachineId == 102 ? 0.60f : 0.6875f;
                config.EditorConfigureCaptureMotion(config.DropHeight, lift, tunedDescend);
                config.EditorConfigureScoopMotion(tunedDescend, lift, 42.5f, 112.5f);
                config.EditorConfigureScoopDischarge(0.68f);
                config.EditorConfigureScoopAwardTiming(0.2f, 3f);
                config.EditorConfigureReturnSpeed(1.2f);
                EditorUtility.SetDirty(config);
            }
        }

        private static Material CreateMaterial(string path, Color color, float metallic, float smoothness)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsurePhysicsLayers(out int prizeLayer, out int handleLayer)
        {
            prizeLayer = EnsureLayer("ClawPrize");
            handleLayer = EnsureLayer("ScoopHandle");
            if (prizeLayer >= 0 && handleLayer >= 0)
                Physics.IgnoreLayerCollision(prizeLayer, handleLayer, true);
        }

        private static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) return existing;
            SerializedObject tagManager = new(AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int index = 8; index < 32; index++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(index);
                if (!string.IsNullOrEmpty(layer.stringValue)) continue;
                layer.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                return index;
            }
            return -1;
        }

        private static void MoveLegacyAsset(string source, string destination)
        {
            if (AssetDatabase.LoadMainAssetAtPath(source) == null ||
                AssetDatabase.LoadMainAssetAtPath(destination) != null) return;
            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error)) Debug.LogWarning("[ScoopInstaller] " + error);
        }

        private static void EnsureFolder(string path)
        {
            string current = "Assets";
            foreach (string part in path.Split('/').Skip(1))
            {
                string next = current + "/" + part;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part);
                current = next;
            }
        }
    }
}
#endif
