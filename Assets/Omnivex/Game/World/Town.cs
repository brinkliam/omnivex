using System;
using System.Collections.Generic;
using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    public sealed class Building
    {
        public string Kind;
        public Transform T;
        public MeshRenderer R;
        public Vector2 C, Half;
        public float H, Hp, MaxHp, Req, Value;
        public Dna Dna;
        public int Stage;            // 0 intact, 1 cracked, 2 failing, 3 gone
        public float CollapseT = -1, ChewFx, Tilt;
        public Vector3 TiltAxis;
        public bool Tree;            // swallowed whole on touch instead of chewed
        public bool Standing => Stage < 3 && CollapseT < 0;
    }

    /// <summary>
    /// STAGE 2 — HARROWGATE. A seeded town at dusk: suburbs, midtown, downtown towers, an industrial quarter, parks
    /// to hide in, and a military base. Every building can be chewed apart in stages once you're big enough;
    /// collapses throw debris, shake the camera and burst into biomass.
    /// </summary>
    public sealed class Town : WorldBase
    {
        public const float Pitch = 46f, BlockSize = 36f;
        public override float Bound => 158f;
        public readonly List<Building> Buildings = new List<Building>();
        public PlayerCreature Player; public Ecosystem Eco; public CameraRig Rig; public AudioDirector Audio;
        public event Action<Building> OnDestroyed;
        public Vector3 BaseCenter { get; private set; }
        public Vector3 PlayerStart => new Vector3(0, 0, -Bound + 14);

        enum BlockType { Suburb, Midtown, Downtown, Plaza, Park, Industrial, Base }
        readonly Dictionary<(int, int), BlockType> _blocks = new Dictionary<(int, int), BlockType>();
        Zone _suburb, _midtown, _downtown, _plaza, _park, _industrial, _base, _outskirts;
        Material _bldMat, _groundMat;
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        float _tooSmallToast;
        int _built;

        // pooled falling debris
        sealed class Chunk { public Transform t; public Vector3 v, spin; public float life; }
        readonly List<Chunk> _chunks = new List<Chunk>();
        Mesh _chunkMesh;

        public override void Build(int seed)
        {
            Seed = seed;
            _rng = new Rng(seed ^ 0x2A2A);
            Buildings.Clear(); _blocks.Clear(); _built = 0;
            _suburb = new Zone { Name = "Hollowmere Suburbs", Primary = Dna.Flesh, Secondary = Dna.Flora, Tint = Palette.Hex(0x2A3B2A), Danger = 0.3f };
            _midtown = new Zone { Name = "Midtown", Primary = Dna.Flesh, Secondary = Dna.Mineral, Tint = Palette.Hex(0x30303C), Danger = 0.5f };
            _downtown = new Zone { Name = "Downtown Spires", Primary = Dna.Mineral, Secondary = Dna.Flesh, Tint = Palette.Hex(0x2C2C3A), Danger = 0.6f };
            _plaza = new Zone { Name = "Founders' Plaza", Primary = Dna.Flesh, Secondary = Dna.Mineral, Tint = Palette.Hex(0x3A3446), Danger = 0.4f };
            _park = new Zone { Name = "Gloamwood Park", Primary = Dna.Flora, Secondary = Dna.Flesh, Tint = Palette.Hex(0x1C3A24), Danger = 0.2f, Cover = true };
            _industrial = new Zone { Name = "Coilworks", Primary = Dna.Volt, Secondary = Dna.Alloy, Tint = Palette.Hex(0x3A3024), Danger = 0.6f };
            _base = new Zone { Name = "Fort Harrow", Primary = Dna.Alloy, Secondary = Dna.Flesh, Tint = Palette.Hex(0x2E3424), Danger = 1f };
            _outskirts = new Zone { Name = "Outskirts", Primary = Dna.Flora, Secondary = Dna.Flesh, Tint = Palette.Hex(0x1A2418), Danger = 0.2f };

            _bldMat = Mats.Bio("building", Palette.Hdr(Palette.Hex(0x9FB6FF), 0.25f), 0.35f, Color.black, 0, 0, 0.25f);
            _bldMat.SetColor("_WindowGlow", Palette.Hdr(Color.white, 2.2f));
            _groundMat = Mats.Bio("townGround", Color.black, 0, Color.black, 0, 0, 0.1f);
            _groundMat.SetColor("_WindowGlow", Palette.Hdr(Color.white, 1.8f));
            _chunkMesh = MeshKit.Box(Palette.Hex(0x8A8796), Palette.Hex(0x4A4856));

            // lay out the grid: downtown core, a plaza, a midtown ring, parks, an industrial side and a base opposite it
            int side = _rng.Range(0, 4);
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            var ind = dirs[side]; var bas = dirs[(side + 1) % 4];
            for (int i = -3; i <= 3; i++)
                for (int j = -3; j <= 3; j++)
                {
                    var c = new Vector2(i, j) * Pitch;
                    if (c.magnitude > Bound - 12) continue;
                    int ring = Mathf.Max(Mathf.Abs(i), Mathf.Abs(j));
                    BlockType t = ring <= 1 ? BlockType.Downtown : ring == 2 ? BlockType.Midtown : BlockType.Suburb;
                    if (i == 0 && j == 0) t = BlockType.Plaza;
                    if (ring >= 2 && (i * ind.Item1 + j * ind.Item2) >= 2 && Mathf.Abs(i * ind.Item2 + j * ind.Item1) <= 1) t = BlockType.Industrial;
                    if (ring == 3 && (i * bas.Item1 + j * bas.Item2) == 3 && Mathf.Abs(i * bas.Item2 + j * bas.Item1) <= 1) t = BlockType.Base;
                    _blocks[(i, j)] = t;
                }
            // two parks among the suburbs/midtown
            var candidates = new List<(int, int)>();
            foreach (var kv in _blocks) if (kv.Value == BlockType.Suburb || kv.Value == BlockType.Midtown) candidates.Add(kv.Key);
            for (int k = 0; k < 3 && candidates.Count > 0; k++) { int idx = _rng.Range(0, candidates.Count); _blocks[candidates[idx]] = BlockType.Park; candidates.RemoveAt(idx); }
            BaseCenter = new Vector3(bas.Item1, 0, bas.Item2) * Pitch * 3;
            // the player emerges on the side away from the base
            BuildGround();
            foreach (var kv in _blocks) BuildBlock(kv.Key.Item1, kv.Key.Item2, kv.Value);
            BuildLights();
        }

        public override float HeightAt(float x, float z)
        {
            float d = Mathf.Sqrt(x * x + z * z);
            return d < Bound - 2 ? 0 : (d - (Bound - 2)) * 0.6f;
        }

        public override Zone ZoneAt(Vector3 p)
        {
            if (new Vector2(p.x, p.z).magnitude > Bound - 8) return _outskirts;
            int i = Mathf.RoundToInt(p.x / Pitch), j = Mathf.RoundToInt(p.z / Pitch);
            if (!_blocks.TryGetValue((i, j), out var t)) return _outskirts;
            return t switch
            {
                BlockType.Suburb => _suburb, BlockType.Midtown => _midtown, BlockType.Downtown => _downtown, BlockType.Plaza => _plaza,
                BlockType.Park => _park, BlockType.Industrial => _industrial, _ => _base,
            };
        }

        public string ZoneName(Vector3 p) => ZoneAt(p).Name;

        // ------------------------------------------------------------------ construction

        void BuildGround()
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>(); var n = new List<Vector3>();
            var asphalt = Palette.Hex(0x1A1C24);
            MeshKit.AddBox(v, c, t, n, new Vector3(0, -0.5f, 0), new Vector3(Bound * 2 + 60, 1, Bound * 2 + 60), asphalt, asphalt);
            foreach (var kv in _blocks)
            {
                var ctr = new Vector3(kv.Key.Item1 * Pitch, 0, kv.Key.Item2 * Pitch);
                var kind = kv.Value;
                Color top = kind switch
                {
                    BlockType.Park => Palette.Hex(0x1E3A22), BlockType.Suburb => Palette.Hex(0x243A26), BlockType.Base => Palette.Hex(0x3A3C2E),
                    BlockType.Industrial => Palette.Hex(0x3A342C), BlockType.Plaza => Palette.Hex(0x4A4450), _ => Palette.Hex(0x34343E),
                };
                MeshKit.AddBox(v, c, t, n, ctr + new Vector3(0, 0.12f, 0), new Vector3(BlockSize + 2, 0.24f, BlockSize + 2), Palette.Hex(0x55545E), Palette.Hex(0x3A3A44));   // kerb
                MeshKit.AddBox(v, c, t, n, ctr + new Vector3(0, 0.2f, 0), new Vector3(BlockSize - 2, 0.2f, BlockSize - 2), top, top);
                // lane dashes on the two roads leading east and north from this block
                for (int k = -3; k <= 3; k++)
                {
                    MeshKit.AddBox(v, c, t, n, ctr + new Vector3(Pitch / 2, 0.02f, k * 5), new Vector3(0.35f, 0.04f, 2.4f), Palette.Hex(0xE8D58A), Palette.Hex(0xE8D58A));
                    MeshKit.AddBox(v, c, t, n, ctr + new Vector3(k * 5, 0.02f, Pitch / 2), new Vector3(2.4f, 0.04f, 0.35f), Palette.Hex(0xE8D58A), Palette.Hex(0xE8D58A));
                }
                // streetlights at the corner: pole + glowing head
                var corner = ctr + new Vector3(BlockSize / 2 + 1.5f, 0, BlockSize / 2 + 1.5f);
                MeshKit.AddBox(v, c, t, n, corner + new Vector3(0, 3, 0), new Vector3(0.3f, 6, 0.3f), Palette.Hex(0x2A2A30), Palette.Hex(0x2A2A30));
                MeshKit.AddBox(v, c, t, n, corner + new Vector3(0, 6.1f, 0), new Vector3(1.2f, 0.4f, 1.2f), Palette.Hex(0xFFB45A), Palette.Hex(0xFFB45A), 1f);
            }
            // outskirts hills ring
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2;
                var p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (Bound + 10);
                float s = _rng.Range(14f, 26f);
                MeshKit.AddBox(v, c, t, n, p + new Vector3(0, s * 0.2f, 0), new Vector3(s, s * 0.5f, s), Palette.Hex(0x1C2A1E), Palette.Hex(0x0E1410));
            }
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "TownGround" };
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds();
            var go = new GameObject("TownGround"); go.transform.SetParent(transform);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = _groundMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void BuildBlock(int i, int j, BlockType type)
        {
            var ctr = new Vector2(i * Pitch, j * Pitch);
            float q = BlockSize / 4;
            switch (type)
            {
                case BlockType.Suburb:
                    for (int a = -1; a <= 1; a += 2)
                        for (int b = -1; b <= 1; b += 2)
                        {
                            var c = ctr + new Vector2(a, b) * q;
                            float w = _rng.Range(7f, 10f), d = _rng.Range(7f, 9.5f);
                            AddBuilding("house", c, w, d, _rng.Range(4.5f, 6.5f), HouseColor(), Palette.Hex(0x6A2E2E), true, 1.7f, 34, Dna.Flesh, 0.45f);
                            AddTree(c + new Vector2(a * 6.5f, -b * 6f));
                        }
                    break;
                case BlockType.Midtown:
                    AddBuilding("apartments", ctr + new Vector2(-q, 0), _rng.Range(12f, 15f), _rng.Range(20f, 26f), _rng.Range(11f, 16f), Palette.Hex(0x6E6A78), Palette.Hex(0x3A3844), false, 2.5f, 110, Dna.Flesh, 0.55f);
                    AddBuilding("shop", ctr + new Vector2(q, -q), _rng.Range(10f, 13f), _rng.Range(9f, 12f), _rng.Range(5f, 7f), Palette.Hex(0x7A5A4A), Palette.Hex(0x3A2E2A), false, 1.9f, 46, Dna.Flesh, 0.7f);
                    AddBuilding("shop", ctr + new Vector2(q, q), _rng.Range(10f, 13f), _rng.Range(9f, 12f), _rng.Range(5f, 8f), Palette.Hex(0x4A5A7A), Palette.Hex(0x2A2E3A), false, 1.9f, 46, Dna.Mineral, 0.7f);
                    break;
                case BlockType.Downtown:
                    AddBuilding("tower", ctr + new Vector2(-q * 0.9f, -q * 0.9f), _rng.Range(12f, 15f), _rng.Range(12f, 15f), _rng.Range(22f, 34f), Palette.Hex(0x50586E), Palette.Hex(0x2A2E3A), false, 3.3f, 240, Dna.Mineral, 0.5f);
                    AddBuilding("tower", ctr + new Vector2(q * 0.9f, q * 0.9f), _rng.Range(12f, 15f), _rng.Range(12f, 15f), _rng.Range(18f, 30f), Palette.Hex(0x5E5A6E), Palette.Hex(0x2E2A3A), false, 3.3f, 220, Dna.Mineral, 0.5f);
                    AddBuilding("shop", ctr + new Vector2(q, -q), 10, 10, 6, Palette.Hex(0x6A4A5A), Palette.Hex(0x2E2A30), false, 1.9f, 46, Dna.Flesh, 0.7f);
                    break;
                case BlockType.Plaza:
                    AddBuilding("hall", ctr + new Vector2(0, q * 1.2f), 18, 9, 10, Palette.Hex(0x8A7A6A), Palette.Hex(0x4A3A2E), true, 2.8f, 150, Dna.Mineral, 0.6f);
                    for (int k = 0; k < 6; k++) AddTree(ctr + new Vector2(_rng.Range(-14f, 14f), _rng.Range(-14f, -2f)));
                    break;
                case BlockType.Park:
                    for (int k = 0; k < 16; k++) AddTree(ctr + new Vector2(_rng.Range(-15f, 15f), _rng.Range(-15f, 15f)));
                    break;
                case BlockType.Industrial:
                    AddBuilding("factory", ctr + new Vector2(0, -q), 24, 13, 9, Palette.Hex(0x5A4A3A), Palette.Hex(0x3A3028), false, 2.6f, 150, Dna.Alloy, 0.35f);
                    for (int k = -1; k <= 1; k++)
                        AddBuilding("substation", ctr + new Vector2(k * 10, q * 1.1f), 6, 6, 4, Palette.Hex(0x4A5A6A), Palette.Hex(0x2A3A4A), false, 1.6f, 40, Dna.Volt, 0.9f);
                    break;
                case BlockType.Base:
                    AddBuilding("hangar", ctr + new Vector2(0, q), 22, 13, 8, Palette.Hex(0x4A5238), Palette.Hex(0x2E3424), false, 2.8f, 170, Dna.Alloy, 0.3f);
                    AddBuilding("bunker", ctr + new Vector2(-q, -q), 9, 7, 3.5f, Palette.Hex(0x56583E), Palette.Hex(0x34362A), false, 1.8f, 50, Dna.Alloy, 0.2f);
                    AddBuilding("bunker", ctr + new Vector2(q, -q), 9, 7, 3.5f, Palette.Hex(0x56583E), Palette.Hex(0x34362A), false, 1.8f, 50, Dna.Alloy, 0.2f);
                    break;
            }
        }

        Color HouseColor()
        {
            var set = new[] { 0x8A6A5A, 0x6A7A8A, 0x8A8A6A, 0x7A5A6A, 0x5A7A6A, 0x9A8A7A };
            return Palette.Hex((uint)set[_rng.Range(0, set.Length)]);
        }

        void AddBuilding(string kind, Vector2 c, float w, float d, float h, Color wall, Color roof, bool pitched, float req, float value, Dna dna, float lit)
        {
            var mesh = MeshKit.Building(w, d, h, wall, roof, pitched, Seed * 31 + _built++, lit);
            var go = new GameObject(kind); go.transform.SetParent(transform);
            go.transform.position = new Vector3(c.x, 0.3f, c.y);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = _bldMat;
            float hp = Mathf.Max(30, w * d * h * 0.14f);
            Buildings.Add(new Building { Kind = kind, T = go.transform, R = mr, C = c, Half = new Vector2(w, d) / 2, H = h, Hp = hp, MaxHp = hp, Req = req, Value = value, Dna = dna });
        }

        void AddTree(Vector2 c)
        {
            var go = new GameObject("tree"); go.transform.SetParent(transform);
            go.transform.position = new Vector3(c.x, 0.3f, c.y);
            var v = new List<Vector3>(); var col = new List<Color>(); var t = new List<int>(); var n = new List<Vector3>();
            float h = _rng.Range(3f, 5.5f);
            MeshKit.AddBox(v, col, t, n, new Vector3(0, h * 0.3f, 0), new Vector3(0.5f, h * 0.6f, 0.5f), Palette.Hex(0x4A3424), Palette.Hex(0x2A1E14));
            var leaf = Color.Lerp(Palette.Hex(0x2F6A3A), Palette.Of(Dna.Flora), _rng.Range(0f, 0.35f));
            MeshKit.AddBox(v, col, t, n, new Vector3(0, h * 0.75f, 0), new Vector3(h * 0.7f, h * 0.55f, h * 0.7f), leaf, leaf * 0.6f, _rng.Chance(0.2f) ? 0.35f : 0f);
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetColors(col); m.SetTriangles(t, 0); m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = _bldMat;
            Buildings.Add(new Building { Kind = "tree", T = go.transform, R = mr, C = c, Half = new Vector2(0.6f, 0.6f), H = h, Hp = 1, MaxHp = 1, Req = 0.6f, Value = 6, Dna = Dna.Flora, Tree = true });
        }

        void BuildLights()
        {
            int k = 0;
            foreach (var kv in _blocks)
            {
                if (((kv.Key.Item1 + kv.Key.Item2) & 1) != 0 || k++ > 9) continue;
                var go = new GameObject("StreetGlow"); go.transform.SetParent(transform);
                go.transform.position = new Vector3(kv.Key.Item1 * Pitch + Pitch / 2, 9, kv.Key.Item2 * Pitch + Pitch / 2);
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 34; l.intensity = 1.6f; l.color = Palette.Hex(0xFFB45A); l.shadows = LightShadows.None;
            }
            var basel = new GameObject("BaseSearchlight"); basel.transform.SetParent(transform);
            basel.transform.position = BaseCenter + Vector3.up * 12;
            var bl = basel.AddComponent<Light>(); bl.type = LightType.Point; bl.range = 40; bl.intensity = 2f; bl.color = Palette.Hex(0xBFD8FF);
        }

        // ------------------------------------------------------------------ collision

        static Vector2 Closest(Building b, Vector2 p) => new Vector2(Mathf.Clamp(p.x, b.C.x - b.Half.x, b.C.x + b.Half.x), Mathf.Clamp(p.y, b.C.y - b.Half.y, b.C.y + b.Half.y));

        public override Vector3 Resolve(Vector3 pos, float r)
        {
            pos = base.Resolve(pos, r);
            var p = new Vector2(pos.x, pos.z);
            foreach (var b in Buildings)
            {
                if (!b.Standing || b.Tree) continue;
                if (Mathf.Abs(p.x - b.C.x) > b.Half.x + r || Mathf.Abs(p.y - b.C.y) > b.Half.y + r) continue;
                var q = Closest(b, p);
                var d = p - q;
                float min = r * 0.75f;
                if (d.sqrMagnitude < min * min)
                {
                    if (d.sqrMagnitude < 1e-5f)
                    {
                        // centre is inside the footprint: push out along the shallowest axis
                        float dx = b.Half.x - Mathf.Abs(p.x - b.C.x), dy = b.Half.y - Mathf.Abs(p.y - b.C.y);
                        if (dx < dy) p.x = b.C.x + Mathf.Sign(p.x - b.C.x) * (b.Half.x + min); else p.y = b.C.y + Mathf.Sign(p.y - b.C.y) * (b.Half.y + min);
                    }
                    else p = q + d.normalized * min;
                }
            }
            return new Vector3(p.x, pos.y, p.y);
        }

        // ------------------------------------------------------------------ destruction

        public override void DamageStructures(Vector3 at, float radius, float dmg)
        {
            var p = new Vector2(at.x, at.z);
            foreach (var b in Buildings)
            {
                if (!b.Standing) continue;
                if ((Closest(b, p) - p).sqrMagnitude > radius * radius) continue;
                if (b.Tree) { Fell(b, false); continue; }
                Damage(b, dmg);
            }
        }

        public override bool HitStructure(Vector3 at, float r, float dmg)
        {
            var p = new Vector2(at.x, at.z);
            foreach (var b in Buildings)
            {
                if (!b.Standing || b.Tree || at.y > b.H + 1) continue;
                if (Mathf.Abs(p.x - b.C.x) > b.Half.x + r || Mathf.Abs(p.y - b.C.y) > b.Half.y + r) continue;
                if (dmg > 0) Damage(b, dmg);
                return true;
            }
            return false;
        }

        void Damage(Building b, float dmg)
        {
            if (!b.Standing) return;
            b.Hp -= dmg;
            float f = b.Hp / b.MaxHp;
            int stage = f > 0.66f ? 0 : f > 0.33f ? 1 : 2;
            if (stage > b.Stage)
            {
                b.Stage = stage;
                b.TiltAxis = new Vector3(_rng.Range(-1f, 1f), 0, _rng.Range(-1f, 1f)).normalized;
                b.Tilt = stage * 2.5f;
                Burst(b, 10 + stage * 8);
                Audio?.At("vent", b.T.position, 0.5f);
            }
            if (b.Hp <= 0) Collapse(b);
        }

        void Collapse(Building b)
        {
            b.CollapseT = 0;
            Burst(b, 26);
            float size = Mathf.Clamp01(b.H / 30f);
            Rig?.Shake(0.25f + size * 0.45f);
            Audio?.At("slam", b.T.position, 0.6f + size * 0.4f);
        }

        void Fell(Building b, bool eaten)
        {
            b.Stage = 3;
            b.T.gameObject.SetActive(false);
            if (eaten) Player.Consume(b.Value, b.Dna, b.T.position + Vector3.up * b.H * 0.5f, 0.6f);
            else Eco.SpawnMorsel(b.T.position + Vector3.up, b.Value, b.Dna);
            Fx.I.Burst(b.T.position + Vector3.up * b.H * 0.6f, Palette.Hdr(Palette.Of(Dna.Flora), 1.6f), 10, 3, 0.5f, 0.6f);
        }

        void Burst(Building b, int n)
        {
            var top = new Vector3(b.C.x, b.H * 0.7f, b.C.y);
            Fx.I.Burst(top, new Color(0.55f, 0.52f, 0.6f, 1f), n / 2, 3 + b.H * 0.15f, 1.2f + b.H * 0.05f, 1.6f, true);
            for (int i = 0; i < n / 3; i++) Throw(top + new Vector3(_rng.Range(-b.Half.x, b.Half.x), 0, _rng.Range(-b.Half.y, b.Half.y)), 0.4f + b.H * 0.03f);
        }

        void Throw(Vector3 at, float size)
        {
            Chunk ch = null;
            foreach (var c in _chunks) if (!c.t.gameObject.activeSelf) { ch = c; break; }
            if (ch == null)
            {
                if (_chunks.Count > 160) return;
                var go = new GameObject("chunk"); go.transform.SetParent(transform);
                go.AddComponent<MeshFilter>().sharedMesh = _chunkMesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = _bldMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ch = new Chunk { t = go.transform };
                _chunks.Add(ch);
            }
            ch.t.gameObject.SetActive(true);
            ch.t.position = at; ch.t.localScale = Vector3.one * size * _rng.Range(0.6f, 1.4f);
            ch.v = new Vector3(_rng.Range(-1f, 1f), _rng.Range(0.6f, 1.6f), _rng.Range(-1f, 1f)) * (6 + size * 4);
            ch.spin = new Vector3(_rng.Range(-400f, 400f), _rng.Range(-400f, 400f), _rng.Range(-400f, 400f));
            ch.life = 2.2f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0 || Player == null) return;
            var pp = Player.transform.position;
            var p2 = new Vector2(pp.x, pp.z);
            float pr = Player.Radius;
            var s = Player.Genome != null ? Player.Genome.Current : Stats.Base;
            _tooSmallToast -= dt;

            foreach (var b in Buildings)
            {
                if (b.CollapseT >= 0)
                {
                    b.CollapseT += dt;
                    float u = Mathf.Clamp01(b.CollapseT / 1.4f);
                    b.T.position = new Vector3(b.C.x, 0.3f - b.H * 0.95f * u * u, b.C.y);
                    b.T.rotation = Quaternion.AngleAxis(b.Tilt + u * 14, b.TiltAxis);
                    if (_rng.Chance(dt * 18)) Throw(new Vector3(b.C.x + _rng.Range(-b.Half.x, b.Half.x), b.H * (1 - u), b.C.y + _rng.Range(-b.Half.y, b.Half.y)), 0.5f + b.H * 0.03f);
                    if (_rng.Chance(dt * 10)) Fx.I.Burst(new Vector3(b.C.x, 0.5f, b.C.y), new Color(0.5f, 0.48f, 0.55f, 1), 4, 4, 2f + b.H * 0.06f, 1.8f, true);
                    if (u >= 1)
                    {
                        b.CollapseT = -1; b.Stage = 3;
                        b.T.gameObject.SetActive(false);
                        int n = Mathf.Clamp(2 + (int)(b.Value / 30), 2, 9);
                        for (int i = 0; i < n; i++)
                            Eco.SpawnMorsel(new Vector3(b.C.x + _rng.Range(-b.Half.x, b.Half.x) * 0.7f, 1, b.C.y + _rng.Range(-b.Half.y, b.Half.y) * 0.7f), b.Value / n, b.Dna);
                        Fx.I.Ring(new Vector3(b.C.x, 0.3f, b.C.y), Palette.Hdr(Palette.Warning, 2f), Mathf.Max(b.Half.x, b.Half.y), Mathf.Max(b.Half.x, b.Half.y) * 3, 0.8f);
                        OnDestroyed?.Invoke(b);
                    }
                    continue;
                }
                if (!b.Standing) continue;
                if (b.Tilt > 0) b.T.rotation = Quaternion.AngleAxis(b.Tilt + Mathf.Sin(Time.time * 30) * b.ChewFx * 0.6f, b.TiltAxis);
                b.ChewFx = Mathf.MoveTowards(b.ChewFx, 0, dt * 3);

                if (!Player.IsAlive || !Player.Controllable) continue;
                if (Mathf.Abs(p2.x - b.C.x) > b.Half.x + pr + 0.5f || Mathf.Abs(p2.y - b.C.y) > b.Half.y + pr + 0.5f) continue;
                float dist = (Closest(b, p2) - p2).magnitude;
                if (dist > pr * 0.95f + 0.3f) continue;
                if (pr < b.Req)
                {
                    if (_tooSmallToast <= 0) { _tooSmallToast = 4f; Toast?.Invoke(b.Kind); }
                    continue;
                }
                if (b.Tree) { Fell(b, true); continue; }
                float dps = 26f * s.Bite * (1 + s.Demolish) * Mathf.Pow(pr, 1.2f);
                Damage(b, dps * dt);
                b.ChewFx = 1;
                if (_rng.Chance(dt * 8)) { Throw(new Vector3(Closest(b, p2).x, Mathf.Min(b.H, pr * 2), Closest(b, p2).y), 0.3f + pr * 0.1f); Player.Body.Swallow(0.15f); }
                if (_rng.Chance(dt * 4)) Audio?.Gulp(0.9f, true);
            }

            foreach (var c in _chunks)
            {
                if (!c.t.gameObject.activeSelf) continue;
                c.life -= dt;
                c.v.y -= 22 * dt;
                var np = c.t.position + c.v * dt;
                if (np.y < 0.3f) { np.y = 0.3f; c.v.y *= -0.35f; c.v.x *= 0.6f; c.v.z *= 0.6f; c.spin *= 0.5f; }
                c.t.position = np;
                c.t.Rotate(c.spin * dt);
                if (c.life < 0.4f) c.t.localScale *= 1 - dt * 3;
                if (c.life <= 0) c.t.gameObject.SetActive(false);
            }
        }

        /// <summary>Raised when the player bumps something it's too small to break (UI shows a hint).</summary>
        public Action<string> Toast;

        public int Remaining { get { int n = 0; foreach (var b in Buildings) if (b.Standing && !b.Tree) n++; return n; } }

        public override void Clear()
        {
            base.Clear();
            Buildings.Clear(); _chunks.Clear();
        }
    }
}
