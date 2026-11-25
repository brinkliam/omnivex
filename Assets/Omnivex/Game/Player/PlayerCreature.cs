using System;
using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>
    /// The Omnivex. Steers with momentum, swallows anything smaller, and runs every mutation's real behaviour:
    /// reach, armour, thorns, dash, lure, gravity, volt discharge, acid spit, regeneration, spore spawnlings.
    /// </summary>
    public sealed class PlayerCreature : MonoBehaviour
    {
        public Genome Genome { get; private set; }
        public CreatureBody Body { get; private set; }
        public WorldBase Arena; public Ecosystem Eco; public CameraRig Rig; public AudioDirector Audio;
        public float Radius { get; private set; } = 0.5f;
        public float Health { get; private set; }
        public float MaxHealth => Genome.Current.MaxHealth;
        public bool IsAlive => Health > 0;
        public bool Controllable = true;
        /// <summary>Set once the run is over (results showing): nothing can hurt or kill the creature any more.</summary>
        public bool RunEnded;
        public bool Hidden { get; private set; }
        public float FrenzyTime;        // rewarded-ad boost: +50% biomass
        public Vector3 Velocity { get; private set; }
        public Vector3 MouthPoint => transform.position + transform.forward * Radius * 0.55f;
        public float MouthRadius => Radius * (0.85f + (Genome.Current.Reach - 1f) * 0.55f) * (_dashT > 0 ? 1.25f : 1f);
        public float DashCooldown01 => _dashCd <= 0 ? 1 : 1 - _dashCd / DashCooldownTotal;
        float DashCooldownTotal => Mathf.Max(0.9f, 2.4f - Genome.Current.Dash * 0.35f);

        public event Action<Critter, float> OnAte;
        public event Action<int> OnEvolutionReady;
        public event Action<float> OnHurt;
        public event Action OnDied;
        public int Kills;

        float _heading, _dashT, _dashCd, _invuln, _voltT, _acidT, _sporeT, _growPulse, _stillT, _toxHint, _cannonT, _empT;
        bool _wasDashing;
        public bool Dashing => _dashT > 0;
        Vector3 _vel;

        public void Init(SkinDef skin)
        {
            var bodyGo = new GameObject("Body"); bodyGo.transform.SetParent(transform, false);
            Body = bodyGo.AddComponent<CreatureBody>();
            Body.Init(skin);
        }

        public void BeginRun(Genome g)
        {
            RunEnded = false;
            Genome = g;
            Body.Clear();
            Body.Sync(g, false);
            Radius = g.Radius;
            Health = MaxHealth;
            Kills = 0; FrenzyTime = 0;
            _vel = Vector3.zero; _dashCd = 0; _invuln = 1.5f;
            transform.position = new Vector3(0, Arena.HeightAt(0, -12) + Radius, -12);
            _heading = 0;
            transform.localScale = Vector3.one * Radius * 2;
            gameObject.SetActive(true);
        }

        public void Revive(float fraction)
        {
            Health = MaxHealth * fraction;
            _invuln = 3f;
            Fx.I.Ring(transform.position, Palette.Hdr(Palette.Accent, 3f), Radius, Radius * 10, 0.9f);
            Eco.ForEachNear(transform.position, Radius * 9, (c, d) => { if (c.Hostile) c.Vel = (c.Pos - transform.position).normalized * 16; });
        }

        public void Dash()
        {
            if (!Controllable || !IsAlive || _dashCd > 0) return;
            _dashT = 0.32f; _dashCd = DashCooldownTotal;
            var dir = transform.forward;
            _vel = dir * Speed() * (2.6f + Genome.Current.Dash * 0.3f);
            Audio?.Play("dash", 0.7f, 1f + UnityEngine.Random.Range(-0.05f, 0.05f));
            Fx.I.Burst(transform.position - dir * Radius, Palette.Hdr(Palette.Accent, 2f), 10, 3, Radius * 0.3f, 0.4f);
            Rig.Punch(0.04f);
        }

        float Speed() => Genome.Current.Speed * (0.55f + Radius * 0.9f) * (Hidden ? 0.8f : 1f);

        void Update()
        {
            if (Genome == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            var s = Genome.Current;

            // grow toward the genome's size
            float target = Genome.Radius;
            if (target > Radius + 0.001f) { Radius = Mathf.Lerp(Radius, target, 1 - Mathf.Exp(-dt * 4)); }
            transform.localScale = Vector3.one * Radius * 2 * (1 + _growPulse * 0.08f);
            _growPulse = Mathf.MoveTowards(_growPulse, 0, dt * 2);

            // movement
            Vector2 input = Controllable && IsAlive ? InputRouter.I.Move : Vector2.zero;
            var want = new Vector3(input.x, 0, input.y) * Speed();
            _dashT -= dt; _dashCd -= dt; _invuln -= dt;
            if (_wasDashing && _dashT <= 0 && s.Quake > 0) Quake(s);
            _wasDashing = _dashT > 0;
            if (_dashT <= 0)
            {
                float accel = 1 - Mathf.Exp(-dt * (2.4f + s.Turn * 0.35f));
                _vel = Vector3.Lerp(_vel, want, accel);
            }
            else _vel = Vector3.Lerp(_vel, transform.forward * _vel.magnitude, dt * 4);
            var pos = transform.position + _vel * dt;
            pos = Arena.Resolve(pos, Radius);
            float ground = Arena.HeightAt(pos.x, pos.z) + Radius * 0.85f;
            pos.y = Mathf.Lerp(transform.position.y, ground + Mathf.Sin(Time.time * 2.2f) * Radius * 0.05f, 1 - Mathf.Exp(-dt * 8));
            Velocity = (pos - transform.position) / dt;
            transform.position = pos;
            if (_vel.sqrMagnitude > 0.04f)
            {
                float targetH = Mathf.Atan2(_vel.x, _vel.z) * Mathf.Rad2Deg;
                _heading = Mathf.MoveTowardsAngle(_heading, targetH, dt * (200 + s.Turn * 40));
            }
            float bank = Mathf.Clamp(Mathf.DeltaAngle(_heading, Mathf.Atan2(want.x, want.z) * Mathf.Rad2Deg), -40, 40) * (want.sqrMagnitude > 0.1f ? 0.3f : 0);
            transform.rotation = Quaternion.Euler(0, _heading, -bank);
            Body.Speed01 = Mathf.Clamp01(_vel.magnitude / Mathf.Max(1, Speed() * 1.6f));

            // stealth: drift slowly inside the kelp and predators lose you
            var zone = Arena.ZoneAt(pos);
            _stillT = _vel.magnitude < Speed() * 0.35f ? _stillT + dt : 0;
            Hidden = zone.Cover && _stillT > 0.6f;

            if (IsAlive && s.Regen > 0) Health = Mathf.Min(MaxHealth, Health + s.Regen * dt);
            if (FrenzyTime > 0) FrenzyTime -= dt;

            Abilities(s, dt);

            Rig.SubjectRadius = Radius;
            Rig.SightBonus = s.Sight;
            Rig.TargetVelocity = _vel;
        }

        void Abilities(Stats s, float dt)
        {
            var p = transform.position;
            // lure + gravity: small things drift into the mouth
            float pull = s.Lure + s.Gravity * 1.6f;
            if (pull > 0)
            {
                float range = Radius * (3.5f + s.Lure * 1.2f + s.Gravity * 3.5f) + 2;
                Eco.ForEachNear(p, range, (c, d) =>
                {
                    if (c.Kind.Brain == Brain.Ally || c.Radius >= Radius * c.Kind.EatRatio) return;
                    var to = (MouthPoint - c.Pos); to.y = 0;
                    float str = (1.2f + pull * 1.4f) * (0.5f + Radius * 0.6f) * (1 - d / range + 0.25f);
                    c.Pos += to.normalized * str * dt;
                    if (s.Gravity > 0) c.Pos += Vector3.Cross(Vector3.up, to.normalized) * str * 0.5f * dt;   // spiral in
                });
            }
            // volt discharge
            if (s.Volt > 0)
            {
                float interval = Mathf.Max(1.6f, 4.6f - (s.Volt - 1) * 0.9f);
                _voltT += dt;
                Body.ChargeVolt = _voltT / interval;
                if (_voltT >= interval)
                {
                    _voltT = 0;
                    float range = Radius * 3.2f + 2.2f;
                    int hits = 0;
                    Eco.ForEachNear(p, range, (c, d) =>
                    {
                        if (c.Kind.Brain == Brain.Ally || c.Kind.Brain == Brain.Morsel || hits > 7) return;
                        hits++;
                        Fx.I.Lightning(p + Vector3.up * Radius * 0.6f, c.Pos, Palette.Hdr(Palette.Of(Dna.Volt), 4f));
                        Eco.Damage(c, 18 * s.Volt * (0.6f + Radius * 0.5f));
                    });
                    BossHook?.Invoke(p, range, 18 * s.Volt * (0.6f + Radius * 0.5f));
                    Arena.DamageStructures(p, range, 12 * s.Volt * Radius, Instigator.Player);
                    Fx.I.Ring(p, Palette.Hdr(Palette.Of(Dna.Volt), 2.5f), Radius, range, 0.35f, 0.08f);
                    Audio?.Play("zap", 0.6f, 1f + UnityEngine.Random.Range(-0.1f, 0.1f));
                }
            }
            // acid spit
            if (s.Acid > 0)
            {
                _acidT += dt;
                float interval = Mathf.Max(0.45f, 1.7f / s.Acid);
                if (_acidT >= interval)
                {
                    var target = Eco.NearestThreat(p, 9 + Radius * 4);
                    Vector3? aim = target != null ? target.Pos : BossAim?.Invoke(p, 12 + Radius * 4);
                    if (aim.HasValue)
                    {
                        _acidT = 0;
                        Projectiles.I.Spit(MouthPoint + Vector3.up * Radius * 0.2f, aim.Value, 14 + Radius * 4, 12 * s.Acid * (0.6f + Radius * 0.5f), Radius * 0.22f);
                        Audio?.Play("spit", 0.5f, 1.1f);
                    }
                }
            }
            // railspine: slugs at the nearest threat, the boss, or the nearest building
            if (s.Cannon > 0)
            {
                _cannonT += dt;
                if (_cannonT >= Mathf.Max(0.6f, 2.2f / s.Cannon))
                {
                    var target = Eco.NearestThreat(p, 14 + Radius * 4);
                    Vector3? aim = target != null ? target.Pos : BossAim?.Invoke(p, 16 + Radius * 4);
                    if (!aim.HasValue && Arena is Town town) aim = NearestBuilding(town, p, 10 + Radius * 3);
                    if (aim.HasValue)
                    {
                        _cannonT = 0;
                        Projectiles.I.Slug(p + Vector3.up * Radius * 0.8f, aim.Value, 30 * s.Cannon * (0.6f + Radius * 0.4f), 0.25f + Radius * 0.08f);
                        Audio?.Play("zap", 0.45f, 1.6f);
                    }
                }
            }
            // EMP: stall every vehicle and aircraft nearby
            if (s.Emp > 0)
            {
                _empT += dt;
                if (_empT >= Mathf.Max(3f, 7f - s.Emp * 1.5f))
                {
                    _empT = 0;
                    float range = Radius * 4 + 6;
                    Eco.ForEachNear(p, range + 10, (c, d) => { if (c.Kind.Military || c.Kind == Kinds.Car) { c.Stun = 2.5f + s.Emp; Eco.Damage(c, 10 * s.Emp); } });
                    Fx.I.Ring(p, Palette.Hdr(Palette.Of(Dna.Volt), 3f), Radius, range, 0.5f, 0.06f);
                    Audio?.Play("zap", 0.8f, 0.6f);
                }
            }
            // spores / crown: allies hatch on their own
            if (Genome.Level("crown") > 0)
            {
                _sporeT += dt;
                if (_sporeT > 7f && Eco.Allies < 8) { _sporeT = 0; HatchSpawnling(p); }
            }
        }

        public Action<Vector3, float, float> BossHook;     // area damage that should also reach the boss
        public Func<Vector3, float, Vector3?> BossAim;

        void HatchSpawnling(Vector3 at)
        {
            var c = Eco.Spawn(Kinds.Spawnling, at + UnityEngine.Random.insideUnitSphere * Radius, Radius * 0.42f);
            Fx.I.Burst(c.Pos, Palette.Hdr(Palette.Accent, 2f), 10, 3, c.Radius * 0.5f, 0.5f);
        }

        /// <summary>Called by the ecosystem for each nearby critter: eat it, get bitten, or bump.</summary>
        public void Interact(Critter c, float dist)
        {
            if (!IsAlive || !Controllable) return;
            var k = c.Kind;
            if (k.Brain == Brain.Ally) return;
            if (k.Altitude > 0 && Radius < k.Altitude * 0.35f) return;   // can't reach flyers until you're tall enough
            bool edible = k.Brain == Brain.Morsel || c.Radius < Radius * k.EatRatio;
            float mouthDist = (c.Pos - MouthPoint).magnitude;
            if (edible && (mouthDist < MouthRadius + c.Radius * 0.6f || dist < Radius * 0.9f))
            {
                Eat(c);
                return;
            }
            float touch = Radius * 0.95f + c.Radius * 0.8f;
            if (dist < touch)
            {
                // bigger hostile things bite
                if (c.Hostile && !edible && (c.State == 2 || (k.Brain == Brain.Predator && c.Cooldown <= 0)))
                {
                    float dmg = k.Damage * Mathf.Sqrt(c.Radius / Radius) * (c.Elite ? 1.5f : 1f);
                    TakeDamage(dmg, c.Pos);
                    c.Cooldown = 1.3f; c.State = 0;
                    var s = Genome.Current;
                    if (s.Thorns > 0) { Eco.Damage(c, s.Thorns * (0.7f + Radius * 0.4f)); Fx.I.Burst(c.Pos, Palette.Hdr(Palette.Of(Dna.Mineral), 2.5f), 8, 4, 0.2f * Radius, 0.4f); }
                }
                else if (k.ContactDamage > 0 && !edible && c.Cooldown <= 0)
                {
                    TakeDamage(k.ContactDamage, c.Pos);
                    c.Cooldown = 1f;
                    if (k == Kinds.Voltling) Fx.I.Lightning(c.Pos, transform.position, Palette.Hdr(Palette.Of(Dna.Volt), 3));
                }
                // soft push so big things feel solid
                var away = transform.position - c.Pos; away.y = 0;
                if (away.sqrMagnitude > 0.0001f)
                {
                    float pen = touch - dist;
                    transform.position += away.normalized * pen * 0.5f;
                    c.Pos -= away.normalized * pen * 0.5f;
                }
            }
        }

        void Eat(Critter c)
        {
            var k = c.Kind;
            float value = c.Value > 0 ? c.Value : Genome.ValueOf(c.Radius) * (k.Rare ? 3f : 1f);
            float mult = FrenzyTime > 0 ? 1.5f : 1f;
            int before = Genome.PendingEvolutions;
            int unlocked = Genome.Feed(value, c.Dna, mult);
            Eco.BeginDigest(c);
            Body.Swallow(Mathf.Clamp(c.Radius / Radius, 0.2f, 0.9f));
            _growPulse = Mathf.Min(1, _growPulse + c.Radius / Radius);
            var col = Palette.Of(c.Dna);
            Fx.I.Absorb(c.Pos, MouthPoint, Palette.Hdr(col, 2.2f), 6 + (int)(c.Radius / Radius * 10), c.Radius);
            Rig.Punch(0.01f + c.Radius / Radius * 0.02f);
            Audio?.Gulp(c.Radius / Radius, k.Brain == Brain.Morsel);
            // the toxin tax: Toxblooms burn going down unless you're adapted to toxins
            if (k.ContactDamage > 0 && k.Dna == Dna.Toxin && Genome.Share(Dna.Toxin) < 0.25f && Genome.Level("acid") == 0)
                TakeDamage(k.ContactDamage, c.Pos, true);
            if (k != Kinds.Morsel) Kills++;
            if (UnityEngine.Random.value < Genome.Current.Spores && Eco.Allies < 8 && k.Brain != Brain.Morsel) HatchSpawnling(c.Pos);
            OnAte?.Invoke(c, value * mult);
            if (unlocked > 0) OnEvolutionReady?.Invoke(Genome.PendingEvolutions);
        }

        static Vector3? NearestBuilding(Town town, Vector3 p, float range)
        {
            Building best = null; float bd = range * range;
            foreach (var b in town.Buildings)
            {
                if (!b.Standing || b.Tree) continue;
                float d = (new Vector2(p.x, p.z) - b.C).sqrMagnitude;
                if (d < bd) { bd = d; best = b; }
            }
            return best == null ? (Vector3?)null : new Vector3(best.C.x, 1, best.C.y);
        }

        void Quake(Stats s)
        {
            float range = Radius * (2.5f + s.Quake);
            Fx.I.Ring(transform.position, Palette.Hdr(Palette.Of(Dna.Mineral), 3f), Radius * 0.5f, range, 0.6f, 0.08f);
            Fx.I.Burst(transform.position, new Color(0.5f, 0.48f, 0.5f, 1), 14, 6, Radius * 0.4f, 1f, true);
            Rig.Shake(0.3f); Rig.Punch(-0.05f);
            Audio?.Play("slam", 0.7f, 1.3f);
            Arena.DamageStructures(transform.position, range, 60 * s.Quake * Radius, Instigator.Player);
            Eco.ForEachNear(transform.position, range, (c, d) => { if (c.Hostile || c.Kind == Kinds.Car) { c.Stun = 1.2f; Eco.Damage(c, 25 * s.Quake); } });
            BossHook?.Invoke(transform.position, range, 40 * s.Quake);
        }

        /// <summary>Swallow a piece of scenery (a tree, a building's remains) for biomass.</summary>
        public void Consume(float value, Dna dna, Vector3 at, float bulge)
        {
            int unlocked = Genome.Feed(value, dna, FrenzyTime > 0 ? 1.5f : 1f);
            Fx.I.Absorb(at, MouthPoint, Palette.Hdr(Palette.Of(dna), 2.2f), 8, Radius * 0.4f);
            Body.Swallow(bulge);
            _growPulse = Mathf.Min(1, _growPulse + 0.3f);
            Audio?.Gulp(0.5f, false);
            if (unlocked > 0) OnEvolutionReady?.Invoke(Genome.PendingEvolutions);
        }

        public void Heal(float fraction) { Health = Mathf.Min(MaxHealth, Health + MaxHealth * fraction); }

        public void PlaceAt(Vector3 p)
        {
            _vel = Vector3.zero;
            transform.position = new Vector3(p.x, Arena.HeightAt(p.x, p.z) + Radius, p.z);
            _heading = 0;
        }

        public void FeedFromAlly(Critter victim)
        {
            float value = Genome.ValueOf(victim.Radius) * 0.6f;
            int unlocked = Genome.Feed(value, victim.Dna, FrenzyTime > 0 ? 1.5f : 1f);
            Fx.I.Absorb(victim.Pos, transform.position, Palette.Hdr(Palette.Accent, 1.5f), 4, victim.Radius);
            OnAte?.Invoke(victim, value);
            if (unlocked > 0) OnEvolutionReady?.Invoke(Genome.PendingEvolutions);
        }

        public void TakeDamage(float amount, Vector3 from, bool ignoreInvuln = false)
        {
            if (RunEnded || !IsAlive || (_invuln > 0 && !ignoreInvuln)) return;
            if (_dashT > 0 && !ignoreInvuln) { Fx.I.Burst(transform.position, Palette.Hdr(Palette.Accent, 2f), 4, 3, Radius * 0.2f, 0.3f); return; }   // dashing slips through
            float dmg = amount * (1 - Genome.Current.Armor);
            Health -= dmg;
            _invuln = 0.45f;
            Body.Hit();
            var away = transform.position - from; away.y = 0;
            _vel += away.normalized * (4 + Radius * 2);
            Rig.Shake(0.25f + dmg / MaxHealth);
            Fx.I.Burst(transform.position, Palette.Hdr(Palette.Danger, 2f), 10, 4, Radius * 0.3f, 0.5f, true);
            Audio?.Play("hurt", 0.8f, 1f + UnityEngine.Random.Range(-0.08f, 0.08f));
            OnHurt?.Invoke(dmg);
            if (Health <= 0)
            {
                Health = 0;
                Fx.I.Burst(transform.position, Palette.Hdr(Palette.Accent, 3f), 40, 7, Radius * 0.6f, 1.2f);
                Fx.I.Ring(transform.position, Palette.Hdr(Palette.Accent, 3f), Radius, Radius * 6, 0.8f);
                OnDied?.Invoke();
            }
        }

        public void Mutated()
        {
            Health = Mathf.Min(MaxHealth, Health + MaxHealth * 0.25f);
            Body.Sync(Genome, true);
            _growPulse = 1;
        }
    }
}
