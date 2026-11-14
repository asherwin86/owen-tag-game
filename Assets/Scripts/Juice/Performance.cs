using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TagGame.Gameplay;

namespace TagGame.Juice
{
    /// <summary>
    /// Lets the game use the whole computer: no frame cap or v-sync wait, 4x anti-aliasing, and a render resolution
    /// that climbs up to 150% while the frame rate stays high (sharper picture, graphics card working harder) and drops
    /// back down as soon as the game starts to lag, so it stays smooth.
    /// </summary>
    public class Performance : MonoBehaviour
    {
        public static float RenderScale { get; private set; } = 1f;
        public static float Fps { get; private set; } = 60f;

        private UniversalRenderPipelineAsset _urp;
        private float _t, _frames, _adjustAt;
        private const float MinScale = 0.6f, MaxScale = 1.5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<Performance>() != null) return;
            if (FindFirstObjectByType<PlayerController>() == null) return;
            new GameObject("Performance").AddComponent<Performance>();
        }

        private void Start()
        {
            QualitySettings.vSyncCount = 0;
            // the browser decides the frame rate on the web (-1 = as fast as the screen); on a PC, go as fast as possible
            Application.targetFrameRate = Application.platform == RuntimePlatform.WebGLPlayer ? -1 : 300;
            _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (_urp != null)
            {
                _urp.msaaSampleCount = 4;
                _urp.renderScale = RenderScale = 1f;
            }
            _adjustAt = Time.unscaledTime + 4f;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime; _frames++;
            if (_t >= 1f) { Fps = _frames / _t; _t = 0f; _frames = 0f; }
            if (_urp == null || Time.unscaledTime < _adjustAt) return;
            _adjustAt = Time.unscaledTime + 2f;
            float s = RenderScale;
            if (Fps > 58f) s = Mathf.Min(MaxScale, s + 0.1f);
            else if (Fps < 40f) s = Mathf.Max(MinScale, s - 0.15f);
            if (Fps < 28f && _urp.msaaSampleCount > 1) _urp.msaaSampleCount = 1;
            else if (Fps > 58f && _urp.msaaSampleCount < 4 && s >= 1f) _urp.msaaSampleCount = 4;
            if (!Mathf.Approximately(s, RenderScale)) { RenderScale = s; _urp.renderScale = s; }
        }
    }
}
