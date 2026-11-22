using System;
using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>
    /// THE BASTION — Stage 2 boss. Harrowgate's last answer: a four-legged siege fortress that marches out of Fort
    /// Harrow. Readable patterns:
    ///   - Sweep laser: a thin aiming line, then a beam that sweeps and cuts through buildings. Dash through it.
    ///   - Barrage: artillery rings land around you; afterwards the reactor overheats and its armour opens. Bite it.
    ///   - Stomp: a shockwave ring rolls outward. Dash through the wavefront (dashing grants brief invulnerability).
    /// Phase two (below half) adds hunter drones and quickens everything.
    /// </summary>
    public sealed class Bastion : MonoBehaviour, IBoss
    {
        public PlayerCreature Player; public Ecosystem Eco; public CameraRig Rig; public AudioDirector Audio; public Town Town;
        public float Hp { get; private set; }
        public float MaxHp { get; private set; } = 2600;
        public float Radius { get; private set; } = 10;
        public bool Active { get; private set; }
        public Vector3 Position => _pos;
        public string Title => "THE BASTION";
        public bool Exposed => _state == S.Overheated;
        public int Phase => Hp < MaxHp * 0.5f ? 2 : 1;
        public event Action OnDefeated;
        public event Action OnIntroDone;

        enum S { Intro, Idle, LaserTele, Laser, Barrage, Overheated, StompTele, Stomp, Drones, Dying }
        S _state;
        float _t, _stateT, _flash, _biteCd, _laserAngle, _laserFrom, _stompR;
        int _cycle;
        Vector3 _pos;
        float _heading;
        Transform _hull, _turret, _reactor, _plateL, _plateR;
        readonly Transform[] _legs = new Transform[4];
        Material _armor, _core;
        LineRenderer _beam;

        public void Build()
        {
            var box = MeshKit.Box(Color.white, new Color(0.55f, 0.55f, 0.6f));
            var sphere = MeshKit.Blob(20, 14, Color.white, new Color(0.6f, 0.6f, 0.6f));
            _armor = Mats.BioUnique("bastion_armor", Palette.Hdr(Palette.Hex(0x9FB6FF), 0.4f), 0.5f, Palette.Hdr(Palette.Warning, 2f), 0.15f, 0);
            _armor.SetColor("_BaseColor", Palette.Hex(0x4A5238)); _armor.SetFloat("_Gloss", 0.6f); _armor.SetFloat("_VeinScale", 1.4f);
            _core = Mats.BioUnique("bastion_core", Palette.Hdr(Palette.Warning, 2f), 2f, Palette.Hdr(Palette.Warning, 4f), 1f, 0.05f);
            _core.SetColor("_BaseColor", Palette.Hex(0x3A1A0A));

            _hull = Part("Hull", transform, box, _armor, new Vector3(0, 0.9f, 0), new Vector3(1.3f, 0.55f, 1.7f));
            Part("Deck", _hull, box, _armor, new Vector3(0, 0.6f, -0.1f), new Vector3(0.8f, 0.4f, 0.6f));
            _turret = new GameObject("Turret").transform; _turret.SetParent(transform, false); _turret.localPosition = new Vector3(0, 1.45f, 0.15f);
            Part("TurretBody", _turret, box, _armor, Vector3.zero, new Vector3(0.7f, 0.35f, 0.8f));
            Part("Barrel", _turret, box, _armor, new Vector3(0, 0.02f, 0.85f), new Vector3(0.14f, 0.14f, 1.1f));
            Part("Barrel2", _turret, box, _armor, new Vector3(0.2f, 0.02f, 0.75f), new Vector3(0.1f, 0.1f, 0.9f));
            var eye = Mats.Bio("bastion_eye", Color.black, 0, Color.black, 0, 0, 1f); eye.SetColor("_EmissionColor", Palette.Hdr(Palette.Danger, 5f));
            Part("Eye", _turret, sphere, eye, new Vector3(-0.22f, 0.05f, 0.42f), Vector3.one * 0.16f);
            _reactor = Part("Reactor", transform, sphere, _core, new Vector3(0, 1.1f, -0.85f), Vector3.one * 0.45f);
            _plateL = Part("PlateL", transform, box, _armor, new Vector3(-0.16f, 1.1f, -0.92f), new Vector3(0.32f, 0.5f, 0.1f));
            _plateR = Part("PlateR", transform, box, _armor, new Vector3(0.16f, 1.1f, -0.92f), new Vector3(0.32f, 0.5f, 0.1f));
            for (int i = 0; i < 4; i++)
            {
                var pivot = new GameObject("Leg" + i).transform; pivot.SetParent(transform, false);
                pivot.localPosition = new Vector3(i < 2 ? -0.72f : 0.72f, 0.8f, i % 2 == 0 ? 0.65f : -0.65f);
                Part("Thigh", pivot, box, _armor, new Vector3(0, -0.15f, 0), new Vector3(0.22f, 0.45f, 0.22f));
                Part("Shin", pivot, box, _armor, new Vector3(i < 2 ? -0.12f : 0.12f, -0.55f, 0), new Vector3(0.18f, 0.55f, 0.18f));
                Part("Foot", pivot, box, _armor, new Vector3(i < 2 ? -0.12f : 0.12f, -0.82f, 0.05f), new Vector3(0.34f, 0.1f, 0.42f));
                _legs[i] = pivot;
            }
            var bgo = new GameObject("Beam"); bgo.transform.SetParent(transform, false);
            _beam = bgo.AddComponent<LineRenderer>();
            _beam.sharedMaterial = Mats.Particle(true, 0.9f, 1); _beam.positionCount = 2; _beam.useWorldSpace = true; _beam.enabled = false;
            _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var lg = new GameObject("BastionLight"); lg.transform.SetParent(transform, false); lg.transform.localPosition = new Vector3(0, 1.2f, -0.9f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = Palette.Warning; l.range = 3; l.intensity = 3;
            gameObject.SetActive(false);
        }

        Transform Part(string n, Transform parent, Mesh m, Material mat, Vector3 p, Vector3 s)
        {
            var go = new GameObject(n); go.transform.SetParent(parent, false);
            go.transform.localPosition = p; go.transform.localScale = s;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        public void Summon(float playerRadius)
        {
            Radius = Mathf.Max(9f, playerRadius * 2.8f);
            MaxHp = 2600; Hp = MaxHp;
            _pos = Town.BaseCenter; _pos.y = 0;
            var to = Player.transform.position - _pos; _heading = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            transform.localScale = Vector3.one * Radius;
            _armor.SetFloat("_Dissolve", 0); _core.SetFloat("_Dissolve", 0);
            gameObject.SetActive(true);
            Active = true;
            Go(S.Intro); _cycle = 0;
            Audio?.Play("roar", 1f, 0.5f);
        }

        public void Despawn() { Active = false; _beam.enabled = false; gameObject.SetActive(false); }

        void Go(S s) { _state = s; _stateT = 0; }

        void Update()
        {
            if (!Active) return;
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            _t += dt; _stateT += dt; _biteCd -= dt; _flash = Mathf.MoveTowards(_flash, 0, dt * 4);
            float spd = Phase == 2 ? 1.3f : 1f;
            var pp = Player.transform.position;
            var to = pp - _pos; to.y = 0;
            float dist = to.magnitude;
            bool walking = false;

            switch (_state)
            {
                case S.Intro:
                    walking = Walk(to, dist, dt, 5f, 18 + Radius);
                    Rig.Cinematic(_pos + Vector3.up * Radius, Radius * 4.2f, 1, 38);
                    if (_stateT > 0.5f && _stateT < 4) Rig.Shake(dt * 0.4f);
                    if (_stateT > 4.6f) { Rig.Cinematic(_pos, Radius * 4, 0); Go(S.Idle); OnIntroDone?.Invoke(); }
                    break;
                case S.Idle:
                    walking = Walk(to, dist, dt, 3.2f * spd, 14 + Radius);
                    if (_stateT > 1.5f / spd)
                    {
                        _cycle++;
                        int pick = _cycle % (Phase == 2 ? 4 : 3);
                        if (pick == 1) { Go(S.LaserTele); Audio?.Play("charge", 0.9f, 1.2f); }
                        else if (pick == 2) { Go(S.Barrage); Audio?.Play("charge", 0.9f, 0.7f); }
                        else if (pick == 3) Go(S.Drones);
                        else { Go(S.StompTele); Audio?.Play("charge", 1f, 0.5f); }
                    }
                    break;
                case S.LaserTele:
                {
                    _laserAngle = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    _laserFrom = _laserAngle - 40;
                    AimBeam(_laserFrom, 0.12f, Palette.Hdr(Palette.Danger, 1.5f));
                    if (_stateT > 1.2f / spd) Go(S.Laser);
                    break;
                }
                case S.Laser:
                {
                    float u = _stateT / 1.7f;
                    float ang = Mathf.Lerp(_laserFrom, _laserFrom + 80, u);
                    var dir = AimBeam(ang, 0.9f + Mathf.Sin(_t * 60) * 0.15f, Palette.Hdr(Palette.Danger, 4f));
                    var origin = _pos + Vector3.up * Radius * 1.2f;
                    // beam damage along the line
                    var rel = pp - origin; rel.y = 0;
                    float along = Vector3.Dot(rel, dir);
                    if (along > 0 && (rel - dir * along).magnitude < 1.2f + Player.Radius * 0.6f) Player.TakeDamage(14, origin + dir * along);
                    if ((int)(_stateT * 10) != (int)((_stateT - dt) * 10))
                        for (int i = 1; i < 6; i++) Town.DamageStructures(new Vector3(origin.x, 0, origin.z) + dir * i * 8, 2.5f, 120);
                    if (_stateT % 0.1f < dt) Fx.I.Burst(origin + dir * Mathf.Min(60, along > 0 ? along : 30), Palette.Hdr(Palette.Warning, 3f), 3, 4, 0.6f, 0.3f);
                    Rig.Shake(dt * 0.3f);
                    if (u >= 1) { _beam.enabled = false; Go(S.Idle); }
                    break;
                }
                case S.Barrage:
                {
                    _turret.localRotation = Quaternion.Euler(-35, 0, 0);
                    if (_stateT < dt * 1.01f)
                    {
                        int n = Phase == 2 ? 8 : 6;
                        for (int i = 0; i < n; i++)
                        {
                            float a = i / (float)n * Mathf.PI * 2 + _t;
                            float r = i == 0 ? 0 : 5 + Player.Radius * 2;
                            Projectiles.I.Strike(pp + Player.Velocity * 0.8f + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r, 1.6f + i * 0.08f, 3 + Player.Radius * 0.6f, 18);
                        }
                        Fx.I.Burst(_turret.position + Vector3.up * Radius * 0.3f, Palette.Hdr(Palette.Warning, 3f), 30, 12, Radius * 0.1f, 0.5f);
                        Audio?.Play("slam", 1f, 1.2f);
                    }
                    if (_stateT > 2.2f) { Go(S.Overheated); Audio?.Play("vent", 1f, 0.7f); }
                    break;
                }
                case S.Overheated:
                    _turret.localRotation = Quaternion.Slerp(_turret.localRotation, Quaternion.Euler(20, 0, 0), dt * 3);
                    if (UnityEngine.Random.value < dt * 20) Fx.I.Burst(_reactor.position + Vector3.up * Radius * 0.3f, new Color(0.7f, 0.7f, 0.75f, 1), 2, 3, Radius * 0.12f, 1.2f, true);
                    if (_stateT > 3.8f / Mathf.Sqrt(spd)) Go(S.Idle);
                    break;
                case S.StompTele:
                    transform.position = _pos + Vector3.up * Mathf.Sin(Mathf.Clamp01(_stateT / 0.9f) * Mathf.PI * 0.5f) * Radius * 0.25f;
                    _flash = 0.2f + Mathf.Abs(Mathf.Sin(_stateT * 12)) * 0.3f;
                    if (_stateT > 0.9f / spd) { Go(S.Stomp); _stompR = Radius * 0.5f; Rig.Shake(0.6f); Audio?.Play("slam", 1f, 0.6f); }
                    break;
                case S.Stomp:
                {
                    float prev = _stompR;
                    _stompR += dt * (22 + Radius);
                    if ((int)(_stateT * 12) != (int)((_stateT - dt) * 12)) Fx.I.Ring(_pos, Palette.Hdr(Palette.Warning, 3f), prev, _stompR, 0.25f, 0.05f);
                    if (dist + Player.Radius * 0.5f > prev && dist - Player.Radius * 0.5f < _stompR) Player.TakeDamage(20, _pos);
                    Town.DamageStructures(_pos, _stompR, 30 * dt);
                    if (_stompR > Radius * 4.5f) Go(S.Idle);
                    break;
                }
                case S.Drones:
                    for (int i = 0; i < 3; i++) { var c = Eco.Spawn(Kinds.Drone, _pos + Vector3.up * 3 + UnityEngine.Random.insideUnitSphere * Radius, Kinds.Drone.Size); c.Cooldown = 2 + i; }
                    Audio?.Play("zap", 0.8f, 0.7f);
                    Go(S.Idle);
                    break;
                case S.Dying:
                {
                    float u = _stateT / 3f;
                    Time.timeScale = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(u * 1.2f));
                    if ((int)(_stateT * 7) != (int)((_stateT - dt) * 7))
                    {
                        Fx.I.Burst(_pos + Vector3.up * Radius + UnityEngine.Random.insideUnitSphere * Radius, Palette.Hdr(Palette.Warning, 4f), 24, 12, Radius * 0.12f, 0.8f);
                        Audio?.Play("slam", 0.8f, UnityEngine.Random.Range(0.8f, 1.3f));
                    }
                    transform.position = _pos + Vector3.down * Radius * 0.4f * u;
                    transform.rotation = Quaternion.Euler(u * 12, _heading, u * 8);
                    _armor.SetFloat("_Dissolve", Mathf.Clamp01(u - 0.2f));
                    Rig.Cinematic(_pos + Vector3.up * Radius, Radius * 4, 1, 45);
                    Rig.Shake(dt * 0.6f);
                    if (u >= 1)
                    {
                        Time.timeScale = 1;
                        Fx.I.Ring(_pos, Palette.Hdr(Palette.Warning, 4f), Radius, Radius * 8, 1.4f, 0.06f);
                        Fx.I.Burst(_pos + Vector3.up * Radius, Palette.Hdr(Palette.Warning, 4f), 90, 18, Radius * 0.2f, 1.6f);
                        for (int i = 0; i < 12; i++) Eco.SpawnMorsel(_pos + UnityEngine.Random.insideUnitSphere * Radius, 22f, Dna.Alloy);
                        Rig.Cinematic(_pos, Radius * 4, 0);
                        Despawn();
                        OnDefeated?.Invoke();
                    }
                    break;
                }
            }

            if (_state != S.StompTele && _state != S.Dying) transform.position = _pos;
            if (_state != S.Dying) transform.rotation = Quaternion.Euler(0, _heading, 0);
            if (_state != S.Barrage && _state != S.Overheated && _state != S.Dying)
            {
                var local = Quaternion.Inverse(transform.rotation) * to;
                float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                _turret.localRotation = Quaternion.Slerp(_turret.localRotation, Quaternion.Euler(0, Mathf.Clamp(yaw, -100, 100), 0), dt * 3);
            }
            // legs: a diagonal gait while walking, settled while standing
            for (int i = 0; i < 4; i++)
            {
                float phase = (i == 0 || i == 3) ? 0 : Mathf.PI;
                float swing = walking ? Mathf.Sin(_t * 4 * spd + phase) * 22 : 0;
                _legs[i].localRotation = Quaternion.Slerp(_legs[i].localRotation, Quaternion.Euler(swing, 0, 0), dt * 8);
            }
            if (walking && ((int)(_t * 4 * spd / Mathf.PI)) != ((int)((_t - dt) * 4 * spd / Mathf.PI))) { Rig.Shake(0.08f); Fx.I.Burst(_pos, new Color(0.4f, 0.38f, 0.42f, 1), 4, 3, Radius * 0.15f, 0.8f, true); }
            float open = Exposed ? 1 : 0;
            _plateL.localPosition = Vector3.Lerp(_plateL.localPosition, new Vector3(-0.16f - open * 0.32f, 1.1f, -0.92f), dt * 6);
            _plateR.localPosition = Vector3.Lerp(_plateR.localPosition, new Vector3(0.16f + open * 0.32f, 1.1f, -0.92f), dt * 6);
            _core.SetColor("_EmissionColor", Palette.Hdr(Palette.Warning, 0.4f + open * (2f + Mathf.Sin(_t * 12))));
            _armor.SetFloat("_Flash", _flash);
            _armor.SetFloat("_VeinAmount", 0.15f + (Phase == 2 ? 0.5f : 0));

            if (_state != S.Intro && _state != S.Dying) Contact(pp, dist);
        }

        bool Walk(Vector3 to, float dist, float dt, float speed, float keep)
        {
            float target = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            _heading = Mathf.MoveTowardsAngle(_heading, target, dt * 40);
            if (dist <= keep) return false;
            var fwd = Quaternion.Euler(0, _heading, 0) * Vector3.forward;
            _pos += fwd * speed * dt;
            _pos = Town.Clamp(_pos, Radius);
            Town.DamageStructures(_pos, Radius * 0.9f, 400 * dt);   // it walks through the town
            return true;
        }

        Vector3 AimBeam(float angleDeg, float width, Color hdr)
        {
            var dir = Quaternion.Euler(0, angleDeg, 0) * Vector3.forward;
            var origin = _pos + Vector3.up * Radius * 1.2f;
            _beam.enabled = true;
            _beam.SetPosition(0, origin + dir * Radius * 0.9f);
            _beam.SetPosition(1, new Vector3(origin.x, 1, origin.z) + dir * 70);
            _beam.widthMultiplier = width;
            _beam.startColor = hdr; _beam.endColor = hdr;
            return dir;
        }

        void Contact(Vector3 pp, float dist)
        {
            float reach = Radius * 0.9f + Player.Radius * 0.8f;
            if (dist > reach) return;
            if (Exposed)
            {
                if (_biteCd > 0) return;
                _biteCd = 0.35f;
                Damage(32 * Player.Genome.Current.Bite * (0.5f + Player.Radius * 0.35f), true);
                Player.Body.Swallow(0.5f);
                Fx.I.Absorb(_reactor.position, Player.MouthPoint, Palette.Hdr(Palette.Warning, 2.5f), 8, 0.8f);
                Audio?.Gulp(0.8f, false);
                Rig.Punch(0.03f);
            }
            else
            {
                Player.TakeDamage(10, _pos);
                var push = (pp - _pos); push.y = 0;
                Player.transform.position += push.normalized * (reach - dist);
            }
        }

        public void Damage(float amount, bool bite = false)
        {
            if (!Active || _state == S.Dying || _state == S.Intro) return;
            if (!Exposed && !bite) amount *= 0.25f;
            int before = Phase;
            Hp -= amount;
            _flash = Mathf.Max(_flash, 0.5f);
            if (Phase == 2 && before == 1) { Audio?.Play("roar", 1f, 0.7f); Rig.Shake(0.5f); Go(S.Drones); }
            if (Hp <= 0) { Hp = 0; _beam.enabled = false; Projectiles.I.ClearHostile(); Go(S.Dying); Audio?.Play("roar", 1f, 0.45f); }
        }

        public bool HitTest(Vector3 p, float r, float dmg)
        {
            if (!Active) return false;
            var d = p - _pos; d.y = 0;
            if (d.magnitude > Radius * 0.85f + r) return false;
            Damage(dmg);
            return true;
        }

        public Vector3? AimPoint(Vector3 from, float range)
        {
            if (!Active || _state == S.Intro || _state == S.Dying) return null;
            return (from - _pos).magnitude < range + Radius ? _pos + Vector3.up * Radius * 0.6f : (Vector3?)null;
        }
    }
}
