using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OMX.Core;
using UnityEngine;

namespace OMX.Tests
{
    public class GenomeTests
    {
        [Test] public void FeedingGrowsBiomassAndRadiusMonotonically()
        {
            var g = new Genome();
            float r0 = g.Radius;
            g.Feed(10, Dna.Flesh);
            Assert.AreEqual(10, g.BiomassTotal, 1e-4);
            Assert.Greater(g.Radius, r0);
            float prev = 0;
            for (int i = 0; i < 400; i += 5) { float r = Genome.RadiusFor(i); Assert.GreaterOrEqual(r, prev); prev = r; }
        }

        [Test] public void ThresholdsUnlockEvolutions()
        {
            var g = new Genome();
            int n = g.Feed(Genome.EvolutionThreshold(1) + 0.01f, Dna.Flora);
            Assert.AreEqual(1, n);
            Assert.AreEqual(1, g.PendingEvolutions);
            n = g.Feed(Genome.EvolutionThreshold(3) - g.BiomassTotal + 0.01f, Dna.Flora);
            Assert.AreEqual(2, n);
            Assert.AreEqual(3, g.PendingEvolutions);
        }

        [Test] public void GrantedEvolutionsDoNotShiftBiomassThresholds()
        {
            var g = new Genome();
            g.GrantEvolution();
            Assert.AreEqual(1, g.PendingEvolutions);
            int n = g.Feed(Genome.EvolutionThreshold(1) + 0.01f, Dna.Flora);
            Assert.AreEqual(1, n, "first biomass threshold still pays out after a free evolution");
            Assert.AreEqual(2, g.PendingEvolutions);
        }

        [Test] public void ApplyingMutationChangesStatsAndConsumesEvolution()
        {
            var g = new Genome();
            g.Feed(Genome.EvolutionThreshold(1) + 1, Dna.Mineral);
            float before = g.Current.Armor;
            Assert.IsTrue(g.Apply("plates"));
            Assert.AreEqual(0, g.PendingEvolutions);
            Assert.AreEqual(1, g.Evolutions);
            Assert.Greater(g.Current.Armor, before);
            Assert.Greater(g.Current.MaxHealth, Stats.Base.MaxHealth);
        }

        [Test] public void ArmourStacksMultiplicativelyAndNeverReachesOne()
        {
            var g = new Genome();
            for (int i = 0; i < 3; i++) g.Apply("plates");
            g.SetBonus(new Stats { Armor = 0.2f });
            Assert.Less(g.Current.Armor, 0.75f);
            Assert.Greater(g.Current.Armor, 0.5f);
        }

        [Test] public void MaxLevelIsRespected()
        {
            var g = new Genome();
            var def = MutationCatalog.Get("eyes");
            for (int i = 0; i < def.MaxLevel; i++) Assert.IsTrue(g.Apply("eyes"));
            Assert.IsFalse(g.Apply("eyes"));
            Assert.AreEqual(def.MaxLevel, g.Level("eyes"));
        }

        [Test] public void InfectionMutationIsLockedUntilFlagged()
        {
            var g = new Genome();
            Assert.IsFalse(g.Apply("crown"));
            g.Flags.Add("matriarch_infection");
            Assert.IsTrue(g.Apply("crown"));
        }

        [Test] public void EveryMutationHasAPartAndARealEffect()
        {
            foreach (var m in MutationCatalog.All)
            {
                Assert.IsFalse(string.IsNullOrEmpty(m.Part), m.Id + " has no body part");
                var s = m.PerLevel;
                float sum = Math.Abs(s.Speed) + Math.Abs(s.Turn) + s.Reach + s.Biomass + s.Armor + s.MaxHealth + s.Regen + s.Thorns + s.Lure + s.Dash + s.Volt + s.Acid + s.Gravity + s.Spores + s.Sight + s.Bite;
                Assert.Greater(sum, 0, m.Id + " changes no stat");
            }
        }
    }

