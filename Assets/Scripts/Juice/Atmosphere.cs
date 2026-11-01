using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TagGame.Gameplay;
using TagGame.Maps;

namespace TagGame.Juice
{
    /// <summary>
    /// Makes the world look more real: distance fog per map, soft sun shadows, and a film-style colour grade
    /// (tone mapping, a little bloom, contrast and a soft vignette) through URP post-processing.
    /// </summary>
    public class Atmosphere : MonoBehaviour
    {
        private static readonly Color FogColor = new Color(0.74f, 0.82f, 0.9f);
        private static readonly Vector2[] FogRange = { new Vector2(45f, 140f), new Vector2(70f, 230f), new Vector2(90f, 320f) };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<Atmosphere>() != null) return;
            if (FindFirstObjectByType<PlayerController>() == null) return;
            new GameObject("Atmosphere").AddComponent<Atmosphere>();
        }

        private void Start()
        {
            // sun: soft shadows that reach far enough for the big maps
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 0.7f;
            }
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) urp.shadowDistance = 90f;

            // colour grade
            var cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) data.renderPostProcessing = true;
            }
            var vol = gameObject.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tm = profile.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var ca = profile.Add<ColorAdjustments>(true); ca.postExposure.Override(0.35f); ca.contrast.Override(14f); ca.saturation.Override(10f);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.45f); bloom.threshold.Override(1.05f); bloom.scatter.Override(0.6f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.24f); vig.smoothness.Override(0.45f);
            vol.profile = profile;

            if (MapManager.I != null) { MapManager.I.OnMapChanged += Apply; Apply(MapManager.I.Current); }
            else Apply(0);
        }

        private void OnDestroy()
        {
            if (MapManager.I != null) MapManager.I.OnMapChanged -= Apply;
        }

        private void Apply(int map)
        {
            int m = Mathf.Clamp(map, 0, FogRange.Length - 1);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = FogRange[m].x;
            RenderSettings.fogEndDistance = FogRange[m].y;
        }
    }
}
