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
        public int Kills, Evolutions, Essence, Tier, Stage = 1, Buildings;
        public string Build;
        public List<string> Mutations = new List<string>();
        public bool Doubled;
        public bool Abandoned;   // the player chose END RUN
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
        public Town Town; public Military Military; public Bastion Bastion;
        public System.Action<int> OnStageChanged;
        /// <summary>1 = the tide pool, 2 = Harrowgate.</summary>
        public int Stage { get; private set; } = 1;
        public IBoss ActiveBoss => Stage == 2 ? (IBoss)Bastion : Boss;
        int _buildingsDestroyed;
        bool _runEnded;   // set once by FinishRun: payout, results and world shutdown happen exactly once
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
            Boss.OnIntroDone += IntroDone;
            Bastion.OnIntroDone += IntroDone;
            Boss.OnDefeated += OnBossDefeated;
            Bastion.OnDefeated += OnBastionDefeated;
            Player.BossHook = (p, r, dmg) => { var b = ActiveBoss; if (b.Active && (p - b.Position).magnitude < r + b.Radius * 0.6f) b.Damage(dmg); };
            Player.BossAim = (p, range) => ActiveBoss.AimPoint(p, range);
            Proj.HitBoss = (p, r, dmg) => ActiveBoss.HitTest(p, r, dmg);
            Town.OnDestroyed += b =>
            {
                if (!b.KilledByPlayer || _runEnded) return;   // gunships, artillery and the Bastion wreck the town too; that isn't the player's noise
                Military.OnBuilding(b);
                if (b.Tree) return;
                _buildingsDestroyed++;
                Services.Ads.NotifyGameplay();
                if (_seenThisSave.Add("b:" + b.Kind)) { Services.Save.codex.Add("b:" + b.Kind); UI.Toast(b.Kind.ToUpperInvariant() + " DEVOURED", MutationCatalog.DnaName(b.Dna) + " biomass spills out", Palette.Of(b.Dna)); }
            };
            Town.Toast = kind => UI.Toast("TOO SOLID", "Grow bigger to break the " + kind, Palette.UiDim);
            Military.OnStarsChanged = stars => { if (stars > 0 && State == RunState.Playing) UI.Toast(new string('*', stars) + "  ALERT", StarText(stars), Palette.Danger); };
            foreach (var c in Services.Save.codex) _seenThisSave.Add(c);
        }

        void IntroDone() { if (State == RunState.BossIntro) { State = RunState.Playing; Player.Controllable = true; } }

        static string StarText(int s) => s switch
        {
            1 => "Patrol cars are on their way.", 2 => "Rifle squads deployed.", 3 => "Gunships in the air.",
            4 => "Siege tanks rolling out of Fort Harrow.", _ => "Hunter drones released. Hide or fight.",
        };

        // ------------------------------------------------------------------ stages

        /// <summary>Put the player, ecosystem, projectiles and mood into the pool or the town.</summary>
        void SetStage(int stage)
        {
            Stage = stage;
            bool town = stage == 2;
            Arena.gameObject.SetActive(!town); Hazards.gameObject.SetActive(!town);
            Town.gameObject.SetActive(town);
            WorldBase world = town ? (WorldBase)Town : Arena;
            Eco.Arena = world; Player.Arena = world; Proj.Arena = world;
            Eco.Stage = stage;
            Rig.ZoomScale = town ? 1.45f : 1f;
            if (!town) Military.End();
            OnStageChanged?.Invoke(stage);
        }

        void EnterStage2()
        {
            Boss.Despawn(); Proj.ClearAll(); Eco.Clear();
            Town.Clear();
            Town.Build(Seed);
            SetStage(2);
            Player.Genome.Flags.Add("stage2");
            Player.PlaceAt(Town.PlayerStart);
            Player.Heal(1f);
            Rig.Yaw = 0; Rig.Cinematic(Player.transform.position, 20, 0); Rig.SnapToTarget();
            Eco.SpawningEnabled = true; Eco.PreyTarget = 46;
            Eco.Populate(30);
            Military.Begin();
            _bossSpawned = false; _bossDone = false; _bossCountdown = -1; _buildingsDestroyed = 0;
            State = RunState.Playing;
            Player.Controllable = true;
            UI.ShowHud();
            UI.Flash(Color.white, 1f);
            UI.BigTitle("STAGE 2 - HARROWGATE", "You crawl out of the gutter. The town has never seen anything like you.", 4.5f);
            Audio.Play("sting", 1f, 0.6f);
            if (Services.Save.bestStage < 2) Services.Save.bestStage = 2;
            Services.Persist();
            Services.Analytics.Track("stage_start", ("stage", "2"), ("t", RunTime.ToString("0")), ("biomass", Player.Genome.BiomassTotal.ToString("0")));
        }

        // ------------------------------------------------------------------ dev shortcuts (?dev=1 only, wired in Bootstrap)

        readonly Rng _devRng = new Rng(5);

        void DevSettle()
        {
            Player.Mutated();
            Player.Heal(1f);
            _lastTier = Player.Genome.Tier;
            _evoDelay = -1;
        }

        /// <summary>F9: straight to Harrowgate at the normal Stage 2 starting size (end of the pool), no Evolve screens.</summary>
        public void DevJumpToStage2()
        {
            if (Stage != 1 || (State != RunState.Playing && State != RunState.BossIntro)) return;
            Boss.Despawn();
            Player.Genome.GrowSilently(Genome.TierThreshold(5), _devRng);
            DevSettle();
            EnterStage2();
            UI.Toast("DEV: STAGE 2", $"Biomass {Player.Genome.BiomassTotal:0}, {Genome.TierNames[Player.Genome.Tier]}", Palette.Warning);
        }

        /// <summary>F8: start the Bastion fight now at the normal boss-fight size, without queuing evolutions.</summary>
        public void DevBastion()
        {
            if (State != RunState.Playing) return;
            if (Stage == 1) DevJumpToStage2();
            if (Stage != 2 || Bastion.Active) return;
            Player.Genome.GrowSilently(Genome.TierThreshold(8), _devRng);
            DevSettle();
            _bossCountdown = -1;
            SpawnBoss();
        }

        /// <summary>F10: grow one size tier right now (visibly), auto-picking the evolutions on the way.</summary>
        public void DevGrowTier()
        {
            if (State != RunState.Playing) return;
            var g = Player.Genome;
            float target = g.Tier + 1 < Genome.TierNames.Length ? Genome.TierThreshold(g.Tier + 1) : g.BiomassTotal * 1.35f;
            g.GrowSilently(target, _devRng);
            Player.Mutated();
            UI.Toast("DEV: GROW", $"Biomass {g.BiomassTotal:0}, radius {g.Radius:0.0}", Palette.Warning);
        }

        // ------------------------------------------------------------------ flow

        public void EnterLair()
        {
            Time.timeScale = 1;
            State = RunState.Lair;
            Player.Controllable = false;
            Boss.Despawn(); Bastion.Despawn();
            Proj.ClearAll();
            Eco.Clear();
            SetStage(1);
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
            SetStage(1);
            Eco.Clear(); Proj.ClearAll(); Boss.Despawn(); Bastion.Despawn();
            _buildingsDestroyed = 0;
            var g = new Genome();
            g.SetBonus(Lab.BonusFor(save));
            if (save.LabLevel("primer") > 0) g.GrantEvolution();
            _offerRng = new Rng(Seed * 31 + save.runs);
            Player.BeginRun(g);
            Rig.Yaw = 0; Rig.Target = Player.transform; Rig.SnapToTarget(); Rig.Cinematic(Vector3.zero, 10, 0);
            Eco.SpawningEnabled = true; Eco.PreyTarget = 58; Eco.PredatorScale = 1;
            Eco.Populate(34);
            RerollsLeft = Lab.FreeRerolls(save);
            _runEnded = false;
            RevivedThisRun = false; _bossSpawned = false; _bossDone = false; _lastTier = 0; RunTime = 0;
            _evoDelay = g.PendingEvolutions > 0 ? 1.2f : -1; _bossCountdown = -1;
            Player.Controllable = true;
            State = RunState.Playing;
            UI.ShowHud(); UI.ResetStickHint();
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
            Services.Ads.Tick();   // real-time ad timeout, runs even while the game is paused
            float dt = Time.deltaTime;
            if (State == RunState.Playing || State == RunState.BossIntro)
            {
                RunTime += dt;
                UpdateThreat();
                int tier = Player.Genome.Tier;
                if (tier > _lastTier) { _lastTier = tier; TierUp(tier); }
                if (_evoDelay < 0 && State == RunState.Playing && Player.Genome.PendingEvolutions > 0 && Player.IsAlive) _evoDelay = 0.6f;
                if (_evoDelay >= 0 && State == RunState.Playing) { _evoDelay -= dt; if (_evoDelay < 0) OpenEvolution(); }
                if (!_bossSpawned && tier >= (Stage == 2 ? 8 : 4) && _bossCountdown < 0)
                {
                    _bossCountdown = 4f;
                    if (Stage == 2) UI.BigTitle("FORT HARROW OPENS ITS GATES", "Something with legs is marching out of the base.", 3.5f);
                    else UI.BigTitle("THE TRENCH STIRS", "Something enormous is waking in the centre of the pool.", 3.5f);
                    Audio.Play("roar", 0.5f, 0.6f);
                    Rig.Shake(0.4f);
                }
                if (_bossCountdown >= 0 && !_bossSpawned)
                {
                    _bossCountdown -= dt;
                    if (_bossCountdown < 0) SpawnBoss();
                }
                Audio.Intensity = Mathf.MoveTowards(Audio.Intensity, ActiveBoss.Active ? 2 : Mathf.Max(Threat, Stage == 2 ? Mathf.Min(1, Military.Stars * 0.3f) : 0), dt * 0.6f);
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
                if (_clearT > 7.5f) { if (Stage == 1) EnterStage2(); else FinishRun(true); }
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
            5 => "Apex of the pool. The rim is within reach.",
            6 => "Houses are snacks. Cars are mints.",
            7 => "The skyline is starting to look edible.",
            _ => "Harrowgate is yours. Fort Harrow knows it.",
        };

        void OnAte(Critter c, float value)
        {
            Services.Ads.NotifyGameplay();
            if (Stage == 2) Military.OnAte(c);
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
            foreach (var gift in new[] { ("matriarch_infection", "crown"), ("bastion_core", "reactor") })
                if (Player.Genome.Flags.Contains(gift.Item1) && Player.Genome.CanTake(MutationCatalog.Get(gift.Item2)) && !_offer.Exists(m => m.Id == gift.Item2))
                {
                    if (_offer.Count >= 3) _offer[0] = MutationCatalog.Get(gift.Item2); else _offer.Insert(0, MutationCatalog.Get(gift.Item2));
                }
            if (_offer.Count == 0)
            {
                // every mutation is maxed: spend all pending evolutions silently and carry on (never strand the stage clear)
                while (Player.Genome.PendingEvolutions > 0) Player.Genome.SkipEvolution();
                if (_bossDone && State == RunState.Playing) BeginStageClear();
                return;
            }
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
            if (Stage == 2)
            {
                Bastion.Summon(Mathf.Max(Player.Radius, Player.Genome.Radius));
                State = RunState.BossIntro;
                Player.Controllable = false;
                UI.BossIntro(Bastion.Title, "Dash through the beam and the shockwave. Bite the reactor when it overheats.");
                Services.Analytics.Track("boss_start", ("stage", "2"), ("t", RunTime.ToString("0")));
                return;
            }
            Eco.ForEachNear(Vector3.zero, 26, (c, d) => { if (c.Kind.Brain != Brain.Ally) { c.Vel = c.Pos.normalized * 14; } });
            Eco.PreyTarget = 34; Eco.PredatorScale = 0.25f;
            Boss.Summon(Player.Radius);
            State = RunState.BossIntro;
            Player.Controllable = false;
            UI.BossIntro(Boss.Title, "Wait for the slam. When her core opens, bite.");
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

        void OnBastionDefeated()
        {
            _bossDone = true;
            Services.Save.bossKills++;
            Military.End();
            Player.Genome.Flags.Add("bastion_core");
            Player.Genome.GrantEvolution();
            UI.BigTitle("THE BASTION FALLS", "Its reactor is still warm. You swallow it whole.", 3.5f);
            Audio.Play("sting", 1f, 0.55f);
            UI.Flash(Palette.Warning, 0.8f);
            Services.Analytics.Track("boss_defeated", ("stage", "2"), ("t", RunTime.ToString("0")));
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
            if (Stage == 1) UI.BigTitle("VIRETH TIDEPOOL CONSUMED", "The pool is a puddle in a gutter. Above it, a town of giants waits.", 6f);
            else UI.BigTitle("HARROWGATE CONSUMED", "The town is rubble. The sky above it is next. (Stage 3 is in development.)", 6f);
            Audio.Intensity = 0;
        }

        // ------------------------------------------------------------------ death & results

        void OnDied()
        {
            if (_runEnded || State == RunState.Results) return;   // never offer a revive once results are up
            State = RunState.Dying;
            _dyingT = 0;
            Player.Controllable = false;
            Services.Analytics.Track("death", ("tier", Player.Genome.Tier.ToString()), ("t", RunTime.ToString("0")), ("boss", ActiveBoss.Active ? "1" : "0"), ("stage", Stage.ToString()));
            bool canRevive = !RevivedThisRun && Services.Ads.CanShow(Placements.SecondChance, out _);
            UI.ShowRevive(canRevive);
        }

        public void AcceptRevive()
        {
            if (_runEnded) return;
            Services.Ads.ShowRewarded(Placements.SecondChance, grant =>
            {
                if (_runEnded) return;
                RevivedThisRun = true;
                Time.timeScale = 1;
                State = RunState.Playing;
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

        void FinishRun(bool cleared, bool abandoned = false)
        {
            if (_runEnded) return;
            _runEnded = true;
            // shut the fight down first: no boss, no shells in the air, no military, and the creature can't be hurt
            Player.RunEnded = true;
            Boss.Despawn(); Bastion.Despawn();
            Proj.ClearAll();
            Military.End();
            Eco.RemoveHostiles();
            Time.timeScale = 1;
            Rig.Cinematic(Player.transform.position, 20, 0);
            var g = Player.Genome;
            var save = Services.Save;
            int essence = Payout.Essence(g.BiomassTotal, Player.Kills, g.Evolutions, Stage == 2 || _bossDone, cleared) + (Stage == 2 ? 40 + _buildingsDestroyed + (_bossDone ? 60 : 0) : 0);
            var r = new RunResult
            {
                Cleared = cleared, BossKilled = _bossDone, Biomass = g.BiomassTotal, Seconds = RunTime, Kills = Player.Kills,
                Evolutions = g.Evolutions, Essence = essence, Tier = g.Tier, Build = g.BuildTitle, Seed = Seed, Stage = Stage, Buildings = _buildingsDestroyed, Abandoned = abandoned,
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
            FinishRun(false, abandoned: true);
        }

        public void ActivateFrenzy()
        {
            Services.Ads.ShowRewarded(Placements.FeedingFrenzy, grant =>
            {
                Player.FrenzyTime = 90;
                UI.Toast("FEEDING FRENZY", "+50% biomass for 90 seconds", Palette.Of(Dna.Flesh));
            });
        }

        void OnApplicationFocus(bool focused) => Services?.Ads.AppPaused(!focused);

        void OnApplicationPause(bool paused)
        {
            Services?.Ads.AppPaused(paused);
            if (!paused) return;
            Services?.Persist();
            Services?.Analytics.Flush();
            if (State == RunState.Playing) Pause();
        }

        void OnApplicationQuit() { Services?.Persist(); Services?.Analytics.Flush(); }
    }
}
