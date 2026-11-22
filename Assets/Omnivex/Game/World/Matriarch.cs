using System;
using System.Collections.Generic;
using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>
    /// THE HOLLOW MATRIARCH — Stage 1 boss. A colossal bell-jelly that nests in the central trench.
    /// Readable, skill-checked patterns: spore volleys with gaps to slip through, tentacle slams with filling telegraphs,
    /// brood summons. After every slam she's exhausted and her core opens - only then can you bite her.
    /// Phase two (below half health) adds a rotating spore spiral and quickens everything.
    /// </summary>
    public sealed class Matriarch : MonoBehaviour
    {
        public PlayerCreature Player; public Ecosystem Eco; public CameraRig Rig; public AudioDirector Audio; public Arena Arena;
        public float Hp, MaxHp = 1100;
        public float Radius = 5f;
        public bool Active { get; private set; }
        public bool Exposed => _state == S.Exhausted;
        public int Phase => Hp < MaxHp * 0.5f ? 2 : 1;
        public event Action OnDefeated;
        public event Action OnIntroDone;
        public string Title => "THE HOLLOW MATRIARCH";

        enum S { Intro, Idle, VolleyTele, Volley, SlamTele, Slam, Exhausted, BroodTele, Spiral, Dying }
        S _state;
        float _t, _stateT, _biteCd, _flash, _sinkY;
        int _cycle;
        Vector3 _pos;
        Transform _bell, _core, _crown;
        Material _bellMat, _coreMat;
        readonly List<Transform[]> _tentacles = new List<Transform[]>();
        readonly List<(Transform t, MeshRenderer r, Vector3 p)> _slams = new List<(Transform, MeshRenderer, Vector3)>();
        MaterialPropertyBlock _mpb;
        Light _light;

        public void Build()
        {
            _mpb = new MaterialPropertyBlock();
            var sphere = MeshKit.Blob(32, 20, Color.white, new Color(0.35f, 0.3f, 0.45f), 0.25f, 11);
            var cone = MeshKit.Cone(8, new Color(0.6f, 0.6f, 0.7f), Color.white);
            var voidC = Palette.Of(Dna.Void);
            _bellMat = Mats.BioUnique("matriarch_bell", Palette.Hdr(voidC, 1.4f), 1.2f, Palette.Hdr(Palette.Hex(0xFF6AD5), 3f), 0.8f, 0.03f);
            _bellMat.SetColor("_BaseColor", Palette.Hex(0x3A1E5A)); _bellMat.SetFloat("_VeinScale", 2.2f); _bellMat.SetFloat("_Pulse", 1.4f); _bellMat.SetFloat("_Gloss", 0.8f);
            _coreMat = Mats.BioUnique("matriarch_core", Palette.Hdr(Palette.Accent, 2f), 2f, Palette.Hdr(Palette.Accent, 4f), 1f, 0.1f);
            _coreMat.SetColor("_BaseColor", Palette.Hex(0x0A2E2A));

            _bell = Part("Bell", transform, sphere, _bellMat, new Vector3(0, 0.25f, 0), new Vector3(1, 0.62f, 1));
            Part("BellRim", transform, sphere, _bellMat, new Vector3(0, -0.02f, 0), new Vector3(1.12f, 0.16f, 1.12f));
            _core = Part("Core", transform, sphere, _coreMat, new Vector3(0, 0.05f, 0), Vector3.one * 0.42f);
            _crown = new GameObject("Crown").transform; _crown.SetParent(transform, false);
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2;
                Part("spike", _crown, cone, _bellMat, new Vector3(Mathf.Cos(a) * 0.32f, 0.5f, Mathf.Sin(a) * 0.32f), new Vector3(0.1f, 0.1f, 0.5f), Quaternion.LookRotation(new Vector3(Mathf.Cos(a) * 0.5f, 1, Mathf.Sin(a) * 0.5f)).eulerAngles);
            }
            var tentMat = Mats.Bio("matriarch_tent", Palette.Hdr(voidC, 1.2f), 1f, Palette.Hdr(Palette.Hex(0xFF6AD5), 2.5f), 0.7f, 0.06f);
            for (int i = 0; i < 12; i++)
            {
                var segs = new Transform[9];
                for (int s = 0; s < segs.Length; s++)
                    segs[s] = Part("tent", transform, sphere, tentMat, Vector3.zero, Vector3.one * (0.13f - s * 0.011f));
                _tentacles.Add(segs);
            }
            var lg = new GameObject("MatriarchLight"); lg.transform.SetParent(transform, false); lg.transform.localPosition = new Vector3(0, 0.2f, 0);
            _light = lg.AddComponent<Light>(); _light.type = LightType.Point; _light.color = Palette.Hex(0xFF6AD5); _light.range = 2.5f; _light.intensity = 3;
            gameObject.SetActive(false);
        }

        Transform Part(string n, Transform parent, Mesh m, Material mat, Vector3 p, Vector3 s, Vector3 euler = default)
        {
            var go = new GameObject(n); go.transform.SetParent(parent, false);
            go.transform.localPosition = p; go.transform.localScale = s; go.transform.localEulerAngles = euler;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            return go.transform;
        }

        public void Summon(float playerRadius)
        {
            Radius = Mathf.Max(4.5f, playerRadius * 3.2f);
            MaxHp = 1100; Hp = MaxHp;
            _bellMat.SetFloat("_Dissolve", 0); _coreMat.SetFloat("_Dissolve", 0);
            _pos = new Vector3(0, Arena.HeightAt(0, 0) - Radius, 0);
            transform.position = _pos; transform.localScale = Vector3.one * Radius * 2;
            gameObject.SetActive(true);
            Active = true;
            _state = S.Intro; _stateT = 0; _cycle = 0; _sinkY = 0;
            Audio?.Play("roar", 1f, 0.8f);
            Rig.Shake(0.6f);
        }

        public void Despawn()
        {
            Active = false;
            foreach (var s in _slams) if (s.t != null) Destroy(s.t.gameObject);
            _slams.Clear();
            gameObject.SetActive(false);
        }

        void Go(S s) { _state = s; _stateT = 0; }

        void Update()
        {
            if (!Active) return;
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            _t += dt; _stateT += dt; _biteCd -= dt; _flash = Mathf.MoveTowards(_flash, 0, dt * 4);
            var pp = Player.transform.position;
            float speedMul = Phase == 2 ? 1.3f : 1f;
            float hover = Arena.HeightAt(0, 0) + Radius * 0.55f;

            switch (_state)
            {
                case S.Intro:
                {
                    float u = Mathf.Clamp01(_stateT / 3.2f);
                    _pos.y = Mathf.Lerp(Arena.HeightAt(0, 0) - Radius, hover, Mathf.SmoothStep(0, 1, u));
                    Rig.Cinematic(new Vector3(0, hover, 0), Radius * 5.5f, 1, 40);
                    if (_stateT > 0.6f && _stateT < 2.8f) Rig.Shake(dt * 0.7f);
                    if (_stateT > 3.6f) { Rig.Cinematic(_pos, Radius * 5, 0); Go(S.Idle); OnIntroDone?.Invoke(); }
                    break;
                }
                case S.Idle:
                    Drift(pp, dt, 1.6f * speedMul);
                    if (_stateT > 1.6f / speedMul)
                    {
                        _cycle++;
                        int pick = _cycle % (Phase == 2 ? 4 : 3);
                        Go(pick == 0 ? S.BroodTele : pick == 1 ? S.VolleyTele : pick == 3 ? S.Spiral : S.SlamTele);
                        if (_state == S.SlamTele) PlaceSlams(pp);
                        if (_state == S.VolleyTele || _state == S.BroodTele) Audio?.Play("charge", 0.8f, _state == S.BroodTele ? 0.7f : 1f);
                    }
                    break;
                case S.VolleyTele:
                    Drift(pp, dt, 0.4f);
                    _flash = 0.25f + Mathf.Abs(Mathf.Sin(_stateT * 14)) * 0.35f;
                    if (_stateT > 1.0f / speedMul) Go(S.Volley);
                    break;
                case S.Volley:
                {
                    int rings = Phase == 2 ? 3 : 2;
                    int ring = (int)(_stateT / 0.55f);
                    if (ring < rings && _stateT - ring * 0.55f < dt * 1.01f) FireRing(ring);
                    if (_stateT > rings * 0.55f + 0.6f) Go(S.Idle);
                    break;
                }
                case S.SlamTele:
                {
                    float fill = Mathf.Clamp01(_stateT / (1.35f / speedMul));
                    foreach (var s in _slams) { _mpb.SetFloat("_Fill", fill); _mpb.SetColor("_Color", Palette.Hdr(Palette.Danger, 1 + fill * 2)); s.r.SetPropertyBlock(_mpb); }
                    _sinkY = Mathf.Lerp(_sinkY, -Radius * 0.1f, dt * 3);
                    if (fill >= 1) Go(S.Slam);
                    break;
                }
                case S.Slam:
                {
                    float slamR = Radius * 0.55f;
                    foreach (var s in _slams)
                    {
                        Fx.I.Ring(s.p, Palette.Hdr(Palette.Danger, 3f), slamR * 0.3f, slamR * 1.4f, 0.5f);
                        Fx.I.Burst(s.p + Vector3.up, Palette.Hdr(Palette.Hex(0xFF6AD5), 2.5f), 18, 8, 0.6f, 0.7f);
                        if ((new Vector3(pp.x, 0, pp.z) - new Vector3(s.p.x, 0, s.p.z)).magnitude < slamR + Player.Radius * 0.6f) Player.TakeDamage(32, s.p);
                        Eco.ForEachNear(s.p, slamR + 1, (c, d) => { if (c.Kind.Brain != Brain.Morsel) Eco.Damage(c, 80, false); });
                        Destroy(s.t.gameObject);
                    }
                    _slams.Clear();
                    Rig.Shake(0.55f);
                    Audio?.Play("slam", 1f, 0.9f);
                    Go(S.Exhausted);
                    break;
                }
                case S.Exhausted:
                    _sinkY = Mathf.Lerp(_sinkY, -Radius * 0.35f, dt * 4);
                    if (_stateT > 3.4f / Mathf.Sqrt(speedMul)) { Go(S.Idle); }
                    break;
                case S.BroodTele:
                    _flash = Mathf.Abs(Mathf.Sin(_stateT * 8)) * 0.3f;
                    if (_stateT > 0.9f)
                    {
                        int n = Phase == 2 ? 4 : 3;
                        for (int i = 0; i < n; i++)
                        {
                            float a = i / (float)n * Mathf.PI * 2 + _t;
                            var at = _pos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Radius * 0.9f;
                            var c = Eco.Spawn(Kinds.Brood, at, Player.Radius * 0.85f);
                            c.Vel = (at - _pos).normalized * 8;
                            Fx.I.Burst(at, Palette.Hdr(Palette.Of(Dna.Void), 2.5f), 12, 4, 0.5f, 0.6f, true);
                        }
                        Go(S.Idle);
                    }
                    break;
                case S.Spiral:
                {
                    Drift(pp, dt, 0.3f);
                    if (_stateT < 3.2f && ((int)(_stateT * 12) != (int)((_stateT - dt) * 12)))
                    {
                        for (int arm = 0; arm < 3; arm++)
                        {
                            float a = _stateT * 2.1f + arm * Mathf.PI * 2 / 3;
                            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                            Projectiles.I.Spore(_pos + dir * Radius * 0.6f, dir, 9f, 12, 0.35f + Player.Radius * 0.12f);
                        }
                    }
                    if (_stateT > 3.8f) Go(S.Idle);
                    break;
                }
                case S.Dying:
                {
                    float u = _stateT / 2.6f;
                    Time.timeScale = Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(u * 1.2f));
                    if ((int)(_stateT * 6) != (int)((_stateT - dt) * 6))
                        Fx.I.Burst(_pos + UnityEngine.Random.insideUnitSphere * Radius * 0.7f, Palette.Hdr(Palette.Hex(0xFF6AD5), 4f), 20, 10, 0.8f, 0.8f);
                    _bellMat.SetFloat("_Dissolve", Mathf.Clamp01(u));
                    _coreMat.SetFloat("_Dissolve", Mathf.Clamp01(u * 1.1f));
                    Rig.Cinematic(_pos, Radius * 4.2f, 1, 50);
                    Rig.Shake(dt * 0.6f);
                    if (u >= 1)
                    {
                        Time.timeScale = 1;
                        Fx.I.Ring(_pos, Palette.Hdr(Palette.Accent, 4f), Radius, Radius * 9, 1.4f, 0.06f);
                        Fx.I.Burst(_pos, Palette.Hdr(Palette.Accent, 4f), 80, 16, 1.2f, 1.6f);
                        for (int i = 0; i < 14; i++) Eco.SpawnMorsel(_pos + UnityEngine.Random.insideUnitSphere * Radius, 9f, Dna.Void);
                        Rig.Cinematic(_pos, Radius * 4, 0);
                        Despawn();
                        OnDefeated?.Invoke();
                    }
                    break;
                }
            }

            // body animation
            float pulse = Mathf.Sin(_t * 1.6f);
            transform.position = new Vector3(_pos.x, _state == S.Intro || _state == S.Dying ? _pos.y : hover + _sinkY + pulse * Radius * 0.04f, _pos.z);
            if (_state != S.Intro) _pos.y = transform.position.y;
            _bell.localScale = new Vector3(1 + pulse * 0.04f, 0.62f - pulse * 0.05f, 1 + pulse * 0.04f);
            _crown.localRotation = Quaternion.Euler(0, _t * 12, 0);
            float exposed = _state == S.Exhausted ? 1 : 0;
            _core.localPosition = Vector3.Lerp(_core.localPosition, new Vector3(0, exposed > 0 ? 0.32f : 0.05f, exposed > 0 ? 0.0f : 0), dt * 4);
            _core.localScale = Vector3.one * (0.42f + exposed * 0.12f + Mathf.Sin(_t * 9) * 0.02f * exposed);
            _coreMat.SetColor("_EmissionColor", Palette.Hdr(Palette.Accent, exposed * (1.5f + Mathf.Sin(_t * 10) * 0.8f)));
            _bellMat.SetFloat("_Flash", _flash);
            _light.intensity = 3 + _flash * 6 + exposed * 3;
            _light.color = exposed > 0 ? Palette.Accent : Palette.Hex(0xFF6AD5);
            AnimateTentacles(dt);

            if (_state != S.Intro && _state != S.Dying) ContactWithPlayer(pp);
        }

        void Drift(Vector3 target, float dt, float speed)
        {
            var to = target - _pos; to.y = 0;
            var want = _pos + to.normalized * Mathf.Min(to.magnitude - Radius * 1.4f, speed * dt);
            if (to.magnitude > Radius * 1.6f) _pos = new Vector3(want.x, _pos.y, want.z);
            var flat = new Vector2(_pos.x, _pos.z);
            if (flat.magnitude > 30) { flat = flat.normalized * 30; _pos.x = flat.x; _pos.z = flat.y; }
        }

        void PlaceSlams(Vector3 pp)
        {
            var lead = Player.Velocity; lead.y = 0;
            var spots = new[] { pp, pp + lead * 0.9f, pp + Quaternion.Euler(0, 120, 0) * Vector3.forward * Radius * 0.8f + lead * 0.3f };
            int n = Phase == 2 ? 3 : 2;
            for (int i = 0; i < n; i++)
            {
                var p = Arena.Clamp(spots[i], 2); p.y = Arena.HeightAt(p.x, p.z) + 0.15f;
                var go = new GameObject("SlamTelegraph");
                go.transform.position = p; go.transform.localScale = new Vector3(Radius * 1.1f, 1, Radius * 1.1f);
                go.AddComponent<MeshFilter>().sharedMesh = MeshKit.QuadXZ();
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Mats.Glow(Palette.Hdr(Palette.Danger, 1.5f), 2);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _slams.Add((go.transform, mr, p));
            }
            Audio?.Play("charge", 0.9f, 0.6f);
        }

        void FireRing(int ring)
        {
            int count = 18 + Phase * 4;
            float gapAt = UnityEngine.Random.Range(0, Mathf.PI * 2);
            float offset = ring * 0.17f;
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2 + offset;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, gapAt * Mathf.Rad2Deg)) < 26) continue;   // the gap to slip through
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Projectiles.I.Spore(_pos + dir * Radius * 0.6f + Vector3.down * Radius * 0.2f, dir, 7.5f + ring, 14, 0.38f + Player.Radius * 0.12f);
            }
            Audio?.Play("spit", 1f, 0.55f);
            _flash = 0.6f;
        }

        void ContactWithPlayer(Vector3 pp)
        {
            var to = pp - _pos; to.y = 0;
            float d = to.magnitude;
            float reach = Radius * 0.62f + Player.Radius * 0.8f;
            if (d > reach) return;
            if (Exposed)
            {
                if (_biteCd <= 0)
                {
                    _biteCd = 0.35f;
                    float dmg = 30 * Player.Genome.Current.Bite * (0.5f + Player.Radius * 0.4f);
                    Damage(dmg, true);
                    Player.Body.Swallow(0.5f);
                    Fx.I.Absorb(_pos + Vector3.up * Radius * 0.2f, Player.MouthPoint, Palette.Hdr(Palette.Accent, 2.5f), 8, 0.6f);
                    Audio?.Gulp(0.8f, false);
                    Rig.Punch(0.03f);
                }
            }
            else
            {
                Player.TakeDamage(12, _pos);
                var push = to.normalized * (reach - d);
                Player.transform.position += push;
            }
        }

        /// <summary>External damage (acid, volt). Reduced while her core is sealed.</summary>
        public void Damage(float amount, bool bite = false)
        {
            if (!Active || _state == S.Dying || _state == S.Intro) return;
            if (!Exposed && !bite) amount *= 0.3f;
            int phaseBefore = Phase;
            Hp -= amount;
            _flash = Mathf.Max(_flash, 0.5f);
            if (Phase == 2 && phaseBefore == 1)
            {
                Audio?.Play("roar", 1f, 1.1f);
                Rig.Shake(0.5f);
                Fx.I.Ring(_pos, Palette.Hdr(Palette.Hex(0xFF6AD5), 3f), Radius, Radius * 4, 0.8f);
            }
            if (Hp <= 0)
            {
                Hp = 0;
                foreach (var s in _slams) if (s.t != null) Destroy(s.t.gameObject);
                _slams.Clear();
                Projectiles.I.ClearHostile();
                Go(S.Dying);
                Audio?.Play("roar", 1f, 0.6f);
            }
        }

        public bool HitTest(Vector3 p, float r, float dmg)
        {
            if (!Active) return false;
            var d = p - _pos; d.y = 0;
            if (d.magnitude > Radius * 0.6f + r) return false;
            Damage(dmg);
            return true;
        }

        public Vector3? AimPoint(Vector3 from, float range)
        {
            if (!Active || _state == S.Intro || _state == S.Dying) return null;
            return (from - _pos).magnitude < range + Radius * 0.6f ? _pos : (Vector3?)null;
        }

        void AnimateTentacles(float dt)
        {
            for (int i = 0; i < _tentacles.Count; i++)
            {
                var segs = _tentacles[i];
                float a = i / (float)_tentacles.Count * Mathf.PI * 2;
                var anchor = transform.position + new Vector3(Mathf.Cos(a), -0.05f, Mathf.Sin(a)) * Radius * 0.5f;
                var prev = anchor;
                for (int s = 0; s < segs.Length; s++)
                {
                    float sway = Mathf.Sin(_t * 1.8f + i * 0.7f - s * 0.5f);
                    var dir = new Vector3(Mathf.Cos(a) * (0.35f + s * 0.08f) + Mathf.Cos(a + 1.57f) * sway * 0.35f, -1f, Mathf.Sin(a) * (0.35f + s * 0.08f) + Mathf.Sin(a + 1.57f) * sway * 0.35f).normalized;
                    var p = prev + dir * Radius * 0.12f;
                    float floor = Arena.HeightAt(p.x, p.z) + 0.2f;
                    if (p.y < floor) p.y = floor;
                    segs[s].position = p;
                    prev = p;
                }
            }
        }
    }
}
