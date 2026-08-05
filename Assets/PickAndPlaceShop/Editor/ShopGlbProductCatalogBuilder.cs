#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Editor
{
    public static class ShopGlbProductCatalogBuilder
    {
        private const string SourceFolder = "Assets/reduced";
        private const string ProductFolder =
            "Assets/PickAndPlaceShop/Resources/Products/CatCatalog";
        private const string VisualFolder = "Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated";
        private const string MeshFolder =
            "Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes";
        private const string MaterialFolder =
            "Assets/PickAndPlaceShop/Resources/ProductMaterials/Generated";
        private const string IconFolder = "Assets/PickAndPlaceShop/Resources/ProductIcons/Generated";
        private const string ConfigPath =
            "Assets/PickAndPlaceShop/Resources/Products/ShopProductVisualConfig.asset";
        private const string CsvPath = "Assets/PickAndPlaceShop/Docs/model_assignment.csv";
        private const int PreviewLayer = 31;

        [MenuItem("Tools/Pick And Place Shop/Rebuild GLB Product Catalog")]
        public static void Rebuild()
        {
            EnsureFolder(VisualFolder);
            EnsureFolder(MeshFolder);
            EnsureFolder(MaterialFolder);
            EnsureRuntimeMaterialTemplate();
            EnsureFolder(IconFolder);
            EnsureFolder(Path.GetDirectoryName(CsvPath).Replace('\\', '/'));
            ClearGeneratedIcons();
            ShopProductVisualConfig config = EnsureConfig();
            string[] glbPaths = AssetDatabase.FindAssets("", new[] { SourceFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (glbPaths.Length == 0) throw new InvalidOperationException("Assets/reduced GLB가 없습니다.");

            var wrappers = new List<GameObject>(glbPaths.Length);
            int unreadableMeshes = 0;
            for (int index = 0; index < glbPaths.Length; index++)
            {
                EditorUtility.DisplayProgressBar("GLB 상품 래퍼", glbPaths[index],
                    index / (float)glbPaths.Length * 0.35f);
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(glbPaths[index]);
                if (source == null) throw new InvalidOperationException("GLB 임포트 실패: " + glbPaths[index]);
                GameObject wrapper = BuildWrapper(source, index, config.TargetLongestSide,
                    out int unreadable);
                unreadableMeshes += unreadable;
                wrappers.Add(wrapper);
            }

            ShopProductDefinition[] products = AssetDatabase.FindAssets("t:ShopProductDefinition",
                    new[] { ProductFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopProductDefinition>)
                .Where(product => product != null)
                .OrderBy(product => product.ProductId)
                .ToArray();
            var csv = new StringBuilder("model_file,product_id,stable_item_id,display_name,tint_hex\n");
            for (int index = 0; index < products.Length; index++)
            {
                EditorUtility.DisplayProgressBar("상품 아이콘과 데이터", products[index].DisplayName,
                    0.35f + index / (float)Mathf.Max(1, products.Length) * 0.65f);
                int modelIndex = index % wrappers.Count;
                int reuse = index / wrappers.Count;
                Color tint = reuse == 0 ? Color.white : DeterministicTint(modelIndex, reuse);
                Sprite icon = RenderIcon(products[index].ProductId, wrappers[modelIndex], tint,
                    config.ThumbnailResolution);
                products[index].EditorConfigureVisual(wrappers[modelIndex], icon, tint);
                EditorUtility.SetDirty(products[index]);
                csv.Append(Csv(Path.GetFileName(glbPaths[modelIndex]))).Append(',')
                    .Append(products[index].ProductId).Append(',')
                    .Append(Csv(products[index].StableItemId)).Append(',')
                    .Append(Csv(products[index].DisplayName)).Append(',')
                    .Append(ColorUtility.ToHtmlStringRGBA(tint)).Append('\n');
            }
            File.WriteAllText(CsvPath, csv.ToString(), new UTF8Encoding(true));
            AssetDatabase.ImportAsset(CsvPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
            Debug.Log("[ProductVisuals] COMPLETE glb=" + glbPaths.Length +
                      " wrappers=" + wrappers.Count + " products=" + products.Length +
                      " placeholder=" + products.Count(p => p.PlaceholderArtwork) +
                      " unreadableMeshes=" + unreadableMeshes + " csv=" + CsvPath);
        }

        [MenuItem("Tools/Pick And Place Shop/Bake Build-Safe Product Materials")]
        public static void BakeExistingMaterials()
        {
            EnsureFolder(MaterialFolder);
            EnsureRuntimeMaterialTemplate();
            string[] paths = AssetDatabase.FindAssets("t:Prefab", new[] { VisualFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileName(path).StartsWith("ProductVisual_", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            int baked = 0;
            for (int index = 0; index < paths.Length; index++)
            {
                EditorUtility.DisplayProgressBar("빌드 안전 머티리얼 베이크", paths[index],
                    index / (float)Mathf.Max(1, paths.Length));
                GameObject root = PrefabUtility.LoadPrefabContents(paths[index]);
                try
                {
                    baked += BakeMaterials(root, index);
                    PrefabUtility.SaveAsPrefabAsset(root, paths[index]);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
            Debug.Log($"[ProductMaterials] COMPLETE prefabs={paths.Length} materials={baked} " +
                      "shader=Universal Render Pipeline/Lit");
        }

        [MenuItem("Tools/Pick And Place Shop/Regenerate Product Icons From Front")]
        public static void RegenerateIconsFromFront()
        {
            EnsureFolder(IconFolder);
            ShopProductVisualConfig config = EnsureConfig();
            ShopProductDefinition[] products = AssetDatabase.FindAssets("t:ShopProductDefinition",
                    new[] { ProductFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShopProductDefinition>)
                .Where(product => product != null && product.VisualPrefab != null)
                .OrderBy(product => product.ProductId)
                .ToArray();
            for (int index = 0; index < products.Length; index++)
            {
                EditorUtility.DisplayProgressBar("상품 정면 아이콘", products[index].DisplayName,
                    index / (float)Mathf.Max(1, products.Length));
                Sprite icon = RenderIcon(products[index].ProductId, products[index].VisualPrefab,
                    products[index].Tint, config.ThumbnailResolution);
                products[index].EditorConfigureVisual(products[index].VisualPrefab, icon, products[index].Tint);
                EditorUtility.SetDirty(products[index]);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
            Debug.Log("[ProductIcons] COMPLETE front-facing=" + products.Length);
        }

        private static GameObject BuildWrapper(GameObject source, int index, float targetSize,
            out int unreadableMeshes)
        {
            string prefabPath = VisualFolder + "/ProductVisual_" + index.ToString("D3") + ".prefab";
            GameObject root = new("ProductVisual_" + index.ToString("D3"));
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            foreach (Collider item in root.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(item);
            foreach (Rigidbody item in root.GetComponentsInChildren<Rigidbody>(true))
                UnityEngine.Object.DestroyImmediate(item);
            foreach (MonoBehaviour item in root.GetComponentsInChildren<MonoBehaviour>(true))
                UnityEngine.Object.DestroyImmediate(item);
            MakeMeshesRuntimeOnly(root, index);
            BakeMaterials(root, index);
            Bounds bounds = CalculateBounds(root);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = longest > 0.0001f ? targetSize / longest : 1f;
            model.transform.localScale *= scale;
            bounds = CalculateBounds(root);
            model.transform.position -= bounds.center;
            unreadableMeshes = root.GetComponentsInChildren<MeshFilter>(true)
                .Count(filter => filter.sharedMesh != null && filter.sharedMesh.isReadable);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        private static int BakeMaterials(GameObject root, int modelIndex)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit 셰이더를 찾지 못했습니다.");
            int materialIndex = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] sourceMaterials = renderer.sharedMaterials;
                Material[] bakedMaterials = new Material[sourceMaterials.Length];
                for (int slot = 0; slot < sourceMaterials.Length; slot++)
                {
                    Material source = sourceMaterials[slot];
                    string path = MaterialFolder + "/ProductMaterial_" + modelIndex.ToString("D3") + "_" +
                                  materialIndex.ToString("D2") + ".mat";
                    materialIndex++;
                    if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
                    Material baked = new(shader)
                    {
                        name = Path.GetFileNameWithoutExtension(path),
                        enableInstancing = true
                    };
                    CopyGltfMaterial(source, baked);
                    AssetDatabase.CreateAsset(baked, path);
                    bakedMaterials[slot] = baked;
                }
                renderer.sharedMaterials = bakedMaterials;
            }
            return materialIndex;
        }

        private static void CopyGltfMaterial(Material source, Material target)
        {
            Color baseColor = ReadColor(source, "baseColorFactor", "_BaseColor", "_Color", Color.white);
            Texture baseMap = ReadTexture(source, "baseColorTexture", "_BaseMap", "_MainTex");
            target.SetColor("_BaseColor", baseColor);
            if (baseMap != null) target.SetTexture("_BaseMap", baseMap);

            Texture normal = ReadTexture(source, "normalTexture", "_BumpMap");
            if (normal != null)
            {
                target.SetTexture("_BumpMap", normal);
                target.EnableKeyword("_NORMALMAP");
                target.SetFloat("_BumpScale", ReadFloat(source, "normalTexture_scale", "_BumpScale", 1f));
            }

            Texture occlusion = ReadTexture(source, "occlusionTexture", "_OcclusionMap");
            if (occlusion != null)
            {
                target.SetTexture("_OcclusionMap", occlusion);
                target.SetFloat("_OcclusionStrength",
                    ReadFloat(source, "occlusionTexture_strength", "_OcclusionStrength", 1f));
            }

            float metallic = ReadFloat(source, "metallicFactor", "_Metallic", 0f);
            float roughness = source != null && source.HasProperty("roughnessFactor")
                ? source.GetFloat("roughnessFactor")
                : 1f - ReadFloat(source, "_Smoothness", "_Smoothness", 0.35f);
            target.SetFloat("_Metallic", metallic);
            target.SetFloat("_Smoothness", Mathf.Clamp01(1f - roughness));
            Color emission = ReadColor(source, "emissiveFactor", "_EmissionColor", "_EmissionColor", Color.black);
            Texture emissionMap = ReadTexture(source, "emissiveTexture", "_EmissionMap");
            if (emission.maxColorComponent > 0.001f || emissionMap != null)
            {
                target.SetColor("_EmissionColor", emission);
                if (emissionMap != null) target.SetTexture("_EmissionMap", emissionMap);
                target.EnableKeyword("_EMISSION");
            }
            target.SetFloat("_Cull", ReadFloat(source, "_Cull", "_Cull", 2f));
        }

        private static Color ReadColor(Material source, string first, string second, string third, Color fallback)
        {
            if (source == null) return fallback;
            if (source.HasProperty(first)) return source.GetColor(first);
            if (source.HasProperty(second)) return source.GetColor(second);
            if (source.HasProperty(third)) return source.GetColor(third);
            return fallback;
        }

        private static Texture ReadTexture(Material source, params string[] names)
        {
            if (source == null) return null;
            foreach (string name in names)
                if (source.HasProperty(name) && source.GetTexture(name) != null) return source.GetTexture(name);
            return null;
        }

        private static float ReadFloat(Material source, string first, string second, float fallback)
        {
            if (source == null) return fallback;
            if (source.HasProperty(first)) return source.GetFloat(first);
            if (source.HasProperty(second)) return source.GetFloat(second);
            return fallback;
        }

        private static void EnsureRuntimeMaterialTemplate()
        {
            const string path = MaterialFolder + "/RuntimeLitBase.mat";
            Material template = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit 셰이더를 찾지 못했습니다.");
            if (template == null)
            {
                template = new Material(shader) { name = "RuntimeLitBase", enableInstancing = true };
                AssetDatabase.CreateAsset(template, path);
            }
            else template.shader = shader;
            template.SetColor("_BaseColor", Color.white);
            template.SetFloat("_Metallic", 0f);
            template.SetFloat("_Smoothness", 0.25f);
            template.SetColor("_EmissionColor", Color.black);
            template.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(template);
        }

        private static void MakeMeshesRuntimeOnly(GameObject root, int modelIndex)
        {
            int meshIndex = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                filter.sharedMesh = CreateRuntimeMesh(filter.sharedMesh, modelIndex, meshIndex++);
            }
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                renderer.sharedMesh = CreateRuntimeMesh(renderer.sharedMesh, modelIndex, meshIndex++);
            }
        }

        private static Mesh CreateRuntimeMesh(Mesh source, int modelIndex, int meshIndex)
        {
            string path = MeshFolder + "/ProductMesh_" + modelIndex.ToString("D3") + "_" +
                          meshIndex.ToString("D2") + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            Mesh copy = UnityEngine.Object.Instantiate(source);
            copy.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            copy.UploadMeshData(true);
            EditorUtility.SetDirty(copy);
            return copy;
        }

        private static Sprite RenderIcon(int productId, GameObject prefab, Color tint, int resolution)
        {
            GameObject stage = new("ProductIconStage");
            stage.transform.position = new Vector3(10000f, 10000f, 10000f);
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, stage.transform);
            SetLayer(model.transform, PreviewLayer);
            ShopProductVisuals.ApplyTint(model, tint);
            Bounds bounds = CalculateBounds(model);
            GameObject lightObject = new("Light", typeof(Light));
            lightObject.transform.SetParent(stage.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.cullingMask = 1 << PreviewLayer;
            GameObject cameraObject = new("Camera", typeof(Camera));
            cameraObject.transform.SetParent(stage.transform, false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(0.06f,
                Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.28f);
            camera.cullingMask = 1 << PreviewLayer;
            camera.transform.position = bounds.center + new Vector3(0.45f, 0.3f, 0.8f);
            camera.transform.LookAt(bounds.center);
            RenderTexture render = RenderTexture.GetTemporary(resolution, resolution, 24,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
            camera.targetTexture = render;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = render;
            Texture2D texture = new(resolution, resolution, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0);
            texture.Apply(false, false);
            string path = IconFolder + "/ProductIcon_" + productId.ToString("D4") + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(render);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(stage);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one * 0.1f);
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int index = 0; index < root.childCount; index++) SetLayer(root.GetChild(index), layer);
        }

        private static Color DeterministicTint(int modelIndex, int reuse)
        {
            float hue = Mathf.Repeat(modelIndex * 0.6180339f + reuse * 0.271828f, 1f);
            return Color.HSVToRGB(hue, 0.24f + reuse * 0.08f, 1f);
        }

        private static ShopProductVisualConfig EnsureConfig()
        {
            ShopProductVisualConfig config = AssetDatabase.LoadAssetAtPath<ShopProductVisualConfig>(ConfigPath);
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<ShopProductVisualConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        private static void ClearGeneratedIcons()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { IconFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    AssetDatabase.DeleteAsset(path);
            }
        }

        private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

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
