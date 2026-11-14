using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TagGame.Gameplay;
using TagGame.Maps;
using TagGame.Mega;

namespace TagGame.Juice
{
    /// <summary>
    /// The World's look: a big warm sun with a glowing disc and lens flare, a deep blue sky, long soft shadows on
    /// everything, a stronger film grade, distance haze, and a sea of real moving water (with lakes) that reflects the
    /// sky and glints in the sun. Only switched on while the World (Mega Mode) is loaded.
    /// </summary>
    public class Realism : MonoBehaviour
    {
        public static Realism I { get; private set; }
        public static bool Flying;   // set by the planes: longer view and thinner haze

        private Light _sun;
        private Quaternion _sunRot; private float _sunInt; private Color _sunCol;
        private Material _skyMat; private float _skySize, _skyConv, _skyThick, _skyExp; private Color _skyTint;
        private Volume _vol;
        private Transform _sea, _farSea;
        private Material _water;
        private bool _on;
        private float _savedShadowDist = 90f; private int _savedCascades = 1;
        private LensFlareComponentSRP _flare;
        private AmbientMode _ambMode; private Color _ambSky, _ambEq, _ambGround;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<Realism>() != null) return;
            if (FindFirstObjectByType<PlayerController>() == null) return;
            new GameObject("Realism").AddComponent<Realism>();
        }

        private void Awake() { I = this; }

        private void Start()
        {
            if (MapManager.I != null) MapManager.I.OnMapChanged += OnMap;
        }

        private void OnDestroy() { if (MapManager.I != null) MapManager.I.OnMapChanged -= OnMap; }

        private void OnMap(int map)
        {
            if (map == MapManager.MegaId) TurnOn(); else TurnOff();
        }

        /// <summary>A material for water (the sea and lakes). Falls back to a shiny lit material if the shader can't run.</summary>
        public Material Water()
        {
            if (_water != null) return _water;
            var sh = Resources.Load<Shader>("TagWater");
            if (sh == null) sh = Shader.Find("TagGame/Water");
            if (sh != null && sh.isSupported) _water = new Material(sh);
            else
            {
                var pc = FindFirstObjectByType<PlayerController>();
                var r = pc != null ? pc.GetComponent<Renderer>() : null;
                _water = r != null ? new Material(r.sharedMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _water.mainTexture = null;
                _water.color = new Color(0.08f, 0.32f, 0.45f);
                if (_water.HasProperty("_Smoothness")) _water.SetFloat("_Smoothness", 0.95f);
                if (_water.HasProperty("_Metallic")) _water.SetFloat("_Metallic", 0.1f);
            }
            return _water;
        }

        private void TurnOn()
        {
            if (_on) return;
            _on = true;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { _sun = l; break; }
            if (_sun != null)
            {
                _sunRot = _sun.transform.rotation; _sunInt = _sun.intensity; _sunCol = _sun.color;
                _sun.transform.rotation = Quaternion.Euler(36f, 215f, 0f);     // afternoon sun: long shadows
                _sun.intensity = 1.55f;
                _sun.color = new Color(1f, 0.93f, 0.8f);
                _sun.shadows = LightShadows.Soft;
                _sun.shadowStrength = 0.72f;
                RenderSettings.sun = _sun;
                try
                {
                    _flare = _sun.GetComponent<LensFlareComponentSRP>();
                    if (_flare == null) _flare = _sun.gameObject.AddComponent<LensFlareComponentSRP>();
                    _flare.lensFlareData = MakeFlare();
                    _flare.intensity = 0.8f;
                    _flare.scale = 1f;
                    _flare.useOcclusion = true;
                    _flare.occlusionRadius = 0.6f;
                    _flare.enabled = true;
                }
                catch (System.Exception e) { Debug.Log("[Realism] lens flare off: " + e.Message); }
            }
            _skyMat = RenderSettings.skybox;
            if (_skyMat != null && _skyMat.HasProperty("_SunSize"))
            {
                _skySize = _skyMat.GetFloat("_SunSize");
                _skyConv = _skyMat.HasProperty("_SunSizeConvergence") ? _skyMat.GetFloat("_SunSizeConvergence") : 5f;
                _skyThick = _skyMat.HasProperty("_AtmosphereThickness") ? _skyMat.GetFloat("_AtmosphereThickness") : 1f;
                _skyExp = _skyMat.HasProperty("_Exposure") ? _skyMat.GetFloat("_Exposure") : 1.3f;
                _skyTint = _skyMat.HasProperty("_SkyTint") ? _skyMat.GetColor("_SkyTint") : Color.gray;
                _skyMat.SetFloat("_SunSize", 0.06f);
                if (_skyMat.HasProperty("_SunSizeConvergence")) _skyMat.SetFloat("_SunSizeConvergence", 3f);
                if (_skyMat.HasProperty("_AtmosphereThickness")) _skyMat.SetFloat("_AtmosphereThickness", 1.15f);
                if (_skyMat.HasProperty("_Exposure")) _skyMat.SetFloat("_Exposure", 1.35f);
                if (_skyMat.HasProperty("_SkyTint")) _skyMat.SetColor("_SkyTint", new Color(0.45f, 0.55f, 0.75f));
            }
            // bright, warm fill light so shadows stay readable instead of going dark blue
            _ambMode = RenderSettings.ambientMode; _ambSky = RenderSettings.ambientSkyColor; _ambEq = RenderSettings.ambientEquatorColor; _ambGround = RenderSettings.ambientGroundColor;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.74f, 0.88f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.6f, 0.56f);
            RenderSettings.ambientGroundColor = new Color(0.4f, 0.37f, 0.33f);
            DynamicGI.UpdateEnvironment();

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                _savedShadowDist = urp.shadowDistance; _savedCascades = urp.shadowCascadeCount;
                urp.shadowDistance = 260f;
                urp.shadowCascadeCount = 4;
            }

            // stronger, warmer grade on top of the normal one
            _vol = gameObject.AddComponent<Volume>();
            _vol.isGlobal = true; _vol.priority = 20f;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = p.Add<Bloom>(true); bloom.intensity.Override(0.9f); bloom.threshold.Override(0.95f); bloom.scatter.Override(0.72f); bloom.tint.Override(new Color(1f, 0.92f, 0.82f));
            var ca = p.Add<ColorAdjustments>(true); ca.postExposure.Override(0.2f); ca.contrast.Override(20f); ca.saturation.Override(16f);
            var wb = p.Add<WhiteBalance>(true); wb.temperature.Override(8f);
            var vig = p.Add<Vignette>(true); vig.intensity.Override(0.26f); vig.smoothness.Override(0.5f);
            var lgg = p.Add<LiftGammaGain>(true); lgg.lift.Override(new Vector4(0.98f, 0.99f, 1.04f, 0f)); lgg.gain.Override(new Vector4(1.03f, 1f, 0.96f, 0f));
            _vol.profile = p;

            // the sea: a fine moving grid that follows the camera, plus a big flat sheet out to the horizon
            var water = Water();
            _sea = new GameObject("Sea").transform;
            var mf = _sea.gameObject.AddComponent<MeshFilter>(); mf.sharedMesh = Grid(160, 3200f);
            var mr = _sea.gameObject.AddComponent<MeshRenderer>(); mr.sharedMaterial = water;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true;
            _farSea = new GameObject("FarSea").transform;
            var mf2 = _farSea.gameObject.AddComponent<MeshFilter>(); mf2.sharedMesh = Grid(8, 80000f);
            var mr2 = _farSea.gameObject.AddComponent<MeshRenderer>(); mr2.sharedMaterial = water;
            mr2.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static LensFlareDataSRP MakeFlare()
        {
            var d = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            LensFlareDataElementSRP El(SRPLensFlareType type, float pos, float size, Color tint, float intensity)
            {
                var e = new LensFlareDataElementSRP();
                e.flareType = type;
                e.position = pos;
                e.sizeXY = Vector2.one;
                e.uniformScale = size;
                e.tint = tint;
                e.localIntensity = intensity;
                return e;
            }
            d.elements = new[]
            {
                El(SRPLensFlareType.Circle, 0f, 9f, new Color(1f, 0.85f, 0.6f, 0.5f), 0.7f),
                El(SRPLensFlareType.Circle, 0f, 2.2f, new Color(1f, 0.95f, 0.85f, 1f), 1f),
                El(SRPLensFlareType.Ring, 0.45f, 1.6f, new Color(0.6f, 0.8f, 1f, 0.3f), 0.25f),
                El(SRPLensFlareType.Polygon, 0.8f, 0.6f, new Color(0.7f, 1f, 0.8f, 0.3f), 0.3f),
                El(SRPLensFlareType.Circle, 1.3f, 0.9f, new Color(0.9f, 0.6f, 1f, 0.3f), 0.25f),
            };
            return d;
        }

        private static Mesh Grid(int n, float size)
        {
            var m = new Mesh();
            if ((n + 1) * (n + 1) > 65000) m.indexFormat = IndexFormat.UInt32;
            var v = new Vector3[(n + 1) * (n + 1)];
            for (int z = 0; z <= n; z++) for (int x = 0; x <= n; x++) v[z * (n + 1) + x] = new Vector3((x / (float)n - 0.5f) * size, 0f, (z / (float)n - 0.5f) * size);
            var t = new int[n * n * 6]; int k = 0;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int a = z * (n + 1) + x, b = a + 1, c = a + n + 1, d = c + 1;
                    t[k++] = a; t[k++] = c; t[k++] = b; t[k++] = b; t[k++] = c; t[k++] = d;
                }
            m.vertices = v; m.triangles = t;
            var nn = new Vector3[v.Length]; for (int i = 0; i < nn.Length; i++) nn[i] = Vector3.up; m.normals = nn;
            m.bounds = new Bounds(Vector3.zero, new Vector3(size, 20f, size));
            return m;
        }

        private void TurnOff()
        {
            if (!_on) return;
            _on = false;
            if (_sun != null) { _sun.transform.rotation = _sunRot; _sun.intensity = _sunInt; _sun.color = _sunCol; }
            if (_flare != null) _flare.enabled = false;
            RenderSettings.ambientMode = _ambMode; RenderSettings.ambientSkyColor = _ambSky; RenderSettings.ambientEquatorColor = _ambEq; RenderSettings.ambientGroundColor = _ambGround;
            if (_skyMat != null && _skyMat.HasProperty("_SunSize"))
            {
                _skyMat.SetFloat("_SunSize", _skySize);
                if (_skyMat.HasProperty("_SunSizeConvergence")) _skyMat.SetFloat("_SunSizeConvergence", _skyConv);
                if (_skyMat.HasProperty("_AtmosphereThickness")) _skyMat.SetFloat("_AtmosphereThickness", _skyThick);
                if (_skyMat.HasProperty("_Exposure")) _skyMat.SetFloat("_Exposure", _skyExp);
                if (_skyMat.HasProperty("_SkyTint")) _skyMat.SetColor("_SkyTint", _skyTint);
            }
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) { urp.shadowDistance = _savedShadowDist; urp.shadowCascadeCount = _savedCascades; }
            if (_vol != null) Destroy(_vol);
            if (_sea != null) Destroy(_sea.gameObject);
            if (_farSea != null) Destroy(_farSea.gameObject);
            Flying = false;
        }

        private void LateUpdate()
        {
            if (!_on) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 c = cam.transform.position;
            if (_sea != null) _sea.position = new Vector3(Mathf.Round(c.x / 40f) * 40f, MegaWorld.SeaLevel, Mathf.Round(c.z / 40f) * 40f);
            if (_farSea != null) _farSea.position = new Vector3(Mathf.Round(c.x / 400f) * 400f, MegaWorld.SeaLevel - 0.08f, Mathf.Round(c.z / 400f) * 400f);
            // haze: thick on the ground (hides the far edge of the detail), thin up in the air
            float alt = Mathf.Clamp01((c.y - 20f) / 400f);
            float want = Flying ? 1f : alt;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.Lerp(new Color(0.72f, 0.8f, 0.88f), new Color(0.66f, 0.76f, 0.9f), want);
            RenderSettings.fogStartDistance = Mathf.Lerp(220f, 900f, want);
            RenderSettings.fogEndDistance = Mathf.Lerp(2300f, 9500f, want);
            cam.farClipPlane = Mathf.Lerp(2500f, 11000f, want);
        }
    }
}
