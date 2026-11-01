using System.Collections.Generic;
using UnityEngine;
using TagGame.Gameplay;

namespace TagGame.Maps
{
    /// <summary>
    /// Owns the playable map. Map 0 is the original small arena (the scene's Ground plane).
    /// Maps 1 and 2 (School, City) are big levels built at runtime from boxes, with the same
    /// result on every machine so online players all see the same level.
    /// Everything is made from primitives/generated meshes, so no assets and nothing for WebGL to strip.
    /// </summary>
    public partial class MapManager : MonoBehaviour
    {
        public static MapManager I { get; private set; }

        public const int Count = 3;
        public static readonly string[] Names = { "Arena", "School", "City" };
        public static readonly string[] Blurbs = { "Small & chaotic", "Huge: classrooms, gym, halls", "Huge: streets, blocks, alleys" };
        public static readonly float[] SpawnRadius = { 9f, 20f, 24f };
        public static readonly int[] PracticeTaggers = { 6, 10, 14 };
        private static readonly Vector3[] CamOffset = { new Vector3(0f, 4f, -7f), new Vector3(0f, 11f, -8f), new Vector3(0f, 15f, -9f) };
        private const string PrefMap = "TagGame.PracticeMap";

        public int Current { get; private set; } = -1;
        public int Selected { get; private set; }
        public MapGrid Grid { get; private set; }
        public System.Action<int> OnMapChanged;

