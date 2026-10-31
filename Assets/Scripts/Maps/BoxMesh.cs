using UnityEngine;

namespace TagGame.Maps
{
    /// <summary>Builds a box mesh whose UVs are in metres, so a window texture tiles at a constant
    /// size on any building. Submesh 0 = the four sides, submesh 1 = the top (so the roof can
    /// have its own material).</summary>
    public static class BoxMesh
    {
        public static Mesh Make(float sx, float h, float sz, float tileU, float tileV)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            var v = new Vector3[20];
            var uv = new Vector2[20];
            var n = new Vector3[20];
            // sides: 4 quads (front +z, back -z, right +x, left -x), from y=0 up to y=h
            Side(v, uv, n, 0, new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz), h, sx, tileU, tileV, Vector3.forward);
            Side(v, uv, n, 4, new Vector3(hx, 0, -hz), new Vector3(-hx, 0, -hz), h, sx, tileU, tileV, Vector3.back);
            Side(v, uv, n, 8, new Vector3(hx, 0, hz), new Vector3(hx, 0, -hz), h, sz, tileU, tileV, Vector3.right);
            Side(v, uv, n, 12, new Vector3(-hx, 0, -hz), new Vector3(-hx, 0, hz), h, sz, tileU, tileV, Vector3.left);
            // top
            v[16] = new Vector3(-hx, h, -hz); v[17] = new Vector3(-hx, h, hz); v[18] = new Vector3(hx, h, hz); v[19] = new Vector3(hx, h, -hz);
            for (int i = 16; i < 20; i++) { n[i] = Vector3.up; uv[i] = new Vector2(v[i].x / tileU, v[i].z / tileU); }
            var m = new Mesh { name = "BoxMesh" };
            m.vertices = v; m.normals = n; m.uv = uv;
            m.subMeshCount = 2;
            var side = new int[24];
            for (int q = 0; q < 4; q++)
            {
                int b = q * 4, t = q * 6;
                side[t] = b; side[t + 1] = b + 2; side[t + 2] = b + 1;
                side[t + 3] = b; side[t + 4] = b + 3; side[t + 5] = b + 2;
            }
            m.SetTriangles(side, 0);
            m.SetTriangles(new[] { 16, 17, 18, 16, 18, 19 }, 1);
            m.RecalculateBounds();
            return m;
        }

        private static void Side(Vector3[] v, Vector2[] uv, Vector3[] n, int i, Vector3 a, Vector3 b, float h, float width, float tileU, float tileV, Vector3 normal)
        {
            // a -> b is left-to-right as seen from outside
            v[i] = a; v[i + 1] = a + Vector3.up * h; v[i + 2] = b + Vector3.up * h; v[i + 3] = b;
            uv[i] = new Vector2(0f, 0f); uv[i + 1] = new Vector2(0f, h / tileV); uv[i + 2] = new Vector2(width / tileU, h / tileV); uv[i + 3] = new Vector2(width / tileU, 0f);
            for (int k = 0; k < 4; k++) n[i + k] = normal;
        }
    }
}