    public class EvolutionOfferTests
    {
        [Test] public void OffersAreUniqueAndTakeable()
        {
            var g = new Genome();
            var rng = new Rng(1);
            for (int i = 0; i < 200; i++)
            {
                var o = Evolution.Offer(g, rng);
                Assert.AreEqual(3, o.Count);
                Assert.AreEqual(3, o.Select(m => m.Id).Distinct().Count());
                Assert.IsTrue(o.All(g.CanTake));
            }
        }

        [Test] public void DietShapesTheOffers()
        {
            int Count(Dna eat)
            {
                var g = new Genome();
                g.Feed(100, eat);
                var rng = new Rng(7);
                int hits = 0;
                for (int i = 0; i < 600; i++) hits += Evolution.Offer(g, rng).Count(m => m.Dna == Dna.Mineral);
                return hits;
            }
            int mineralEater = Count(Dna.Mineral), floraEater = Count(Dna.Flora);
            Assert.Greater(mineralEater, floraEater * 1.8f, $"mineral diet {mineralEater} vs flora diet {floraEater}");
        }

        [Test] public void DominantStrandIsAlwaysRepresented()
        {
            var g = new Genome();
            g.Feed(50, Dna.Toxin);
            var rng = new Rng(3);
            for (int i = 0; i < 300; i++) Assert.IsTrue(Evolution.Offer(g, rng).Any(m => m.Dna == Dna.Toxin));
        }

        [Test] public void OffersShrinkGracefullyWhenNearlyEverythingIsMaxed()
        {
            var g = new Genome();
            foreach (var m in MutationCatalog.All) { g.Flags.Add("matriarch_infection"); for (int i = 0; i < m.MaxLevel; i++) g.Apply(m.Id); }
            Assert.AreEqual(0, Evolution.Offer(g, new Rng(1)).Count);
        }

        [Test] public void SameSeedSameOffers()
        {
            var a = Evolution.Offer(new Genome(), new Rng(42)).Select(m => m.Id).ToArray();
            var b = Evolution.Offer(new Genome(), new Rng(42)).Select(m => m.Id).ToArray();
            CollectionAssert.AreEqual(a, b);
        }
    }

    public class SaveTests
    {
        SaveStore Make(MemoryFileStore fs) => new SaveStore(fs, "s.json", d => JsonUtility.ToJson(d), j => JsonUtility.FromJson<SaveData>(j));

        [Test] public void RoundTrip()
        {
            var fs = new MemoryFileStore(); var st = Make(fs);
            var d = new SaveData { essence = 123, runs = 4 };
            d.SetLab("hunger", 2); d.codex.Add("m:plates");
            st.Save(d);
            var back = st.Load(out var o);
            Assert.AreEqual(LoadOutcome.Loaded, o);
            Assert.AreEqual(123, back.essence);
            Assert.AreEqual(2, back.LabLevel("hunger"));
            CollectionAssert.Contains(back.codex, "m:plates");
        }

        [Test] public void FreshWhenNothingSaved()
        {
            Make(new MemoryFileStore()).Load(out var o);
            Assert.AreEqual(LoadOutcome.Fresh, o);
        }

        [Test] public void CorruptMainRecoversFromBackup()
        {
            var fs = new MemoryFileStore(); var st = Make(fs);
            st.Save(new SaveData { essence = 10 });
            st.Save(new SaveData { essence = 20 });
            fs.Files["s.json"] = fs.Files["s.json"].Replace("20", "99");   // tamper: checksum no longer matches
            var d = st.Load(out var o);
            Assert.AreEqual(LoadOutcome.RecoveredFromBackup, o);
            Assert.AreEqual(10, d.essence);
            Assert.IsTrue(fs.Exists("s.json.corrupt"));
        }

        [Test] public void BothCorruptResetsSafely()
        {
            var fs = new MemoryFileStore(); var st = Make(fs);
            fs.Files["s.json"] = "garbage"; fs.Files["s.json.bak"] = "OMX|1|deadbeef|{}";
            var d = st.Load(out var o);
            Assert.AreEqual(LoadOutcome.ResetAfterCorruption, o);
            Assert.AreEqual(0, d.essence);
        }

