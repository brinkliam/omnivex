using System.Collections.Generic;
using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    public enum RunState { Lair, Playing, Evolving, Paused, Dying, BossIntro, StageClear, Results }

    public sealed class RunResult
    {
        public bool Cleared, BossKilled;
        public float Biomass, Seconds;
        public int Kills, Evolutions, Essence, Tier;
        public string Build;
        public List<string> Mutations = new List<string>();
        public bool Doubled;
        public int Seed;
    }

    /// <summary>
    /// The run loop: hatch, feed, evolve, survive the Matriarch, consume the pool. Owns pacing decisions
    /// (evolution timing, boss trigger, revives, rewards) and tells the ad manager when interruption is unacceptable.
    /// </summary>
    public sealed class RunController : MonoBehaviour, IGameplayGate
    {
        public ServicesHub Services;
        public PlayerCreature Player; public Ecosystem Eco; public Arena Arena; public Hazards Hazards;
        public Matriarch Boss; public CameraRig Rig; public AudioDirector Audio; public UIRoot UI; public Projectiles Proj;
        public RunState State { get; private set; } = RunState.Lair;
        public float RunTime { get; private set; }
        public int RerollsLeft { get; private set; }
        public bool RevivedThisRun { get; private set; }
        public RunResult LastResult { get; private set; }
        public int Seed { get; private set; }
        public float Threat { get; private set; }

        bool _bossSpawned, _bossDone;
        int _lastTier;
        float _evoDelay = -1, _bossCountdown = -1, _dyingT, _clearT;
        readonly HashSet<string> _seenThisSave = new HashSet<string>();
        Rng _offerRng;

        public bool IsCriticalMoment => State != RunState.Lair && State != RunState.Results;
        public bool IsOnline => Application.internetReachability != NetworkReachability.NotReachable || Application.isEditor;

        public void Init()
        {
            Player.OnEvolutionReady += _ => { if (_evoDelay < 0) _evoDelay = 0.55f; Fx.I.Ring(Player.transform.position, Palette.Hdr(Palette.Accent, 3f), Player.Radius, Player.Radius * 4, 0.6f); Audio.Play("select", 0.7f, 1.2f); };
            Player.OnAte += OnAte;
            Player.OnDied += OnDied;
            Eco.OnKilled += c => { Player.Kills++; };
            Boss.OnIntroDone += () => { if (State == RunState.BossIntro) { State = RunState.Playing; Player.Controllable = true; } };
            Boss.OnDefeated += OnBossDefeated;
            Player.BossHook = (p, r, dmg) => { if (Boss.Active && (p - Boss.transform.position).magnitude < r + Boss.Radius * 0.6f) Boss.Damage(dmg); };
            Player.BossAim = (p, range) => Boss.AimPoint(p, range);
            Proj.HitBoss = (p, r, dmg) => Boss.HitTest(p, r, dmg);
            foreach (var c in Services.Save.codex) _seenThisSave.Add(c);
        }

        // ------------------------------------------------------------------ flow

        public void EnterLair()
        {
            Time.timeScale = 1;
            State = RunState.Lair;
            Player.Controllable = false;
            Boss.Despawn();
            Proj.ClearAll();
            Eco.Clear();
            Eco.SpawningEnabled = true; Eco.PreyTarget = 40; Eco.PredatorScale = 0.6f;
            Player.BeginRun(new Genome());   // the lair shows a fresh hatchling
            Player.Controllable = false;
            Eco.Populate(24);
            UI.ShowLair();
            Audio.Intensity = 0;
        }

        public void StartRun()
        {
            var save = Services.Save;
            Seed = save.seed != 0 ? save.seed : Random.Range(1, 999999);
            if (Arena.Seed != Seed) RebuildArena(Seed);
            Eco.Clear(); Proj.ClearAll(); Boss.Despawn();
            var g = new Genome();
            g.SetBonus(Lab.BonusFor(save));
            if (save.LabLevel("primer") > 0) g.GrantEvolution();
            _offerRng = new Rng(Seed * 31 + save.runs);
            Player.BeginRun(g);
            Rig.Yaw = 0; Rig.Target = Player.transform; Rig.SnapToTarget(); Rig.Cinematic(Vector3.zero, 10, 0);
            Eco.SpawningEnabled = true; Eco.PreyTarget = 58; Eco.PredatorScale = 1;
            Eco.Populate(34);
            RerollsLeft = Lab.FreeRerolls(save);
            RevivedThisRun = false; _bossSpawned = false; _bossDone = false; _lastTier = 0; RunTime = 0;
            _evoDelay = g.PendingEvolutions > 0 ? 1.2f : -1; _bossCountdown = -1;
            Player.Controllable = true;
            State = RunState.Playing;
            UI.ShowHud();
            UI.BigTitle("VIRETH TIDEPOOL", "Eat what is smaller. Flee what is not.", 3f);
            Audio.Play("sting", 0.6f, 1.2f);
            Services.Analytics.Track("run_start", ("seed", Seed.ToString()), ("run", save.runs.ToString()));
            Services.Ads.NotifyGameplay();
        }

        void RebuildArena(int seed)
        {
            foreach (Transform c in Arena.transform) Destroy(c.gameObject);
            Arena.Obstacles.Clear(); Arena.VentSpots.Clear();
            Arena.Build(seed);
            foreach (Transform c in Hazards.transform) Destroy(c.gameObject);
            Hazards.Build();
            Eco.Init(seed);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (State == RunState.Playing || State == RunState.BossIntro)
            {
                RunTime += dt;
                UpdateThreat();
                int tier = Player.Genome.Tier;
                if (tier > _lastTier) { _lastTier = tier; TierUp(tier); }
                if (_evoDelay < 0 && State == RunState.Playing && Player.Genome.PendingEvolutions > 0 && Player.IsAlive) _evoDelay = 0.6f;
                if (_evoDelay >= 0 && State == RunState.Playing) { _evoDelay -= dt; if (_evoDelay < 0) OpenEvolution(); }
                if (!_bossSpawned && tier >= 4 && _bossCountdown < 0)
                {
                    _bossCountdown = 4f;
                    UI.BigTitle("THE TRENCH STIRS", "Something enormous is waking in the centre of the pool.", 3.5f);
                    Audio.Play("roar", 0.5f, 0.6f);
                    Rig.Shake(0.4f);
                }
                if (_bossCountdown >= 0 && !_bossSpawned)
                {
                    _bossCountdown -= dt;
                    if (_bossCountdown < 0) SpawnBoss();
                }
                Audio.Intensity = Mathf.MoveTowards(Audio.Intensity, Boss.Active ? 2 : Threat, dt * 0.6f);
            }
            if (State == RunState.Dying)
            {
                _dyingT += Time.unscaledDeltaTime;
                Time.timeScale = Mathf.MoveTowards(Time.timeScale, 0.15f, Time.unscaledDeltaTime * 2);
            }
            if (State == RunState.StageClear)
            {
                _clearT += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(_clearT / 6f);
                Rig.Cinematic(Player.transform.position, Mathf.Lerp(20, 170, u * u), 1, Mathf.Lerp(55, 86, u));
                if (_clearT > 7.5f) FinishRun(true);
            }
        }

        void UpdateThreat()
        {
            float t = 0;
            var pp = Player.transform.position;
            foreach (var c in Eco.Live)
            {
                if (!c.Alive || !c.Hostile || c.Radius < Player.Radius) continue;
                float d = (c.Pos - pp).magnitude;
                if (d < 10 + c.Radius * 3) t = Mathf.Max(t, 1 - d / (10 + c.Radius * 3) + 0.4f);
            }
            Threat = Mathf.Clamp01(t);
        }

        void TierUp(int tier)
        {
            string name = Genome.TierNames[tier].ToUpperInvariant();
            UI.BigTitle(name, TierFlavor(tier), 2.6f);
            Audio.Play("sting", 0.9f, 0.8f + tier * 0.08f);
            Fx.I.Ring(Player.transform.position, Palette.Hdr(Palette.Accent, 4f), Player.Radius, Player.Radius * 14, 1.1f, 0.05f);
            Fx.I.Burst(Player.transform.position, Palette.Hdr(Palette.Accent, 3f), 40, 6 + tier * 2, Player.Radius * 0.4f, 1f);
            Rig.Punch(-0.12f); Rig.Shake(0.3f);
            UI.Flash(Palette.Accent, 0.35f);
            Services.Analytics.Track("tier_up", ("tier", tier.ToString()), ("t", RunTime.ToString("0")));
        }

        static string TierFlavor(int t) => t switch
        {
            1 => "The mites that chased you are food now.",
            2 => "Your shadow scares the shallows.",
            3 => "Predators think twice.",
            4 => "Nothing in this pool is bigger than you. Almost nothing.",
            _ => "Apex.",
        };

        void OnAte(Critter c, float value)
        {
            Services.Ads.NotifyGameplay();
            string key = "c:" + c.Kind.Id;
            if (c.Kind.Brain != Brain.Morsel && c.Kind.Brain != Brain.Ally && _seenThisSave.Add(key))
            {
                Services.Save.codex.Add(key);
                UI.Toast("NEW DNA: " + c.Kind.Name.ToUpperInvariant(), MutationCatalog.DnaName(c.Dna) + " strand absorbed", Palette.Of(c.Dna));
            }
        }

        // ------------------------------------------------------------------ evolution

        List<MutationDef> _offer;

        void OpenEvolution()
        {
            if (Player.Genome.PendingEvolutions <= 0) return;
            _offer = Evolution.Offer(Player.Genome, _offerRng, 3, Lab.RareLuck(Services.Save));
            if (Player.Genome.Flags.Contains("matriarch_infection") && Player.Genome.CanTake(MutationCatalog.Get("crown")) && !_offer.Exists(m => m.Id == "crown"))
            {
                if (_offer.Count >= 3) _offer[0] = MutationCatalog.Get("crown"); else _offer.Insert(0, MutationCatalog.Get("crown"));
            }
            if (_offer.Count == 0) { Player.Genome.SkipEvolution(); return; }
            State = RunState.Evolving;
            Time.timeScale = 0;
            Player.Controllable = false;
            Audio.Play("evolve", 0.8f, 1f);
            UI.ShowEvolution(_offer, Player.Genome);
            Services.Analytics.Track("evolution_offer", ("n", (Player.Genome.Evolutions + 1).ToString()), ("dominant", Player.Genome.Dominant.ToString()));
        }

        public bool CanAdReroll => Services.Ads.CanShow(Placements.MutationReroll, out _);

        public void Reroll(bool viaAd)
        {
            if (viaAd)
            {
                Services.Ads.ShowRewarded(Placements.MutationReroll, grant => { DoReroll(); });
                return;
            }
            if (RerollsLeft <= 0) return;
            RerollsLeft--;
            DoReroll();
        }

        void DoReroll()
        {
            _offer = Evolution.Offer(Player.Genome, _offerRng, 3, Lab.RareLuck(Services.Save));
            Audio.Play("click", 0.8f, 0.8f);
            UI.ShowEvolution(_offer, Player.Genome);
            Services.Analytics.Track("evolution_reroll");
        }

        public void Choose(MutationDef m)
        {
            if (!Player.Genome.Apply(m.Id)) return;
            if (!Services.Save.codex.Contains("m:" + m.Id)) Services.Save.codex.Add("m:" + m.Id);
            Player.Mutated();
            UI.HideEvolution();
            Time.timeScale = 1;
            State = RunState.Playing;
            Player.Controllable = true;
            Audio.Play("reward", 0.8f, 1f);
            Fx.I.Burst(Player.transform.position, Palette.Hdr(Palette.Rarity(m.Rarity), 3f), 46, 7, Player.Radius * 0.45f, 1f);
            Fx.I.Ring(Player.transform.position, Palette.Hdr(Palette.Of(m.Dna), 3f), Player.Radius * 0.5f, Player.Radius * 7, 0.8f);
            Rig.Punch(0.12f);
            UI.Flash(Palette.Of(m.Dna), m.Rarity == Rarity.Legendary ? 0.6f : 0.3f);
            UI.Toast(m.Name.ToUpperInvariant() + (Player.Genome.Level(m.Id) > 1 ? " " + Roman(Player.Genome.Level(m.Id)) : ""), Player.Genome.BuildTitle, Palette.Of(m.Dna));
            Services.Analytics.Track("evolution_chosen", ("id", m.Id), ("level", Player.Genome.Level(m.Id).ToString()));
            if (Player.Genome.PendingEvolutions > 0) _evoDelay = 0.8f;
            if (_bossDone && Player.Genome.PendingEvolutions == 0) BeginStageClear();
        }

        static string Roman(int n) => n switch { 2 => "II", 3 => "III", _ => "I" };

        // ------------------------------------------------------------------ boss

        void SpawnBoss()
        {
            _bossSpawned = true;
            Eco.ForEachNear(Vector3.zero, 26, (c, d) => { if (c.Kind.Brain != Brain.Ally) { c.Vel = c.Pos.normalized * 14; } });
            Eco.PreyTarget = 34; Eco.PredatorScale = 0.25f;
            Boss.Summon(Player.Radius);
            State = RunState.BossIntro;
            Player.Controllable = false;
            UI.BossIntro(Boss.Title);
            Services.Analytics.Track("boss_start", ("t", RunTime.ToString("0")), ("biomass", Player.Genome.BiomassTotal.ToString("0")));
        }

        void OnBossDefeated()
        {
            _bossDone = true;
            Services.Save.bossKills++;
            if (!Services.Save.skins.Contains("ember")) { Services.Save.skins.Add("ember"); UI.Toast("SKIN UNLOCKED", "Ember Maw", Palette.Hex(0xFF8A2A)); }
            Player.Genome.Flags.Add("matriarch_infection");
            Player.Genome.GrantEvolution();
            UI.BigTitle("THE MATRIARCH FALLS", "Her brood remembers you. Something of her is inside you now.", 3.5f);
            Audio.Play("sting", 1f, 0.7f);
            UI.Flash(Color.white, 0.8f);
            Services.Analytics.Track("boss_defeated", ("t", RunTime.ToString("0")));
            Services.Persist();
            _evoDelay = 3.2f;
        }

        void BeginStageClear()
        {
            State = RunState.StageClear;
            _clearT = 0;
            Player.Controllable = false;
            Eco.SpawningEnabled = false;
            UI.ShowHud(false);
            UI.BigTitle("VIRETH TIDEPOOL CONSUMED", "The pool is a puddle. Beyond its rim, a world of giants waits.", 6f);
            Audio.Intensity = 0;
        }

        // ------------------------------------------------------------------ death & results

        void OnDied()
        {
            State = RunState.Dying;
            _dyingT = 0;
            Player.Controllable = false;
            Services.Analytics.Track("death", ("tier", Player.Genome.Tier.ToString()), ("t", RunTime.ToString("0")), ("boss", Boss.Active ? "1" : "0"));
            bool canRevive = !RevivedThisRun && Services.Ads.CanShow(Placements.SecondChance, out _);
            UI.ShowRevive(canRevive);
        }

        public void AcceptRevive()
        {
            Services.Ads.ShowRewarded(Placements.SecondChance, grant =>
            {
                RevivedThisRun = true;
                Time.timeScale = 1;
                State = Boss.Active ? RunState.Playing : RunState.Playing;
                Player.Revive(0.5f);
                Player.Controllable = true;
                Projectiles.I.ClearHostile();
                UI.ShowHud();
                Services.Analytics.Track("revive", ("t", RunTime.ToString("0")));
            }, (result, why) =>
            {
                if (result != AdResult.Completed) { UI.Toast("NO AD AVAILABLE", "Your run ends here.", Palette.UiDim); FinishRun(false); }
            });
        }

        public void DeclineRevive() => FinishRun(false);

        void FinishRun(bool cleared)
        {
            Time.timeScale = 1;
            Rig.Cinematic(Player.transform.position, 20, 0);
            var g = Player.Genome;
            var save = Services.Save;
            int essence = Payout.Essence(g.BiomassTotal, Player.Kills, g.Evolutions, _bossDone, cleared);
            var r = new RunResult
            {
                Cleared = cleared, BossKilled = _bossDone, Biomass = g.BiomassTotal, Seconds = RunTime, Kills = Player.Kills,
                Evolutions = g.Evolutions, Essence = essence, Tier = g.Tier, Build = g.BuildTitle, Seed = Seed,
            };
            foreach (var kv in g.Levels) r.Mutations.Add(MutationCatalog.Get(kv.Key).Name + (kv.Value > 1 ? " " + Roman(kv.Value) : ""));
            LastResult = r;
            save.essence += essence;
            save.runs++;
            save.totalKills += Player.Kills;
            save.bestBiomass = Mathf.Max(save.bestBiomass, g.BiomassTotal);
            save.bestEvolutions = Mathf.Max(save.bestEvolutions, g.Evolutions);
            Services.Persist();
            Services.Analytics.Track("run_end", ("cleared", cleared ? "1" : "0"), ("essence", essence.ToString()), ("t", RunTime.ToString("0")), ("tier", g.Tier.ToString()));
            Services.Analytics.Flush();
            State = RunState.Results;
            Player.Controllable = false;
            Eco.SpawningEnabled = false;
            UI.ShowResults(r);
        }

        public void DoubleEssence()
        {
            var r = LastResult;
            if (r == null || r.Doubled) return;
            Services.Ads.ShowRewarded(Placements.DoubleEssence, grant =>
            {
                if (r.Doubled) return;
                r.Doubled = true;
                Services.Save.essence += r.Essence;
                Services.Persist();
                Audio.Play("reward", 1f, 1f);
                UI.ShowResults(r);
            });
        }

        /// <summary>Leaving results is the natural break where an interstitial is allowed (subject to every cap).</summary>
        public void LeaveResults()
        {
            Services.Ads.TryShowInterstitial(LastResult != null && LastResult.Cleared ? Placements.StageComplete : Placements.AfterRun);
            Eco.Clear();
            EnterLair();
        }

        public void Pause()
        {
            if (State != RunState.Playing) return;
            State = RunState.Paused;
            Time.timeScale = 0;
            UI.ShowPause();
        }

        public void Resume()
        {
            if (State != RunState.Paused) return;
            State = RunState.Playing;
            Time.timeScale = 1;
            UI.ShowHud();
        }

        public void Abandon()
        {
            Time.timeScale = 1;
            FinishRun(false);
        }

        public void ActivateFrenzy()
        {
            Services.Ads.ShowRewarded(Placements.FeedingFrenzy, grant =>
            {
                Player.FrenzyTime = 90;
                UI.Toast("FEEDING FRENZY", "+50% biomass for 90 seconds", Palette.Of(Dna.Flesh));
            });
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            Services?.Persist();
            Services?.Analytics.Flush();
            if (State == RunState.Playing) Pause();
        }

        void OnApplicationQuit() { Services?.Persist(); Services?.Analytics.Flush(); }
    }
}
