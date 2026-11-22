using System.Collections.Generic;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>Pooled effects: particle bursts, absorb streams, shockwave rings, lightning arcs. Nothing allocates per hit.</summary>
    public sealed class Fx : MonoBehaviour
    {
        public static Fx I { get; private set; }
        ParticleSystem _add, _goo, _bubbles;
        Transform _follow;

        sealed class RingFx { public Transform t; public MeshRenderer r; public float age, life, r0, r1; public Color c; }
        readonly List<RingFx> _rings = new List<RingFx>();
        sealed class Arc { public LineRenderer lr; public float age, life; }
        readonly List<Arc> _arcs = new List<Arc>();
        MaterialPropertyBlock _mpb;
        public float Quality = 1f;

        public void Init(Transform follow)
        {
            I = this; _follow = follow; _mpb = new MaterialPropertyBlock();
            _add = MakeSystem("FxAdd", Mats.Particle(true, 0.7f), 1500);
            _goo = MakeSystem("FxGoo", Mats.Particle(false, 0.45f, 2), 800);
            _bubbles = MakeAmbient();
        }

        ParticleSystem MakeSystem(string name, Material mat, int max)
        {
            var go = new GameObject(name); go.transform.SetParent(transform);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.maxParticles = max; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1; main.startSpeed = 0; main.startSize = 0.2f;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 1, 1, 0));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.dampen = 0.08f; limit.limit = 0;
            limit.drag = 2.2f; limit.multiplyDragByParticleSize = false;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.material = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        ParticleSystem MakeAmbient()
        {
            var go = new GameObject("Motes"); go.transform.SetParent(transform);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.maxParticles = 400; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5, 9); main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(Palette.Hdr(Palette.Accent, 0.9f), Palette.Hdr(Palette.Hex(0x9FFFE0), 0.4f));
            var em = ps.emission; em.rateOverTime = 45;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(60, 6, 60); sh.position = new Vector3(0, 3, 0);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.25f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.3f), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.material = Mats.Particle(true, 0.9f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        public void SetAmbientScale(float s)
        {
            var sh = _bubbles.shape; sh.scale = new Vector3(60 * s, 6 * s, 60 * s);
            var main = _bubbles.main; main.startSizeMultiplier = s;
        }

        void Update()
        {
            if (_follow != null) _bubbles.transform.position = _follow.position;
            float dt = Time.deltaTime;
            for (int i = 0; i < _rings.Count; i++)
            {
                var r = _rings[i];
                if (!r.t.gameObject.activeSelf) continue;
                r.age += dt;
                float u = r.age / r.life;
                if (u >= 1) { r.t.gameObject.SetActive(false); continue; }
                float e = 1 - (1 - u) * (1 - u) * (1 - u);
                float rad = Mathf.Lerp(r.r0, r.r1, e);
                r.t.localScale = new Vector3(rad * 2, 1, rad * 2);
                _mpb.SetColor("_Color", r.c * (1 - u));
                r.r.SetPropertyBlock(_mpb);
            }
            for (int i = 0; i < _arcs.Count; i++)
            {
                var a = _arcs[i];
                if (!a.lr.enabled) continue;
                a.age += dt;
                if (a.age >= a.life) a.lr.enabled = false;
                else a.lr.widthMultiplier = 0.12f * (1 - a.age / a.life) + 0.02f;
            }
        }

        public void Burst(Vector3 p, Color hdr, int count, float speed, float size, float life = 0.6f, bool goo = false)
        {
            count = Mathf.Max(1, Mathf.RoundToInt(count * Quality));
            var ps = goo ? _goo : _add;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                var dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.6f;
                ep.position = p + dir * size * 0.5f;
                ep.velocity = dir * speed * Random.Range(0.4f, 1.1f);
                ep.startSize = size * Random.Range(0.5f, 1.2f);
                ep.startLifetime = life * Random.Range(0.6f, 1.2f);
                ep.startColor = hdr;
                ps.Emit(ep, 1);
            }
        }

        /// <summary>Glowing motes that fly from the meal into the mouth.</summary>
        public void Absorb(Vector3 from, Vector3 to, Color hdr, int count, float size)
        {
            count = Mathf.Max(1, Mathf.RoundToInt(count * Quality));
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                float life = Random.Range(0.25f, 0.45f);
                var start = from + Random.insideUnitSphere * size;
                ep.position = start;
                ep.velocity = (to - start) / life * 1.6f;   // drag slows them, so overshoot the speed
                ep.startSize = size * Random.Range(0.25f, 0.55f);
                ep.startLifetime = life;
                ep.startColor = hdr;
                _add.Emit(ep, 1);
            }
        }

        public void Ring(Vector3 p, Color hdr, float r0, float r1, float life, float thickness = 0.12f)
        {
            RingFx ring = null;
            foreach (var r in _rings) if (!r.t.gameObject.activeSelf) { ring = r; break; }
            if (ring == null)
            {
                var go = new GameObject("Ring"); go.transform.SetParent(transform);
                go.AddComponent<MeshFilter>().sharedMesh = MeshKit.QuadXZ();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Mats.Glow(Color.white, 1, thickness, true);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring = new RingFx { t = go.transform, r = mr };
                _rings.Add(ring);
            }
            ring.r.sharedMaterial.SetFloat("_Thickness", thickness);
            ring.t.position = p + Vector3.up * 0.06f;
            ring.age = 0; ring.life = life; ring.r0 = r0; ring.r1 = r1; ring.c = hdr;
            ring.t.gameObject.SetActive(true);
        }

        public void Lightning(Vector3 a, Vector3 b, Color hdr)
        {
            Arc arc = null;
            foreach (var x in _arcs) if (!x.lr.enabled) { arc = x; break; }
            if (arc == null)
            {
                var go = new GameObject("Arc"); go.transform.SetParent(transform);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = Mats.Particle(true, 0.9f, 1);
                lr.positionCount = 8; lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                arc = new Arc { lr = lr };
                _arcs.Add(arc);
            }
            arc.lr.startColor = hdr; arc.lr.endColor = hdr;
            float len = Vector3.Distance(a, b);
            var side = Vector3.Cross((b - a).normalized, Vector3.up);
            for (int i = 0; i < 8; i++)
            {
                float u = i / 7f;
                var p = Vector3.Lerp(a, b, u);
                if (i > 0 && i < 7) p += side * Random.Range(-0.18f, 0.18f) * len + Vector3.up * Random.Range(0, 0.1f) * len;
                arc.lr.SetPosition(i, p);
            }
            arc.age = 0; arc.life = 0.16f; arc.lr.enabled = true;
        }
    }
}
