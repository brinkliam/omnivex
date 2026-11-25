using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>
    /// Harrowgate's response, from one to five stars. Loud play (collapsing buildings, eating crowds, killing soldiers)
    /// raises the alert; hiding in a park or wearing Chameleon Skin lets it cool. Each star unlocks a new answer:
    /// patrol cars, rifle squads, gunships, siege tanks, and at five stars hunter drones.
    /// </summary>
    public sealed class Military : MonoBehaviour
    {
        public Ecosystem Eco; public PlayerCreature Player; public Town Town; public AudioDirector Audio;
        public float Alert { get; private set; }
        public int Stars => Mathf.Clamp(Mathf.FloorToInt(Alert), 0, 5);
        public bool Active;
        public float Quiet { get; private set; }   // seconds since the last loud act
        float _spawnT;
        int _lastStars;
        public System.Action<int> OnStarsChanged;
        Rng _rng = new Rng(77);

        public void Begin() { Alert = 0.4f; Quiet = 0; _lastStars = 0; Active = true; }
        public void End() { Active = false; Alert = 0; }

        float Gain => Mathf.Max(0.25f, 1f - (Player.Genome?.Current.Stealth ?? 0));

        public void Noise(float amount)
        {
            if (!Active) return;
            Alert = Mathf.Min(5.5f, Alert + amount * Gain);
            Quiet = 0;
        }

        public void OnAte(Critter c)
        {
            if (c.Kind == Kinds.Townsfolk) Noise(0.035f);
            else if (c.Kind == Kinds.Car) Noise(0.06f);
            else if (c.Kind.Military) Noise(0.2f);
        }

        public void OnBuilding(Building b) => Noise(b.Tree ? 0.02f : 0.25f + b.H * 0.02f);

        void Update()
        {
            if (!Active || Player == null || !Player.IsAlive) return;
            float dt = Time.deltaTime;
            Quiet += dt;
            // cools whenever the player stops making noise, faster at high alert (others' destruction doesn't count)
            float decay = Player.Hidden ? 0.28f + 0.04f * Stars : Quiet > 8 ? 0.08f + 0.03f * Stars : 0;
            Alert = Mathf.Max(0, Alert - decay * dt);
            int stars = Stars;
            if (stars != _lastStars)
            {
                if (stars > _lastStars) Audio?.Play("charge", 0.6f, 0.6f + stars * 0.1f);
                _lastStars = stars;
                OnStarsChanged?.Invoke(stars);
            }

            _spawnT -= dt;
            if (_spawnT > 0) return;
            _spawnT = 1.2f;
            int police = 0, soldiers = 0, helis = 0, tanks = 0, drones = 0;
            foreach (var c in Eco.Live)
            {
                if (!c.Alive) continue;
                if (c.Kind == Kinds.Police) police++;
                else if (c.Kind == Kinds.Soldier) soldiers++;
                else if (c.Kind == Kinds.Heli) helis++;
                else if (c.Kind == Kinds.Tank) tanks++;
                else if (c.Kind == Kinds.Drone) drones++;
            }
            if (stars >= 1 && police < 2 + stars) { Spawn(Kinds.Police, 1); return; }
            if (stars >= 2 && soldiers < (stars - 1) * 4) { Spawn(Kinds.Soldier, 4); return; }
            if (stars >= 3 && helis < stars - 2) { Spawn(Kinds.Heli, 1); return; }
            if (stars >= 4 && tanks < stars - 3) { Spawn(Kinds.Tank, 1, true); return; }
            if (stars >= 5 && drones < 4) Spawn(Kinds.Drone, 2);
        }

        void Spawn(CritterKind k, int n, bool fromBase = false)
        {
            var pp = Player.transform.position;
            Vector3 at;
            if (fromBase) at = Town.BaseCenter;
            else
            {
                float a = _rng.Range(0, Mathf.PI * 2);
                at = pp + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (22 + Player.Radius * 9);
                at = Town.Clamp(at, 6);
            }
            for (int i = 0; i < n; i++)
            {
                var c = Eco.Spawn(k, at + new Vector3(_rng.Range(-2f, 2f), 0, _rng.Range(-2f, 2f)), k.Size);
                c.Cooldown = _rng.Range(1.5f, 3f);
                c.Wander = _rng.Range(0, 100f);
            }
        }
    }
}
