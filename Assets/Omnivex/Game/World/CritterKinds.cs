using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    public enum Brain { Drift, Skittish, Predator, Lancer, Anchored, Morsel, Ally, Shooter, Ram, Flyer, Artillery }

    public sealed class CritterKind
    {
        public string Id, Name;
        public Dna Dna;
        public Brain Brain;
        public float Speed = 3f, Turn = 4f;
        public float Damage;              // per bite, scaled by size ratio
        public float EatRatio = 0.92f;    // must be this much smaller than the player to be swallowed
        public float HpPerArea = 10f;
        public bool Rare;
        public float ContactDamage;       // hurts the eater (Toxbloom) / brushes (Voltling)
        public float Altitude;            // flyers hover this high
        public float Value;               // fixed biomass (town things have real-world sizes)
        public float Size;                // fixed radius (town things); 0 = relative to the player
        public bool Military;
    }

    public static class Kinds
    {
        public static readonly CritterKind Driftspore = new CritterKind { Id = "driftspore", Name = "Driftspore", Dna = Dna.Flora, Brain = Brain.Drift, Speed = 0.7f };
        public static readonly CritterKind Glimmermite = new CritterKind { Id = "glimmermite", Name = "Glimmermite", Dna = Dna.Flesh, Brain = Brain.Skittish, Speed = 4.6f, Turn = 7 };
        public static readonly CritterKind Shellbit = new CritterKind { Id = "shellbit", Name = "Shellbit", Dna = Dna.Mineral, Brain = Brain.Drift, Speed = 1.2f, EatRatio = 0.78f, HpPerArea = 30 };
        public static readonly CritterKind Voltling = new CritterKind { Id = "voltling", Name = "Voltling", Dna = Dna.Volt, Brain = Brain.Skittish, Speed = 3.8f, Turn = 10, ContactDamage = 4 };
        public static readonly CritterKind Toxbloom = new CritterKind { Id = "toxbloom", Name = "Toxbloom", Dna = Dna.Toxin, Brain = Brain.Anchored, Speed = 0, ContactDamage = 7 };
        public static readonly CritterKind Snapjaw = new CritterKind { Id = "snapjaw", Name = "Snapjaw", Dna = Dna.Flesh, Brain = Brain.Predator, Speed = 4.4f, Turn = 3.4f, Damage = 16, HpPerArea = 40 };
        public static readonly CritterKind Lancer = new CritterKind { Id = "lancer", Name = "Lancer Eel", Dna = Dna.Volt, Brain = Brain.Lancer, Speed = 3.2f, Turn = 2.5f, Damage = 22, HpPerArea = 34 };
        public static readonly CritterKind Gulper = new CritterKind { Id = "gulper", Name = "Gulper", Dna = Dna.Void, Brain = Brain.Skittish, Speed = 3.3f, Turn = 2.2f, Rare = true, HpPerArea = 25, EatRatio = 1.05f };
        public static readonly CritterKind Brood = new CritterKind { Id = "brood", Name = "Broodling", Dna = Dna.Void, Brain = Brain.Predator, Speed = 5.4f, Turn = 5, Damage = 9, HpPerArea = 30 };
        public static readonly CritterKind Morsel = new CritterKind { Id = "morsel", Name = "Ichor", Dna = Dna.Flesh, Brain = Brain.Morsel, Speed = 0.4f, EatRatio = 99f };
        public static readonly CritterKind Spawnling = new CritterKind { Id = "spawnling", Name = "Spawnling", Dna = Dna.Toxin, Brain = Brain.Ally, Speed = 7f, Turn = 9, Damage = 10, HpPerArea = 30 };

        // ---- Stage 2: Harrowgate. Real-world sizes, so you feel yourself outgrow the town.
        public static readonly CritterKind Townsfolk = new CritterKind { Id = "townsfolk", Name = "Townsfolk", Dna = Dna.Flesh, Brain = Brain.Skittish, Speed = 2.6f, Turn = 8, Size = 0.32f, Value = 2.2f, HpPerArea = 20 };
        public static readonly CritterKind Car = new CritterKind { Id = "car", Name = "Car", Dna = Dna.Alloy, Brain = Brain.Skittish, Speed = 5.5f, Turn = 2.6f, Size = 1.05f, Value = 9, HpPerArea = 20 };
        public static readonly CritterKind Police = new CritterKind { Id = "police", Name = "Patrol Car", Dna = Dna.Alloy, Brain = Brain.Shooter, Speed = 6.5f, Turn = 2.8f, Size = 1.1f, Value = 12, Damage = 7, HpPerArea = 25, Military = true };
        public static readonly CritterKind Soldier = new CritterKind { Id = "soldier", Name = "Rifle Squad", Dna = Dna.Flesh, Brain = Brain.Shooter, Speed = 2.6f, Turn = 6, Size = 0.36f, Value = 4, Damage = 3.5f, HpPerArea = 40, Military = true };
        public static readonly CritterKind Heli = new CritterKind { Id = "heli", Name = "Gunship", Dna = Dna.Alloy, Brain = Brain.Flyer, Speed = 6f, Turn = 1.8f, Size = 1.6f, Value = 40, Damage = 9, HpPerArea = 22, Altitude = 9, EatRatio = 1.1f, Military = true };
        public static readonly CritterKind Tank = new CritterKind { Id = "tank", Name = "Siege Tank", Dna = Dna.Alloy, Brain = Brain.Artillery, Speed = 1.8f, Turn = 1.4f, Size = 2.6f, Value = 70, Damage = 16, HpPerArea = 28, EatRatio = 0.95f, Military = true };
        public static readonly CritterKind Drone = new CritterKind { Id = "drone", Name = "Hunter Drone", Dna = Dna.Volt, Brain = Brain.Shooter, Speed = 5f, Turn = 5, Size = 0.5f, Value = 6, Damage = 4, HpPerArea = 30, Altitude = 4, Military = true };

        public static readonly CritterKind[] Prey = { Driftspore, Glimmermite, Shellbit, Voltling, Toxbloom };

        public static CritterKind PreyFor(Dna d, Rng r) => d switch
        {
            Dna.Flora => Driftspore,
            Dna.Flesh => Glimmermite,
            Dna.Mineral => Shellbit,
            Dna.Volt => Voltling,
            Dna.Toxin => Toxbloom,
            _ => Prey[r.Range(0, Prey.Length)],
        };
    }
}
