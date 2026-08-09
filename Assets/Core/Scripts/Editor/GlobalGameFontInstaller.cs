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
        private const string SourceFontPath = "Assets/Core/Resources/Fonts/GFCRedSpirit-Medium.otf";
        private const string TmpFontPath = "Assets/Core/Resources/Fonts/GFCRedSpirit-Medium SDF.asset";
        private const string SettingsPath = "Assets/Core/Resources/GlobalGameFontSettings.asset";
        private const string TmpSettingsPath = "Assets/Core/TextMesh Pro/Resources/TMP Settings.asset";

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
            SerializedObject serializedTmpFont = new SerializedObject(tmpFont);
            serializedTmpFont.FindProperty("m_SourceFontFile").objectReferenceValue = sourceFont;
            serializedTmpFont.FindProperty("m_SourceFontFileGUID").stringValue = AssetDatabase.AssetPathToGUID(SourceFontPath);
            serializedTmpFont.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
            serializedTmpFont.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tmpFont);

            GlobalGameFontSettings settings = AssetDatabase.LoadAssetAtPath<GlobalGameFontSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<GlobalGameFontSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            SerializedObject serializedSettings = new SerializedObject(settings);
            serializedSettings.FindProperty("legacyFont").objectReferenceValue = sourceFont;
            serializedSettings.FindProperty("legacyMediumFont").objectReferenceValue = sourceFont;
            serializedSettings.FindProperty("legacyBoldFont").objectReferenceValue = sourceFont;
            serializedSettings.FindProperty("textMeshProFont").objectReferenceValue = tmpFont;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            ConfigureTmpSettings(tmpFont);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GlobalGameFont] GFC Red Spirit 전역 폰트 설정 완료: " + SourceFontPath);
        }

        private static void ConfigureTmpSettings(TMP_FontAsset tmpFont)
        {
            TMP_Settings tmpSettings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (tmpSettings == null)
            {
                tmpSettings = ScriptableObject.CreateInstance<TMP_Settings>();
                tmpSettings.name = "TMP Settings";
                AssetDatabase.CreateAsset(tmpSettings, TmpSettingsPath);
            }

            SerializedObject serialized = new SerializedObject(tmpSettings);
            serialized.FindProperty("assetVersion").stringValue = "1.1.0";
            serialized.FindProperty("m_enableKerning").boolValue = true;
            serialized.FindProperty("m_enableParseEscapeCharacters").boolValue = true;
            serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = tmpFont;
            serialized.FindProperty("m_defaultFontSize").floatValue = 36f;
            serialized.FindProperty("m_defaultAutoSizeMinRatio").floatValue = 0.5f;
            serialized.FindProperty("m_defaultAutoSizeMaxRatio").floatValue = 2f;
            serialized.FindProperty("m_defaultTextMeshProTextContainerSize").vector2Value = new Vector2(20f, 5f);
            serialized.FindProperty("m_defaultTextMeshProUITextContainerSize").vector2Value = new Vector2(200f, 50f);
            serialized.FindProperty("m_GetFontFeaturesAtRuntime").boolValue = true;
            serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
            serialized.FindProperty("m_warningsDisabled").boolValue = false;
            serialized.FindProperty("m_UseModernHangulLineBreakingRules").boolValue = true;
            SerializedProperty fallbacks = serialized.FindProperty("m_fallbackFontAssets");
            if (fallbacks != null) fallbacks.arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tmpSettings);
        }
    }
}