        [Test] public void TruncatedWriteIsDetected()
        {
            var fs = new MemoryFileStore(); var st = Make(fs);
            st.Save(new SaveData { essence = 5 });
            st.Save(new SaveData { essence = 6 });
            fs.Files["s.json"] = fs.Files["s.json"].Substring(0, fs.Files["s.json"].Length / 2);
            Assert.AreEqual(5, st.Load(out var o).essence);
            Assert.AreEqual(LoadOutcome.RecoveredFromBackup, o);
        }

        [Test] public void NewerVersionIsNotClobbered()
        {
            var fs = new MemoryFileStore(); var st = Make(fs);
            string json = JsonUtility.ToJson(new SaveData { essence = 77 });
            fs.Files["s.json"] = "OMX|99|" + SaveStore.Checksum(json).ToString("x8") + "|" + json;
            st.Load(out var o);
            Assert.AreNotEqual(LoadOutcome.Loaded, o);
        }

        [Test] public void MigrationFillsMissingFields()
        {
            var fs = new MemoryFileStore(); var st = Make(fs);
            string json = "{\"version\":1,\"essence\":3}";
            fs.Files["s.json"] = "OMX|1|" + SaveStore.Checksum(json).ToString("x8") + "|" + json;
            var d = st.Load(out var o);
            Assert.AreEqual(LoadOutcome.Loaded, o);
            Assert.IsNotNull(d.settings); Assert.IsNotNull(d.lab); Assert.AreEqual("abyssal", d.skin);
        }
    }

    public class AdTests
    {
        sealed class Gate : IGameplayGate { public bool IsCriticalMoment { get; set; } public bool IsOnline { get; set; } = true; }

        (AdManager ads, MockAdProvider mock, ManualClock clock, Gate gate, EntitlementStore ent, AnalyticsService an) Make()
        {
            var clock = new ManualClock { Now = 1000 };
            var an = new AnalyticsService(clock, true, "t");
            var ent = new EntitlementStore(true, an);
            var mock = new MockAdProvider();
            var gate = new Gate();
            var ads = new AdManager(AdConfig.Default(), mock, clock, ent, an, gate);
            ads.Initialize();
            return (ads, mock, clock, gate, ent, an);
        }

        [Test] public void RewardOnlyOnCompletion()
        {
            var (ads, mock, _, _, _, _) = Make();
            int rewards = 0;
            mock.SkipNextShow = true;
            ads.ShowRewarded(Placements.DoubleEssence, _ => rewards++);
            Assert.AreEqual(0, rewards);
            mock.FailNextShow = true;
            ads.ShowRewarded(Placements.MutationReroll, _ => rewards++);
            Assert.AreEqual(0, rewards);
            ads.ShowRewarded(Placements.SecondChance, _ => rewards++);
            Assert.AreEqual(1, rewards);
        }

        [Test] public void DuplicateCallbacksGrantOnce()
        {
            var (ads, mock, _, _, _, an) = Make();
            mock.DuplicateCallbacks = true;
            int rewards = 0;
            ads.ShowRewarded(Placements.DoubleEssence, _ => rewards++);
            Assert.AreEqual(1, rewards);
            Assert.AreEqual(1, an.Count("ad_duplicate_callback"));
        }

        [Test] public void NoFillOrOfflineReportsUnavailable()
        {
            var (ads, mock, _, gate, _, _) = Make();
            gate.IsOnline = false;
            AdResult got = AdResult.Completed;
            ads.ShowRewarded(Placements.DoubleEssence, _ => Assert.Fail(), (r, why) => got = r);
            Assert.AreEqual(AdResult.Unavailable, got);
            gate.IsOnline = true; mock.Fill = false;
            var (ads2, mock2, _, _, _, _) = Make();
            mock2.Fill = false;
            Assert.IsFalse(ads2.CanShow(Placements.SecondChance, out var why2));
        }

