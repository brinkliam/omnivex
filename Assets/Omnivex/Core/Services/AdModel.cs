using System;
using System.Collections.Generic;

namespace OMX.Core
{
    public enum AdFormat { Rewarded, Interstitial }

    public enum AdResult { Completed, Skipped, Failed, Unavailable, Blocked }

    /// <summary>Every ad opportunity in Omnivex. Gameplay code only ever names a placement, never an SDK.</summary>
    public static class Placements
    {
        public const string DoubleEssence = "rw_double_essence";      // results screen: double the run's essence
        public const string MutationReroll = "rw_mutation_reroll";    // evolution screen: one extra reroll
        public const string SecondChance = "rw_second_chance";        // death: revive at 50% health, once per run
        public const string FeedingFrenzy = "rw_feeding_frenzy";      // 90 s of +50% biomass
        public const string DailyBoost = "rw_daily_boost";            // double the daily gift
        public const string CosmeticProgress = "rw_cosmetic_progress"; // +1 toward a skin
        public const string AfterRun = "int_after_run";               // natural break: back to the lair after results
        public const string StageComplete = "int_stage_complete";     // natural break: after a stage summary
    }

    [Serializable]
    public class PlacementConfig
    {
        public string id;
        public AdFormat format;
        public bool enabled = true;
        public double cooldownSeconds;
        public int maxPerSession;
        public int maxPerDay;
        public string rewardId;
        public double estimatedEcpmUsd;
    }

    [Serializable]
    public class AdConfig
    {
        public bool adsEnabled = true;
        public bool testMode = true;          // stays true until the account owner supplies production ids
        public double interstitialGlobalCooldownSeconds = 300;
        public double interstitialMinSessionSeconds = 240;   // never in the first four minutes
        public int interstitialMaxPerSession = 3;
        public List<PlacementConfig> placements = new List<PlacementConfig>();

        public PlacementConfig Get(string id)
        {
            foreach (var p in placements) if (p.id == id) return p;
            return null;
        }

        /// <summary>Conservative launch defaults; remote config can override any field.</summary>
        public static AdConfig Default()
        {
            var c = new AdConfig();
            void R(string id, string reward, double cd, int perSession, int perDay) =>
                c.placements.Add(new PlacementConfig { id = id, format = AdFormat.Rewarded, rewardId = reward, cooldownSeconds = cd, maxPerSession = perSession, maxPerDay = perDay, estimatedEcpmUsd = 12 });
            R(Placements.DoubleEssence, Rewards.DoubleEssence, 30, 8, 20);
            R(Placements.MutationReroll, Rewards.Reroll, 20, 6, 20);
            R(Placements.SecondChance, Rewards.Revive, 0, 4, 12);
            R(Placements.FeedingFrenzy, Rewards.Frenzy, 300, 2, 6);
            R(Placements.DailyBoost, Rewards.DailyDouble, 0, 1, 1);
            R(Placements.CosmeticProgress, Rewards.SkinShard, 120, 3, 6);
            c.placements.Add(new PlacementConfig { id = Placements.AfterRun, format = AdFormat.Interstitial, cooldownSeconds = 300, maxPerSession = 2, estimatedEcpmUsd = 6 });
            c.placements.Add(new PlacementConfig { id = Placements.StageComplete, format = AdFormat.Interstitial, cooldownSeconds = 420, maxPerSession = 1, estimatedEcpmUsd = 6 });
            return c;
        }
    }

    public static class Rewards
    {
        public const string DoubleEssence = "double_essence";
        public const string Reroll = "reroll";
        public const string Revive = "revive";
        public const string Frenzy = "frenzy";
        public const string DailyDouble = "daily_double";
        public const string SkinShard = "skin_shard";

        public static string Describe(string id) => id switch
        {
            DoubleEssence => "Double this run's Essence",
            Reroll => "One extra mutation reroll",
            Revive => "Reform at half health",
            Frenzy => "Feeding Frenzy: +50% biomass for 90 s",
            DailyDouble => "Double today's gift",
            SkinShard => "+1 skin shard",
            _ => id
        };
    }

    public interface IClock { double Now { get; } DateTime UtcNow { get; } }

    public sealed class SystemClock : IClock
    {
        readonly DateTime _start = DateTime.UtcNow;
        public double Now => (DateTime.UtcNow - _start).TotalSeconds;
        public DateTime UtcNow => DateTime.UtcNow;
    }

    public sealed class ManualClock : IClock
    {
        public double Now { get; set; }
        public DateTime UtcNow => new DateTime(2026, 1, 1).AddSeconds(Now);
    }

    /// <summary>The thing that actually plays ads: the mock in development, LevelPlay on device.</summary>
    public interface IAdProvider
    {
        string Name { get; }
        bool IsTestMode { get; }
        void Initialize(Action<bool> done);
        bool IsReady(string placementId, AdFormat format);
        void Load(string placementId, AdFormat format);
        /// <summary>The manager defends against duplicate or missing callbacks. rewardEarned is true only on verified completion.</summary>
        void Show(string placementId, AdFormat format, Action<AdResult, bool, double> onClosed);
    }

    /// <summary>Lets the ad manager ask whether this is a bad moment to interrupt the player.</summary>
    public interface IGameplayGate
    {
        bool IsCriticalMoment { get; }   // combat, a boss, the evolution screen, a cinematic
        bool IsOnline { get; }
    }
}
