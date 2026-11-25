using System;
using System.Collections.Generic;

namespace OMX.Core
{
    /// <summary>Deterministic xorshift RNG (seeded worlds and testable offers).</summary>
    public sealed class Rng
    {
        uint _s;
        public Rng(int seed) { _s = (uint)seed * 2654435761u ^ 0x9E3779B9u; if (_s == 0) _s = 1; }
        public uint NextU() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
        public float Value => (NextU() & 0xFFFFFF) / (float)0x1000000;
        public float Range(float a, float b) => a + (b - a) * Value;
        public int Range(int a, int bExclusive) => a + (int)(NextU() % (uint)Math.Max(1, bExclusive - a));
        public bool Chance(float p) => Value < p;
    }

    /// <summary>
    /// A run's genome: what the creature has eaten (DNA affinity), which mutations it carries, its growth and its
    /// derived stats. Pure logic so it can be tested and simulated without Unity.
    /// </summary>
    public sealed class Genome
    {
        public readonly Dictionary<string, int> Levels = new Dictionary<string, int>();
        public readonly float[] Affinity = new float[7];
        public readonly HashSet<string> Flags = new HashSet<string>();
        public float BiomassTotal { get; private set; }
        public int Evolutions { get; private set; }
        public int PendingEvolutions { get; private set; }
        public int ThresholdsCrossed { get; private set; }
        public Stats Bonus;   // permanent Genome Lab bonuses, applied under the mutations

        public int Level(string id) => Levels.TryGetValue(id, out var n) ? n : 0;

        // ----------------------------------------------------------------- growth

        /// <summary>Biomass needed for the n-th evolution (1-based). Gentle power curve, never exponential.</summary>
        public static float EvolutionThreshold(int n) => n <= 0 ? 0 : (float)(4.0 * Math.Pow(n, 1.6) * (n > 12 ? Math.Pow(n / 12.0, 1.2) : 1.0));

        /// <summary>Creature radius from total biomass. Cube-root-ish so mass feels like volume.</summary>
        public static float RadiusFor(float biomass) => 0.5f * (float)Math.Pow(1.0 + biomass / 8.0, 0.42);

        /// <summary>Biomass gained for eating something of the given radius.</summary>
        public static float ValueOf(float preyRadius) => 3.5f * preyRadius * preyRadius;

        public float Radius => RadiusFor(BiomassTotal);
        public float NextThreshold => EvolutionThreshold(ThresholdsCrossed + 1);
        public float PrevThreshold => EvolutionThreshold(ThresholdsCrossed);

        public static readonly string[] TierNames = { "Speck", "Mite", "Grub", "Hunter", "Brute", "Apex", "Ravager", "Behemoth", "Colossus" };
        static readonly float[] TierAt = { 0, 10, 40, 100, 200, 320, 500, 760, 1100 };
        public int Tier { get { int t = 0; for (int i = 0; i < TierAt.Length; i++) if (BiomassTotal >= TierAt[i]) t = i; return t; } }
        public static float TierThreshold(int tier) => TierAt[Math.Max(0, Math.Min(TierAt.Length - 1, tier))];

        /// <summary>Feed the genome. Returns how many new evolutions became available.</summary>
        public int Feed(float rawBiomass, Dna dna, float multiplier = 1f)
        {
            float gain = rawBiomass * Current.Biomass * multiplier;
            BiomassTotal += gain;
            Affinity[(int)dna] += gain;
            int unlocked = 0;
            while (BiomassTotal >= EvolutionThreshold(ThresholdsCrossed + 1)) { ThresholdsCrossed++; PendingEvolutions++; unlocked++; }
            return unlocked;
        }

        /// <summary>Share of everything eaten that carried this strand (0..1).</summary>
        public float Share(Dna d)
        {
            float sum = 0; foreach (var a in Affinity) sum += a;
            return sum <= 0 ? 0 : Affinity[(int)d] / sum;
        }

        public Dna Dominant
        {
            get { int best = 0; for (int i = 1; i < Affinity.Length; i++) if (Affinity[i] > Affinity[best]) best = i; return (Dna)best; }
        }

        // ----------------------------------------------------------------- mutation

        /// <summary>
        /// Testing/dev: grow to a biomass target in one go, auto-picking each evolution as it unlocks, so no
        /// evolution choices are left pending (no chain of Evolve screens).
        /// </summary>
        public void GrowSilently(float targetBiomass, Rng rng)
        {
            int guard = 0;
            while (BiomassTotal < targetBiomass && guard++ < 1000)
            {
                Feed(Math.Max(0.5f, Math.Min(20f, targetBiomass - BiomassTotal) / Math.Max(0.1f, Current.Biomass)), (Dna)rng.Range(0, 5));
                while (PendingEvolutions > 0)
                {
                    var o = Evolution.Offer(this, rng);
                    if (o.Count == 0) SkipEvolution(); else Apply(o[0].Id);
                }
            }
        }

