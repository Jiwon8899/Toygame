using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core
{
    [DefaultExecutionOrder(-10000)]
    public sealed class GlobalGameFontApplier : MonoBehaviour
    {
        private const string SettingsResourceName = "GlobalGameFontSettings";
        private const float RefreshInterval = 0.25f;

        private static GlobalGameFontApplier instance;
        private static GlobalGameFontSettings settings;
        private float nextRefreshTime;

        public static Font LegacyFont => LoadSettings() != null ? settings.LegacyFont : null;
        public static TMP_FontAsset TextMeshProFont => LoadSettings() != null ? settings.TextMeshProFont : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            settings = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            GlobalGameFontSettings loadedSettings = LoadSettings();
            if (loadedSettings == null || !loadedSettings.IsConfigured)
            {
                Debug.LogError("[GlobalGameFont] Resources/GlobalGameFontSettings가 없거나 폰트가 지정되지 않았습니다.");
                return;
            }

            if (instance != null) return;
            GameObject host = new GameObject("[Global] Game Font Applier");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<GlobalGameFontApplier>();
        }

        private static GlobalGameFontSettings LoadSettings()
        {
            if (settings == null)
            {
                settings = Resources.Load<GlobalGameFontSettings>(SettingsResourceName);
            }

            return settings;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyAllLoadedText();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime) return;
            nextRefreshTime = Time.unscaledTime + RefreshInterval;
            ApplyAllLoadedText();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyAllLoadedText();
        }

        public static void ApplyNow()
        {
            ApplyAllLoadedText();
        }

        public static void ApplyTo(GameObject root)
        {
            GlobalGameFontSettings loadedSettings = LoadSettings();
            if (loadedSettings == null || root == null) return;

            ApplyLegacyText(root.GetComponentsInChildren<Text>(true), loadedSettings);
            ApplyTextMeshes(root.GetComponentsInChildren<TextMesh>(true), loadedSettings.LegacyFont);
            ApplyTextMeshPro(root.GetComponentsInChildren<TMP_Text>(true), loadedSettings.TextMeshProFont);
            ApplyUiDocuments(root.GetComponentsInChildren<UIDocument>(true), loadedSettings.LegacyFont);
        }

        private static void ApplyAllLoadedText()
        {
            GlobalGameFontSettings loadedSettings = LoadSettings();
            if (loadedSettings == null || !loadedSettings.IsConfigured) return;

            ApplyLegacyText(Resources.FindObjectsOfTypeAll<Text>(), loadedSettings);
            ApplyTextMeshes(Resources.FindObjectsOfTypeAll<TextMesh>(), loadedSettings.LegacyFont);
            ApplyTextMeshPro(Resources.FindObjectsOfTypeAll<TMP_Text>(), loadedSettings.TextMeshProFont);
            ApplyUiDocuments(Resources.FindObjectsOfTypeAll<UIDocument>(), loadedSettings.LegacyFont);
        }

        private static bool IsLoaded(Component component)
        {
            return component != null && component.gameObject.scene.IsValid();
        }

        private static void ApplyLegacyText(Text[] textComponents, GlobalGameFontSettings loadedSettings)
        {
            Font font = loadedSettings != null ? loadedSettings.LegacyFont : null;
            if (font == null) return;
            foreach (Text text in textComponents)
            {
                if (!IsLoaded(text) || loadedSettings.IsLegacyFamilyFont(text.font)) continue;
                text.font = font;
                text.SetAllDirty();
            }
        }

        private static void ApplyTextMeshes(TextMesh[] textMeshes, Font font)
        {
            if (font == null) return;
            foreach (TextMesh textMesh in textMeshes)
            {
                if (!IsLoaded(textMesh)) continue;
                if (textMesh.font != font) textMesh.font = font;

                Renderer renderer = textMesh.GetComponent<Renderer>();
                if (renderer != null && renderer.sharedMaterial != font.material)
                {
                    renderer.sharedMaterial = font.material;
                }
            }
        }

        private static void ApplyTextMeshPro(TMP_Text[] textComponents, TMP_FontAsset font)
        {
            if (font == null) return;
            foreach (TMP_Text text in textComponents)
            {
                if (!IsLoaded(text) || text.font == font) continue;
                text.font = font;
                text.SetAllDirty();
            }
        }

        private static void ApplyUiDocuments(UIDocument[] documents, Font font)
        {
            if (font == null) return;
            StyleFontDefinition fontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            foreach (UIDocument document in documents)
            {
                if (!IsLoaded(document) || document.rootVisualElement == null) continue;
                document.rootVisualElement.style.unityFontDefinition = fontDefinition;
            }
        }
    }
}
