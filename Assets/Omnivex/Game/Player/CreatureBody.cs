using System.Collections.Generic;
using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>
    /// The player's visible body. Rebuilt from the genome every time it mutates: each mutation grows real parts
    /// (tentacles, plates, spines, siphons, a lure, eyes, a volt organ, acid sacs, cilia, a gravity halo, a crown).
    /// Animated procedurally: squash and stretch, swallow bulge, chained tentacles, idle breathing.
    /// </summary>
    public sealed class CreatureBody : MonoBehaviour
    {
        sealed class Chain
        {
            public Transform Anchor; public Transform[] Segs; public float SegLen, Phase, Amp;
        }

        Transform _core, _nucleus, _mouthRing, _parts, _halo;
        Material _skin, _accent, _glowMat, _haloMat;
        readonly List<Chain> _chains = new List<Chain>();
        readonly List<(Transform t, Vector3 baseScale, float born)> _grow = new List<(Transform, Vector3, float)>();
        readonly Dictionary<string, List<Transform>> _byPart = new Dictionary<string, List<Transform>>();
        Mesh _sphere, _cone, _fin;
        SkinDef _skinDef;
        float _bulge, _hitBristle, _molt, _t, _charge;
        public Vector3 Velocity;
        public float Speed01;
        public float ChargeVolt { set => _charge = value; }

        public void Init(SkinDef skin)
        {
            _sphere = MeshKit.Blob(24, 16, Color.white, new Color(0.5f, 0.55f, 0.6f));
            _cone = MeshKit.Cone(8, new Color(0.75f, 0.75f, 0.8f), Color.white);
            _fin = MeshKit.Cone(5, new Color(0.7f, 0.7f, 0.7f), Color.white, 0.1f);
            _parts = new GameObject("Mutations").transform; _parts.SetParent(transform, false);
            ApplySkin(skin);
        }

        public void ApplySkin(SkinDef skin)
        {
            _skinDef = skin;
            var body = Palette.Hex(skin.Body); var glow = Palette.Hex(skin.Glow); var acc = Palette.Hex(skin.Accent);
            _skin ??= Mats.BioUnique("player_skin", glow, 1f, glow, 0.6f, 0.05f);
            _skin.SetColor("_BaseColor", body);
            _skin.SetColor("_RimColor", Palette.Hdr(glow, 1.4f)); _skin.SetFloat("_Rim", 1.1f);
            _skin.SetColor("_GlowColor", Palette.Hdr(glow, 3.2f)); _skin.SetFloat("_VeinAmount", 0.5f); _skin.SetFloat("_VeinScale", 3.2f);
            _skin.SetFloat("_Gloss", 0.7f); _skin.SetFloat("_Pulse", 3f);
            _accent ??= Mats.BioUnique("player_accent", acc, 1f, acc, 0.5f, 0.02f);
            _accent.SetColor("_BaseColor", acc); _accent.SetColor("_RimColor", Palette.Hdr(acc, 1.2f)); _accent.SetColor("_GlowColor", Palette.Hdr(acc, 2.5f));
            _glowMat ??= Mats.BioUnique("player_glow", glow, 0.5f, glow, 0, 0.08f);
            _glowMat.SetColor("_BaseColor", Color.black); _glowMat.SetColor("_EmissionColor", Palette.Hdr(glow, 4f));
            _haloMat ??= Mats.Aura(Palette.Hdr(glow, 1.5f));
            _haloMat.SetColor("_Color", Palette.Hdr(glow, 1.5f));

            if (_core == null)
            {
                _core = Make("Core", transform, _sphere, _skin, Vector3.zero, Vector3.zero, Vector3.one);
                _nucleus = Make("Nucleus", _core, _sphere, _glowMat, new Vector3(0, 0.08f, -0.05f), Vector3.zero, Vector3.one * 0.38f);
                _halo = Make("Halo", transform, _sphere, _haloMat, Vector3.zero, Vector3.zero, Vector3.one * 1.18f);
                _mouthRing = new GameObject("Mouth").transform; _mouthRing.SetParent(_core, false);
                _mouthRing.localPosition = new Vector3(0, 0, 0.42f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i / 8f * Mathf.PI * 2;
                    Make("tooth", _mouthRing, _cone, _accent, new Vector3(Mathf.Cos(a) * 0.17f, Mathf.Sin(a) * 0.13f, 0), new Vector3(0, 0, 0) + Quaternion.LookRotation(new Vector3(-Mathf.Cos(a) * 0.6f, -Mathf.Sin(a) * 0.6f, 1)).eulerAngles, new Vector3(0.07f, 0.07f, 0.16f));
                }
                Make("eye", _core, _sphere, _glowMat, new Vector3(0.16f, 0.28f, 0.3f), Vector3.zero, Vector3.one * 0.11f);
                Make("eye", _core, _sphere, _glowMat, new Vector3(-0.16f, 0.28f, 0.3f), Vector3.zero, Vector3.one * 0.11f);
            }
        }

        Transform Make(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 euler, Vector3 scale, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localEulerAngles = euler; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        Transform Grow(string part, Transform t)
        {
            _grow.Add((t, t.localScale, _t));
            t.localScale = Vector3.zero;
            if (!_byPart.TryGetValue(part, out var l)) _byPart[part] = l = new List<Transform>();
            l.Add(t);
            return t;
        }

        int Count(string part) => _byPart.TryGetValue(part, out var l) ? l.Count : 0;

        /// <summary>Adds parts for any mutation levels not yet represented. Existing parts stay, so growth is cumulative.</summary>
        public void Sync(Genome g, bool animate)
        {
            foreach (var kv in g.Levels)
            {
                var def = MutationCatalog.Get(kv.Key);
                int want = kv.Value;
                int have = _haveLevels.TryGetValue(kv.Key, out var h) ? h : 0;
                for (int lvl = have + 1; lvl <= want; lvl++) AddPart(def.Part, lvl);
                _haveLevels[kv.Key] = want;
            }
            if (!animate) { foreach (var x in _grow) x.t.localScale = x.baseScale; _grow.Clear(); }
            else _molt = 1;
        }
        readonly Dictionary<string, int> _haveLevels = new Dictionary<string, int>();

        public void Clear()
        {
            foreach (Transform c in _parts) Destroy(c.gameObject);
            foreach (var ch in _chains) foreach (var s in ch.Segs) if (s != null) Destroy(s.gameObject);
            _chains.Clear(); _grow.Clear(); _byPart.Clear(); _haveLevels.Clear();
        }

        void AddPart(string part, int level)
        {
            var acc = _accent; var skin = _skin;
            switch (part)
            {
                case "tentacles":
                {
                    int per = level == 1 ? 4 : 2;
                    for (int i = 0; i < per; i++)
                    {
                        float a = (Count("tentacles") * 47 + i * (360f / per)) * Mathf.Deg2Rad;
                        var anchor = new GameObject("tentacle").transform; anchor.SetParent(_parts, false);
                        anchor.localPosition = new Vector3(Mathf.Cos(a) * 0.38f, -0.12f, Mathf.Sin(a) * 0.3f + 0.05f);
                        Grow("tentacles", anchor);
                        var segs = new Transform[7];
                        for (int s = 0; s < segs.Length; s++)
                        {
                            float sc = 0.17f * (1 - s / 9f) * (1 + level * 0.12f);
                            segs[s] = Make("seg", null, _sphere, s % 2 == 0 ? skin : acc, Vector3.zero, Vector3.zero, Vector3.one * sc, false);
                            segs[s].position = anchor.position;
                        }
                        _chains.Add(new Chain { Anchor = anchor, Segs = segs, SegLen = 0.13f * (1 + level * 0.15f), Phase = a * 3, Amp = 0.6f });
                    }
                    break;
                }
                case "spines":
                    for (int i = 0; i < 7 + level * 3; i++)
                    {
                        var dir = Fib(i + level * 13, 40);
                        if (dir.y < -0.3f) continue;
                        var t = Make("spine", _parts, _cone, acc, dir * 0.45f, Quaternion.LookRotation(dir).eulerAngles, new Vector3(0.1f, 0.1f, 0.32f + level * 0.07f));
                        Grow("spines", t);
                    }
                    break;
                case "plates":
                    for (int i = 0; i < 5; i++)
                    {
                        float a = (i - 2) * 26 + level * 9;
                        var t = Make("plate", _parts, _sphere, acc, Quaternion.Euler(0, a, 0) * new Vector3(0, 0.3f + level * 0.03f, -0.05f - (level - 1) * 0.12f),
                            new Vector3(-20 - level * 6, a, 0), new Vector3(0.48f, 0.12f, 0.36f));
                        Grow("plates", t);
                    }
                    break;
                case "siphons":
                    for (int i = -1; i <= 1; i += 2)
                    {
                        var t = Make("siphon", _parts, _cone, skin, new Vector3(i * (0.2f + level * 0.05f), -0.02f, -0.42f), new Vector3(0, 180 + i * 18, 0), new Vector3(0.22f, 0.22f, 0.3f + level * 0.06f));
                        Make("jet", t, _sphere, _glowMat, new Vector3(0, 0, 1.05f), Vector3.zero, Vector3.one * 0.55f, false);
                        Grow("siphons", t);
                    }
                    break;
                case "lure":
                {
                    var stalk = Make("lureStalk", _parts, _cone, skin, new Vector3(0, 0.38f, 0.15f), new Vector3(-60 - level * 5, 0, 0), new Vector3(0.06f, 0.06f, 0.55f + level * 0.15f));
                    var bulb = Make("lureBulb", stalk, _sphere, _glowMat, new Vector3(0, 0, 1.05f), Vector3.zero, new Vector3(3.2f, 3.2f, 0.55f) * (1 + level * 0.15f), false);
                    var l = bulb.gameObject.AddComponent<Light>(); l.type = LightType.Point; l.color = Palette.Hex(_skinDef.Glow); l.range = 4 + level; l.intensity = 2.5f; l.shadows = LightShadows.None;
                    Grow("lure", stalk);
                    break;
                }
                case "eyes":
                    for (int i = -1; i <= 1; i += 2)
                    {
                        var stalk = Make("eyeStalk", _parts, _cone, skin, new Vector3(i * 0.18f, 0.3f, 0.2f), new Vector3(-55, i * 25, 0), new Vector3(0.07f, 0.07f, 0.38f + level * 0.08f));
                        Make("eyeBall", stalk, _sphere, _glowMat, new Vector3(0, 0, 1.05f), Vector3.zero, new Vector3(2.4f, 2.4f, 0.42f), false);
                        Grow("eyes", stalk);
                    }
                    break;
                case "maw":
                {
                    var ring = new GameObject("maw").transform; ring.SetParent(_parts, false);
                    ring.localPosition = new Vector3(0, 0, 0.44f + level * 0.02f);
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i / 12f * Mathf.PI * 2;
                        Make("fang", ring, _cone, acc, new Vector3(Mathf.Cos(a) * 0.24f, Mathf.Sin(a) * 0.2f, 0), Quaternion.LookRotation(new Vector3(-Mathf.Cos(a), -Mathf.Sin(a), 0.6f)).eulerAngles, new Vector3(0.06f, 0.06f, 0.2f + level * 0.04f));
                    }
                    Grow("maw", ring);
                    break;
                }
                case "bulk":
                {
                    var t = Make("bulk", _parts, _sphere, skin, new Vector3(0, 0.05f, -0.22f), Vector3.zero, new Vector3(0.82f, 0.72f, 0.7f) * (1 + level * 0.08f));
                    Grow("bulk", t);
                    break;
                }
                case "acid":
                    for (int i = -1; i <= 1; i += 2)
                    {
                        var sac = MakeGlowSac(new Vector3(i * 0.36f, 0.08f, -0.12f + level * 0.05f), Palette.Of(Dna.Toxin), 0.22f + level * 0.04f);
                        Grow("acid", sac);
                    }
                    break;
                case "membrane":
                {
                    var t = Make("membrane", _parts, _sphere, _haloMat, Vector3.zero, Vector3.zero, Vector3.one * (1.08f + level * 0.04f), false);
                    Grow("membrane", t);
                    for (int i = 0; i < 3; i++) Grow("membrane", Make("frill", _parts, _fin, acc, new Vector3(0, 0.12f, -0.35f), new Vector3(-30, 180 + (i - 1) * 40, 0), new Vector3(0.4f, 0.04f, 0.3f)));
                    break;
                }
                case "volt":
                {
                    var organ = MakeGlowSac(new Vector3(0, 0.36f, -0.18f), Palette.Of(Dna.Volt), 0.26f + level * 0.04f);
                    for (int i = 0; i < 3; i++) Make("coil", organ, _cone, acc, Vector3.zero, new Vector3(-60, i * 120, 0), new Vector3(0.25f, 0.25f, 1.2f));
                    Grow("volt", organ);
                    break;
                }
                case "cilia":
                {
                    var ring = new GameObject("cilia").transform; ring.SetParent(_parts, false);
                    for (int i = 0; i < 26; i++)
                    {
                        float a = i / 26f * Mathf.PI * 2;
                        Make("cilium", ring, _cone, acc, new Vector3(Mathf.Cos(a) * 0.48f, -0.05f, Mathf.Sin(a) * 0.48f), new Vector3(10, -a * Mathf.Rad2Deg + 90, 0), new Vector3(0.03f, 0.03f, 0.18f + level * 0.04f), false);
                    }
                    Grow("cilia", ring);
                    break;
                }
                case "spores":
                    for (int i = 0; i < 4 + level * 2; i++)
                    {
                        var dir = Fib(i * 3 + 5, 30); if (dir.y < 0) dir.y = -dir.y;
                        Grow("spores", MakeGlowSac(dir * 0.46f, Palette.Of(Dna.Toxin), 0.1f));
                    }
                    break;
                case "gravity":
                {
                    var ring = Make("gravityRing", _parts, MeshKit.QuadXZ(), Mats.Glow(Palette.Hdr(Palette.Of(Dna.Void), 2.5f), 1, 0.08f), new Vector3(0, 0.05f, 0), Vector3.zero, Vector3.one * (2.6f + level * 0.6f), false);
                    var orb = MakeGlowSac(new Vector3(0, 0.52f, 0), Palette.Of(Dna.Void), 0.2f);
                    Grow("gravity", ring); Grow("gravity", orb);
                    break;
                }
                case "crown":
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i / 7f * Mathf.PI * 2;
                        Grow("crown", Make("crownHorn", _parts, _cone, _glowMat, new Vector3(Mathf.Cos(a) * 0.26f, 0.4f, Mathf.Sin(a) * 0.26f - 0.05f), new Vector3(-70, -a * Mathf.Rad2Deg + 90, 0), new Vector3(0.08f, 0.08f, 0.42f)));
                    }
                    break;
            }
        }

        Transform MakeGlowSac(Vector3 pos, Color c, float size)
        {
            var mat = Mats.Bio("sac_" + c, Palette.Hdr(c, 1.2f), 1.2f, Palette.Hdr(c, 3f), 0.9f, 0.08f, 0.9f, 5f, 8f);
            mat.SetColor("_EmissionColor", Palette.Hdr(c, 0.6f));
            return Make("sac", _parts, _sphere, mat, pos, Vector3.zero, Vector3.one * size, false);
        }

        static Vector3 Fib(int i, int n)
        {
            float y = 1 - (i % n + 0.5f) / n * 2;
            float r = Mathf.Sqrt(1 - y * y);
            float a = i * 2.39996f;
            return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }

        public void Swallow(float amount) => _bulge = Mathf.Min(1, _bulge + amount);
        public void Hit() => _hitBristle = 1;

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            _t += dt;
            // grow-in animation for new parts (elastic overshoot)
            for (int i = _grow.Count - 1; i >= 0; i--)
            {
                var (t, s, born) = _grow[i];
                if (t == null) { _grow.RemoveAt(i); continue; }
                float u = Mathf.Clamp01((_t - born) / 0.7f);
                float e = 1 + Mathf.Sin(u * Mathf.PI * 2.5f) * (1 - u) * 0.5f;
                t.localScale = s * Mathf.SmoothStep(0, 1, u) * e;
                if (u >= 1) { t.localScale = s; _grow.RemoveAt(i); }
            }

            _bulge = Mathf.MoveTowards(_bulge, 0, dt * 2.2f);
            _hitBristle = Mathf.MoveTowards(_hitBristle, 0, dt * 3);
            _molt = Mathf.MoveTowards(_molt, 0, dt * 0.8f);

            // squash & stretch along motion, breathing at rest
            float breathe = Mathf.Sin(_t * 2.4f) * 0.035f;
            float stretch = Speed01 * 0.16f;
            float bulge = Mathf.Sin(_bulge * Mathf.PI) * 0.18f;
            _core.localScale = new Vector3(1 - stretch * 0.5f + breathe + bulge, 1 - stretch * 0.4f - breathe + bulge * 0.6f, 1 + stretch + bulge * 0.4f);
            _nucleus.localScale = Vector3.one * (0.38f + Mathf.Sin(_t * 3.1f) * 0.03f + _molt * 0.2f);
            _halo.localScale = Vector3.one * (1.16f + _molt * 0.5f + Mathf.Sin(_t * 1.7f) * 0.02f);
            _mouthRing.localScale = Vector3.one * (1 + _bulge * 0.6f);
            _skin.SetFloat("_VeinAmount", 0.45f + _molt * 0.55f + _charge * 0.4f);
            _skin.SetFloat("_Flash", _hitBristle * 0.55f);

            if (_byPart.TryGetValue("spines", out var spines))
                foreach (var s in spines) if (s != null) s.localPosition = s.localPosition.normalized * (0.45f + _hitBristle * 0.08f);
            if (_byPart.TryGetValue("cilia", out var cilia))
                foreach (var c in cilia) if (c != null) c.localRotation = Quaternion.Euler(Mathf.Sin(_t * 18) * 6, _t * 30, 0);
            if (_byPart.TryGetValue("gravity", out var grav))
                foreach (var g in grav) if (g != null && g.name == "gravityRing") g.localRotation = Quaternion.Euler(0, _t * 90, 0);
            if (_byPart.TryGetValue("siphons", out var sip))
                foreach (var s in sip) if (s != null && s.childCount > 0) s.GetChild(0).localScale = Vector3.one * (0.4f + Speed01 * 0.5f + Mathf.Sin(_t * 30) * 0.05f);
            if (_byPart.TryGetValue("volt", out var volt))
                foreach (var v in volt) if (v != null) v.localRotation = Quaternion.Euler(0, _t * (60 + _charge * 400), 0);

            // tentacles: each segment trails its parent with a travelling wave
            var back = -transform.forward;
            var side = transform.right;
            float scale = transform.lossyScale.x;
            foreach (var ch in _chains)
            {
                if (ch.Anchor == null) continue;
                var prev = ch.Anchor.position;
                bool hidden = ch.Anchor.localScale.x < 0.05f;
                for (int i = 0; i < ch.Segs.Length; i++)
                {
                    var s = ch.Segs[i];
                    s.gameObject.SetActive(!hidden);
                    float len = ch.SegLen * scale;
                    var dir = (s.position - prev);
                    if (dir.sqrMagnitude < 1e-6f) dir = back;
                    dir = Vector3.Slerp(dir.normalized, (back + (ch.Anchor.position - transform.position).normalized).normalized, dt * 3f);
                    var wave = side * Mathf.Sin(_t * 6 + ch.Phase - i * 0.8f) * ch.Amp * len * (0.4f + Speed01);
                    var want = prev + dir.normalized * len + wave * 0.5f;
                    s.position = want;
                    s.localScale = Vector3.one * scale * 0.17f * (1 - i / 9f);
                    prev = want;
                }
            }
        }

        void OnDestroy()
        {
            foreach (var ch in _chains) foreach (var s in ch.Segs) if (s != null) Destroy(s.gameObject);
        }
    }
}
