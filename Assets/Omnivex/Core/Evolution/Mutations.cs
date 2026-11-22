using System;
using System.Collections.Generic;

namespace OMX.Core
{
    /// <summary>The DNA strands that food carries. What you eat decides which mutations you're offered.</summary>
    public enum Dna { Flesh, Flora, Mineral, Volt, Toxin, Void }

    public enum Rarity { Common, Rare, Legendary }

    /// <summary>Builds the player can lean into. Hybrids happen naturally because cards come from several strands.</summary>
    public enum Archetype { Predator, Titan, Parasite, Technophage, Cosmic }

    /// <summary>All the numbers a mutation can change. Values add per level, except Armor which stacks multiplicatively.</summary>
    public struct Stats
    {
        public float Speed, Turn, Reach, Biomass, Armor, MaxHealth, Regen, Thorns, Lure, Dash, Volt, Acid, Gravity, Spores, Sight, Bite;

        public static Stats Base => new Stats { Speed = 6.5f, Turn = 7f, Reach = 1f, Biomass = 1f, MaxHealth = 100, Bite = 1f };

        public static Stats operator +(Stats a, Stats b) => new Stats
        {
            Speed = a.Speed + b.Speed, Turn = a.Turn + b.Turn, Reach = a.Reach + b.Reach, Biomass = a.Biomass + b.Biomass,
            Armor = 1f - (1f - a.Armor) * (1f - b.Armor), MaxHealth = a.MaxHealth + b.MaxHealth, Regen = a.Regen + b.Regen,
            Thorns = a.Thorns + b.Thorns, Lure = a.Lure + b.Lure, Dash = a.Dash + b.Dash, Volt = a.Volt + b.Volt, Acid = a.Acid + b.Acid,
            Gravity = a.Gravity + b.Gravity, Spores = a.Spores + b.Spores, Sight = a.Sight + b.Sight, Bite = a.Bite + b.Bite,
        };
    }

    public sealed class MutationDef
    {
        public string Id, Name, Blurb, Part;
        public Dna Dna;
        public Rarity Rarity;
        public Archetype Archetype;
        public int MaxLevel = 3;
        public Stats PerLevel;
        /// <summary>Only offered after this flag is set (e.g. a boss infection).</summary>
        public string RequiresFlag;
    }

