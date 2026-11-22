using System;
using System.Collections.Generic;
using OMX.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static OMX.Game.UIKit;

namespace OMX.Game
{
    public sealed partial class UIRoot
    {
        void ClearScreens() { _screens.Clear(); _screens.pickingMode = PickingMode.Ignore; }

        VisualElement Modal(float dim = 0.6f)
        {
            ClearScreens();
            var bg = Centered();
            bg.style.backgroundColor = new Color(Palette.Abyss.r, Palette.Abyss.g, Palette.Abyss.b, dim);
            _screens.Add(bg);
            return bg;
        }

        // ------------------------------------------------------------------ lair (title)

        public void ShowLair()
        {
            _hud.style.display = DisplayStyle.None;
            ClearScreens();
            var s = Services.Save;
            if (!s.settings.consentAsked) { ShowConsent(); return; }

            var root = Box(); Fill(root); root.style.justifyContent = Justify.SpaceBetween; root.style.alignItems = Align.Center;
            root.style.paddingTop = 40; root.style.paddingBottom = 30;
            _screens.Add(root);

            var top = Box(); top.style.alignItems = Align.Center;
            var title = Text("OMNIVEX", 96, Palette.Accent, true, TextAnchor.MiddleCenter);
            title.style.letterSpacing = 14;
            top.Add(title);
            top.Add(Text("Start as nothing.", 24, Palette.Ui, false, TextAnchor.MiddleCenter));
            var stats = Box(true); stats.style.marginTop = 14; stats.style.justifyContent = Justify.Center; stats.style.flexWrap = Wrap.Wrap;
            stats.Add(Chip($"ESSENCE {s.essence}", Palette.Hex(0xFFE04A), 16));
            stats.Add(Spacer(8));
            stats.Add(Chip($"BEST BIOMASS {s.bestBiomass:0}", Palette.Accent, 16));
            stats.Add(Spacer(8));
            stats.Add(Chip($"MATRIARCHS {s.bossKills}", Palette.Hex(0xFF9AE0), 16));
            top.Add(stats);
            root.Add(top);

            var bottom = Box(); bottom.style.alignItems = Align.Center;
            if (Daily.Claimable(s, DateTime.UtcNow))
            {
                var gift = Panel(14); gift.style.alignItems = Align.Center; gift.style.marginBottom = 10;
                int amount = Payout.DailyGift(s.lastDailyUtc == DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd") ? s.dailyStreak + 1 : 0);
                gift.Add(Text($"DAILY GIFT: {amount} ESSENCE", 18, Palette.Hex(0xFFE04A), true, TextAnchor.MiddleCenter));
                var gr = Box(true);
                gr.Add(Btn("CLAIM", () => { Daily.Claim(s, DateTime.UtcNow); Services.Persist(); Audio.Play("reward"); ShowLair(); }, false, 16));
                var dbl = Btn("CLAIM x2 (WATCH AD)", () =>
                {
                    Services.Ads.ShowRewarded(Placements.DailyBoost, g => { int got = Daily.Claim(s, DateTime.UtcNow); s.essence += got; Services.Persist(); Audio.Play("reward"); ShowLair(); });
                }, false, 16);
                SetBtnEnabled(dbl, Services.Ads.CanShow(Placements.DailyBoost, out _));
                gr.Add(dbl);
                gift.Add(gr);
                bottom.Add(gift);
            }
            var hatch = Btn("HATCH", () => Run.StartRun(), true, 34, 280);
            hatch.style.paddingTop = 18; hatch.style.paddingBottom = 18;
            bottom.Add(hatch);
            var frenzy = Btn("HATCH WITH FEEDING FRENZY (AD)", () =>
            {
                Services.Ads.ShowRewarded(Placements.FeedingFrenzy, g => { Run.StartRun(); Player.FrenzyTime = 90; Toast("FEEDING FRENZY", "+50% biomass for 90 seconds", Palette.Of(Dna.Flesh)); });
            }, false, 15);
            SetBtnEnabled(frenzy, Services.Ads.CanShow(Placements.FeedingFrenzy, out _));
            bottom.Add(frenzy);
            var row = Box(true); row.style.flexWrap = Wrap.Wrap; row.style.justifyContent = Justify.Center;
            row.Add(Btn("GENOME LAB", ShowLab, false, 18));
            row.Add(Btn("FORMS", ShowSkins, false, 18));
            row.Add(Btn("SETTINGS", () => ShowSettings(ShowLair), false, 18));
            bottom.Add(row);
            bottom.Add(Text("Test build · ads and purchases are simulated, no real money", 13, Palette.UiDim, false, TextAnchor.MiddleCenter));
            root.Add(bottom);
        }

        void ShowConsent()
        {
            var bg = Modal(0.75f);
            var p = Panel(28); p.style.maxWidth = 620; p.style.alignItems = Align.Center;
            p.Add(Text("BEFORE YOU HATCH", 30, Palette.Accent, true, TextAnchor.MiddleCenter));
            p.Add(Spacer(10));
            p.Add(Text("Omnivex can record anonymous gameplay statistics (how far you get, which mutations you pick, which optional ads you watch) to balance the game. No name, email, contacts or location are collected. You can change this any time in Settings.", 18, Palette.Ui, false, TextAnchor.MiddleCenter));
            p.Add(Spacer(16));
            var r = Box(true);
            r.Add(Btn("ALLOW", () => { Services.SetConsent(true); ShowLair(); }, true, 20));
            r.Add(Btn("NO THANKS", () => { Services.SetConsent(false); ShowLair(); }, false, 20));
            p.Add(r);
            bg.Add(p);
        }

        // ------------------------------------------------------------------ genome lab

        void ShowLab()
        {
            var bg = Modal(0.7f);
            var s = Services.Save;
            var p = Panel(22); p.style.width = Length.Percent(92); p.style.maxWidth = 760; p.style.maxHeight = Length.Percent(92);
            var head = Box(true); head.style.justifyContent = Justify.SpaceBetween; head.style.alignItems = Align.Center;
            head.Add(Text("GENOME LAB", 30, Palette.Accent, true));
            head.Add(Chip($"ESSENCE {s.essence}", Palette.Hex(0xFFE04A), 18));
            p.Add(head);
            p.Add(Text("Permanent upgrades carried into every hatchling.", 16, Palette.UiDim));
            var list = new ScrollView(); list.style.flexGrow = 1; list.style.marginTop = 10;
            foreach (var u in Lab.All)
            {
                int lvl = s.LabLevel(u.Id);
                var row = Box(true); row.style.alignItems = Align.Center; row.style.marginBottom = 8;
                row.style.backgroundColor = new Color(1, 1, 1, 0.04f); Radius(row, 12); Pad(row, 10);
                var info = Box(); info.style.flexGrow = 1;
                info.Add(Text($"{u.Name}   {new string('|', lvl)}{new string('.', u.MaxLevel - lvl)}", 20, Palette.Ui, true));
                info.Add(Text(u.Blurb, 15, Palette.UiDim));
                row.Add(info);
                if (lvl >= u.MaxLevel) row.Add(Chip("MAX", Palette.Accent));
                else
                {
                    int cost = u.Cost(lvl);
                    var b = Btn($"{cost}", () => { if (Lab.TryBuy(s, u.Id)) { Services.Persist(); Audio.Play("reward"); Services.Analytics.Track("lab_buy", ("id", u.Id), ("level", (lvl + 1).ToString())); ShowLab(); } else Audio.Play("error"); }, false, 18, 90);
                    SetBtnEnabled(b, s.essence >= cost);
                    row.Add(b);
                }
                list.Add(row);
            }
            p.Add(list);
            p.Add(Btn("BACK", ShowLair, false, 18));
            bg.Add(p);
        }

        // ------------------------------------------------------------------ forms (skins)

        void ShowSkins()
        {
            var bg = Modal(0.7f);
            var s = Services.Save;
            var p = Panel(22); p.style.width = Length.Percent(92); p.style.maxWidth = 760; p.style.maxHeight = Length.Percent(92);
            var head = Box(true); head.style.justifyContent = Justify.SpaceBetween; head.style.alignItems = Align.Center;
            head.Add(Text("FORMS", 30, Palette.Accent, true));
            head.Add(Chip($"SHARDS {s.skinShards}", Palette.Hex(0xE8B8FF), 18));
            p.Add(head);
            p.Add(Text("Cosmetic only. Forms never change stats.", 16, Palette.UiDim));
            var list = new ScrollView(); list.style.flexGrow = 1; list.style.marginTop = 10;
            foreach (var sk in Skins.All)
            {
                bool owned = s.skins.Contains(sk.Id) || (sk.Product != null && Services.Entitlements.Has(sk.Product));
                if (owned && !s.skins.Contains(sk.Id)) s.skins.Add(sk.Id);
                var row = Box(true); row.style.alignItems = Align.Center; row.style.marginBottom = 8;
                row.style.backgroundColor = new Color(1, 1, 1, 0.04f); Radius(row, 12); Pad(row, 10);
                var sw = Box(); sw.style.width = 44; sw.style.height = 44; Radius(sw, 22); sw.style.marginRight = 12;
                sw.style.backgroundColor = Palette.Hex(sk.Body); Border(sw, Palette.Hex(sk.Glow), 4);
                row.Add(sw);
                var info = Box(); info.style.flexGrow = 1;
                info.Add(Text(sk.Name, 20, Palette.Hex(sk.Glow), true));
                info.Add(Text(owned ? (s.skin == sk.Id ? "Equipped" : "Owned") : sk.Unlock, 15, Palette.UiDim));
                row.Add(info);
                if (owned)
                {
                    var eq = Btn(s.skin == sk.Id ? "WORN" : "WEAR", () => { s.skin = sk.Id; Player.Body.ApplySkin(sk); Services.Persist(); ShowSkins(); }, false, 16);
                    SetBtnEnabled(eq, s.skin != sk.Id);
                    row.Add(eq);
                }
                else
                {
                    if (sk.Shards > 0)
                    {
                        var shard = Btn($"{s.skinShards}/{sk.Shards}", () =>
                        {
                            if (s.skinShards >= sk.Shards) { s.skinShards -= sk.Shards; s.skins.Add(sk.Id); Services.Persist(); Audio.Play("reward"); ShowSkins(); }
                        }, false, 16);
                        SetBtnEnabled(shard, s.skinShards >= sk.Shards);
                        row.Add(shard);
                    }
                    if (sk.Product != null)
                        row.Add(Btn(Services.Purchases.PriceLabel(sk.Product), () => Services.Buy(sk.Product, ok => { if (ok) { s.skins.Add(sk.Id); Services.Persist(); Audio.Play("reward"); } ShowSkins(); }), false, 14));
                }
                list.Add(row);
            }
            p.Add(list);
            var adShard = Btn("+1 SHARD (WATCH AD)", () => Services.Ads.ShowRewarded(Placements.CosmeticProgress, g => { s.skinShards++; Services.Persist(); Audio.Play("reward"); ShowSkins(); }), false, 16);
            SetBtnEnabled(adShard, Services.Ads.CanShow(Placements.CosmeticProgress, out _));
            var r = Box(true); r.style.justifyContent = Justify.Center;
            r.Add(adShard);
            r.Add(Btn("BACK", ShowLair, false, 16));
            p.Add(r);
            bg.Add(p);
        }

        // ------------------------------------------------------------------ settings

        void ShowSettings(Action back)
        {
            var bg = Modal(0.7f);
            var st = Services.Save.settings;
            var p = Panel(24); p.style.width = Length.Percent(92); p.style.maxWidth = 640;
            p.Add(Text("SETTINGS", 30, Palette.Accent, true));
            p.Add(Slider("Music", st.music, v => { st.music = v; Audio.MusicVolume = v; }));
            p.Add(Slider("Sound effects", st.sfx, v => { st.sfx = v; Audio.SfxVolume = v; }));
            var q = Box(true); q.style.alignItems = Align.Center; q.style.marginTop = 8;
            q.Add(Text("Quality", 18, Palette.Ui)); q.Add(Spacer(12));
            string[] names = { "LOW", "MEDIUM", "HIGH" };
            for (int i = 0; i < 3; i++)
            {
                int qi = i;
                var b = Btn(names[i], () => { st.quality = qi; Bootstrap.ApplyQuality(qi); Services.Persist(); ShowSettings(back); }, Bootstrap.CurrentQuality == i, 15);
                q.Add(b);
            }
            p.Add(q);
            p.Add(Toggle("Screen shake", st.shake, v => { st.shake = v; Rig.ShakeEnabled = v; }));
            p.Add(Toggle("Reduce flashing", st.reducedFlashing, v => st.reducedFlashing = v));
            p.Add(Toggle("Anonymous analytics", st.analyticsConsent, v => Services.SetConsent(v)));
            var r = Box(true); r.style.flexWrap = Wrap.Wrap; r.style.marginTop = 10;
            bool adFree = Services.Entitlements.Has(Products.RemoveAds);
            var rm = Btn(adFree ? "AD-FREE ACTIVE" : $"REMOVE FORCED ADS {Services.Purchases.PriceLabel(Products.RemoveAds)}", () => Services.Buy(Products.RemoveAds, ok => ShowSettings(back)), false, 15);
            SetBtnEnabled(rm, !adFree);
            r.Add(rm);
            r.Add(Btn("RESTORE PURCHASES", () => Services.Purchases.Restore(list => { foreach (var x in list) Services.Entitlements.Accept(x); Toast("PURCHASES RESTORED", "", Palette.Accent); }), false, 15));
            p.Add(r);
            p.Add(Text("Remove Forced Ads stops interstitials. Optional reward videos stay available, always your choice.", 14, Palette.UiDim));
            p.Add(Btn("DONE", () => { Services.Persist(); back(); }, true, 20));
            bg.Add(p);
        }

        VisualElement Slider(string label, float value, Action<float> set)
        {
            var row = Box(true); row.style.alignItems = Align.Center; row.style.marginTop = 10;
            var l = Text(label, 18, Palette.Ui); l.style.width = 170; row.Add(l);
            var s = new UnityEngine.UIElements.Slider(0, 1) { value = value };
            s.style.flexGrow = 1;
            s.RegisterValueChangedCallback(e => set(e.newValue));
            row.Add(s);
            return row;
        }

        VisualElement Toggle(string label, bool value, Action<bool> set)
        {
            var row = Box(true); row.style.alignItems = Align.Center; row.style.marginTop = 8;
            var l = Text(label, 18, Palette.Ui); l.style.flexGrow = 1; row.Add(l);
            Button b = null;
            b = Btn(value ? "ON" : "OFF", () => { value = !value; set(value); b.text = value ? "ON" : "OFF"; }, false, 15, 80);
            row.Add(b);
            return row;
        }

        // ------------------------------------------------------------------ in-run screens

        public void ShowPause()
        {
            _hud.style.display = DisplayStyle.None;
            var bg = Modal(0.6f);
            var p = Panel(26); p.style.alignItems = Align.Center; p.style.minWidth = 340;
            p.Add(Text("PAUSED", 34, Palette.Accent, true, TextAnchor.MiddleCenter));
            var g = Player.Genome;
            p.Add(Text(g.BuildTitle, 18, Palette.Ui, false, TextAnchor.MiddleCenter));
            var muts = new List<string>();
            foreach (var kv in g.Levels) muts.Add(MutationCatalog.Get(kv.Key).Name + (kv.Value > 1 ? " x" + kv.Value : ""));
            p.Add(Text(muts.Count > 0 ? string.Join(" · ", muts) : "No mutations yet", 14, Palette.UiDim, false, TextAnchor.MiddleCenter));
            p.Add(Spacer(10));
            p.Add(Btn("RESUME", Run.Resume, true, 22, 260));
            var fr = Btn("FEEDING FRENZY 90s (WATCH AD)", () => { Run.Resume(); Run.ActivateFrenzy(); }, false, 15, 260);
            SetBtnEnabled(fr, Services.Ads.CanShow(Placements.FeedingFrenzy, out _) && Player.FrenzyTime <= 0);
            p.Add(fr);
            p.Add(Btn("SETTINGS", () => ShowSettings(ShowPause), false, 18, 260));
            p.Add(Btn("END RUN", Run.Abandon, false, 18, 260));
            bg.Add(p);
        }

        public void ShowEvolution(List<MutationDef> offer, Genome g)
        {
            _hud.style.display = DisplayStyle.None;
            var bg = Modal(0.55f);
            var col = Box(); col.style.alignItems = Align.Center; col.style.width = Length.Percent(96);
            var t = Text("EVOLVE", 54, Palette.Accent, true, TextAnchor.MiddleCenter); t.style.letterSpacing = 10;
            col.Add(t);
            var dom = g.Dominant;
            col.Add(Text($"Your diet leans {MutationCatalog.DnaName(dom)} ({g.Share(dom) * 100:0}%). Your body follows what you eat.", 17, Palette.UiDim, false, TextAnchor.MiddleCenter));
            var cards = Box(true); cards.style.flexWrap = Wrap.Wrap; cards.style.justifyContent = Justify.Center; cards.style.marginTop = 14;
            int i = 0;
            foreach (var m in offer)
            {
                var card = Card(m, g);
                card.style.opacity = 0;
                card.style.translate = new Translate(0, 40);
                int idx = i++;
                card.schedule.Execute(() => { card.style.opacity = 1; card.style.translate = new Translate(0, 0); }).ExecuteLater(60 + idx * 110);
                card.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity"), new StylePropertyName("translate") };
                card.style.transitionDuration = new List<TimeValue> { new TimeValue(0.25f), new TimeValue(0.3f) };
                cards.Add(card);
            }
            col.Add(cards);
            var rr = Box(true); rr.style.marginTop = 12;
            var free = Btn($"REROLL ({Run.RerollsLeft} FREE)", () => Run.Reroll(false), false, 17);
            SetBtnEnabled(free, Run.RerollsLeft > 0);
            rr.Add(free);
            var ad = Btn("REROLL (WATCH AD)", () => Run.Reroll(true), false, 17);
            SetBtnEnabled(ad, Run.CanAdReroll);
            rr.Add(ad);
            col.Add(rr);
            bg.Add(col);
        }

        VisualElement Card(MutationDef m, Genome g)
        {
            var rc = Palette.Rarity(m.Rarity);
            var dc = Palette.Of(m.Dna);
            var card = new Button(() => { Audio.Play("select"); Run.Choose(m); });
            card.text = "";
            card.style.width = 210; card.style.minHeight = 290; card.style.marginLeft = 8; card.style.marginRight = 8; card.style.marginBottom = 8;
            card.style.backgroundColor = new Color(0.03f, 0.1f, 0.13f, 0.96f);
            Radius(card, 18); Border(card, rc, m.Rarity == Rarity.Legendary ? 3 : 2);
            Pad(card, 16);
            card.style.flexDirection = FlexDirection.Column; card.style.alignItems = Align.FlexStart; card.style.justifyContent = Justify.FlexStart;
            card.RegisterCallback<PointerEnterEvent>(_ => { card.style.scale = new Scale(new Vector3(1.05f, 1.05f, 1)); Audio.Play("hover", 0.5f); });
            card.RegisterCallback<PointerLeaveEvent>(_ => card.style.scale = new Scale(Vector3.one));
            var swatch = Box(); swatch.style.height = 6; swatch.style.alignSelf = Align.Stretch; swatch.style.backgroundColor = dc; Radius(swatch, 3); swatch.pickingMode = PickingMode.Ignore;
            card.Add(swatch);
            card.Add(Spacer(8));
            var chips = Box(true); chips.pickingMode = PickingMode.Ignore;
            chips.Add(Chip(MutationCatalog.DnaName(m.Dna).ToUpperInvariant(), dc, 13)); chips.Add(Spacer(4));
            chips.Add(Chip(m.Rarity.ToString().ToUpperInvariant(), rc, 13));
            card.Add(chips);
            card.Add(Spacer(10));
            card.Add(Text(m.Name, 24, Palette.Ui, true));
            int lvl = g.Level(m.Id);
            card.Add(Text(lvl == 0 ? "NEW ORGAN" : $"LEVEL {lvl} > {lvl + 1}", 14, rc, true));
            card.Add(Spacer(8));
            card.Add(Text(m.Blurb, 16, Palette.UiDim));
            var grow = Box(); grow.style.flexGrow = 1; grow.pickingMode = PickingMode.Ignore; card.Add(grow);
            card.Add(Text(m.Archetype.ToString().ToUpperInvariant() + " PATH", 13, new Color(rc.r, rc.g, rc.b, 0.8f), true));
            return card;
        }

        public void HideEvolution() { ClearScreens(); _hud.style.display = DisplayStyle.Flex; }

        public void ShowRevive(bool canRevive)
        {
            _hud.style.display = DisplayStyle.None;
            var bg = Modal(0.35f);
            var p = Panel(26); p.style.alignItems = Align.Center; p.style.minWidth = 360;
            p.Add(Text("DISSOLVING...", 40, Palette.Danger, true, TextAnchor.MiddleCenter));
            p.Add(Text("Your cells scatter into the silt.", 17, Palette.UiDim, false, TextAnchor.MiddleCenter));
            p.Add(Spacer(12));
            if (canRevive)
            {
                p.Add(Btn("REFORM AT HALF HEALTH (WATCH AD)", Run.AcceptRevive, true, 18, 320));
                p.Add(Text("Once per run.", 13, Palette.UiDim, false, TextAnchor.MiddleCenter));
            }
            p.Add(Btn(canRevive ? "LET GO" : "SEE RESULTS", Run.DeclineRevive, false, 18, 320));
            bg.Add(p);
        }

        public void ShowResults(RunResult r)
        {
            _hud.style.display = DisplayStyle.None;
            var bg = Modal(0.7f);
            var p = Panel(26); p.style.alignItems = Align.Center; p.style.width = Length.Percent(92); p.style.maxWidth = 640;
            p.Add(Text(r.Cleared ? "POOL CONSUMED" : "DISSOLVED", 42, r.Cleared ? Palette.Accent : Palette.Danger, true, TextAnchor.MiddleCenter));
            p.Add(Text(r.Build, 22, Palette.Ui, true, TextAnchor.MiddleCenter));
            p.Add(Spacer(8));
            var grid = Box(true); grid.style.flexWrap = Wrap.Wrap; grid.style.justifyContent = Justify.Center;
            void Stat(string k, string v) { var b = Box(); b.style.alignItems = Align.Center; b.style.width = 130; b.style.marginBottom = 8; b.Add(Text(v, 26, Palette.Ui, true, TextAnchor.MiddleCenter)); b.Add(Text(k, 13, Palette.UiDim, false, TextAnchor.MiddleCenter)); grid.Add(b); }
            Stat("BIOMASS", r.Biomass.ToString("0"));
            Stat("DEVOURED", r.Kills.ToString());
            Stat("EVOLUTIONS", r.Evolutions.ToString());
            Stat("SIZE", Genome.TierNames[r.Tier]);
            Stat("TIME", $"{(int)r.Seconds / 60}:{(int)r.Seconds % 60:00}");
            Stat("SEED", r.Seed.ToString());
            p.Add(grid);
            p.Add(Text(r.Mutations.Count > 0 ? string.Join(" · ", r.Mutations) : "No mutations", 14, Palette.UiDim, false, TextAnchor.MiddleCenter));
            p.Add(Spacer(12));
            p.Add(Text($"+{(r.Doubled ? r.Essence * 2 : r.Essence)} ESSENCE", 32, Palette.Hex(0xFFE04A), true, TextAnchor.MiddleCenter));
            if (!r.Doubled)
            {
                var d = Btn("DOUBLE IT (WATCH AD)", Run.DoubleEssence, false, 18, 300);
                SetBtnEnabled(d, Services.Ads.CanShow(Placements.DoubleEssence, out _));
                p.Add(d);
            }
            p.Add(Btn("CONTINUE", Run.LeaveResults, true, 22, 300));
            p.Add(Text($"Share this pool with seed {r.Seed} (set it in a future build's seed field)", 12, Palette.UiDim, false, TextAnchor.MiddleCenter));
            bg.Add(p);
        }

        // ------------------------------------------------------------------ test ad presenter

        void ShowTestAd(string placement, AdFormat format, Action<AdResult, bool> done)
        {
            _adLayer.Clear();
            _adLayer.pickingMode = PickingMode.Position;
            var bg = Centered(); bg.style.backgroundColor = new Color(0.02f, 0.02f, 0.04f, 0.97f);
            _adLayer.Add(bg);
            bg.Add(Text("TEST AD", 48, Palette.Hex(0xFFE04A), true, TextAnchor.MiddleCenter));
            bg.Add(Text("Development placeholder. No real ad is shown and no money is earned.", 17, Palette.UiDim, false, TextAnchor.MiddleCenter));
            bg.Add(Text(format == AdFormat.Rewarded ? "Reward: " + Rewards.Describe(Services.Ads.Config.Get(placement)?.rewardId) : "Interstitial at a natural break", 18, Palette.Ui, false, TextAnchor.MiddleCenter));
            var count = Text("3", 40, Palette.Ui, true, TextAnchor.MiddleCenter);
            bg.Add(count);
            var row = Box(true);
            bg.Add(row);
            float prevScale = Time.timeScale;
            Time.timeScale = 0;
            bool finished = false;
            void Finish(AdResult res, bool earned)
            {
                if (finished) return;
                finished = true;
                _adLayer.Clear(); _adLayer.pickingMode = PickingMode.Ignore;
                Time.timeScale = prevScale;
                done(res, earned);
            }
            if (format == AdFormat.Rewarded) row.Add(Btn("SKIP (NO REWARD)", () => Finish(AdResult.Skipped, false), false, 15));
            int secs = 3;
            bg.schedule.Execute(() =>
            {
                secs--;
                count.text = secs > 0 ? secs.ToString() : "";
                if (secs == 0) row.Add(Btn(format == AdFormat.Rewarded ? "CLOSE & CLAIM" : "CLOSE", () => Finish(AdResult.Completed, format == AdFormat.Rewarded), true, 18));
            }).Every(1000).Until(() => secs <= 0 || finished);
        }
    }
}
