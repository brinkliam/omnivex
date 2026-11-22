using System.Collections.Generic;
using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>
    /// Thermal vents in the Vent Field. A readable telegraph fills, then the vent erupts, scalding anything on it -
    /// including predators you lure across. Small creatures fear them; giants shrug them off.
    /// </summary>
    public sealed class Hazards : MonoBehaviour
    {
        public WorldBase Arena; public Ecosystem Eco; public PlayerCreature Player; public AudioDirector Audio;
        sealed class Vent { public Vector3 p; public float timer, tele; public Transform decal; public MeshRenderer mr; public Light light; }
        readonly List<Vent> _vents = new List<Vent>();
        const float VentRadius = 2.6f;
        MaterialPropertyBlock _mpb;

        public void Build()
        {
            _mpb = new MaterialPropertyBlock();
            foreach (var old in _vents) if (old.decal != null) Destroy(old.decal.gameObject);
            _vents.Clear();
            var rng = new Rng(Arena.Seed ^ 0x77);
            foreach (var s in Arena.VentSpots)
            {
                var v = new Vent { p = new Vector3(s.x, Arena.HeightAt(s.x, s.y), s.y), timer = rng.Range(2f, 9f) };
                var go = new GameObject("VentTelegraph"); go.transform.SetParent(transform);
                go.transform.position = v.p + Vector3.up * 0.12f;
                go.transform.localScale = new Vector3(VentRadius * 2, 1, VentRadius * 2);
                go.AddComponent<MeshFilter>().sharedMesh = MeshKit.QuadXZ();
                v.mr = go.AddComponent<MeshRenderer>(); v.mr.sharedMaterial = Mats.Glow(Palette.Hdr(Palette.Warning, 1.6f), 2);
                v.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                v.decal = go.transform;
                var lg = new GameObject("VentLight"); lg.transform.SetParent(go.transform, false); lg.transform.localPosition = new Vector3(0, 1.5f, 0);
                v.light = lg.AddComponent<Light>(); v.light.type = LightType.Point; v.light.color = Palette.Warning; v.light.range = 7; v.light.intensity = 0.6f;
                _vents.Add(v);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            foreach (var v in _vents)
            {
                v.timer -= dt;
                if (v.timer > 0 && v.timer < 1.4f) v.tele = 1 - v.timer / 1.4f; else if (v.timer > 0) v.tele = 0;
                _mpb.SetFloat("_Fill", v.tele);
                _mpb.SetColor("_Color", Palette.Hdr(Palette.Warning, 0.4f + v.tele * 2.2f));
                v.mr.SetPropertyBlock(_mpb);
                v.light.intensity = 0.5f + v.tele * 4f + Mathf.Sin(Time.time * 3 + v.p.x) * 0.2f;
                if (v.timer <= 0) Erupt(v);
            }
        }

        void Erupt(Vent v)
        {
            v.timer = Random.Range(6.5f, 10f);
            v.tele = 0;
            Fx.I.Burst(v.p + Vector3.up * 0.5f, Palette.Hdr(Palette.Warning, 3f), 26, 9, 0.5f, 0.9f);
            Fx.I.Burst(v.p + Vector3.up * 0.5f, Palette.Hdr(Palette.Hex(0xFF6A3A), 1.2f), 14, 6, 0.9f, 1.4f, true);
            Fx.I.Ring(v.p, Palette.Hdr(Palette.Warning, 2.5f), 0.5f, VentRadius * 1.3f, 0.5f);
            float dToPlayer = (Player.transform.position - v.p).magnitude;
            if (dToPlayer < 28) Audio?.At("vent", v.p, 0.8f);
            if (dToPlayer < VentRadius + Player.Radius * 0.5f) Player.TakeDamage(26f / Mathf.Max(1, Player.Radius * 0.9f), v.p);
            Eco.ForEachNear(v.p, VentRadius + 1, (c, d) =>
            {
                if (c.Kind.Brain == Brain.Morsel || c.Kind.Brain == Brain.Ally) return;
                if (d < VentRadius + c.Radius * 0.5f) Eco.Damage(c, 45, false);
            });
        }
    }
}
