using System;
using System.IO;
using Blocks.Gameplay.Core;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace PickAndPlaceShop.EditorTools
{
    public static class ShopUiThemeInstaller
    {
        private const string ThemePath = "Assets/PickAndPlaceShop/Resources/ShopUiTheme.asset";
        private const string UiRoot = "Assets/PickAndPlaceShop/Art/UI/";
        private const string GfcFontPath = "Assets/Core/Resources/Fonts/GFCRedSpirit-Medium.otf";
        private const string ExistingTmpFontPath = "Assets/Core/Resources/Fonts/GFCRedSpirit-Medium SDF.asset";
        private const string GlobalFontSettingsPath = "Assets/Core/Resources/GlobalGameFontSettings.asset";

        [MenuItem("Tools/Pick And Place Shop/UI/Install Warm UI Theme")]
        public static void Install()
        {
            ConfigureSprite(UiRoot + "Sprites/radius_12.png", new Vector4(13f, 13f, 13f, 13f));
            ConfigureSprite(UiRoot + "Sprites/radius_20.png", new Vector4(21f, 21f, 21f, 21f));
            ConfigureSprite(UiRoot + "Sprites/radius_28.png", new Vector4(29f, 29f, 29f, 29f));
            ConfigureSprite(UiRoot + "Sprites/pill_capsule.png", new Vector4(32f, 0f, 32f, 0f));
            ConfigureSprite(UiRoot + "Sprites/foil_gradient.png", Vector4.zero);
            ConfigureSprite(UiRoot + "Sprites/foil_gradient_shine.png", Vector4.zero);

            string[] icons =
            {
                "paw", "moon", "coin", "star", "store", "package", "gift", "target",
                "capsule", "people", "shoe", "ticket", "idea", "expand"
            };
            foreach (string icon in icons) ConfigureSprite(UiRoot + "Icons/" + icon + ".png", Vector4.zero);

            Font gfc = Require<Font>(GfcFontPath);
            TMP_FontAsset tmpFont = Require<TMP_FontAsset>(ExistingTmpFontPath);

            Directory.CreateDirectory("Assets/PickAndPlaceShop/Resources");
            ShopUiTheme theme = AssetDatabase.LoadAssetAtPath<ShopUiTheme>(ThemePath);
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<ShopUiTheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }

            SerializedObject serializedTheme = new(theme);
            Set(serializedTheme, "regularFont", gfc);
            Set(serializedTheme, "mediumFont", gfc);
            Set(serializedTheme, "boldFont", gfc);
            Set(serializedTheme, "radius12", Require<Sprite>(UiRoot + "Sprites/radius_12.png"));
            Set(serializedTheme, "radius20", Require<Sprite>(UiRoot + "Sprites/radius_20.png"));
            Set(serializedTheme, "radius28", Require<Sprite>(UiRoot + "Sprites/radius_28.png"));
            Set(serializedTheme, "pillCapsule", Require<Sprite>(UiRoot + "Sprites/pill_capsule.png"));
            Set(serializedTheme, "foilGradient", Require<Sprite>(UiRoot + "Sprites/foil_gradient.png"));
            Set(serializedTheme, "foilGradientShine", Require<Sprite>(UiRoot + "Sprites/foil_gradient_shine.png"));
            foreach (string icon in icons) Set(serializedTheme, icon, Require<Sprite>(UiRoot + "Icons/" + icon + ".png"));
            serializedTheme.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(theme);

            GlobalGameFontSettings global = Require<GlobalGameFontSettings>(GlobalFontSettingsPath);
            SerializedObject serializedGlobal = new(global);
            Set(serializedGlobal, "legacyFont", gfc);
            Set(serializedGlobal, "legacyMediumFont", gfc);
            Set(serializedGlobal, "legacyBoldFont", gfc);
            Set(serializedGlobal, "textMeshProFont", tmpFont);
            serializedGlobal.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(global);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ShopUI] Warm theme installed with the project GFC Red Spirit font, 14 icons, and 6 UI sprites.");
        }

        private static void ConfigureSprite(string path, Vector4 border)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("UI texture not found.", path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        private static T Require<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException(typeof(T).Name + " asset not found.", path);
            return asset;
        }

        private static void Set(SerializedObject target, string property, UnityEngine.Object value)
        {
            SerializedProperty serializedProperty = target.FindProperty(property);
            if (serializedProperty == null) throw new MissingFieldException(target.targetObject.name, property);
            serializedProperty.objectReferenceValue = value;
        }
    }
}
