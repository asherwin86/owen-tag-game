using TMPro;
using UnityEngine;

namespace TagGame.Gameplay
{
    /// <summary>Builds a cartoon face (eyes + smile) on the front (+Z) of a capsule, plus an
    /// optional floating name that always faces the camera. All made from primitives so it
    /// needs no assets and survives WebGL shader stripping.</summary>
    public class PlayerFace : MonoBehaviour
    {
        private TextMeshPro _label;
        private Transform _labelT;
        private bool _faceBuilt;

        public static PlayerFace Attach(GameObject root, string displayName, Material baseMaterial)
        {
            var face = root.GetComponent<PlayerFace>();
            if (face == null) face = root.AddComponent<PlayerFace>();
            face.Build(displayName, baseMaterial);
            return face;
        }

        public void SetName(string displayName)
        {
            if (_label != null) _label.text = displayName;
        }

        private void Build(string displayName, Material baseMaterial)
        {
            if (_faceBuilt) { if (_labelT != null) SetName(displayName); else if (!string.IsNullOrEmpty(displayName)) BuildLabel(displayName); return; }
            _faceBuilt = true;

            Material white = Tinted(baseMaterial, Color.white);
            Material black = Tinted(baseMaterial, new Color(0.05f, 0.05f, 0.05f));

            Part(PrimitiveType.Sphere, "EyeL", new Vector3(-0.17f, 0.5f, 0.42f), Vector3.one * 0.22f, white);
            Part(PrimitiveType.Sphere, "EyeR", new Vector3(0.17f, 0.5f, 0.42f), Vector3.one * 0.22f, white);
            Part(PrimitiveType.Sphere, "PupilL", new Vector3(-0.17f, 0.5f, 0.52f), Vector3.one * 0.1f, black);
            Part(PrimitiveType.Sphere, "PupilR", new Vector3(0.17f, 0.5f, 0.52f), Vector3.one * 0.1f, black);
            Part(PrimitiveType.Sphere, "Mouth", new Vector3(0f, 0.22f, 0.47f), new Vector3(0.24f, 0.07f, 0.06f), black);

            if (!string.IsNullOrEmpty(displayName)) BuildLabel(displayName);
        }

        private void BuildLabel(string displayName)
        {
            {
                var go = new GameObject("NameLabel");
                _labelT = go.transform;
                _labelT.SetParent(transform, false);
                _label = go.AddComponent<TextMeshPro>();
                _label.text = displayName;
                _label.fontSize = 3.2f;
                _label.alignment = TextAlignmentOptions.Center;
                _label.textWrappingMode = TextWrappingModes.NoWrap;
                _label.color = Color.white;
                _label.outlineWidth = 0.25f;
                _label.outlineColor = new Color32(0, 0, 0, 255);
                _label.rectTransform.sizeDelta = new Vector2(8f, 1f);
            }
        }

        private void Part(PrimitiveType type, string partName, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = partName;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Material Tinted(Material source, Color color)
        {
            Material m = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = color;
            return m;
        }

        private void LateUpdate()
        {
            if (_labelT == null) return;
            var cam = Camera.main;
            _labelT.position = transform.position + Vector3.up * 1.55f;
            if (cam != null) _labelT.rotation = cam.transform.rotation;
        }
    }
}