        private GameObject _ground;
        private FollowCamera _cam;
        private Material _baseMat;
        private Transform _root;
        private readonly Dictionary<int, Material> _flatMats = new Dictionary<int, Material>();
        private Texture2D _winTex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<MapManager>() != null) return;
            if (FindFirstObjectByType<PlayerController>() == null) return;
            new GameObject("MapManager").AddComponent<MapManager>();
        }

        private void Awake()
        {
            I = this;
            _ground = GameObject.Find("Ground");
            _cam = FindFirstObjectByType<FollowCamera>();
            var player = FindFirstObjectByType<PlayerController>();
            var prend = player != null ? player.GetComponent<Renderer>() : null;
            _baseMat = prend != null ? prend.sharedMaterial : (_ground != null ? _ground.GetComponent<Renderer>().sharedMaterial : null);
            Selected = Mathf.Clamp(PlayerPrefs.GetInt(PrefMap, 0), 0, Count - 1);
            Current = 0; // the scene starts on the Arena
        }

        private void Start()
        {
            gameObject.AddComponent<MapPickerUI>();
            Load(Selected);
        }

        private void OnDestroy() { if (I == this) I = null; }

        /// <summary>Practice-mode choice (saved).</summary>
        public void Select(int id)
        {
            Selected = Mathf.Clamp(id, 0, Count - 1);
            PlayerPrefs.SetInt(PrefMap, Selected);
            PlayerPrefs.Save();
            Load(Selected);
        }

        public void LoadSelected() { Load(Selected); }

        public void Load(int id)
        {
            id = Mathf.Clamp(id, 0, Count - 1);
            if (id == Current && (id == 0 || _root != null)) { ApplyCamera(id); return; }
            if (_root != null) { Destroy(_root.gameObject); _root = null; }
            _flatMats.Clear();
            Grid = null;
            Current = id;
            if (_ground != null) _ground.SetActive(id == 0);
            if (id == 1) BuildSchool();
            else if (id == 2) BuildCity();
            ApplyCamera(id);
            OnMapChanged?.Invoke(id);
        }

        private void ApplyCamera(int id)
        {
            if (_cam != null) _cam.Offset = CamOffset[id];
        }

        // ------------------------------------------------------------ AI helpers

        /// <summary>How far from the centre bots may roam on the current map.</summary>
        public float PlayLimit => Current == 2 ? 120f : (Current == 1 ? 72f : 14f);

        public int PracticeTaggerCount => PracticeTaggers[Mathf.Clamp(Current, 0, Count - 1)];

        /// <summary>Where practice tagger i of n starts: spread evenly on a ring around the centre.</summary>
        public Vector3 TaggerSpawn(int i, int n, float y)
        {
            float r = Current == 0 ? 12f : SpawnRadius[Current] + 4f;
            float a = ((i + 0.5f) / Mathf.Max(1, n)) * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }

        // ------------------------------------------------------------ building blocks

        private Material Tint(Color c, float smooth = 0.1f)
        {
            int key = ((int)(c.r * 255f) << 24) | ((int)(c.g * 255f) << 16) | ((int)(c.b * 255f) << 8) | (int)(smooth * 100f);
            if (_flatMats.TryGetValue(key, out var m)) return m;
            m = _baseMat != null ? new Material(_baseMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.mainTexture = null;
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            _flatMats[key] = m;
            return m;
        }

        private Material Windowed(Color wall)
        {
            if (_winTex == null) _winTex = MakeWindowTexture();
            int key = 0x7000000 ^ ((int)(wall.r * 255f) << 16) ^ ((int)(wall.g * 255f) << 8) ^ (int)(wall.b * 255f);
            if (_flatMats.TryGetValue(key, out var m)) return m;
            m = _baseMat != null ? new Material(_baseMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.mainTexture = _winTex;
            m.color = wall;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            _flatMats[key] = m;
            return m;
        }

        private static Texture2D MakeWindowTexture()
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    Color c = Color.white;
                    bool inWin = x >= 14 && x < 50 && y >= 18 && y < 50;
                    bool frame = x >= 11 && x < 53 && y >= 15 && y < 53;
                    if (frame) c = new Color(0.78f, 0.78f, 0.8f);
                    if (inWin) c = (x + y) % 22 < 4 ? new Color(0.55f, 0.7f, 0.85f) : new Color(0.28f, 0.4f, 0.55f);
                    if (y < 4) c = new Color(0.82f, 0.82f, 0.82f);
                    px[y * S + x] = c;
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        private Transform Root()
        {
            if (_root == null) _root = new GameObject("Map_" + Names[Mathf.Clamp(Current, 0, Count - 1)]).transform;
            return _root;
        }

        /// <summary>A solid (or decorative) box from y0 up to y0+h, centred on (cx,cz).</summary>
        private GameObject Box(float cx, float cz, float sx, float sz, float h, Color c, bool solid = true, float y0 = 0f, Material wall = null, Material roof = null)
        {
            var go = new GameObject("Box");
            go.transform.SetParent(Root(), false);
            go.transform.position = new Vector3(cx, y0, cz);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = BoxMesh.Make(sx, h, sz, 4f, 3.5f);
            var mr = go.AddComponent<MeshRenderer>();
            Material top = roof != null ? roof : Tint(c * 0.9f);
            mr.sharedMaterials = new[] { wall != null ? wall : Tint(c), top };
            // tall things (buildings, walls, trees) cast real shadows; small clutter doesn't, to keep the web build fast
            mr.shadowCastingMode = h >= 2.5f ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (solid)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, h * 0.5f, 0f);
                bc.size = new Vector3(sx, h, sz);
                if (Grid != null && h > 0.4f && y0 < 0.3f) Grid.BlockRect(cx, cz, sx, sz, 0.8f);
            }
            return go;
        }

        /// <summary>Flat coloured patch on the ground (no collider): roads, paths, courts.</summary>
        private void Patch(float cx, float cz, float sx, float sz, Color c, float top = 0.01f)
        {
            var go = new GameObject("Patch");
            go.transform.SetParent(Root(), false);
            go.transform.position = new Vector3(cx, top - 0.01f, cz);
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh.Make(sx, 0.01f, sz, 4f, 4f);
            var mr = go.AddComponent<MeshRenderer>();
            var m = Tint(c, 0.05f);
            mr.sharedMaterials = new[] { m, m };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private void MakeGround(float half, Color c)
        {
            var go = new GameObject("MapGround");
            go.transform.SetParent(Root(), false);
            go.transform.position = new Vector3(0f, -1f, 0f);
            float size = half * 2f + 40f;
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh.Make(size, 1f, size, 10f, 10f);
            var mr = go.AddComponent<MeshRenderer>();
            var m = Tint(c, 0.02f);
            mr.sharedMaterials = new[] { m, m };
            mr.receiveShadows = true;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.5f, 0f);
            bc.size = new Vector3(size, 1f, size);
        }

        private void Tree(float x, float z, float scale, Color leaf)
        {
            Box(x, z, 0.5f * scale, 0.5f * scale, 2.2f * scale, new Color(0.4f, 0.28f, 0.18f), true);
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(Root(), false);
            s.transform.position = new Vector3(x, 3.1f * scale, z);
            s.transform.localScale = new Vector3(2.6f, 2.4f, 2.6f) * scale;
            var r = s.GetComponent<Renderer>();
            r.sharedMaterial = Tint(leaf);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>A wall along the X axis at z, from x0 to x1, with door gaps (centre,width).</summary>
        private void WallX(float z, float x0, float x1, float thick, float h, Color c, params Vector2[] gaps)
        {
            Segments(x0, x1, gaps, (a, b) => Box((a + b) * 0.5f, z, b - a, thick, h, c));
        }

        private void WallZ(float x, float z0, float z1, float thick, float h, Color c, params Vector2[] gaps)
        {
            Segments(z0, z1, gaps, (a, b) => Box(x, (a + b) * 0.5f, thick, b - a, h, c));
        }

        private static void Segments(float a, float b, Vector2[] gaps, System.Action<float, float> make)
        {
            var list = new List<Vector2>(gaps);
            list.Sort((p, q) => p.x.CompareTo(q.x));
            float cur = a;
            foreach (var g in list)
            {
                float g0 = g.x - g.y * 0.5f, g1 = g.x + g.y * 0.5f;
                if (g0 > cur + 0.01f) make(cur, Mathf.Min(g0, b));
                cur = Mathf.Max(cur, g1);
            }
            if (cur < b - 0.01f) make(cur, b);
        }

        /// <summary>Tiny deterministic random numbers so every machine builds the identical level.</summary>
        private struct Rng
        {
            private uint _s;
            public Rng(uint seed) { _s = seed == 0 ? 1u : seed; }
            public uint Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
            public float Value() { return (Next() & 0xFFFFFF) / (float)0x1000000; }
            public float Range(float a, float b) { return a + (b - a) * Value(); }
            public int Int(int a, int b) { return a + (int)(Value() * (b - a)); }
            public bool Chance(float p) { return Value() < p; }
        }
    }
}
