using System;
using System.Collections.Generic;

namespace OMX.Core
{
    [Serializable] public class KeyLevel { public string key; public int level; }

    [Serializable]
    public class SettingsData
    {
        public float music = 0.7f, sfx = 0.9f;
        public int quality = -1;          // -1 = auto
        public bool haptics = true, shake = true, reducedFlashing;
        public bool consentAsked, analyticsConsent;
    }

    /// <summary>Everything that persists between runs. Versioned; see SaveStore for migration and recovery.</summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public long essence;
        public List<KeyLevel> lab = new List<KeyLevel>();
        public List<string> skins = new List<string> { "abyssal" };
        public string skin = "abyssal";
        public int skinShards;
        public List<string> codex = new List<string>();      // mutations ever grown
        public List<string> testReceipts = new List<string>(); // test-store product ids (dev only)
        public float bestBiomass;
        public int runs, bossKills, totalKills, bestEvolutions;
        public string lastDailyUtc = "";
        public int dailyStreak;
        public int seed = 0;               // 0 = random each run
        public SettingsData settings = new SettingsData();

        public int LabLevel(string key) { foreach (var k in lab) if (k.key == key) return k.level; return 0; }
        public void SetLab(string key, int level)
        {
            foreach (var k in lab) if (k.key == key) { k.level = level; return; }
            lab.Add(new KeyLevel { key = key, level = level });
        }
    }

    public sealed class LabUpgrade
    {
        public string Id, Name, Blurb;
        public int MaxLevel, BaseCost;
        public float Growth = 1.55f;
        public int Cost(int currentLevel) => (int)Math.Round(BaseCost * Math.Pow(Growth, currentLevel) / 5.0) * 5;
    }

    /// <summary>The Genome Lab: permanent upgrades bought with Essence. Each one changes play, never just a menu number.</summary>
    public static class Lab
    {
        public static readonly List<LabUpgrade> All = new List<LabUpgrade>
        {
            new LabUpgrade { Id = "cytoplasm", Name = "Dense Cytoplasm", Blurb = "+12 max health per level", MaxLevel = 5, BaseCost = 30 },
            new LabUpgrade { Id = "hunger", Name = "Hungry Genome", Blurb = "+6% biomass from everything", MaxLevel = 5, BaseCost = 40 },
            new LabUpgrade { Id = "nucleus", Name = "Hardened Nucleus", Blurb = "Take 4% less damage per level", MaxLevel = 5, BaseCost = 35 },
            new LabUpgrade { Id = "helix", Name = "Lucky Helix", Blurb = "+1 free mutation reroll each run", MaxLevel = 3, BaseCost = 60, Growth = 1.9f },
            new LabUpgrade { Id = "memory", Name = "Ancestral Memory", Blurb = "Rare and legendary mutations appear more often", MaxLevel = 4, BaseCost = 70, Growth = 1.7f },
            new LabUpgrade { Id = "primer", Name = "Primed Egg", Blurb = "Hatch with your first mutation already chosen", MaxLevel = 1, BaseCost = 120 },
            new LabUpgrade { Id = "flagella", Name = "Strong Flagella", Blurb = "+0.35 base speed per level", MaxLevel = 3, BaseCost = 45 },
        };

        public static LabUpgrade Get(string id) { foreach (var u in All) if (u.Id == id) return u; return null; }

        public static Stats BonusFor(SaveData s) => new Stats
        {
            MaxHealth = 12 * s.LabLevel("cytoplasm"),
            Biomass = 0.06f * s.LabLevel("hunger"),
            Armor = 0.04f * s.LabLevel("nucleus"),
            Speed = 0.35f * s.LabLevel("flagella"),
        };

        public static int FreeRerolls(SaveData s) => 1 + s.LabLevel("helix");
        public static float RareLuck(SaveData s) => 0.15f * s.LabLevel("memory");

        public static bool TryBuy(SaveData s, string id)
        {
            var u = Get(id); if (u == null) return false;
            int lvl = s.LabLevel(id);
            if (lvl >= u.MaxLevel) return false;
            int cost = u.Cost(lvl);
            if (s.essence < cost) return false;
            s.essence -= cost;
            s.SetLab(id, lvl + 1);
            return true;
        }

        public static long TotalCostToMax()
        {
            long t = 0; foreach (var u in All) for (int l = 0; l < u.MaxLevel; l++) t += u.Cost(l);
            return t;
        }
    }

    /// <summary>Essence awarded at the end of a run. Linear in what you did - no exponential runaway.</summary>
    public static class Payout
    {
        public static int Essence(float biomass, int kills, int evolutions, bool bossKilled, bool stageCleared) =>
            (int)Math.Round(biomass * 0.35 + kills * 0.25 + evolutions * 3 + (bossKilled ? 60 : 0) + (stageCleared ? 25 : 0));

        public static int DailyGift(int streak) => 20 + 5 * Math.Min(streak, 6);
    }

    public sealed class SkinDef
    {
        public string Id, Name, Unlock, Product;
        public int Shards;
        public uint Body, Glow, Accent;     // 0xRRGGBB
    }

    /// <summary>Cosmetic skins. Never stat-bearing.</summary>
    public static class Skins
    {
        public static readonly List<SkinDef> All = new List<SkinDef>
        {
            new SkinDef { Id = "abyssal", Name = "Abyssal Bloom", Unlock = "Starting form", Body = 0x1F6F78, Glow = 0x3CF2D8, Accent = 0xFF4F9A },
            new SkinDef { Id = "ember", Name = "Ember Maw", Unlock = "Defeat the Hollow Matriarch", Body = 0x6B1F1A, Glow = 0xFF8A2A, Accent = 0xFFE15A },
            new SkinDef { Id = "crystal", Name = "Crystal Devourer", Unlock = "8 skin shards or purchase", Shards = 8, Product = Products.SkinCrystalDevourer, Body = 0x2A3F7A, Glow = 0x9FE8FF, Accent = 0xE8B8FF },
            new SkinDef { Id = "void", Name = "Void Leviathan", Unlock = "12 skin shards or purchase", Shards = 12, Product = Products.SkinVoidLeviathan, Body = 0x140F24, Glow = 0xA05CFF, Accent = 0x4CFFB0 },
            new SkinDef { Id = "nuclear", Name = "Nuclear Titan", Unlock = "Purchase", Product = Products.SkinNuclearTitan, Body = 0x2B3A12, Glow = 0xB8FF3A, Accent = 0xFFD23A },
        };
        public static SkinDef Get(string id) { foreach (var s in All) if (s.Id == id) return s; return All[0]; }
    }

    public static class Daily
    {
        /// <summary>Returns true and updates the streak if a gift is due today (UTC).</summary>
        public static bool Claimable(SaveData s, DateTime utcNow) => s.lastDailyUtc != utcNow.ToString("yyyy-MM-dd");

        public static int Claim(SaveData s, DateTime utcNow)
        {
            if (!Claimable(s, utcNow)) return 0;
            string yesterday = utcNow.AddDays(-1).ToString("yyyy-MM-dd");
            s.dailyStreak = s.lastDailyUtc == yesterday ? s.dailyStreak + 1 : 0;
            s.lastDailyUtc = utcNow.ToString("yyyy-MM-dd");
            int gift = Payout.DailyGift(s.dailyStreak);
            s.essence += gift;
            return gift;
        }
    }
}
