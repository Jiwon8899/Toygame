using System.IO;
using Blocks.Gameplay.Core;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Blocks.Gameplay.Core.EditorTools
{
    public static class GlobalGameFontInstaller
    {
        private const string SourceFontPath = "Assets/Shooter/GFCRedSpirit-Medium.otf";
        private const string TmpFontPath = "Assets/Shooter/GFCRedSpirit-Medium SDF.asset";
        private const string SettingsPath = "Assets/Core/Resources/GlobalGameFontSettings.asset";

        [MenuItem("Tools/Game/Apply GFC Red Spirit Font Globally")]
        public static void Install()
        {
            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
            {
                throw new FileNotFoundException("전역 폰트 원본을 찾을 수 없습니다.", SourceFontPath);
            }

            TMP_FontAsset tmpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpFontPath);
            if (tmpFont == null || tmpFont.material == null || tmpFont.atlasTexture == null)
            {
                if (tmpFont != null)
                {
                    AssetDatabase.DeleteAsset(TmpFontPath);
                }

                tmpFont = TMP_FontAsset.CreateFontAsset(
                    sourceFont,
                    90,
                    9,
                    GlyphRenderMode.SDFAA,
                    1024,
                    1024,
                    AtlasPopulationMode.Dynamic,
                    true);
                tmpFont.name = "GFCRedSpirit-Medium SDF";
                AssetDatabase.CreateAsset(tmpFont, TmpFontPath);

                Material material = tmpFont.material;
                material.name = "GFCRedSpirit-Medium SDF Material";
                AssetDatabase.AddObjectToAsset(material, tmpFont);

                Texture2D[] atlasTextures = tmpFont.atlasTextures;
                for (int index = 0; index < atlasTextures.Length; index++)
                {
                    Texture2D atlasTexture = atlasTextures[index];
                    atlasTexture.name = "GFCRedSpirit-Medium SDF Atlas " + index;
                    AssetDatabase.AddObjectToAsset(atlasTexture, tmpFont);
                }
            }

            tmpFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            tmpFont.isMultiAtlasTexturesEnabled = true;
            EditorUtility.SetDirty(tmpFont);

            GlobalGameFontSettings settings = AssetDatabase.LoadAssetAtPath<GlobalGameFontSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<GlobalGameFontSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            SerializedObject serializedSettings = new SerializedObject(settings);
            serializedSettings.FindProperty("legacyFont").objectReferenceValue = sourceFont;
            serializedSettings.FindProperty("textMeshProFont").objectReferenceValue = tmpFont;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GlobalGameFont] GFC Red Spirit 전역 폰트 설정 완료: " + SourceFontPath);
        }
    }
}