        [Test] public void HungProviderBlocksFurtherAdsInsteadOfDoubleGranting()
        {
            var (ads, mock, _, _, _, _) = Make();
            mock.NeverCallBack = true;
            int rewards = 0;
            ads.ShowRewarded(Placements.DoubleEssence, _ => rewards++);
            Assert.IsFalse(ads.CanShow(Placements.MutationReroll, out var why));
            Assert.AreEqual("another ad is showing", why);
            Assert.AreEqual(0, rewards);
        }

        [Test] public void InterstitialRespectsCriticalMomentsAndPacing()
        {
            var (ads, _, clock, gate, _, _) = Make();
            gate.IsCriticalMoment = true;
            Assert.IsFalse(ads.TryShowInterstitial(Placements.AfterRun));
            gate.IsCriticalMoment = false;
            Assert.IsTrue(ads.TryShowInterstitial(Placements.AfterRun));
            Assert.IsFalse(ads.TryShowInterstitial(Placements.AfterRun), "cooldown");
            clock.Now += 1000;
            Assert.IsFalse(ads.TryShowInterstitial(Placements.AfterRun), "no gameplay since the last interstitial");
            ads.NotifyGameplay();
            Assert.IsTrue(ads.TryShowInterstitial(Placements.AfterRun));
        }

        [Test] public void NoInterstitialRightAfterARewardedAd()
        {
            var (ads, _, clock, _, _, _) = Make();
            ads.ShowRewarded(Placements.DoubleEssence, _ => { });
            ads.NotifyGameplay();
            Assert.IsFalse(ads.CanShow(Placements.AfterRun, out var why));
            Assert.AreEqual("just watched a rewarded ad", why);
            clock.Now += 120;
            Assert.IsTrue(ads.CanShow(Placements.AfterRun, out _));
        }

        [Test] public void NoInterstitialsEarlyInSession()
        {
            var (ads, _, clock, _, _, _) = Make();
            clock.Now = 30;
            Assert.IsFalse(ads.CanShow(Placements.AfterRun, out var why));
            Assert.AreEqual("too early in session", why);
        }

        [Test] public void AdFreeBlocksInterstitialsButKeepsRewarded()
        {
            var (ads, _, _, _, ent, _) = Make();
            ent.Accept(new PurchaseReceipt { productId = Products.RemoveAds, transactionId = "T1", validated = true, isTest = true });
            Assert.IsFalse(ads.CanShow(Placements.AfterRun, out var why));
            Assert.AreEqual("ad-free", why);
            Assert.IsTrue(ads.CanShow(Placements.DoubleEssence, out _));
        }

        [Test] public void SessionCapsHold()
        {
            var (ads, _, clock, _, _, _) = Make();
            int cap = AdConfig.Default().Get(Placements.FeedingFrenzy).maxPerSession;
            int granted = 0;
            for (int i = 0; i < cap + 3; i++) { ads.ShowRewarded(Placements.FeedingFrenzy, _ => granted++); clock.Now += 1000; }
            Assert.AreEqual(cap, granted);
        }
    }

    public class PurchaseTests
    {
        [Test] public void UnvalidatedOrTestReceiptsInProductionAreRejected()
        {
            var prod = new EntitlementStore(testMode: false);
            Assert.IsFalse(prod.Accept(new PurchaseReceipt { productId = Products.RemoveAds, transactionId = "X", validated = true, isTest = true }));
            Assert.IsFalse(prod.Accept(new PurchaseReceipt { productId = Products.RemoveAds, transactionId = "X", validated = false }));
            Assert.IsTrue(prod.Accept(new PurchaseReceipt { productId = Products.RemoveAds, transactionId = "X", validated = true }));
        }

        [Test] public void CancelledPurchaseGrantsNothing()
        {
            var store = new TestPurchaseProvider { NextPurchaseCancels = true };
            var ent = new EntitlementStore(true);
            store.Purchase(Products.SkinVoidLeviathan, (r, rc) => { Assert.AreEqual(PurchaseResult.Cancelled, r); Assert.IsFalse(ent.Accept(rc)); });
            Assert.IsFalse(ent.Has(Products.SkinVoidLeviathan));
        }