        /// <summary>A free evolution not tied to biomass (Primed Egg, boss infection).</summary>
        public void GrantEvolution() => PendingEvolutions++;

        /// <summary>Spend a pending evolution without taking a card (every card maxed).</summary>
        public void SkipEvolution() { if (PendingEvolutions > 0) { PendingEvolutions--; Evolutions++; } }

        public bool CanTake(MutationDef m) =>
            m != null && Level(m.Id) < m.MaxLevel && (m.RequiresFlag == null || Flags.Contains(m.RequiresFlag));

        /// <summary>Apply a chosen mutation, consuming one pending evolution (if any).</summary>
        public bool Apply(string id)
        {
            var m = MutationCatalog.Get(id);
            if (!CanTake(m)) return false;
            Levels[id] = Level(id) + 1;
            if (PendingEvolutions > 0) { PendingEvolutions--; Evolutions++; }
            _dirty = true;
            return true;
        }

        bool _dirty = true;
        Stats _cached;
        public Stats Current
        {
            get
            {
                if (!_dirty) return _cached;
                var s = Stats.Base + Bonus;
                foreach (var kv in Levels)
                {
                    var m = MutationCatalog.Get(kv.Key);
                    for (int i = 0; i < kv.Value; i++) s = s + m.PerLevel;
                }
                if (s.Speed < 3) s.Speed = 3;
                _cached = s; _dirty = false;
                return s;
            }
        }

        public void SetBonus(Stats b) { Bonus = b; _dirty = true; }

        /// <summary>Which archetype the build leans toward, for the results card and sharing.</summary>
        public Archetype LeaningArchetype
        {
            get
            {
                var score = new int[5];
                foreach (var kv in Levels) score[(int)MutationCatalog.Get(kv.Key).Archetype] += kv.Value;
                int best = 0; for (int i = 1; i < 5; i++) if (score[i] > score[best]) best = i;
                return (Archetype)best;
            }
        }

        public string BuildTitle
        {
            get
            {
                if (Levels.Count == 0) return "Unformed Speck";
                var score = new int[5];
                foreach (var kv in Levels) score[(int)MutationCatalog.Get(kv.Key).Archetype] += kv.Value;
                int a = 0; for (int i = 1; i < 5; i++) if (score[i] > score[a]) a = i;
                int b = a == 0 ? 1 : 0; for (int i = 0; i < 5; i++) if (i != a && score[i] > score[b]) b = i;
                string[] nouns = { "Predator", "Titan", "Parasite", "Technophage", "Cosmic Seed" };
                string[] adj = { "Ravenous", "Armoured", "Creeping", "Crackling", "Gravitic" };
                return score[b] > 0 && score[b] * 2 >= score[a] ? adj[b] + " " + nouns[a] : nouns[a];
            }
        }
    }

    /// <summary>Builds mutation offers. Weighted by rarity, by what the creature has eaten, and by permanent luck.</summary>
    public static class Evolution
    {
        public static float RarityWeight(Rarity r) => r switch { Rarity.Common => 10f, Rarity.Rare => 3.2f, _ => 0.9f };

        public static List<MutationDef> Offer(Genome g, Rng rng, int count = 3, float rareLuck = 0f)
        {
            var pool = new List<MutationDef>();
            var weights = new List<float>();
            foreach (var m in MutationCatalog.All)
            {
                if (!g.CanTake(m)) continue;
                float w = RarityWeight(m.Rarity) * (m.Rarity == Rarity.Common ? 1f : 1f + rareLuck);
                w *= 0.35f + 3.0f * g.Share(m.Dna);          // you are what you eat
                if (g.Level(m.Id) > 0) w *= 1.25f;           // deepen what you have
                if (m.RequiresFlag != null && m.Rarity == Rarity.Legendary) w *= 40f;          // a freshly unlocked infection is always on the table
                pool.Add(m); weights.Add(w);
            }
            var picks = new List<MutationDef>();
            while (picks.Count < count && pool.Count > 0)
            {
                float total = 0; foreach (var w in weights) total += w;
                float r = rng.Value * total; int idx = 0;
                for (; idx < weights.Count - 1; idx++) { r -= weights[idx]; if (r <= 0) break; }
                picks.Add(pool[idx]); pool.RemoveAt(idx); weights.RemoveAt(idx);
            }
            // Guarantee one card from the dominant strand once the diet has a clear direction.
            var dom = g.Dominant;
            if (g.Share(dom) > 0.4f && !picks.Exists(p => p.Dna == dom))
            {
                var alt = pool.Find(p => p.Dna == dom);
                if (alt != null && picks.Count > 0) picks[picks.Count - 1] = alt;
            }
            return picks;
        }
    }
}
