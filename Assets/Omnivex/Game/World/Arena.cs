using System.Collections.Generic;
using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    public sealed class Zone
    {
        public string Name;
        public Dna Primary, Secondary;
        public Color Tint;
        public float Danger;   // 0..1, scales predator density
    }

    public struct Obstacle { public Vector2 p; public float r; }

    /// <summary>
    /// Seeded tide-pool generator. Five feeding grounds around a central trench; the zone you hunt in decides your diet,
    /// so geography is an evolutionary choice. Static scenery is merged into a handful of meshes.
    /// </summary>
    public sealed class Arena : MonoBehaviour
    {
        public const float Radius = 82f;
        public int Seed { get; private set; }
        public readonly List<Obstacle> Obstacles = new List<Obstacle>();
        public readonly List<Vector2> VentSpots = new List<Vector2>();
        readonly Zone[] _zones = new Zone[5];
        float _zoneRot;
        public Zone Shallows { get; private set; }
        public Zone Abyss { get; private set; }
        Rng _rng;

        public static readonly Zone[] Templates =
        {
            new Zone { Name = "Kelp Thicket", Primary = Dna.Flora, Secondary = Dna.Flesh, Tint = Palette.Hex(0x1E4A3A), Danger = 0.35f },
            new Zone { Name = "Crystal Shelf", Primary = Dna.Mineral, Secondary = Dna.Volt, Tint = Palette.Hex(0x1C3656), Danger = 0.45f },
            new Zone { Name = "Vent Field", Primary = Dna.Volt, Secondary = Dna.Toxin, Tint = Palette.Hex(0x3A2A2A), Danger = 0.7f },
            new Zone { Name = "Bloom Reef", Primary = Dna.Flesh, Secondary = Dna.Flora, Tint = Palette.Hex(0x3A2140), Danger = 0.5f },
            new Zone { Name = "Rot Garden", Primary = Dna.Toxin, Secondary = Dna.Flesh, Tint = Palette.Hex(0x2A1E44), Danger = 0.55f },
        };

        public void Build(int seed)
        {
            Seed = seed;
            _rng = new Rng(seed);
            // shuffle zone order and rotate the compass so every seed lays out differently
            var order = new List<int> { 0, 1, 2, 3, 4 };
            for (int i = order.Count - 1; i > 0; i--) { int j = _rng.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }
            for (int i = 0; i < 5; i++) _zones[i] = Templates[order[i]];
            _zoneRot = _rng.Range(0f, 360f);
            Shallows = new Zone { Name = "The Shallows", Primary = Dna.Flora, Secondary = Dna.Flesh, Tint = Palette.SiltLight, Danger = 0.2f };
            Abyss = new Zone { Name = "Abyssal Rim", Primary = Dna.Flesh, Secondary = Dna.Void, Tint = Palette.Abyss, Danger = 1f };

            BuildFloor();
            BuildScenery();
            BuildLights();
        }

        public Zone ZoneAt(Vector3 p)
        {
            float d = new Vector2(p.x, p.z).magnitude;
            if (d < 20) return Shallows;
            if (d > 68) return Abyss;
            float a = Mathf.Repeat(Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg - _zoneRot, 360f);
            return _zones[Mathf.Clamp((int)(a / 72f), 0, 4)];
        }

        /// <summary>Centre direction of a named zone (for the HUD compass and spawning landmarks).</summary>
        public Vector3 ZoneCenter(int i) { float a = (_zoneRot + 36 + i * 72) * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 44f; }
        public Zone ZoneByIndex(int i) => _zones[i];

        public float HeightAt(float x, float z)
        {
            float d = Mathf.Sqrt(x * x + z * z);
            float h = (Mathf.PerlinNoise(x * 0.045f + Seed * 0.13f, z * 0.045f) - 0.5f) * 1.6f;
            h += (Mathf.PerlinNoise(x * 0.16f, z * 0.16f + Seed) - 0.5f) * 0.35f;
            h -= Mathf.Exp(-d * d / 90f) * 1.2f;                     // central trench (the Matriarch's nest)
            if (d > Radius - 6) h += Mathf.Pow((d - (Radius - 6)) * 0.42f, 2);   // rim rises into the cliffs
            return h;
        }

        public Vector3 Clamp(Vector3 p, float margin)
        {
            var flat = new Vector2(p.x, p.z);
            float max = Radius - 4 - margin;
            if (flat.magnitude > max) { flat = flat.normalized * max; p.x = flat.x; p.z = flat.y; }
            return p;
        }

        void BuildFloor()
        {
            int rings = 70, segs = 140;
            float R = Radius + 18;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            v.Add(new Vector3(0, HeightAt(0, 0), 0)); c.Add(Tint(Vector3.zero));
            for (int r = 1; r <= rings; r++)
            {
                float rad = R * Mathf.Pow(r / (float)rings, 1.15f);
                for (int s = 0; s < segs; s++)
                {
                    float a = 2 * Mathf.PI * s / segs;
                    float x = Mathf.Cos(a) * rad, z = Mathf.Sin(a) * rad;
                    var p = new Vector3(x, HeightAt(x, z), z);
                    v.Add(p); c.Add(Tint(p));
                }
            }
            for (int s = 0; s < segs; s++) { t.Add(0); t.Add(1 + (s + 1) % segs); t.Add(1 + s); }
            for (int r = 1; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int a = 1 + (r - 1) * segs + s, b = 1 + (r - 1) * segs + (s + 1) % segs;
                    int a2 = a + segs, b2 = b + segs;
                    t.Add(a); t.Add(b); t.Add(a2); t.Add(b); t.Add(b2); t.Add(a2);
                }
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "Seabed" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            var go = new GameObject("Seabed"); go.transform.SetParent(transform);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Mats.Seabed();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Color Tint(Vector3 p)
        {
            var z = ZoneAt(p);
            float n = Mathf.PerlinNoise(p.x * 0.09f + 40, p.z * 0.09f);
            var col = Color.Lerp(Palette.Silt, z.Tint, 0.75f) * (0.75f + n * 0.5f);
            float d = new Vector2(p.x, p.z).magnitude;
            if (d > Radius - 8) col = Color.Lerp(col, Palette.ReefDark, Mathf.Clamp01((d - (Radius - 8)) / 8));
            // faint living glow in patches
            float glow = Mathf.Clamp01((Mathf.PerlinNoise(p.x * 0.07f, p.z * 0.07f + 90) - 0.62f) * 4) * 0.5f;
            if (d < 9) glow = Mathf.Max(glow, (1 - d / 9) * 0.35f);
            col.a = glow;
            return col;
        }

        void BuildScenery()
        {
            var rocks = new List<CombineInstance>();
            var kelp = new List<CombineInstance>();
            var crystals = new List<CombineInstance>();
            var coral = new List<CombineInstance>();
            var vents = new List<CombineInstance>();
            var rot = new List<CombineInstance>();

            var rockMesh = new Mesh[4];
            for (int i = 0; i < 4; i++) rockMesh[i] = MeshKit.Blob(14, 10, Palette.Reef, Palette.ReefDark, 0.9f, i + Seed, 0.7f);
            var kelpMesh = new Mesh[3];
            for (int i = 0; i < 3; i++) kelpMesh[i] = MeshKit.Ribbon(10, 0.22f, Palette.Hex(0x0F3B2C), Palette.Hex(0x2FD39A), 2.5f + i, i * 3 + Seed);
            var shard = MeshKit.Cone(6, Palette.Hex(0x2A4F7A), Palette.Hex(0xBFF1FF), 0.02f);
            var coralBlob = MeshKit.Blob(10, 7, Palette.Hex(0xFF7FA0), Palette.Hex(0x5A1F3A), 0.5f, 7);
            var chimney = MeshKit.Cone(9, Palette.Hex(0x2B2222), Palette.Hex(0x5A3A2A), 0.28f);
            var stalk = MeshKit.Ribbon(8, 0.12f, Palette.Hex(0x2A1E44), Palette.Hex(0xB25CFF), 4f, Seed);

            void Add(List<CombineInstance> list, Mesh m, Vector3 p, Quaternion q, Vector3 s) =>
                list.Add(new CombineInstance { mesh = m, transform = Matrix4x4.TRS(p, q, s) });

            // rim cliffs
            for (int i = 0; i < 120; i++)
            {
                float a = i / 120f * Mathf.PI * 2 + _rng.Range(-0.02f, 0.02f);
                float d = Radius + _rng.Range(-2f, 5f);
                var p = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d); p.y = HeightAt(p.x, p.z) - 1;
                float s = _rng.Range(7f, 14f);
                Add(rocks, rockMesh[i % 4], p, Quaternion.Euler(0, _rng.Range(0, 360f), 0), new Vector3(s, s * _rng.Range(0.8f, 1.6f), s));
            }
            // scattered boulders (obstacles)
            for (int i = 0; i < 46; i++)
            {
                var p2 = RandomPoint(14, Radius - 10);
                float s = _rng.Range(1.6f, 5.5f);
                var p = new Vector3(p2.x, HeightAt(p2.x, p2.y) - s * 0.15f, p2.y);
                Add(rocks, rockMesh[i % 4], p, Quaternion.Euler(0, _rng.Range(0, 360f), 0), new Vector3(s, s * 0.8f, s));
                Obstacles.Add(new Obstacle { p = p2, r = s * 0.42f });
            }

            for (int i = 0; i < 1400; i++)
            {
                var p2 = RandomPoint(6, Radius - 6);
                var p = new Vector3(p2.x, HeightAt(p2.x, p2.y), p2.y);
                var z = ZoneAt(p);
                if (BlockedByRock(p2, 0.5f)) continue;
                var q = Quaternion.Euler(_rng.Range(-8f, 8f), _rng.Range(0, 360f), _rng.Range(-8f, 8f));
                switch (z.Primary)
                {
                    case Dna.Flora:
                        if (z == Shallows ? _rng.Chance(0.25f) : true)
                        {
                            float h = z == Shallows ? _rng.Range(0.6f, 1.6f) : _rng.Range(2f, 7f);
                            Add(kelp, kelpMesh[i % 3], p, q, new Vector3(h * 0.6f, h, h * 0.6f));
                        }
                        break;
                    case Dna.Mineral:
                        if (_rng.Chance(0.6f))
                        {
                            int n = _rng.Range(2, 6);
                            for (int k = 0; k < n; k++)
                            {
                                float h = _rng.Range(0.6f, 3.2f);
                                var off = new Vector3(_rng.Range(-0.6f, 0.6f), 0, _rng.Range(-0.6f, 0.6f));
                                Add(crystals, shard, p + off, Quaternion.Euler(-90 + _rng.Range(-30f, 30f), _rng.Range(0, 360f), 0), new Vector3(h * 0.35f, h * 0.35f, h));
                            }
                        }
                        break;
                    case Dna.Volt:
                        if (_rng.Chance(0.06f) && VentSpots.Count < 14 && new Vector2(p.x, p.z).magnitude > 24)
                        {
                            float h = _rng.Range(1.2f, 2.6f);
                            Add(vents, chimney, p, Quaternion.Euler(-90, 0, 0), new Vector3(h * 1.1f, h * 1.1f, h));
                            VentSpots.Add(p2);
                        }
                        else if (_rng.Chance(0.25f)) Add(crystals, shard, p, Quaternion.Euler(-90 + _rng.Range(-20f, 20f), 0, 0), Vector3.one * _rng.Range(0.3f, 0.9f));
                        break;
                    case Dna.Flesh:
                        if (_rng.Chance(z == Abyss ? 0.08f : 0.45f))
                        {
                            float s = _rng.Range(0.5f, 1.8f);
                            for (int k = 0; k < 4; k++)
                                Add(coral, coralBlob, p + new Vector3(_rng.Range(-0.5f, 0.5f) * s, _rng.Range(0, 1.2f) * s, _rng.Range(-0.5f, 0.5f) * s), q, Vector3.one * s * _rng.Range(0.4f, 0.9f));
                        }
                        break;
                    case Dna.Toxin:
                        if (_rng.Chance(0.5f))
                        {
                            float h = _rng.Range(1f, 3.5f);
                            Add(rot, stalk, p, q, new Vector3(h, h, h));
                            Add(rot, coralBlob, p + Vector3.up * h, q, Vector3.one * 0.35f);
                        }
                        break;
                }
            }

            Spawn("Rocks", rocks, Mats.Bio("rock", Palette.Hdr(Palette.Accent, 0.12f), 0.35f, Color.black, 0, 0, 0.15f), true);
            Spawn("Kelp", kelp, Mats.Bio("kelp", Palette.Hdr(Palette.Of(Dna.Flora), 0.5f), 0.8f, Palette.Hdr(Palette.Of(Dna.Flora), 1.4f), 0.5f, 0.12f, 0.2f, 1.2f, 2.5f), false);
            var crystalMat = Mats.Bio("crystal", Palette.Hdr(Palette.Of(Dna.Mineral), 1.6f), 1.2f, Palette.Hdr(Palette.Of(Dna.Mineral), 1.8f), 0.6f, 0, 0.9f, 1.5f, 3f);
            crystalMat.SetColor("_EmissionColor", Palette.Hdr(Palette.Of(Dna.Mineral), 0.25f));
            Spawn("Crystals", crystals, crystalMat, true);
            Spawn("Coral", coral, Mats.Bio("coral", Palette.Hdr(Palette.Of(Dna.Flesh), 0.8f), 0.9f, Palette.Hdr(Palette.Of(Dna.Flesh), 2f), 0.8f, 0.03f, 0.5f, 1.8f, 5f), true);
            var ventMat = Mats.Bio("vent", Palette.Hdr(Palette.Warning, 0.5f), 0.6f, Palette.Hdr(Palette.Warning, 2.4f), 0.7f, 0, 0.2f, 3f, 6f);
            Spawn("Vents", vents, ventMat, true);
            Spawn("RotStalks", rot, Mats.Bio("rot", Palette.Hdr(Palette.Of(Dna.Toxin), 0.8f), 0.9f, Palette.Hdr(Palette.Of(Dna.Toxin), 2f), 0.7f, 0.1f, 0.3f, 1.4f, 3f), false);
        }

        void Spawn(string name, List<CombineInstance> parts, Material mat, bool shadows)
        {
            // split into chunks to stay under sensible vertex counts and keep culling useful
            const int chunk = 220;
            for (int i = 0; i < parts.Count; i += chunk)
            {
                var slice = parts.GetRange(i, Mathf.Min(chunk, parts.Count - i));
                var go = new GameObject(name); go.transform.SetParent(transform);
                go.AddComponent<MeshFilter>().sharedMesh = MeshKit.Combine(slice);
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        void BuildLights()
        {
            for (int i = 0; i < 5; i++)
            {
                var c = ZoneCenter(i);
                var z = _zones[i];
                var go = new GameObject("ZoneLight " + z.Name); go.transform.SetParent(transform);
                go.transform.position = c + Vector3.up * 6;
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 34; l.intensity = 2.2f;
                l.color = Color.Lerp(Palette.Of(z.Primary), Color.white, 0.25f); l.shadows = LightShadows.None;
            }
            var core = new GameObject("TrenchGlow"); core.transform.SetParent(transform);
            core.transform.position = new Vector3(0, 2, 0);
            var cl = core.AddComponent<Light>(); cl.type = LightType.Point; cl.range = 22; cl.intensity = 2.5f; cl.color = Palette.Accent;
        }

        public Vector2 RandomPoint(float minR, float maxR)
        {
            float a = _rng.Range(0, Mathf.PI * 2);
            float d = Mathf.Sqrt(_rng.Range(minR * minR, maxR * maxR));
            return new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
        }

        public bool BlockedByRock(Vector2 p, float r)
        {
            foreach (var o in Obstacles) if ((o.p - p).sqrMagnitude < (o.r + r) * (o.r + r)) return true;
            return false;
        }

        /// <summary>Pushes a circle out of boulders and keeps it inside the pool. Returns the corrected position.</summary>
        public Vector3 Resolve(Vector3 pos, float r)
        {
            var p = new Vector2(pos.x, pos.z);
            foreach (var o in Obstacles)
            {
                var d = p - o.p; float min = o.r + r * 0.7f;
                float sq = d.sqrMagnitude;
                if (sq < min * min && sq > 0.0001f) p = o.p + d.normalized * min;
            }
            float max = Radius - 4 - r;
            if (p.magnitude > max) p = p.normalized * max;
            return new Vector3(p.x, pos.y, p.y);
        }
    }
}
