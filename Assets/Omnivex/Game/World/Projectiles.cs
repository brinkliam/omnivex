using System.Collections.Generic;
using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>Pooled projectiles: the player's acid globs and the Matriarch's spore volleys.</summary>
    public sealed class Projectiles : MonoBehaviour
    {
        public static Projectiles I { get; private set; }
        public Ecosystem Eco; public PlayerCreature Player; public Arena Arena;
        public System.Func<Vector3, float, float, bool> HitBoss;   // (pos, radius, damage) -> hit?

        sealed class P { public Transform t; public Vector3 pos, vel; public float dmg, r, life; public bool hostile; public bool alive; }
        readonly List<P> _list = new List<P>();
        Mesh _sphere;
        Material _acid, _spore;

        public void Init()
        {
            I = this;
            _sphere = MeshKit.Blob(10, 7, Color.white, Color.white);
            _acid = Mats.Bio("proj_acid", Palette.Hdr(Palette.Of(Dna.Toxin), 2f), 1.5f, Color.black, 0, 0.12f);
            _acid.SetColor("_EmissionColor", Palette.Hdr(Palette.Of(Dna.Toxin), 2.4f));
            _spore = Mats.Bio("proj_spore", Palette.Hdr(Palette.Danger, 2f), 1.5f, Color.black, 0, 0.1f);
            _spore.SetColor("_EmissionColor", Palette.Hdr(Palette.Hex(0xFF6A3A), 2.8f));
        }

        P Get(Material m)
        {
            foreach (var p in _list) if (!p.alive) { p.t.GetComponent<MeshRenderer>().sharedMaterial = m; return p; }
            var go = new GameObject("proj"); go.transform.SetParent(transform);
            go.AddComponent<MeshFilter>().sharedMesh = _sphere;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var np = new P { t = go.transform };
            _list.Add(np);
            return np;
        }

        public void Spit(Vector3 from, Vector3 to, float speed, float dmg, float r)
        {
            var p = Get(_acid);
            var dir = to - from; dir.y = 0;
            p.pos = from; p.vel = dir.normalized * speed; p.dmg = dmg; p.r = Mathf.Max(0.12f, r); p.life = 1.6f; p.hostile = false; p.alive = true;
            p.t.gameObject.SetActive(true);
        }

        public void Spore(Vector3 from, Vector3 dir, float speed, float dmg, float r)
        {
            var p = Get(_spore);
            dir.y = 0;
            p.pos = from; p.vel = dir.normalized * speed; p.dmg = dmg; p.r = r; p.life = 6f; p.hostile = true; p.alive = true;
            p.t.gameObject.SetActive(true);
        }

        public void ClearHostile() { foreach (var p in _list) if (p.alive && p.hostile) Kill(p); }
        public void ClearAll() { foreach (var p in _list) if (p.alive) Kill(p); }

        void Kill(P p) { p.alive = false; p.t.gameObject.SetActive(false); }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            foreach (var p in _list)
            {
                if (!p.alive) continue;
                p.life -= dt;
                p.pos += p.vel * dt;
                float ground = Arena.HeightAt(p.pos.x, p.pos.z) + p.r * 2 + 0.3f;
                p.pos.y = Mathf.Lerp(p.pos.y, ground, dt * 5);
                p.t.position = p.pos;
                p.t.localScale = Vector3.one * p.r * 2 * (1 + Mathf.Sin(Time.time * 20) * 0.1f);
                if (p.life <= 0 || new Vector2(p.pos.x, p.pos.z).magnitude > Arena.Radius - 3) { Kill(p); continue; }
                if (p.hostile)
                {
                    if (Player.IsAlive && (Player.transform.position - p.pos).magnitude < Player.Radius * 0.9f + p.r)
                    {
                        Player.TakeDamage(p.dmg, p.pos);
                        Fx.I.Burst(p.pos, Palette.Hdr(Palette.Danger, 2f), 8, 3, p.r, 0.4f);
                        Kill(p);
                    }
                }
                else
                {
                    bool hit = false;
                    Eco.ForEachNear(p.pos, p.r + 4f, (c, d) =>
                    {
                        if (hit || c.Kind.Brain == Brain.Ally || c.Kind.Brain == Brain.Morsel) return;
                        if (d < c.Radius + p.r) { hit = true; Eco.Damage(c, p.dmg); }
                    });
                    if (!hit && HitBoss != null) hit = HitBoss(p.pos, p.r, p.dmg);
                    if (hit)
                    {
                        Fx.I.Burst(p.pos, Palette.Hdr(Palette.Of(Dna.Toxin), 2.5f), 10, 3, p.r * 1.2f, 0.5f, true);
                        Kill(p);
                    }
                }
            }
        }
    }
}
