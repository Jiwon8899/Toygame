using UnityEngine;
using UnityEngine.Rendering;

namespace PickAndPlaceShop
{
    [DefaultExecutionOrder(360)]
    public sealed class ShopDayLightingController : MonoBehaviour
    {
        private static ShopDayLightingController instance;
        private ShopOperationsConfig config;
        private Light sun;
        private ShopPhase observedPhase = (ShopPhase)(-1);
        private Color targetDirectional;
        private Color targetAmbient;
        private float targetIntensity;
        private Quaternion targetRotation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[World] Day Lighting");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopDayLightingController>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            config = ShopOperationsConfig.Load();
        }

        private void Update()
        {
            ShopNetworkGame game = ShopNetworkGame.Instance;
            if (game == null || config == null) return;
            if (sun == null) sun = FindSun();
            if (game.Phase.Value != observedPhase)
            {
                observedPhase = game.Phase.Value;
                SelectPreset(observedPhase);
            }

            float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4.6f /
                                          config.LightingTransitionSeconds);
            if (sun != null)
            {
                sun.color = Color.Lerp(sun.color, targetDirectional, blend);
                sun.intensity = Mathf.Lerp(sun.intensity, targetIntensity, blend);
                sun.transform.rotation = Quaternion.Slerp(sun.transform.rotation, targetRotation, blend);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(RenderSettings.ambientSkyColor,
                targetAmbient, blend);
            RenderSettings.ambientEquatorColor = Color.Lerp(RenderSettings.ambientEquatorColor,
                targetAmbient * 0.72f, blend);
            RenderSettings.ambientGroundColor = Color.Lerp(RenderSettings.ambientGroundColor,
                targetAmbient * 0.42f, blend);
        }

        private void SelectPreset(ShopPhase phase)
        {
            if (phase == ShopPhase.Setup)
            {
                targetRotation = Quaternion.Euler(config.MorningDirectionalEuler);
                targetDirectional = config.MorningDirectionalColor;
                targetIntensity = config.MorningDirectionalIntensity;
                targetAmbient = config.MorningAmbientColor;
            }
            else if (phase == ShopPhase.Open)
            {
                targetRotation = Quaternion.Euler(config.DayDirectionalEuler);
                targetDirectional = config.DayDirectionalColor;
                targetIntensity = config.DayDirectionalIntensity;
                targetAmbient = config.DayAmbientColor;
            }
            else
            {
                targetRotation = Quaternion.Euler(config.NightDirectionalEuler);
                targetDirectional = config.NightDirectionalColor;
                targetIntensity = config.NightDirectionalIntensity;
                targetAmbient = config.NightAmbientColor;
            }
        }

        private static Light FindSun()
        {
            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional)
                return RenderSettings.sun;
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            Light best = null;
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null || lights[i].type != LightType.Directional) continue;
                if (best == null || lights[i].intensity > best.intensity) best = lights[i];
            }
            if (best != null) RenderSettings.sun = best;
            return best;
        }
    }
}
