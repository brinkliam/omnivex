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
            for (int i = 0; i < segs; i++)
            {
                int a = i * 2;
                t.Add(a); t.Add(a + 2); t.Add(a + 1); t.Add(a + 1); t.Add(a + 2); t.Add(a + 3);
                t.Add(a); t.Add(a + 1); t.Add(a + 2); t.Add(a + 1); t.Add(a + 3); t.Add(a + 2);
            }
            var m = new Mesh(); m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
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
