using OMX.Core;
using System.Collections.Generic;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>
    /// A stage's playable world. The tide pool (Arena) and the town (Town) both implement this, so the creature,
    /// the ecosystem, projectiles and hazards don't care which stage they're in.
    /// </summary>
    public abstract class WorldBase : MonoBehaviour
    {
        public int Seed { get; protected set; }
        /// <summary>Distance from the centre that creatures may roam.</summary>
        public abstract float Bound { get; }
        public readonly List<Obstacle> Obstacles = new List<Obstacle>();
        public readonly List<Vector2> VentSpots = new List<Vector2>();
        protected Rng _rng;

        public abstract void Build(int seed);
        public abstract float HeightAt(float x, float z);
        public abstract Zone ZoneAt(Vector3 p);

        /// <summary>Destroy generated content so the world can be rebuilt with a new seed.</summary>
        public virtual void Clear()
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            Obstacles.Clear(); VentSpots.Clear();
        }

        public Vector2 RandomPoint(float minR, float maxR)
        {
            float a = _rng.Range(0, Mathf.PI * 2);
            float d = Mathf.Sqrt(_rng.Range(minR * minR, maxR * maxR));
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
        }

        public Vector3 Clamp(Vector3 p, float margin)
        {
            var flat = new Vector2(p.x, p.z);
            float max = Bound - margin;
            if (flat.magnitude > max) { flat = flat.normalized * max; p.x = flat.x; p.z = flat.y; }
            return p;
        }

        public bool BlockedByRock(Vector2 p, float r)
        {
            foreach (var o in Obstacles) if ((o.p - p).sqrMagnitude < (o.r + r) * (o.r + r)) return true;
            return false;
        }

        /// <summary>Pushes a circle out of solid things and keeps it inside the world.</summary>
        public virtual Vector3 Resolve(Vector3 pos, float r)
        {
            var p = new Vector2(pos.x, pos.z);
            foreach (var o in Obstacles)
            {
                var d = p - o.p; float min = o.r + r * 0.7f;
                float sq = d.sqrMagnitude;
                if (sq < min * min && sq > 0.0001f) p = o.p + d.normalized * min;
            }
            float max = Bound - r;
            if (p.magnitude > max) p = p.normalized * max;
            return new Vector3(p.x, pos.y, p.y);
        }

        /// <summary>Area damage to destructible scenery (buildings). The pool has none.</summary>
        public virtual void DamageStructures(Vector3 p, float radius, float dmg) { }

        /// <summary>True if a projectile at p hits something solid (and applies dmg to it).</summary>
        public virtual bool HitStructure(Vector3 p, float r, float dmg) => false;
    }
}