    /// <summary>The Stage 1 mutation catalogue. Every entry has a body part (Part) the creature grows and a real effect.</summary>
    public static class MutationCatalog
    {
        public static readonly List<MutationDef> All = new List<MutationDef>
        {
            // Predator
            new MutationDef { Id = "lashers", Name = "Lashing Tentacles", Part = "tentacles", Dna = Dna.Flesh, Rarity = Rarity.Common, Archetype = Archetype.Predator,
                Blurb = "Four whipping feeders. +35% bite reach.", PerLevel = new Stats { Reach = 0.35f } },
            new MutationDef { Id = "maw", Name = "Second Maw", Part = "maw", Dna = Dna.Flesh, Rarity = Rarity.Rare, Archetype = Archetype.Predator,
                Blurb = "A ring of inner teeth. +25% biomass from everything, bites the boss harder.", PerLevel = new Stats { Biomass = 0.25f, Bite = 0.5f } },
            new MutationDef { Id = "siphons", Name = "Jet Siphons", Part = "siphons", Dna = Dna.Flora, Rarity = Rarity.Common, Archetype = Archetype.Predator,
                Blurb = "Rear jets. +12% speed and a stronger dash.", PerLevel = new Stats { Speed = 0.8f, Dash = 1f } },
            new MutationDef { Id = "eyes", Name = "Stalk Eyes", Part = "eyes", Dna = Dna.Flesh, Rarity = Rarity.Common, Archetype = Archetype.Predator, MaxLevel = 2,
                Blurb = "See further. Wider view, and arrows point to rare prey.", PerLevel = new Stats { Sight = 1f, Turn = 1f } },
            // Titan
            new MutationDef { Id = "plates", Name = "Chitin Plates", Part = "plates", Dna = Dna.Mineral, Rarity = Rarity.Common, Archetype = Archetype.Titan,
                Blurb = "Overlapping armour. Take 18% less damage.", PerLevel = new Stats { Armor = 0.18f, MaxHealth = 10 } },
            new MutationDef { Id = "spines", Name = "Barbed Spines", Part = "spines", Dna = Dna.Mineral, Rarity = Rarity.Common, Archetype = Archetype.Titan,
                Blurb = "Anything that bites you bleeds. 12 thorn damage per hit.", PerLevel = new Stats { Thorns = 12 } },
            new MutationDef { Id = "bulk", Name = "Gorged Mass", Part = "bulk", Dna = Dna.Flesh, Rarity = Rarity.Common, Archetype = Archetype.Titan,
                Blurb = "+30 max health, a little slower.", PerLevel = new Stats { MaxHealth = 30, Speed = -0.3f } },
            // Parasite
            new MutationDef { Id = "acid", Name = "Acid Sacs", Part = "acid", Dna = Dna.Toxin, Rarity = Rarity.Common, Archetype = Archetype.Parasite,
                Blurb = "Spit dissolving globs at the nearest threat.", PerLevel = new Stats { Acid = 1f } },
            new MutationDef { Id = "lure", Name = "Siren Lure", Part = "lure", Dna = Dna.Flora, Rarity = Rarity.Common, Archetype = Archetype.Parasite,
                Blurb = "A glowing bulb that draws small prey toward your mouth.", PerLevel = new Stats { Lure = 1f } },
            new MutationDef { Id = "membrane", Name = "Mending Membrane", Part = "membrane", Dna = Dna.Flora, Rarity = Rarity.Common, Archetype = Archetype.Parasite,
                Blurb = "Regenerate 2.5 health per second.", PerLevel = new Stats { Regen = 2.5f } },
            new MutationDef { Id = "spores", Name = "Hive Spores", Part = "spores", Dna = Dna.Toxin, Rarity = Rarity.Legendary, Archetype = Archetype.Parasite, MaxLevel = 2,
                Blurb = "Victims may burst into spawnlings that hunt for you.", PerLevel = new Stats { Spores = 0.22f } },
            // Technophage (Stage 1 flavour: living electricity)
            new MutationDef { Id = "volt", Name = "Volt Organ", Part = "volt", Dna = Dna.Volt, Rarity = Rarity.Rare, Archetype = Archetype.Technophage,
                Blurb = "Discharge every few seconds, frying everything nearby.", PerLevel = new Stats { Volt = 1f } },
            new MutationDef { Id = "cilia", Name = "Cilia Fringe", Part = "cilia", Dna = Dna.Volt, Rarity = Rarity.Common, Archetype = Archetype.Technophage,
                Blurb = "Thousands of beating hairs. Sharper turns, +8% speed.", PerLevel = new Stats { Turn = 2.5f, Speed = 0.5f } },
            // Cosmic seed
            new MutationDef { Id = "gravity", Name = "Gravity Gland", Part = "gravity", Dna = Dna.Void, Rarity = Rarity.Legendary, Archetype = Archetype.Cosmic, MaxLevel = 2,
                Blurb = "Bends space. Small things spiral into you.", PerLevel = new Stats { Gravity = 1f, Lure = 0.5f } },
            // Boss infection (the Matriarch's parting gift)
            new MutationDef { Id = "crown", Name = "Matriarch's Crown", Part = "crown", Dna = Dna.Void, Rarity = Rarity.Legendary, Archetype = Archetype.Parasite, MaxLevel = 1,
                RequiresFlag = "matriarch_infection", Blurb = "Her brood now follows you. Spawnlings hatch on their own.", PerLevel = new Stats { Spores = 0.35f, MaxHealth = 25 } },
        };

        static Dictionary<string, MutationDef> _byId;
        public static MutationDef Get(string id)
        {
            if (_byId == null) { _byId = new Dictionary<string, MutationDef>(); foreach (var m in All) _byId[m.Id] = m; }
            return _byId.TryGetValue(id, out var d) ? d : null;
        }

        public static string DnaName(Dna d) => d switch
        {
            Dna.Flesh => "Flesh", Dna.Flora => "Flora", Dna.Mineral => "Mineral", Dna.Volt => "Volt", Dna.Toxin => "Toxin", _ => "Void"
        };
    }
}