        [Test] public void SkinsAreCosmeticOnly()
        {
            // skins carry no Stats at all: the type has colours and unlock data only
            foreach (var f in typeof(SkinDef).GetFields()) Assert.AreNotEqual(typeof(Stats), f.FieldType);
        }
    }

    public class EconomyTests
    {
        [Test] public void LabPurchaseDeductsAndCapsAtMax()
        {
            var s = new SaveData { essence = 100000 };
            var u = Lab.Get("helix");
            for (int i = 0; i < u.MaxLevel; i++) Assert.IsTrue(Lab.TryBuy(s, "helix"));
            Assert.IsFalse(Lab.TryBuy(s, "helix"));
            Assert.Less(s.essence, 100000);
            var poor = new SaveData { essence = 1 };
            Assert.IsFalse(Lab.TryBuy(poor, "hunger"));
            Assert.AreEqual(1, poor.essence);
        }

        [Test] public void CostsGrowGentlyNotExplosively()
        {
            foreach (var u in Lab.All)
                for (int l = 1; l < u.MaxLevel; l++)
                {
                    Assert.Greater(u.Cost(l), u.Cost(l - 1));
                    Assert.LessOrEqual(u.Cost(l), u.Cost(l - 1) * 2.1f, u.Id);
                }
        }

        [Test] public void DailyStreak()
        {
            var s = new SaveData();
            var day = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(Payout.DailyGift(0), Daily.Claim(s, day));
            Assert.AreEqual(0, Daily.Claim(s, day), "once per day");
            Assert.AreEqual(Payout.DailyGift(1), Daily.Claim(s, day.AddDays(1)));
            Assert.AreEqual(Payout.DailyGift(0), Daily.Claim(s, day.AddDays(5)), "streak resets after a gap");
        }

        /// <summary>
        /// Simulates a Stage 1 run: a player eating prey sized like the spawn director makes them, at a modest skill rate.
        /// Guards pacing: the boss should arrive in a single session, with plenty of evolution choices before it.
        /// </summary>
        [Test] public void Stage1PacingSimulation()
        {
            // assumed swallow rates for a cautious, average and aggressive player (prey flee, predators interrupt)
            foreach (float eatsPerSecond in new[] { 0.35f, 0.5f, 0.8f })
            {
                var g = new Genome();
                var rng = new Rng(5);
                float t = 0;
                while (g.Tier < 4 && t < 3600)
                {
                    float preyR = g.Radius * rng.Range(0.22f, 0.8f);
                    g.Feed(Genome.ValueOf(preyR), (Dna)rng.Range(0, 5));
                    while (g.PendingEvolutions > 0) { var o = Evolution.Offer(g, rng); if (o.Count == 0) g.SkipEvolution(); else g.Apply(o[0].Id); }
                    t += 1f / eatsPerSecond;
                }
                Debug.Log($"[sim] {eatsPerSecond}/s: boss at {t:0}s, {g.Evolutions} evolutions, radius {g.Radius:0.00}");
                Assert.Less(t, 900, "boss should arrive within 15 minutes");
                Assert.Greater(t, 150, "boss shouldn't arrive in under 2.5 minutes");
                Assert.GreaterOrEqual(g.Evolutions, 7);
                Assert.LessOrEqual(g.Evolutions, 14);
            }
        }

        /// <summary>Meta progression: runs needed to finish the Genome Lab at typical essence per run.</summary>
        [Test] public void MetaProgressionSimulation()
        {
            long total = Lab.TotalCostToMax();
            int deathRun = Payout.Essence(110, 120, 8, false, false);
            int clearRun = Payout.Essence(260, 240, 12, true, true);
            Debug.Log($"[sim] lab total {total}, death run {deathRun}, clear run {clearRun}");
            Assert.GreaterOrEqual(deathRun, Lab.All.Min(u => u.Cost(0)), "something is affordable after one failed run");
            double runs = total / (0.6 * deathRun + 0.4 * clearRun);
            Assert.Greater(runs, 12, "lab shouldn't be finished in a day");
            Assert.Less(runs, 60, "lab shouldn't take months");
        }
    }
}
