using System.Collections.Generic;
using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>
    /// Pooled projectiles. Player: acid globs and railspine slugs. Enemies: Matriarch spores, rifle bullets, homing
    /// missiles, and artillery strikes that land on a filling ground telegraph.
    /// </summary>
    public sealed class Projectiles : MonoBehaviour
    {
        public static Projectiles I { get; private set; }
        public Ecosystem Eco; public PlayerCreature Player; public WorldBase Arena; public AudioDirector Audio; public CameraRig Rig;
        public System.Func<Vector3, float, float, bool> HitBoss;   // (pos, radius, damage) -> hit?

        enum Kind { Acid, Spore, Bullet, Missile, Bolt }
        sealed class P { public Transform t; public Vector3 pos, vel; public float dmg, r, life; public bool hostile, alive; public Kind kind; }
        readonly List<P> _list = new List<P>();
        sealed class StrikeFx { public Vector3 pos; public float t, delay, radius, dmg; public Transform decal; public MeshRenderer mr; public bool alive; }
        readonly List<StrikeFx> _strikes = new List<StrikeFx>();
        Mesh _sphere;
        Material _acid, _spore, _bullet, _missile, _bolt;
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        public void Init()
        {
            I = this;
            _sphere = MeshKit.Blob(10, 7, Color.white, Color.white);
            _acid = Mats.Bio("proj_acid", Palette.Hdr(Palette.Of(Dna.Toxin), 2f), 1.5f, Color.black, 0, 0.12f);
            _acid.SetColor("_EmissionColor", Palette.Hdr(Palette.Of(Dna.Toxin), 2.4f));
            _spore = Mats.Bio("proj_spore", Palette.Hdr(Palette.Danger, 1.2f), 1.2f, Color.black, 0, 0.1f);
            _spore.SetColor("_BaseColor", Palette.Danger);
            _spore.SetColor("_EmissionColor", Palette.Hdr(Palette.Hex(0xFF3A2A), 0.9f));
            _bullet = Mats.Bio("proj_bullet", Color.black, 0, Color.black, 0, 0);
            _bullet.SetColor("_EmissionColor", Palette.Hdr(Palette.Hex(0xFFD27A), 3f));
            _missile = Mats.Bio("proj_missile", Palette.Hdr(Palette.Danger, 1f), 1f, Color.black, 0, 0);
            _missile.SetColor("_BaseColor", Palette.Hex(0x4A4A50));
            _missile.SetColor("_EmissionColor", Palette.Hdr(Palette.Warning, 0.8f));
            _bolt = Mats.Bio("proj_bolt", Palette.Hdr(Palette.Of(Dna.Alloy), 2f), 1.5f, Color.black, 0, 0);
            _bolt.SetColor("_EmissionColor", Palette.Hdr(Palette.Of(Dna.Alloy), 2.6f));
        }

        P Get(Material m, Kind kind)
        {
            P p = null;
            foreach (var x in _list) if (!x.alive) { p = x; break; }
            if (p == null)
            {
                var go = new GameObject("proj"); go.transform.SetParent(transform);
                go.AddComponent<MeshFilter>().sharedMesh = _sphere;
                var mr = go.AddComponent<MeshRenderer>(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                p = new P { t = go.transform };
                _list.Add(p);
            }
            p.t.GetComponent<MeshRenderer>().sharedMaterial = m;
            p.kind = kind; p.alive = true;
            p.t.gameObject.SetActive(true);
            return p;
        }

        public void Spit(Vector3 from, Vector3 to, float speed, float dmg, float r)
        {
            var p = Get(_acid, Kind.Acid);
            var dir = to - from; dir.y = 0;
            p.pos = from; p.vel = dir.normalized * speed; p.dmg = dmg; p.r = Mathf.Max(0.12f, r); p.life = 1.6f; p.hostile = false;
        }

        public void Slug(Vector3 from, Vector3 to, float dmg, float r)
        {
            var p = Get(_bolt, Kind.Bolt);
            var dir = to - from; dir.y = 0;
            p.pos = from; p.vel = dir.normalized * 34; p.dmg = dmg; p.r = Mathf.Max(0.2f, r); p.life = 1.2f; p.hostile = false;
            Fx.I.Burst(from, Palette.Hdr(Palette.Of(Dna.Alloy), 3f), 6, 6, r, 0.25f);
        }

        public void Spore(Vector3 from, Vector3 dir, float speed, float dmg, float r)
        {
            var p = Get(_spore, Kind.Spore);
            dir.y = 0;
            p.pos = from; p.vel = dir.normalized * speed; p.dmg = dmg; p.r = r; p.life = 6f; p.hostile = true;
        }

        public void Bullet(Vector3 from, Vector3 dir, float speed, float dmg)
        {
            var p = Get(_bullet, Kind.Bullet);
            dir.y = 0;
            p.pos = from + Vector3.up * 0.4f; p.vel = dir.normalized * speed; p.dmg = dmg; p.r = 0.16f; p.life = 2.2f; p.hostile = true;
            if (Audio != null && Player != null && (from - Player.transform.position).sqrMagnitude < 900) Audio.Play("gun", 0.25f, Random.Range(0.9f, 1.15f));
        }

        public void Missile(Vector3 from, float dmg)
        {
            var p = Get(_missile, Kind.Missile);
            var dir = Player.transform.position - from; dir.y = 0;
            p.pos = from; p.vel = dir.normalized * 6; p.dmg = dmg; p.r = 0.35f; p.life = 5f; p.hostile = true;
            Audio?.At("dash", from, 0.4f);
        }

        /// <summary>Artillery: a ground ring fills for `delay` seconds, then the shell lands.</summary>
        public void Strike(Vector3 at, float delay, float radius, float dmg)
        {
            StrikeFx s = null;
            foreach (var x in _strikes) if (!x.alive) { s = x; break; }
            if (s == null)
            {
                var go = new GameObject("StrikeTelegraph"); go.transform.SetParent(transform);
                go.AddComponent<MeshFilter>().sharedMesh = MeshKit.QuadXZ();
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Mats.Glow(Palette.Hdr(Palette.Danger, 1.5f), 2);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                s = new StrikeFx { decal = go.transform, mr = mr };
                _strikes.Add(s);
            }
            at = Arena.Clamp(at, 2); at.y = Arena.HeightAt(at.x, at.z) + 0.2f;
            s.pos = at; s.t = 0; s.delay = delay; s.radius = radius; s.dmg = dmg; s.alive = true;
            s.decal.position = at; s.decal.localScale = new Vector3(radius * 2, 1, radius * 2);
            s.decal.gameObject.SetActive(true);
        }

        public void ClearHostile()
        {
            foreach (var p in _list) if (p.alive && p.hostile) Kill(p);
            foreach (var s in _strikes) if (s.alive) { s.alive = false; s.decal.gameObject.SetActive(false); }
        }
        public void ClearAll() { foreach (var p in _list) if (p.alive) Kill(p); ClearHostile(); }

        void Kill(P p) { p.alive = false; p.t.gameObject.SetActive(false); }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            foreach (var p in _list)
            {
                if (!p.alive) continue;
                p.life -= dt;
                if (p.kind == Kind.Missile && Player.IsAlive)
                {
                    var to = Player.transform.position - p.pos; to.y = 0;
                    p.vel = Vector3.RotateTowards(p.vel, to.normalized * 13, dt * 1.6f, dt * 6);
                    if (Random.value < dt * 30) Fx.I.Burst(p.pos, Palette.Hdr(Palette.Warning, 1.5f), 1, 0.5f, 0.35f, 0.4f, true);
                }
                p.pos += p.vel * dt;
                float ground = Arena.HeightAt(p.pos.x, p.pos.z) + p.r * 2 + 0.3f;
                p.pos.y = Mathf.Lerp(p.pos.y, ground, dt * (p.kind == Kind.Missile ? 1.2f : 5));
                p.t.position = p.pos;
                float stretch = p.kind == Kind.Bullet || p.kind == Kind.Bolt ? 2.6f : 1f;
                p.t.localScale = new Vector3(1, 1, stretch) * p.r * 2 * (1 + Mathf.Sin(Time.time * 20) * 0.1f);
                if (p.vel.sqrMagnitude > 0.01f) p.t.rotation = Quaternion.LookRotation(p.vel);
                if (p.life <= 0 || new Vector2(p.pos.x, p.pos.z).magnitude > Arena.Bound + 1) { Kill(p); continue; }
                if (p.hostile)
                {
                    var d = Player.transform.position - p.pos; d.y = 0;
                    if (Player.IsAlive && d.magnitude < Player.Radius * 0.9f + p.r)
                    {
                        Player.TakeDamage(p.dmg, p.pos);
                        Fx.I.Burst(p.pos, Palette.Hdr(p.kind == Kind.Missile ? Palette.Warning : Palette.Danger, 2f), p.kind == Kind.Missile ? 18 : 6, 4, p.r * 2, 0.5f);
                        if (p.kind == Kind.Missile) { Audio?.Play("slam", 0.5f, 1.4f); Rig?.Shake(0.15f); }
                        Kill(p);
                    }
                    else if (p.kind != Kind.Spore && Arena.HitStructure(p.pos, p.r, p.kind == Kind.Missile ? p.dmg * 2 : 0, Instigator.Enemy)) Kill(p);
                }
                else
                {
                    bool hit = false;
                    Eco.ForEachNear(p.pos, p.r + 12f, (c, _) =>
                    {
                        if (hit || c.Kind.Brain == Brain.Ally || c.Kind.Brain == Brain.Morsel) return;
                        var dd = c.Pos - p.pos; dd.y = 0;
                        if (dd.magnitude < c.Radius + p.r) { hit = true; Eco.Damage(c, p.dmg); }
                    });
                    if (!hit && HitBoss != null) hit = HitBoss(p.pos, p.r, p.dmg);
                    if (!hit) hit = Arena.HitStructure(p.pos, p.r, p.kind == Kind.Bolt ? p.dmg * 1.5f : p.dmg * 0.5f, Instigator.Player);
                    if (hit)
                    {
                        var col = p.kind == Kind.Bolt ? Palette.Of(Dna.Alloy) : Palette.Of(Dna.Toxin);
                        Fx.I.Burst(p.pos, Palette.Hdr(col, 2.5f), 10, 3, p.r * 1.2f, 0.5f, p.kind == Kind.Acid);
                        Kill(p);
                    }
                }
            }

            foreach (var s in _strikes)
            {
                if (!s.alive) continue;
                s.t += dt;
                float u = Mathf.Clamp01(s.t / s.delay);
                _mpb.SetFloat("_Fill", u);
                _mpb.SetColor("_Color", Palette.Hdr(Palette.Danger, 0.8f + u * 2.4f));
                s.mr.SetPropertyBlock(_mpb);
                if (u < 1) continue;
                s.alive = false; s.decal.gameObject.SetActive(false);
                Fx.I.Burst(s.pos + Vector3.up, Palette.Hdr(Palette.Warning, 3.5f), 26, 9, s.radius * 0.4f, 0.7f);
                Fx.I.Burst(s.pos + Vector3.up, new Color(0.35f, 0.32f, 0.36f, 1), 12, 4, s.radius * 0.7f, 1.4f, true);
                Fx.I.Ring(s.pos, Palette.Hdr(Palette.Warning, 3f), 0.5f, s.radius * 1.4f, 0.5f);
                Audio?.At("slam", s.pos, 0.8f);
                if (Player != null && (Player.transform.position - s.pos).magnitude < s.radius + Player.Radius * 0.5f) { Player.TakeDamage(s.dmg, s.pos); Rig?.Shake(0.35f); }
                Eco.ForEachNear(s.pos, s.radius + 1, (c, d) => { if (c.Kind.Brain != Brain.Morsel && !c.Kind.Military) Eco.Damage(c, 60, false); });
                Arena.DamageStructures(s.pos, s.radius, s.dmg * 3, Instigator.Enemy);
            }
        }
    }
}
