using System.Collections.Generic;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>Procedural meshes. Vertex colours carry albedo; all meshes are original geometry built at startup.</summary>
    public static class MeshKit
    {
        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        public static Mesh Cached(string key, System.Func<Mesh> build)
        {
            if (!Cache.TryGetValue(key, out var m) || m == null) { m = build(); m.name = key; Cache[key] = m; }
            return m;
        }

        /// <summary>UV sphere with optional lumpy noise (organic blobs, rocks).</summary>
        public static Mesh Blob(int lon, int lat, Color top, Color bottom, float lump = 0, int seed = 0, float squashY = 1)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int y = 0; y <= lat; y++)
            {
                float a = Mathf.PI * y / lat;
                for (int x = 0; x <= lon; x++)
                {
                    float b = 2 * Mathf.PI * x / lon;
                    var dir = new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b));
                    float r = 0.5f;
                    if (lump > 0)
                    {
                        var p = dir * 2.3f + new Vector3(seed * 1.37f, seed * 0.71f, seed * 2.11f);
                        float nse = Mathf.PerlinNoise(p.x + p.z * 0.5f, p.y + p.z * 0.3f) - 0.5f;
                        nse += (Mathf.PerlinNoise(p.x * 2.1f + 5, p.y * 2.3f - p.z) - 0.5f) * 0.5f;
                        r *= 1 + nse * lump;
                    }
                    var pos = dir * r; pos.y *= squashY;
                    v.Add(pos); n.Add(dir);
                    c.Add(Color.Lerp(bottom, top, dir.y * 0.5f + 0.5f));
                    uv.Add(new Vector2(x / (float)lon, y / (float)lat));
                }
            }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int i0 = y * (lon + 1) + x, i1 = i0 + lon + 1;
                    t.Add(i0); t.Add(i0 + 1); t.Add(i1);
                    t.Add(i0 + 1); t.Add(i1 + 1); t.Add(i1);
                }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            if (lump > 0 || squashY != 1) m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Cone along +Z (spikes, teeth, siphons, horns).</summary>
        public static Mesh Cone(int sides, Color baseCol, Color tipCol, float tipRadius = 0)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (int i = 0; i <= sides; i++)
            {
                float a = 2 * Mathf.PI * i / sides;
                v.Add(new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0)); c.Add(baseCol);
                v.Add(new Vector3(Mathf.Cos(a) * tipRadius, Mathf.Sin(a) * tipRadius, 1)); c.Add(tipCol);
            }
            for (int i = 0; i < sides; i++)
            {
                int a = i * 2;
                t.Add(a); t.Add(a + 1); t.Add(a + 2);
                t.Add(a + 1); t.Add(a + 3); t.Add(a + 2);
            }
            var m = new Mesh(); m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Flat quad on XZ with 0..1 UVs (glow decals, telegraphs, shockwave rings).</summary>
        public static Mesh QuadXZ()
        {
            return Cached("quadxz", () =>
            {
                var m = new Mesh();
                m.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f) };
                m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                m.RecalculateBounds();
                return m;
            });
        }

        /// <summary>Vertical ribbon with a gentle twist (kelp, fronds). Base at origin, height 1.</summary>
        public static Mesh Ribbon(int segs, float width, Color baseCol, Color tipCol, float twist, int seed)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (int i = 0; i <= segs; i++)
            {
                float u = i / (float)segs;
                float w = width * (1 - u * 0.75f) * (0.8f + 0.4f * Mathf.Sin(u * 9 + seed));
                float a = u * twist + seed;
                var side = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * w;
                var center = new Vector3(Mathf.Sin(u * 3 + seed) * 0.08f, u, Mathf.Cos(u * 2.3f + seed) * 0.08f);
                v.Add(center - side); v.Add(center + side);
                var col = Color.Lerp(baseCol, tipCol, u); c.Add(col); c.Add(col);
            }
            int n = v.Count;
            for (int i = 0; i < n; i++) { v.Add(v[i]); c.Add(c[i]); }   // the back face gets its own vertices
            for (int i = 0; i < segs; i++)
            {
                int a = i * 2, b = n + i * 2;
                t.Add(a); t.Add(a + 2); t.Add(a + 1); t.Add(a + 1); t.Add(a + 2); t.Add(a + 3);
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b + 1); t.Add(b + 3); t.Add(b + 2);
            }
            var m = new Mesh(); m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Unit box centred at the origin, flat-shaded, top/bottom tinted. Alpha = window glow (0 here).</summary>
        public static Mesh Box(Color top, Color bottom)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>(); var n = new List<Vector3>();
            AddBox(v, c, t, n, Vector3.zero, Vector3.one, top, bottom);
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        static readonly Vector3[] Faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        public static void AddBox(List<Vector3> v, List<Color> c, List<int> t, List<Vector3> n, Vector3 center, Vector3 size, Color top, Color bottom, float glow = 0)
        {
            foreach (var f in Faces)
            {
                var u = Mathf.Abs(f.y) > 0.5f ? Vector3.right : Vector3.up;
                var w = Vector3.Cross(f, u);
                int b = v.Count;
                for (int i = 0; i < 4; i++)
                {
                    float su = (i == 1 || i == 2) ? 0.5f : -0.5f, sw = i >= 2 ? 0.5f : -0.5f;
                    var p = f * 0.5f + u * su + w * sw;
                    v.Add(center + Vector3.Scale(p, size)); n.Add(f);
                    var col = Color.Lerp(bottom, top, p.y + 0.5f); col.a = glow; c.Add(col);
                }
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
        }

        /// <summary>
        /// A town building: walls, a roof (pitched for houses), and rows of windows whose vertex alpha drives the
        /// shader's window glow. Footprint w x d, height h, base at y=0.
        /// </summary>
        public static Mesh Building(float w, float d, float h, Color wall, Color roof, bool pitched, int seed, float litChance)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>(); var n = new List<Vector3>();
            var rnd = new System.Random(seed);
            AddBox(v, c, t, n, new Vector3(0, h / 2, 0), new Vector3(w, h, d), Color.Lerp(wall, Color.white, 0.08f), wall * 0.7f);
            if (pitched)
            {
                // a gabled roof from two tilted slabs
                float rh = Mathf.Min(w, d) * 0.45f;
                for (int s = -1; s <= 1; s += 2)
                {
                    int b = v.Count;
                    var p0 = new Vector3(-w * 0.55f, h, s * d * 0.55f); var p1 = new Vector3(w * 0.55f, h, s * d * 0.55f);
                    var p2 = new Vector3(w * 0.55f, h + rh, 0); var p3 = new Vector3(-w * 0.55f, h + rh, 0);
                    v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                    var nn = Vector3.Cross(p1 - p0, p3 - p0).normalized * -s; if (nn.y < 0) nn = -nn;
                    for (int i = 0; i < 4; i++) { n.Add(nn); c.Add(roof); }
                    if (s > 0) { t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3); }
                    else { t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2); }
                }
            }
            else
            {
                AddBox(v, c, t, n, new Vector3(0, h + 0.25f, 0), new Vector3(w * 1.02f, 0.5f, d * 1.02f), roof, roof * 0.8f);
                if (rnd.NextDouble() < 0.7) AddBox(v, c, t, n, new Vector3(w * 0.2f, h + 1.1f, -d * 0.15f), new Vector3(w * 0.22f, 1.4f, d * 0.22f), roof * 0.9f, roof * 0.7f);
            }
            // windows: a grid on all four facades
            float floor = 3.2f;
            int floors = Mathf.Max(1, Mathf.FloorToInt((h - 1f) / floor));
            var lit = new Color(1f, 0.78f, 0.42f); var cool = new Color(0.45f, 0.95f, 1f); var dark = new Color(0.08f, 0.1f, 0.14f);
            for (int side = 0; side < 4; side++)
            {
                var f = side == 0 ? Vector3.forward : side == 1 ? Vector3.back : side == 2 ? Vector3.right : Vector3.left;
                float span = side < 2 ? w : d, depth = side < 2 ? d : w;
                int cols = Mathf.Max(1, Mathf.FloorToInt(span / 2.6f));
                for (int fl = 0; fl < floors; fl++)
                    for (int col = 0; col < cols; col++)
                    {
                        bool on = rnd.NextDouble() < litChance;
                        var wc = on ? (rnd.NextDouble() < 0.8 ? lit : cool) : dark;
                        float x = (col + 0.5f) / cols * span - span / 2;
                        float y = 1.4f + fl * floor + floor * 0.45f;
                        if (y > h - 0.6f) continue;
                        var center = f * (depth / 2 + 0.03f) + (side < 2 ? new Vector3(x, y, 0) : new Vector3(0, y, x));
                        var size = side < 2 ? new Vector3(1.3f, 1.5f, 0.06f) : new Vector3(0.06f, 1.5f, 1.3f);
                        AddBox(v, c, t, n, center, size, wc, wc, on ? 1f : 0f);
                    }
            }
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>Merge many (mesh, matrix) pairs into one mesh (static scenery batching).</summary>
        public static Mesh Combine(List<CombineInstance> parts)
        {
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.CombineMeshes(parts.ToArray(), true, true);
            m.RecalculateBounds();
            return m;
        }
    }
}
