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
            Material panMaterial = CreateMaterial(MaterialFolder + "/ScoopPan.mat",
                new Color(0.16f, 0.42f, 0.68f), 0.78f, 0.62f);
            Material edgeMaterial = CreateMaterial(MaterialFolder + "/ScoopEdge.mat",
                new Color(0.82f, 0.9f, 0.98f), 0.9f, 0.48f);

            string[] prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileNameWithoutExtension(path)
                    .StartsWith("ClawMachine_", StringComparison.Ordinal))
                .OrderBy(path => path)
                .ToArray();
            foreach (string path in prefabPaths) UpgradePrefab(path, panMaterial, edgeMaterial);

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

        private static void UpgradePrefab(string path, Material panMaterial, Material edgeMaterial)
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
                BuildScoop(head, diameter, thickness, rimHeight, panMaterial, edgeMaterial,
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
                    head.Find("ScoopRig/Visuals"));
                foreach (Collider collider in scoopRig.GetComponentsInChildren<Collider>(true))
                    if (config != null && config.ScoopPhysicsMaterial != null)
                        collider.material = config.ScoopPhysicsMaterial;
                Transform floorRoot = root.transform.Find("PhysicalFloorWithChute");
                if (floorRoot != null && config != null && config.MachineFloorMaterial != null)
                    foreach (Collider collider in floorRoot.GetComponentsInChildren<Collider>(true))
                        collider.material = config.MachineFloorMaterial;
                machine.EditorConfigureScoopRig(carriageBody, scoopRig);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void BuildScoop(Transform head, float diameter, float thickness, float rimHeight,
            Material panMaterial, Material edgeMaterial, out ShopClawScoopRig rig)
        {
            GameObject rigObject = new("ScoopRig");
            rigObject.transform.SetParent(head, false);
            rigObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            Transform visuals = new GameObject("Visuals").transform;
            visuals.SetParent(rigObject.transform, false);
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "ScoopPanVisual";
            disc.transform.SetParent(visuals, false);
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
                rimVisual.transform.localScale = collider.size;
                UnityEngine.Object.DestroyImmediate(rimVisual.GetComponent<Collider>());
                rimVisual.GetComponent<Renderer>().sharedMaterial = edgeMaterial;
            }

            GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            handle.name = "ScoopHandle";
            handle.transform.SetParent(visuals, false);
            handle.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            handle.transform.localScale = new Vector3(0.07f, 0.55f, 0.07f);
            UnityEngine.Object.DestroyImmediate(handle.GetComponent<Collider>());
            handle.GetComponent<Renderer>().sharedMaterial = edgeMaterial;

            rig = head.GetComponent<ShopClawScoopRig>();
            if (rig == null) rig = head.gameObject.AddComponent<ShopClawScoopRig>();
            rig.EditorConfigure(head.GetComponent<Rigidbody>(), bottomCollider,
                rimColliders.ToArray(), visuals);
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
                float diameter = 1.30f + index * 0.02f;
                float rim = 0.50f + index * 0.01f;
                float scrape = 0.78f + index * 0.02f;
                float lift = Mathf.Clamp(config.LiftSpeed, 0.82f, 1.0f);
                config.EditorConfigureScoop(diameter, 0.065f, rim, scrape, 0.48f,
                    8f, 52f, 0.006f, 0.004f, 0.76f,
                    new Vector4(0.34f, 0.40f, 0.48f, 0.58f));
                config.EditorConfigureRarity(config.RarityWeights, ShopMultiPrizePolicy.AwardAll);
                config.EditorConfigureCaptureMotion(config.DropHeight, lift, config.DescendSpeed);
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
