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
            camera.transform.position = bounds.center + new Vector3(0.45f, 0.3f, -0.8f);
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
