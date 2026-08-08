using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace PickAndPlaceShop.Tests
{
    public sealed class ShopAudioIntegrationTests
    {
        [Test]
        public void TitlePresentation_UsesTheNewImportedSprite()
        {
            ShopTitlePresentationConfig title = ShopTitlePresentationConfig.Load();
            Assert.NotNull(title);
            Assert.NotNull(title.Logo);
            Assert.AreEqual("게임로고2", title.Logo.name);
            Assert.AreEqual("Assets/PickAndPlaceShop/Art/UI/게임로고2.png",
                AssetDatabase.GetAssetPath(title.Logo));
            Assert.That(title.EntranceSeconds, Is.InRange(0.5f, 0.8f));
            Assert.That(title.EntranceStartScale, Is.EqualTo(0.85f).Within(0.001f));
            Assert.That(title.IdleAmplitudePixels, Is.InRange(3f, 5f));
            Assert.That(title.IdlePeriodSeconds, Is.InRange(2f, 3f));
        }

        [Test]
        public void AudioConfig_ReferencesAllMovedClipsAndTuning()
        {
            ShopAudioConfig config = ShopAudioConfig.Load();
            Assert.NotNull(config);
            Assert.NotNull(config.BackgroundMusic);
            Assert.NotNull(config.TitleButtonClick);
            Assert.NotNull(config.MoneyIncrease);
            Assert.AreEqual("Assets/PickAndPlaceShop/Audio/배경음.wav",
                AssetDatabase.GetAssetPath(config.BackgroundMusic));
            Assert.AreEqual("Assets/PickAndPlaceShop/Audio/메인화면 클릭음.wav",
                AssetDatabase.GetAssetPath(config.TitleButtonClick));
            Assert.AreEqual("Assets/PickAndPlaceShop/Audio/돈 올라가는소리.wav",
                AssetDatabase.GetAssetPath(config.MoneyIncrease));
            Assert.That(config.BackgroundMusic.length, Is.GreaterThan(60f));
            Assert.That(config.MoneyMinimumIntervalSeconds, Is.GreaterThanOrEqualTo(0.1f));
        }

        [Test]
        public void AudioManager_UsesSharedButtonsAndCentralFundsChangeEvent()
        {
            const string managerPath = "Assets/PickAndPlaceShop/Scripts/ShopAudioManager.cs";
            string source = File.ReadAllText(managerPath);
            StringAssert.Contains("GetComponentsInChildren<Button>(true)", source);
            StringAssert.Contains("button.onClick.AddListener(PlayTitleButtonClick)", source);
            StringAssert.Contains("Coins.OnValueChanged += OnFundsChanged", source);
            StringAssert.Contains("currentValue <= previousValue", source);
            StringAssert.Contains("MoneyMinimumIntervalSeconds", source);
            Assert.AreEqual(1, Count(source, "PlayTitleButtonClick);"),
                "Title click audio should be attached through one shared path.");
        }

        [Test]
        public void WebGlAudioResumePlugin_IsIncluded()
        {
            const string pluginPath = "Assets/Plugins/WebGL/ShopWebAudio.jslib";
            Assert.IsTrue(File.Exists(pluginPath));
            string source = File.ReadAllText(pluginPath);
            StringAssert.Contains("ShopResumeWebAudio", source);
            StringAssert.Contains("audioContext.resume", source);
        }

        private static int Count(string source, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(value, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }
    }
}
