using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopGlobalFontBuildTests
    {
        private const string SourcePath = "Assets/Core/Resources/Fonts/GFCRedSpirit-Medium.otf";
        private const string TmpFontPath = "Assets/Core/Resources/Fonts/GFCRedSpirit-Medium SDF.asset";
        private const string TmpSettingsPath = "Assets/Core/TextMesh Pro/Resources/TMP Settings.asset";

        [Test]
        public void GfcFontAssets_AreBuildIncludedAndDynamic()
        {
            Font source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
            Object tmp = AssetDatabase.LoadMainAssetAtPath(TmpFontPath);
            Assert.NotNull(source);
            Assert.NotNull(tmp);
            StringAssert.Contains("/Resources/", SourcePath);
            StringAssert.Contains("/Resources/", TmpFontPath);

            SerializedObject serialized = new SerializedObject(tmp);
            Assert.AreSame(source, serialized.FindProperty("m_SourceFontFile").objectReferenceValue);
            Assert.AreEqual(1, serialized.FindProperty("m_AtlasPopulationMode").enumValueIndex,
                "The Korean TMP font must stay Dynamic.");
            Assert.IsTrue(serialized.FindProperty("m_IsMultiAtlasTexturesEnabled").boolValue);
            Assert.IsFalse(serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue);
            Assert.GreaterOrEqual(serialized.FindProperty("m_AtlasWidth").intValue, 1024);
            Assert.GreaterOrEqual(serialized.FindProperty("m_AtlasHeight").intValue, 1024);
        }

        [Test]
        public void TmpSettings_UseGfcAsDefaultWithoutFallbackSubstitution()
        {
            Object tmp = AssetDatabase.LoadMainAssetAtPath(TmpFontPath);
            Object settings = AssetDatabase.LoadMainAssetAtPath(TmpSettingsPath);
            Assert.NotNull(settings, "TMP Settings must be in a Resources folder for WebGL.");

            SerializedObject serialized = new SerializedObject(settings);
            Assert.AreSame(tmp, serialized.FindProperty("m_defaultFontAsset").objectReferenceValue);
            Assert.AreEqual(0, serialized.FindProperty("m_fallbackFontAssets").arraySize);
            Assert.IsFalse(serialized.FindProperty("m_warningsDisabled").boolValue);
            Assert.IsFalse(serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue);
        }

        [Test]
        public void LegacyAndDynamicUi_UseOneGfcFontPath()
        {
            Font source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
            Object tmp = AssetDatabase.LoadMainAssetAtPath(TmpFontPath);
            Assert.NotNull(source);

            Object global = AssetDatabase.LoadMainAssetAtPath(
                "Assets/Core/Resources/GlobalGameFontSettings.asset");
            SerializedObject globalSerialized = new SerializedObject(global);
            Assert.AreSame(source, globalSerialized.FindProperty("legacyFont").objectReferenceValue);
            Assert.AreSame(source, globalSerialized.FindProperty("legacyMediumFont").objectReferenceValue);
            Assert.AreSame(source, globalSerialized.FindProperty("legacyBoldFont").objectReferenceValue);
            Assert.AreSame(tmp, globalSerialized.FindProperty("textMeshProFont").objectReferenceValue);

            Object theme = AssetDatabase.LoadMainAssetAtPath(
                "Assets/PickAndPlaceShop/Resources/ShopUiTheme.asset");
            SerializedObject themeSerialized = new SerializedObject(theme);
            Assert.AreSame(source, themeSerialized.FindProperty("regularFont").objectReferenceValue);
            Assert.AreSame(source, themeSerialized.FindProperty("mediumFont").objectReferenceValue);
            Assert.AreSame(source, themeSerialized.FindProperty("boldFont").objectReferenceValue);
        }

        [Test]
        public void MovedFontGuids_RemainStable()
        {
            Assert.AreEqual("2ef0b417c3f6029429bc266024c9543d", AssetDatabase.AssetPathToGUID(SourcePath));
            Assert.AreEqual("768e8c35165f977449ad074302184a0f", AssetDatabase.AssetPathToGUID(TmpFontPath));
        }
    }
}
