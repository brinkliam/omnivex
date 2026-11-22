using System;
using System.Collections.Generic;
using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>
    /// Spawns, thinks for, and recycles every critter. Sizes are spawned relative to the player, so there is always
    /// something to eat and something to fear - and yesterday's predator becomes today's meal as you grow.
    /// </summary>
    public sealed class Ecosystem : MonoBehaviour
    {
        public Arena Arena;
        public PlayerCreature Player;
        public readonly List<Critter> Live = new List<Critter>();
        readonly Dictionary<string, Stack<Critter>> _pool = new Dictionary<string, Stack<Critter>>();
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        Rng _rng;
        float _spawnTimer, _gulperTimer = 40f;
        public bool SpawningEnabled = true;
        public float PreyTarget = 58, PredatorScale = 1f;
        public event Action<Critter> OnKilled;          // by the player's weapons
        public int Allies { get; private set; }

        public void Init(int seed) { _rng = new Rng(seed ^ 0x5151); }

        // ------------------------------------------------------------------ spawning

        public Critter Spawn(CritterKind k, Vector3 pos, float radius)
        {
            if (!_pool.TryGetValue(k.Id, out var stack)) _pool[k.Id] = stack = new Stack<Critter>();
            var c = stack.Count > 0 ? stack.Pop() : CritterFactory.Build(k, transform);
            c.Go.SetActive(true);
            c.Radius = radius; c.MaxHp = c.Hp = Mathf.Max(1, k.HpPerArea * radius * radius);
            c.Pos = Arena.Resolve(pos, radius); c.Pos.y = Arena.HeightAt(c.Pos.x, c.Pos.z) + radius * 0.9f;
            c.Vel = Vector3.zero; c.Heading = _rng.Range(0, 360f); c.Wander = _rng.Range(0, 100f);
            c.T = _rng.Range(0, 10f); c.Cooldown = _rng.Range(0.5f, 2f); c.State = 0; c.StateTimer = 0; c.Flash = 0;
            c.Alive = true; c.Digest = -1; c.Dna = k.Dna; c.Value = 0; c.Life = k.Brain == Brain.Ally ? 45f : k.Brain == Brain.Morsel ? 30f : -1; c.Elite = false;
            c.Root.position = c.Pos; c.Root.localScale = Vector3.one * radius * 2;
            if (c.Segments != null) for (int i = 0; i < c.Segments.Length; i++) { c.Segments[i].position = c.Pos; c.Segments[i].localScale = Vector3.one * (0.75f - i * 0.08f); }
            SetFx(c, 0, 0);
            Live.Add(c);
            if (k.Brain == Brain.Ally) Allies++;
            return c;
        }

        public Critter SpawnMorsel(Vector3 pos, float value, Dna dna)
        {
            float r = Mathf.Sqrt(value / 3.5f);
            var c = Spawn(Kinds.Morsel, pos, Mathf.Max(0.08f, r));
            c.Dna = dna; c.Value = value;
            c.Vel = new Vector3(_rng.Range(-1f, 1f), 0, _rng.Range(-1f, 1f)) * 3f;
            var col = Palette.Of(dna);
            foreach (var rd in c.Renderers)
            {
                rd.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", col);
                _mpb.SetColor("_EmissionColor", Palette.Hdr(col, 1.6f));
                rd.SetPropertyBlock(_mpb);
            }
            return c;
        }

        void Recycle(Critter c)
        {
            c.Alive = false;
            if (c.Kind.Brain == Brain.Ally) Allies--;
            c.Go.SetActive(false);
            _pool[c.Kind.Id].Push(c);
        }

        public void Clear()
        {
            for (int i = Live.Count - 1; i >= 0; i--) Recycle(Live[i]);
            Live.Clear();
        }

        float ViewRadius => 9f + Player.Radius * 9f;

        void Director(float dt)
        {
            if (!SpawningEnabled || Player == null) return;
            _spawnTimer -= dt;
            if (_spawnTimer > 0) return;
            _spawnTimer = 0.12f;
            float pr = Player.Radius;
            int prey = 0, preds = 0, lancers = 0, gulpers = 0;
            foreach (var c in Live)
            {
                if (c.Kind.Brain == Brain.Predator && c.Kind != Kinds.Brood) preds++;
                else if (c.Kind == Kinds.Lancer) lancers++;
                else if (c.Kind == Kinds.Gulper) gulpers++;
                else if (c.Kind.Brain != Brain.Morsel && c.Kind.Brain != Brain.Ally) prey++;
            }
            int tier = Player.Genome.Tier;
            var pos = SpawnPoint();
            var zone = Arena.ZoneAt(pos);
            int predTarget = Mathf.RoundToInt((3 + tier * 1.6f) * (0.6f + zone.Danger) * PredatorScale);
            if (prey < PreyTarget)
            {
                var dna = _rng.Chance(0.62f) ? zone.Primary : _rng.Chance(0.6f) ? zone.Secondary : (Dna)_rng.Range(0, 5);
                if (dna == Dna.Void) dna = Dna.Flesh;
                var k = Kinds.PreyFor(dna, _rng);
                float r = pr * (_rng.Chance(0.15f) ? _rng.Range(0.75f, 0.9f) : _rng.Range(0.22f, 0.7f));
                if (k == Kinds.Shellbit) r *= 0.9f;
                Spawn(k, pos, r);
            }
            else if (preds < predTarget)
            {
                // most predators are bigger than you; a few are the same size so fights are possible
                float r = pr * (_rng.Chance(0.25f) ? _rng.Range(0.95f, 1.15f) : _rng.Range(1.25f, 2.1f));
                var c = Spawn(Kinds.Snapjaw, pos, r);
                if (tier >= 2 && _rng.Chance(0.12f)) { c.Elite = true; c.Radius *= 1.3f; c.Hp = c.MaxHp = c.MaxHp * 2.2f; c.Root.localScale = Vector3.one * c.Radius * 2; }
            }
            else if (tier >= 1 && lancers < 1 + tier / 2)
            {
                Spawn(Kinds.Lancer, pos, pr * _rng.Range(1.1f, 1.6f));
            }
            else if (tier >= 1 && gulpers < 1 && (_gulperTimer -= 1.2f) <= 0)
            {
                _gulperTimer = 50;
                var far = Arena.RandomPoint(60, Arena.Radius - 10);
                Spawn(Kinds.Gulper, new Vector3(far.x, 0, far.y), pr * 0.95f);
            }
        }

        Vector3 SpawnPoint()
        {
            var pp = Player.transform.position;
            for (int tries = 0; tries < 8; tries++)
            {
                float a = _rng.Range(0, Mathf.PI * 2);
                float d = ViewRadius * _rng.Range(1.05f, 1.7f);
                var p = pp + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
                if (new Vector2(p.x, p.z).magnitude < Arena.Radius - 6) return p;
            }
            var q = Arena.RandomPoint(8, Arena.Radius - 8);
            return new Vector3(q.x, 0, q.y);
        }

        // ------------------------------------------------------------------ damage

        public void Damage(Critter c, float amount, bool byPlayer = true)
        {
            if (!c.Alive || c.Digest >= 0 || c.Kind.Brain == Brain.Morsel) return;
            c.Hp -= amount; c.Flash = 1;
            if (c.Hp > 0) return;
            Kill(c, byPlayer);
        }

        public void Kill(Critter c, bool byPlayer)
        {
            if (!c.Alive) return;
            var col = Palette.Of(c.Dna);
            Fx.I.Burst(c.Pos, Palette.Hdr(col, 2.5f), 14, 4 + c.Radius * 3, c.Radius * 0.6f, 0.7f);
            Fx.I.Burst(c.Pos, col, 8, 2 + c.Radius, c.Radius * 0.7f, 1.1f, true);
            float total = Genome.ValueOf(c.Radius) * 0.85f;
            int n = Mathf.Clamp(Mathf.RoundToInt(c.Radius / Mathf.Max(0.1f, Player.Radius) * 3), 1, 7);
            if (c.Kind.Brain != Brain.Ally)
                for (int i = 0; i < n; i++) SpawnMorsel(c.Pos + UnityEngine.Random.insideUnitSphere * c.Radius * 0.5f, total / n, c.Dna);
            if (byPlayer) OnKilled?.Invoke(c);
            Recycle(c);
            Live.Remove(c);
        }

        /// <summary>Begin the swallow animation; biomass is granted by the player immediately.</summary>
        public void BeginDigest(Critter c)
        {
            c.Digest = 0; c.DigestFrom = c.Pos;
        }

        void SetFx(Critter c, float flash, float dissolve)
        {
            foreach (var r in c.Renderers)
            {
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat("_Flash", flash); _mpb.SetFloat("_Dissolve", dissolve);
                r.SetPropertyBlock(_mpb);
            }
        }

        public void ForEachNear(Vector3 p, float radius, Action<Critter, float> f)
        {
            float sq = radius * radius;
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                if (i >= Live.Count) continue;
                var c = Live[i];
                if (!c.Alive || c.Digest >= 0) continue;
                float d = (c.Pos - p).sqrMagnitude;
                if (d < sq) f(c, Mathf.Sqrt(d));
            }
        }

        public Critter NearestThreat(Vector3 p, float range)
        {
            Critter best = null; float bd = range * range;
            foreach (var c in Live)
            {
                if (!c.Alive || c.Digest >= 0) continue;
                bool threat = (c.Hostile || c.Kind == Kinds.Gulper) && c.Radius >= Player.Radius * 0.85f;
                if (!threat) continue;
                float d = (c.Pos - p).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        public Critter FindRare()
        {
            foreach (var c in Live) if (c.Alive && c.Kind.Rare) return c;
            return null;
        }

        // ------------------------------------------------------------------ simulation

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0 || Player == null) return;
            Director(dt);
            var pp = Player.transform.position;
            float pr = Player.Radius;
            float despawn = ViewRadius * 2.3f;

            for (int i = Live.Count - 1; i >= 0; i--)
            {
                if (i >= Live.Count) continue;
                var c = Live[i];
                c.T += dt;
                if (c.Digest >= 0)
                {
                    c.Digest += dt / 0.22f;
                    float u = Mathf.Clamp01(c.Digest);
                    c.Pos = Vector3.Lerp(c.DigestFrom, Player.MouthPoint, u * u);
                    c.Root.position = c.Pos;
                    c.Root.localScale = Vector3.one * c.Radius * 2 * (1 - u * 0.85f);
                    SetFx(c, 0, u * 0.9f);
                    if (u >= 1) { Recycle(c); Live.RemoveAt(i); }
                    continue;
                }
                if (c.Life > 0 && (c.Life -= dt) <= 0) { if (c.Kind.Brain == Brain.Ally) Kill(c, false); else { Recycle(c); Live.RemoveAt(i); } continue; }

                var toP = pp - c.Pos; toP.y = 0;
                float dist = toP.magnitude;
                if (c.Kind.Brain != Brain.Ally && dist > despawn && !c.Kind.Rare) { Recycle(c); Live.RemoveAt(i); continue; }
                if (c.Radius < pr * 0.14f && dist > ViewRadius * 1.1f && c.Kind.Brain != Brain.Morsel) { Recycle(c); Live.RemoveAt(i); continue; }

                Think(c, toP, dist, pr, dt);
                Move(c, dt);
                Animate(c, dt);
                if (c.Flash > 0) { c.Flash = Mathf.Max(0, c.Flash - dt * 6); SetFx(c, c.Flash, 0); }
                if (!c.Alive) continue;
                Player.Interact(c, dist);
            }
        }

        void Think(Critter c, Vector3 toP, float dist, float pr, float dt)
        {
            var k = c.Kind;
            Vector3 desired = Vector3.zero;
            float speed = k.Speed * (0.6f + c.Radius * 0.55f);
            float wanderA = (Mathf.PerlinNoise(c.Wander, c.T * 0.3f) - 0.5f) * 720f * Mathf.Deg2Rad;
            var wanderDir = new Vector3(Mathf.Cos(wanderA), 0, Mathf.Sin(wanderA));
            bool playerBigger = pr > c.Radius * 1.05f;
            bool canBeEaten = c.Radius < pr * k.EatRatio;
            var away = dist > 0.01f ? -toP / dist : wanderDir;

            switch (k.Brain)
            {
                case Brain.Drift:
                    desired = wanderDir * speed * 0.6f;
                    break;
                case Brain.Morsel:
                    desired = Vector3.zero;
                    break;
                case Brain.Anchored:
                    desired = Vector3.zero;
                    break;
                case Brain.Skittish:
                    float fear = 4 + c.Radius * 4 + pr * 3;
                    if (canBeEaten && dist < fear && !Player.Hidden) desired = (away + wanderDir * 0.35f).normalized * speed * 1.35f;
                    else desired = wanderDir * speed * 0.55f;
                    break;
                case Brain.Predator:
                {
                    c.Cooldown -= dt;
                    float aggro = 7 + c.Radius * 4 + (k == Kinds.Brood ? 30 : 0);
                    if (canBeEaten && dist < aggro) desired = (away + wanderDir * 0.3f).normalized * speed * 1.2f;   // you're the predator now
                    else if (!playerBigger && dist < aggro && !Player.Hidden && Player.IsAlive && Player.Controllable)
                    {
                        if (c.State == 0 && dist < c.Radius + pr + 2.2f && c.Cooldown <= 0) { c.State = 1; c.StateTimer = 0.32f; }   // wind-up: jaws open
                        if (c.State == 1) { desired = Vector3.zero; c.StateTimer -= dt; if (c.StateTimer <= 0) { c.State = 2; c.StateTimer = 0.35f; c.Vel = toP.normalized * speed * 3.2f; } }
                        else if (c.State == 2) { c.StateTimer -= dt; if (c.StateTimer <= 0) { c.State = 0; c.Cooldown = 1.4f; } desired = c.Vel; }
                        else desired = toP.normalized * speed;
                    }
                    else
                    {
                        // hunt smaller critters to keep the pool alive
                        desired = wanderDir * speed * 0.6f;
                        c.State = 0;
                    }
                    break;
                }
                case Brain.Lancer:
                {
                    c.Cooldown -= dt;
                    if (c.State == 0)
                    {
                        if (canBeEaten && dist < 10) desired = away * speed * 1.4f;
                        else desired = wanderDir * speed * 0.5f;
                        if (!playerBigger && dist < 12 + c.Radius * 3 && c.Cooldown <= 0 && Player.IsAlive && !Player.Hidden && Player.Controllable) { c.State = 1; c.StateTimer = 0.85f; c.Aim = toP.normalized; }
                    }
                    else if (c.State == 1)
                    {
                        // telegraph: coil and glow, track slowly
                        c.Aim = Vector3.RotateTowards(c.Aim, toP.normalized, dt * 1.2f, 0);
                        desired = Vector3.zero;
                        c.StateTimer -= dt;
                        if (c.StateTimer <= 0) { c.State = 2; c.StateTimer = 0.9f; }
                    }
                    else
                    {
                        desired = c.Aim * speed * 6.5f;
                        c.Vel = desired;
                        c.StateTimer -= dt;
                        if (c.StateTimer <= 0) { c.State = 0; c.Cooldown = 3.5f; }
                    }
                    break;
                }
                case Brain.Ally:
                {
                    Critter target = null; float bd = 18 * 18;
                    foreach (var o in Live)
                    {
                        if (o == c || !o.Alive || o.Digest >= 0 || o.Kind.Brain == Brain.Ally || o.Kind.Brain == Brain.Morsel) continue;
                        float d = (o.Pos - c.Pos).sqrMagnitude;
                        if (d < bd && (o.Hostile || o.Radius < c.Radius * 1.3f)) { bd = d; target = o; }
                    }
                    if (target != null)
                    {
                        var to = target.Pos - c.Pos; to.y = 0;
                        desired = to.normalized * speed;
                        if (to.magnitude < c.Radius + target.Radius)
                        {
                            c.Cooldown -= dt;
                            if (c.Cooldown <= 0)
                            {
                                c.Cooldown = 0.5f;
                                if (target.Radius < c.Radius * 1.3f && !target.Hostile) { Player.FeedFromAlly(target); BeginDigestTowards(target, c); }
                                else Damage(target, k.Damage);
                            }
                        }
                    }
                    else desired = ((dist > 4 ? toP.normalized : wanderDir) * speed * 0.7f);
                    break;
                }
            }

            // ambient predation keeps the ecosystem alive even off-camera
            if (k.Brain == Brain.Predator && k != Kinds.Brood && c.State == 0 && ((int)(c.T * 4) % 7 == 0))
            {
                foreach (var o in Live)
                {
                    if (o == c || !o.Alive || o.Digest >= 0 || o.Hostile || o.Kind.Brain == Brain.Ally || o.Kind.Rare) continue;
                    if (o.Radius < c.Radius * 0.6f && (o.Pos - c.Pos).sqrMagnitude < (c.Radius + o.Radius) * (c.Radius + o.Radius)) { BeginDigestTowards(o, c); break; }
                }
            }

            if (k.Brain == Brain.Lancer && c.State == 2) return;
            if (k.Brain == Brain.Predator && c.State == 2) return;
            c.Vel = Vector3.Lerp(c.Vel, desired, 1 - Mathf.Exp(-dt * k.Turn * 0.6f));
        }

        void BeginDigestTowards(Critter victim, Critter eater)
        {
            // a critter eaten by another critter just vanishes into it with a puff
            Fx.I.Burst(victim.Pos, Palette.Hdr(Palette.Of(victim.Dna), 1.5f), 5, 2, victim.Radius * 0.5f, 0.4f);
            Recycle(victim);
            Live.Remove(victim);
        }

        void Move(Critter c, float dt)
        {
            if (c.Kind.Brain == Brain.Morsel) c.Vel *= Mathf.Exp(-dt * 2f);
            c.Pos += c.Vel * dt;
            c.Pos = Arena.Resolve(c.Pos, c.Radius);
            float ground = Arena.HeightAt(c.Pos.x, c.Pos.z) + c.Radius * 0.9f;
            float bob = Mathf.Sin(c.T * 2.1f + c.Wander) * c.Radius * 0.12f;
            c.Pos.y = Mathf.Lerp(c.Pos.y, ground + bob, 1 - Mathf.Exp(-dt * 6));
            var flat = new Vector3(c.Vel.x, 0, c.Vel.z);
            if (c.Kind.Brain == Brain.Lancer && c.State == 1) flat = c.Aim;
            if (flat.sqrMagnitude > 0.01f)
            {
                float target = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                c.Heading = Mathf.MoveTowardsAngle(c.Heading, target, dt * 360f);
            }
            c.Root.SetPositionAndRotation(c.Pos, Quaternion.Euler(0, c.Heading, 0));
        }

        void Animate(Critter c, float dt)
        {
            float spd = c.Vel.magnitude;
            if (c.Tail != null) c.Tail.localEulerAngles = new Vector3(0, 180 + Mathf.Sin(c.T * (6 + spd * 3)) * (14 + spd * 6), 0);
            if (c.JawL != null)
            {
                float open = c.State == 1 ? 38 : c.State == 2 ? 4 : 14 + Mathf.Sin(c.T * 3) * 6;
                c.JawL.localEulerAngles = new Vector3(0, open, 0);
                c.JawR.localEulerAngles = new Vector3(0, -open, 0);
            }
            if (c.Kind == Kinds.Driftspore) c.Root.rotation = Quaternion.Euler(0, c.T * 40, 0);
            if (c.Kind == Kinds.Voltling) c.Root.rotation = Quaternion.Euler(0, c.T * 220, 0);
            if (c.Kind.Brain == Brain.Morsel) c.Root.localScale = Vector3.one * c.Radius * 2 * (1 + Mathf.Sin(c.T * 6) * 0.12f);
            if (c.Kind == Kinds.Lancer)
            {
                float coil = c.State == 1 ? 0.6f : 1f;
                var prev = c.Pos;
                var back = -(Quaternion.Euler(0, c.Heading, 0) * Vector3.forward);
                for (int i = 0; i < c.Segments.Length; i++)
                {
                    var s = c.Segments[i];
                    var want = prev + (s.position - prev).normalized * c.Radius * 0.75f * coil;
                    if ((s.position - prev).sqrMagnitude < 0.0001f) want = prev + back * c.Radius * 0.75f;
                    want += Vector3.Cross(back, Vector3.up) * Mathf.Sin(c.T * 7 - i * 0.9f) * c.Radius * 0.1f;
                    s.position = want;
                    prev = want;
                }
                if (c.State == 1) { c.Flash = Mathf.Max(c.Flash, 0.35f + 0.35f * Mathf.Sin(c.T * 30)); }
            }
            if (c.Kind == Kinds.Snapjaw && c.State == 1) c.Flash = Mathf.Max(c.Flash, 0.25f);
        }
    }
}
