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
    public static class ShopPhysicalClawInstaller
    {
        private const string FingerModelPath = "Assets/외형들모음/ClawFinger_low.fbx";
        private const string HousingModelPath = "Assets/외형들모음/ClawHousing_low.fbx";
        private const string ConnectorModelPath = "Assets/외형들모음/CableConnector_low.fbx";
        private const string MaterialFolder = "Assets/PickAndPlaceShop/Materials/PhysicalClaw";
        private const string PrefabFolder = "Assets/PickAndPlaceShop/Prefabs/ClawMachines";
        private const string RigPrefabPath = PrefabFolder + "/SharedPhysicalClawRig.prefab";
        private const string PrizePrefabPath = "Assets/PickAndPlaceShop/Prefabs/ClawPrize_Network.prefab";

        [MenuItem("Tools/Pick And Place Shop/Install Physical Claw Machines")]
        public static void Install()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("물리 집게 설치는 Edit Mode에서 실행해야 합니다.");

            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);
            ConfigureNormalMaps();

            Material fingerMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/ClawFinger_PBR.mat",
                new Color(0.58f, 0.66f, 0.76f), 0.82f, 0.72f,
                "Assets/외형들모음/Textures/ClawFinger_2.png");
            Material housingMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/ClawHousing_PBR.mat",
                new Color(0.12f, 0.32f, 0.52f), 0.74f, 0.68f,
                "Assets/외형들모음/Textures/ClawHousing_2.png");
            Material connectorMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/CableConnector_PBR.mat",
                new Color(0.16f, 0.19f, 0.24f), 0.88f, 0.76f,
                "Assets/외형들모음/Textures/CableConnector_2.png");
            Material capsuleMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/ClawCapsuleShell.mat",
                new Color(0.96f, 0.42f, 0.62f), 0.12f, 0.78f, null);
            Material seamMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/ClawCapsuleSeam.mat",
                new Color(0.08f, 0.10f, 0.14f), 0.64f, 0.88f, null);
            UpgradePrizePrefab(capsuleMaterial, seamMaterial);
            GameObject capsulePrizePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrizePrefabPath);

            GameObject fingerModel = AssetDatabase.LoadAssetAtPath<GameObject>(FingerModelPath);
            GameObject housingModel = AssetDatabase.LoadAssetAtPath<GameObject>(HousingModelPath);
            GameObject connectorModel = AssetDatabase.LoadAssetAtPath<GameObject>(ConnectorModelPath);
            if (fingerModel == null || housingModel == null || connectorModel == null)
                throw new FileNotFoundException("집게 FBX 3종을 모두 찾을 수 없습니다.");

            ShopClawMachineNetwork[] machines =
                UnityEngine.Object.FindObjectsByType<ShopClawMachineNetwork>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (machines.Length == 0)
                throw new InvalidOperationException("현재 씬에서 ShopClawMachineNetwork를 찾지 못했습니다.");

            GameObject sharedRigPrefab = BuildSharedRigPrefab(machines);
            ConfigureSharedRigPrefab();
            sharedRigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);

            foreach (ShopClawMachineNetwork machine in machines.OrderBy(item => item.Config.MachineId))
            {
                ConfigurePreset(machine.Config);
                UpgradeMachine(machine, sharedRigPrefab, fingerModel, housingModel, connectorModel,
                    fingerMaterial, housingMaterial, connectorMaterial, capsulePrizePrefab);
            }

            AssetDatabase.SaveAssets();
            foreach (ShopClawMachineNetwork machine in machines)
                ConnectMachinePrefab(machine);

            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PhysicalClawInstaller] INSTALLED machines=" + machines.Length +
                      " normalMaps=3 prefabs=" + machines.Length);
        }

        private static void UpgradeMachine(ShopClawMachineNetwork machine, GameObject sharedRigPrefab,
            GameObject fingerModel, GameObject housingModel, GameObject connectorModel,
            Material fingerMaterial, Material housingMaterial, Material connectorMaterial,
            GameObject capsulePrizePrefab)
        {
            Transform root = machine.transform;
            Transform head = root.GetComponentsInChildren<Rigidbody>(true)
                .Select(body => body.transform)
                .FirstOrDefault(item => item.name == "ClawHead");
            if (head == null) head = FindDeep(root, "ClawHead");
            if (head == null)
                throw new InvalidOperationException(machine.name + ": ClawHead를 찾지 못했습니다.");

            Transform[] rigChildren = head.Cast<Transform>().Where(child =>
                child.name == "SharedPhysicalClawRig" || child.name == "집게 허브" ||
                child.name == "GripVolume" || child.name == "PhysicalClawVisual" ||
                child.name.StartsWith("집게발_", StringComparison.Ordinal)).ToArray();
            foreach (Transform child in rigChildren)
                UnityEngine.Object.DestroyImmediate(child.gameObject);

            GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(sharedRigPrefab, head);
            rig.name = "SharedPhysicalClawRig";
            rig.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            rig.transform.localScale = Vector3.one;
            Transform[] fingers = Enumerable.Range(1, 3)
                .Select(index => FindDeep(rig.transform, "집게발_" + index)).ToArray();
            if (fingers.Any(item => item == null))
                throw new InvalidOperationException(machine.name + ": 공용 집게 프리팹의 발톱이 누락되었습니다.");
            ShopClawFingerAutoLayout layout = rig.GetComponent<ShopClawFingerAutoLayout>();
            if (layout == null)
                throw new InvalidOperationException(machine.name + ": 공용 집게 자동 정렬 컴포넌트가 없습니다.");
            layout.EditorConfigure(fingers, machine.Config.FingerLayoutRadius,
                machine.Config.FingerLayoutHeight, machine.Config.FingerLayoutTilt);
            layout.ApplyLayout();
            Transform grip = FindDeep(rig.transform, "GripVolume");

            Rigidbody headBody = head.GetComponent<Rigidbody>();
            if (headBody == null)
                throw new InvalidOperationException(machine.name + ": ClawHead Rigidbody가 없습니다.");
            Rigidbody carriageBody = ConfigureAuthoredCarriage(root, machine.Config);
            ConfigurableJoint suspensionJoint = head.GetComponent<ConfigurableJoint>();
            if (suspensionJoint == null)
                suspensionJoint = head.gameObject.AddComponent<ConfigurableJoint>();
            ConfigureAuthoredSuspension(suspensionJoint, carriageBody, machine.Config);
            ConfigureAuthoredFingerPhysics(fingers, headBody, machine.Config);
            machine.EditorConfigurePhysicalRig(carriageBody, suspensionJoint);

            CreateFloorHole(machine);
            CreateSpawnVolume(machine);
            UpgradeChute(machine);
            SerializedObject serializedMachine = new(machine);
            serializedMachine.FindProperty("prizePrefab").objectReferenceValue = capsulePrizePrefab;
            SerializedProperty fingersProperty = serializedMachine.FindProperty("clawFingers");
            fingersProperty.arraySize = fingers.Length;
            for (int i = 0; i < fingers.Length; i++)
                fingersProperty.GetArrayElementAtIndex(i).objectReferenceValue = fingers[i];
            serializedMachine.FindProperty("gripVolume").objectReferenceValue = grip;
            serializedMachine.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(machine);
        }

        private static GameObject BuildSharedRigPrefab(IEnumerable<ShopClawMachineNetwork> machines)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (existing != null) return existing;

            ShopClawMachineNetwork canonical = machines
                .OrderBy(machine => machine.Config != null ? machine.Config.MachineId : int.MaxValue)
                .FirstOrDefault();
            if (canonical == null) throw new InvalidOperationException("공용 집게 원본 기계가 없습니다.");
            Transform head = FindDeep(canonical.transform, "ClawHead");
            if (head == null) throw new InvalidOperationException("공용 집게 원본의 ClawHead가 없습니다.");

            string[] childNames =
            {
                "집게 허브", "집게발_1", "집게발_2", "집게발_3", "GripVolume", "PhysicalClawVisual"
            };
            GameObject temporary = new("SharedPhysicalClawRig");
            try
            {
                foreach (string childName in childNames)
                {
                    Transform source = head.Find(childName);
                    if (source == null)
                        throw new InvalidOperationException("공용 집게 원본에 " + childName + "이(가) 없습니다.");
                    GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, temporary.transform);
                    clone.name = childName;
                    clone.transform.localPosition = source.localPosition;
                    clone.transform.localRotation = source.localRotation;
                    clone.transform.localScale = source.localScale;
                }
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(temporary, RigPrefabPath);
                if (saved == null) throw new InvalidOperationException("공용 집게 프리팹 저장에 실패했습니다.");
                return saved;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temporary);
            }
        }

        private static void ConfigureSharedRigPrefab()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(RigPrefabPath);
            try
            {
                Transform[] fingers = Enumerable.Range(1, 3)
                    .Select(index => FindDeep(contents.transform, "집게발_" + index)).ToArray();
                if (fingers.Any(item => item == null))
                    throw new InvalidOperationException("공용 집게 프리팹의 발톱이 누락되었습니다.");
                ShopClawFingerAutoLayout layout = contents.GetComponent<ShopClawFingerAutoLayout>();
                if (layout == null) layout = contents.AddComponent<ShopClawFingerAutoLayout>();
                layout.EditorConfigure(fingers, 0.69f, -0.38f, 120f);
                layout.ApplyLayout();
                foreach (Transform finger in fingers)
                {
                    Rigidbody body = finger.GetComponent<Rigidbody>();
                    if (body == null) body = finger.gameObject.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.mass = 0.12f;
                    body.linearDamping = 1.8f;
                    body.angularDamping = 2.4f;
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    body.solverIterations = 12;
                    body.solverVelocityIterations = 6;

                    HingeJoint hinge = finger.GetComponent<HingeJoint>();
                    if (hinge == null) hinge = finger.gameObject.AddComponent<HingeJoint>();
                    hinge.autoConfigureConnectedAnchor = false;
                    hinge.anchor = Vector3.zero;
                    hinge.axis = Vector3.right;
                    hinge.useLimits = true;
                    hinge.useMotor = true;
                    hinge.useSpring = false;
                    hinge.enableCollision = false;
                }
                PrefabUtility.SaveAsPrefabAsset(contents, RigPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static Rigidbody ConfigureAuthoredCarriage(Transform machineRoot,
            ShopClawMachineConfig config)
        {
            Transform carriage = machineRoot.Find("PhysicalClawCarriage");
            if (carriage == null)
            {
                GameObject carriageObject = new("PhysicalClawCarriage");
                carriageObject.transform.SetParent(machineRoot, false);
                carriage = carriageObject.transform;
            }
            carriage.localPosition = new Vector3(0f, config.TopHeight, 0f);
            carriage.localRotation = Quaternion.identity;
            carriage.localScale = Vector3.one;
            Rigidbody body = carriage.GetComponent<Rigidbody>();
            if (body == null) body = carriage.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            return body;
        }

        private static void ConfigureAuthoredSuspension(ConfigurableJoint joint,
            Rigidbody carriage, ShopClawMachineConfig config)
        {
            joint.connectedBody = carriage;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector3.zero;
            joint.connectedAnchor = Vector3.zero;
            joint.xMotion = ConfigurableJointMotion.Limited;
            joint.yMotion = ConfigurableJointMotion.Limited;
            joint.zMotion = ConfigurableJointMotion.Limited;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.linearLimit = new SoftJointLimit
            {
                limit = config.SuspensionTravel,
                bounciness = 0f,
                contactDistance = 0.015f
            };
            joint.linearLimitSpring = new SoftJointLimitSpring
            {
                spring = config.SuspensionSpring,
                damper = config.SuspensionDamper
            };
            joint.lowAngularXLimit = new SoftJointLimit { limit = -10f };
            joint.highAngularXLimit = new SoftJointLimit { limit = 10f };
            joint.angularYLimit = new SoftJointLimit { limit = 8f };
            joint.angularZLimit = new SoftJointLimit { limit = 10f };
            joint.rotationDriveMode = RotationDriveMode.XYAndZ;
            joint.angularXDrive = default;
            joint.angularYZDrive = default;
            joint.slerpDrive = default;
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = 0.06f;
            joint.projectionAngle = 14f;
            joint.enableCollision = false;
        }

        private static void ConfigureAuthoredFingerPhysics(Transform[] fingers,
            Rigidbody headBody, ShopClawMachineConfig config)
        {
            float authoredClosedOffset = -config.ClosedFingerAngle +
                                         config.ClosedFingerClearanceAngle;
            float lowerAngle = Mathf.Min(config.ClosedFingerAngle, config.OpenFingerAngle);
            float upperAngle = Mathf.Max(config.ClosedFingerAngle, config.OpenFingerAngle);
            foreach (Transform finger in fingers)
            {
                Rigidbody body = finger.GetComponent<Rigidbody>();
                HingeJoint hinge = finger.GetComponent<HingeJoint>();
                if (body == null || hinge == null)
                    throw new InvalidOperationException(finger.name +
                        ": 공용 프리팹의 authored Rigidbody/HingeJoint가 없습니다.");
                body.isKinematic = true;
                body.useGravity = false;
                body.mass = Mathf.Max(0.08f, config.ClawMass * 0.12f);
                body.linearDamping = 1.8f;
                body.angularDamping = 2.4f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.solverIterations = 12;
                body.solverVelocityIterations = 6;
                hinge.connectedBody = headBody;
                hinge.autoConfigureConnectedAnchor = false;
                hinge.anchor = Vector3.zero;
                hinge.connectedAnchor = headBody.transform.InverseTransformPoint(finger.position);
                hinge.axis = Vector3.right;
                hinge.useLimits = true;
                hinge.limits = new JointLimits
                {
                    min = lowerAngle + authoredClosedOffset - 3f,
                    max = upperAngle + authoredClosedOffset + 3f,
                    bounciness = 0f,
                    contactDistance = 2f
                };
                hinge.useSpring = false;
                hinge.useMotor = true;
                hinge.motor = new JointMotor
                {
                    force = config.OpenMotorTorque,
                    targetVelocity = config.OpenMotorSpeed,
                    freeSpin = false
                };
                hinge.enableCollision = false;
            }
        }

        private static void UpgradeFinger(Transform fingerRoot, int index, GameObject fingerModel,
            Material visualMaterial, PhysicsMaterial physicsMaterial)
        {
            if (physicsMaterial != null)
            {
                physicsMaterial.dynamicFriction = 3.5f;
                physicsMaterial.staticFriction = 4.0f;
                physicsMaterial.bounciness = 0.01f;
                physicsMaterial.frictionCombine = PhysicsMaterialCombine.Maximum;
                physicsMaterial.bounceCombine = PhysicsMaterialCombine.Minimum;
                EditorUtility.SetDirty(physicsMaterial);
            }
            foreach (Transform child in fingerRoot.Cast<Transform>().ToArray())
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            foreach (Collider collider in fingerRoot.GetComponents<Collider>())
                UnityEngine.Object.DestroyImmediate(collider);
            foreach (Renderer renderer in fingerRoot.GetComponents<Renderer>())
                UnityEngine.Object.DestroyImmediate(renderer);
            foreach (MeshFilter filter in fingerRoot.GetComponents<MeshFilter>())
                UnityEngine.Object.DestroyImmediate(filter);

            float yaw = index * 120f;
            fingerRoot.localPosition = Quaternion.Euler(0f, yaw, 0f) *
                                       new Vector3(0f, -0.20f, 0.17f);
            fingerRoot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            fingerRoot.localScale = Vector3.one;

            InstantiateModel(fingerModel, fingerRoot, "ClawFinger_Mesh",
                Vector3.zero, Quaternion.Euler(0f, 0f, 180f), Vector3.one * 0.72f,
                visualMaterial);

            CreateCapsule(fingerRoot, "FingerCollider_Shaft",
                new Vector3(0f, -0.22f, 0f), Quaternion.identity, 0.068f, 0.45f, physicsMaterial);
            CreateCapsule(fingerRoot, "FingerCollider_Curve",
                new Vector3(0f, -0.48f, -0.065f), Quaternion.Euler(-35f, 0f, 0f),
                0.064f, 0.30f, physicsMaterial);
            CreateCapsule(fingerRoot, "FingerCollider_Tip",
                new Vector3(0f, -0.68f, -0.05f), Quaternion.Euler(-90f, 0f, 0f),
                0.070f, 0.34f, physicsMaterial);

            if (fingerRoot.GetComponent<ShopClawFingerContactSensor>() == null)
                fingerRoot.gameObject.AddComponent<ShopClawFingerContactSensor>();
        }

        private static void EnsureHousingCollider(Transform head)
        {
            Transform existing = head.Find("PhysicalClawVisual/HousingCollider");
            GameObject colliderObject;
            if (existing == null)
            {
                colliderObject = new GameObject("HousingCollider");
                colliderObject.transform.SetParent(head.Find("PhysicalClawVisual"), false);
            }
            else colliderObject = existing.gameObject;
            colliderObject.transform.localPosition = new Vector3(0f, -0.04f, 0f);
            SphereCollider collider = colliderObject.GetComponent<SphereCollider>();
            if (collider == null) collider = colliderObject.AddComponent<SphereCollider>();
            collider.radius = 0.28f;
            // The housing previously struck the pile before the fingers closed and pushed the
            // suspended head up to 0.55 m off target. Prize contact belongs to the nine authored
            // finger colliders; this trigger preserves overlap sensing without applying force.
            collider.isTrigger = true;
        }

        private static void CreateFloorHole(ShopClawMachineNetwork machine)
        {
            Transform root = machine.transform;
            Transform oldPhysics = root.Find("PhysicalFloorWithChute");
            if (oldPhysics != null) UnityEngine.Object.DestroyImmediate(oldPhysics.gameObject);

            Transform visualFloor = FindDeep(root, "상품 바닥");
            if (visualFloor != null)
                foreach (Collider collider in visualFloor.GetComponents<Collider>())
                    UnityEngine.Object.DestroyImmediate(collider);
            Transform lowerBody = FindDeep(root, "하부 본체");
            if (lowerBody != null)
                foreach (Collider collider in lowerBody.GetComponents<Collider>())
                    UnityEngine.Object.DestroyImmediate(collider);

            GameObject physicsRoot = new("PhysicalFloorWithChute");
            physicsRoot.transform.SetParent(root, false);
            PhysicsMaterial floorMaterial = machine.Config.MachineFloorMaterial;
            CreateFloorSlab(physicsRoot.transform, "Floor_Left",
                new Vector3(-0.5675f, 0.90f, 0f), new Vector3(2.315f, 0.18f, 2.55f), floorMaterial);
            CreateFloorSlab(physicsRoot.transform, "Floor_ChuteBack",
                new Vector3(1.1575f, 0.90f, 0.6775f), new Vector3(1.135f, 0.18f, 1.195f), floorMaterial);
            CreateFloorSlab(physicsRoot.transform, "Floor_ChuteFront",
                new Vector3(1.1575f, 0.90f, -1.1575f), new Vector3(1.135f, 0.18f, 0.235f), floorMaterial);

            // The original lower cabinet was one solid box and silently blocked the visual chute.
            // Rebuild it around the same opening, then add a collection tray inside the trigger.
            CreateFloorSlab(physicsRoot.transform, "Base_Left",
                new Vector3(-0.5675f, 0.45f, 0f), new Vector3(2.315f, 0.90f, 2.55f), floorMaterial);
            CreateFloorSlab(physicsRoot.transform, "Base_ChuteBack",
                new Vector3(1.1575f, 0.45f, 0.6775f), new Vector3(1.135f, 0.90f, 1.195f), floorMaterial);
            CreateFloorSlab(physicsRoot.transform, "Base_ChuteFront",
                new Vector3(1.1575f, 0.45f, -1.1575f), new Vector3(1.135f, 0.90f, 0.235f), floorMaterial);
            CreateFloorSlab(physicsRoot.transform, "ChuteCatchFloor",
                new Vector3(1.25f, 0.10f, -0.48f), new Vector3(1.18f, 0.16f, 1.05f), floorMaterial);
            CreateFloorSlab(physicsRoot.transform, "ChuteCatchLeft",
                new Vector3(0.61f, 0.46f, -0.48f), new Vector3(0.10f, 0.72f, 1.05f), floorMaterial);
            CreateFloorSlab(physicsRoot.transform, "ChuteCatchRight",
                new Vector3(1.89f, 0.46f, -0.48f), new Vector3(0.10f, 0.72f, 1.05f), floorMaterial);
            CreateFloorSlab(physicsRoot.transform, "ChuteCatchFront",
                new Vector3(1.25f, 0.46f, -1.055f), new Vector3(1.18f, 0.72f, 0.10f), floorMaterial);
            CreateFloorSlab(physicsRoot.transform, "ChuteCatchBack",
                new Vector3(1.25f, 0.46f, 0.095f), new Vector3(1.18f, 0.72f, 0.10f), floorMaterial);
        }

        private static void CreateSpawnVolume(ShopClawMachineNetwork machine)
        {
            Transform old = machine.transform.Find("PrizeSpawnVolume");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            GameObject volume = new("PrizeSpawnVolume");
            volume.transform.SetParent(machine.transform, false);
            volume.transform.localPosition = new Vector3(0f, 1.28f, 0.18f);
            BoxCollider collider = volume.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(2.85f, 0.55f, 2.05f);
        }

        private static void UpgradeChute(ShopClawMachineNetwork machine)
        {
            Transform triggerTransform = FindDeep(machine.transform, "PrizeAwardTrigger");
            if (triggerTransform != null && triggerTransform.TryGetComponent(out BoxCollider trigger))
            {
                trigger.center = Vector3.zero;
                trigger.size = new Vector3(1.18f, 1.10f, 1.05f);
                trigger.isTrigger = true;
            }

            Transform old = machine.transform.Find("ChuteFunnelColliders");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            GameObject funnel = new("ChuteFunnelColliders");
            funnel.transform.SetParent(machine.transform, false);
            CreateSlope(funnel.transform, "Funnel_Left", new Vector3(0.62f, 1.08f, -0.48f),
                new Vector3(0.18f, 0.30f, 1.12f), Quaternion.Euler(0f, 0f, -24f));
            CreateSlope(funnel.transform, "Funnel_Front", new Vector3(1.25f, 1.08f, -1.02f),
                new Vector3(1.18f, 0.30f, 0.18f), Quaternion.Euler(24f, 0f, 0f));
            CreateSlope(funnel.transform, "Funnel_Back", new Vector3(1.25f, 1.08f, 0.06f),
                new Vector3(1.18f, 0.30f, 0.18f), Quaternion.Euler(-24f, 0f, 0f));
        }

        private static void ConfigurePreset(ShopClawMachineConfig config)
        {
            if (config == null) return;
            bool premium = config.MachineId >= 103;
            bool retro = config.MachineId == 102;
            float closeTorque = premium ? 75f : retro ? 105f : 140f;
            float ascentMultiplier = premium ? 0.40f : retro ? 0.55f : 0.70f;
            // Preserve an eight-degree closed clearance so the three tips never intersect;
            // the resulting authored open target remains 58 degrees.
            config.EditorConfigureTorque(-18f, 32f, 8f, closeTorque, 45f, 40f, 80f,
                ascentMultiplier, 8.0f, 0.45f, 0.18f, 3.5f);
            config.EditorConfigureCaptureMotion(premium ? 1.00f : retro ? 0.98f : 0.95f,
                premium ? 1.55f : retro ? 1.40f : 1.25f,
                premium ? 0.55f : retro ? 0.48f : 0.42f);
            config.EditorConfigureSuspension(0.08f, 2200f, 120f);
            config.EditorConfigureGripAssist(premium ? 0.012f : retro ? 0.016f : 0.020f);
            config.EditorConfigureReturnSpeed(premium ? 1.15f : retro ? 1.0f : 0.85f);
            config.EditorConfigureOperator(3f, premium ? 4.15f : retro ? 4.0f : 3.8f,
                premium ? 25f : retro ? 23f : 22f, premium ? 60f : 62f,
                premium ? 2.15f : 2.05f);
            config.EditorConfigureFingerLayout(0.69f, -0.38f, 120f);
            // Cover the full playable bed. The previous Z range stopped at -0.75 while
            // settled prizes can rest near -1.15, making valid prizes unreachable.
            config.EditorConfigureBounds(new Vector2(-1.45f, 1.45f),
                new Vector2(-1.18f, 1.18f));
            EditorUtility.SetDirty(config);
        }

        private static void ConnectMachinePrefab(ShopClawMachineNetwork machine)
        {
            int id = machine.Config != null ? machine.Config.MachineId : machine.GetInstanceID();
            string path = PrefabFolder + "/ClawMachine_" + id + ".prefab";
            GameObject root = machine.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(root))
            {
                PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
                return;
            }

            PrefabUtility.SaveAsPrefabAssetAndConnect(root, path,
                InteractionMode.AutomatedAction, out bool success);
            if (!success) throw new InvalidOperationException("프리팹 저장 실패: " + path);
        }

        private static Material CreateOrUpdateMaterial(string path, Color color,
            float metallic, float smoothness, string normalPath)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit 셰이더를 찾지 못했습니다.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            Texture2D normal = string.IsNullOrEmpty(normalPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 0.75f);
            if (normal != null) material.EnableKeyword("_NORMALMAP");
            else material.DisableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void UpgradePrizePrefab(Material capsuleMaterial, Material seamMaterial)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrizePrefabPath);
            try
            {
                ShopClawPrizeNetwork prize = root.GetComponent<ShopClawPrizeNetwork>();
                if (prize == null) throw new InvalidOperationException("캡슐 네트워크 프리팹 구성요소가 없습니다.");

                foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                foreach (Collider collider in root.GetComponents<Collider>())
                    UnityEngine.Object.DestroyImmediate(collider);
                foreach (Renderer renderer in root.GetComponents<Renderer>())
                    UnityEngine.Object.DestroyImmediate(renderer);
                foreach (MeshFilter filter in root.GetComponents<MeshFilter>())
                    UnityEngine.Object.DestroyImmediate(filter);

                root.name = "ClawCapsule_Network";
                SphereCollider sphere = root.AddComponent<SphereCollider>();
                sphere.center = Vector3.zero;
                sphere.radius = 0.43f;

                GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shell.name = "CapsuleShell";
                shell.transform.SetParent(root.transform, false);
                shell.transform.localPosition = Vector3.zero;
                shell.transform.localScale = new Vector3(0.86f, 0.72f, 0.86f);
                UnityEngine.Object.DestroyImmediate(shell.GetComponent<Collider>());
                Renderer shellRenderer = shell.GetComponent<Renderer>();
                shellRenderer.sharedMaterial = capsuleMaterial;

                GameObject seam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                seam.name = "CapsuleSeam";
                seam.transform.SetParent(root.transform, false);
                seam.transform.localPosition = Vector3.zero;
                seam.transform.localScale = new Vector3(0.455f, 0.025f, 0.455f);
                UnityEngine.Object.DestroyImmediate(seam.GetComponent<Collider>());
                seam.GetComponent<Renderer>().sharedMaterial = seamMaterial;

                prize.Configure(shellRenderer);
                PrefabUtility.SaveAsPrefabAsset(root, PrizePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureNormalMaps()
        {
            string[] paths =
            {
                "Assets/외형들모음/Textures/ClawFinger_2.png",
                "Assets/외형들모음/Textures/ClawHousing_2.png",
                "Assets/외형들모음/Textures/CableConnector_2.png"
            };
            foreach (string path in paths)
            {
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
                importer.convertToNormalmap = false;
                importer.SaveAndReimport();
            }
        }

        private static GameObject InstantiateModel(GameObject model, Transform parent, string name,
            Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            instance.name = name;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = localScale;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterial = material;
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            return instance;
        }

        private static void CreateCapsule(Transform parent, string name, Vector3 localPosition,
            Quaternion localRotation, float radius, float height, PhysicsMaterial material)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            CapsuleCollider collider = go.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.radius = radius;
            collider.height = Mathf.Max(radius * 2f, height);
            collider.material = material;
        }

        private static void CreateFloorSlab(Transform parent, string name,
            Vector3 localPosition, Vector3 size, PhysicsMaterial material)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            BoxCollider collider = go.AddComponent<BoxCollider>();
            collider.size = size;
            collider.material = material;
        }

        private static void CreateSlope(Transform parent, string name, Vector3 localPosition,
            Vector3 size, Quaternion localRotation)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            BoxCollider collider = go.AddComponent<BoxCollider>();
            collider.size = size;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == name);
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
