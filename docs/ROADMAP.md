# Omnivex Roadmap

Engine: **Unity 6000.6.9f1, URP**. Unity was chosen because it has first-class Android and iOS builds, the LevelPlay ads and Unity IAP SDKs, and URP's mobile-scalable renderer and post-processing. This machine already has the Editor with Android and WebGL modules installed, and Unity's CLI can drive the Editor for builds, tests and captures. A browser-only engine was rejected: it can't ship native ad mediation or reach the visual bar.

## Architecture

| Assembly | Purpose |
|---|---|
| `OMX.Core` (no engine refs) | genome, mutation catalogue, evolution offers, economy, saves, ads, analytics, entitlements. Pure and unit-tested. |
| `OMX.Game` | Unity layer: procedural world, critters and AI, player and body, boss, FX, audio synth, camera, input, UI Toolkit, bootstrap |
| `OMX.Editor` | `Omnivex ▸ Setup Project / Build Web / Build Android / Build Windows` |
| `OMX.Tests` | EditMode tests for every Core system, plus pacing and economy simulations |

The scene holds one object (`Bootstrap`), which builds everything from code and data. Content is data-driven: mutations live in `MutationCatalog`, critters in `Kinds`, upgrades in `Lab`, skins in `Skins`, and ad placements in `AdConfig`.

## Phases and acceptance criteria

### Phase 1: Playable foundation (Stage 1, The Parasite). Status: built; see the devlog for verification

- [x] Responsive movement: momentum steering, floating stick, keyboard and gamepad, and a dash
- [x] Satisfying consumption: swallow pull and dissolve, absorb motes, bulge, pitched gulps, camera punch
- [x] Visible growth: the radius follows biomass, the camera zooms out, tier-up shockwaves and titles
- [x] Meaningful mutations: 15, all with body parts and functional effects, weighted by diet
- [x] Attractive environment: seeded tide pool, five zones, caustics, bioluminescence, fog, bloom
- [x] Multiple enemy types: 11 kinds with distinct AI and telegraphs
- [x] Mini-boss: the Hollow Matriarch (3 patterns plus an enrage phase, skill-gated damage window)
- [x] Functional progression: Essence, Genome Lab, daily gift, forms, codex
- [x] Mobile controls: touch stick, a second finger or button to dash, safe area, portrait and landscape
- [x] Working save: versioned, checksummed, with a rolling backup and corruption recovery
- Acceptance: a first-time player understands "eat small, flee big" within 30 s, gets an evolution within about 60 s, and reaches the boss in roughly 5 to 10 min (tested by simulation). The automated tests pass. The web build runs with zero console errors.

### Phase 2: The Monster (town scale). Status: built 2026-11-22, verification in progress

- The stage transition: the pool turns out to be a puddle in a town's gutter; the camera reveals the scale jump
- Buildings with pre-fractured, multi-stage collapse (pooled chunks, dust, debris), and cars and street props
- Military escalation: squads, then trucks, then helicopters and drones, then a tank column. Alert levels respond to how loud you are (stealth vs rampage)
- Technophage mutations: plating from tanks, cannons from turrets, EMP from substations
- Acceptance: a 10 to 15 min stage with at least 3 new mechanics, a boss, and 60 fps on a mid-range Android phone

### Phase 3: The Apocalypse and the Planet Devourer

- City-scale destruction, civilisations with different defence doctrines (primitive / industrial / advanced), nukes, shields, evacuations, rival kaiju
- Planetary scale: an orbital camera transition, continent cracking, ocean draining, core absorption, moons
- Procedural planet types (toxic, frozen, lava, ocean, jungle, mechanical, crystal, living, gas giant, megastructure) from seeds

### Phase 4: Cosmic Horror and the endgame

- System travel, star absorption, galactic empires, ancient entities, universe resets (prestige) with modifiers

### Phase 5: Monetization live

- LevelPlay provider, consent (UMP), Unity IAP with receipt validation, remote config experiments, and an analytics backend sink

### Phase 6: Polish and release

- Device profiling (Android mid and low tier), texture and audio compression, store assets, privacy review, release signing

## Devlog

### 2026-11-21: Phase 1 verification pass

**Automated tests:** 37/37 EditMode tests pass (`unity test . --mode EditMode`). They cover:

- genome growth and thresholds
- mutation application, caps and flag gating
- diet-weighted offers
- save round-trip, corruption, truncation and version recovery
- ad rewards (exactly once, skip/fail/no-fill/offline, hung provider, interstitial pacing, the rewarded→interstitial gap, ad-free, caps)
- purchase receipt validation
- Genome Lab economy and the daily streak
- two simulations

**Simulations:**

- **Stage 1 pacing:** at assumed swallow rates of 0.35 / 0.5 / 0.8 per second, the boss arrives at 417 s / 292 s / 183 s, after 11 evolutions. The first simulation run found the boss arriving in about 2 minutes. Prey value was cut to 3.5·r² and the boss trigger raised to 200 biomass.
- **Meta:** the Genome Lab costs 2,980 Essence in total. That's about 92 Essence per failed run and 272 per cleared run, so roughly 20 runs to finish it. Something is affordable after one run.

**Played in the browser (WebGL build, browser pane).** Verified working:

- consent → lair → hatch, keyboard steering, eating with the new-DNA toast
- evolution cards weighted by diet: a Flesh diet showed Flesh cards; after F10 fed Flora, Flora cards appeared
- free and test-ad rerolls (the ad reroll disables on cooldown afterwards), hybrid build titles ("Armoured Predator", "Armoured Parasite")
- the boss warning, the Matriarch rising and fighting (spore volleys, brood)
- death → revive offer → results → x2 Essence via test ad (124 → 248) → lair → a Genome Lab purchase (248 → 218)

**Found and fixed:**

- A NullReferenceException every frame after a new-seed arena rebuild: vents from the old seed were still being updated.
- The seabed caustics saturated into a flat cyan wash.
- Mutation parts were hidden inside an oversized glow halo, and tentacles looked like loose beads.
- Tier titles and toasts drew over the evolution cards.
- The first seconds had no prey on screen. The pool is now seeded, and critters grow in instead of popping.
- Boss spores read as white instead of danger red.
- The test-ad overlay was see-through, the lab level pips were unclear, and the results screen mentioned a seed field that doesn't exist.

**Second build:**

- A full run went lair → 12 evolutions → boss fight → death → test-ad revive (returned at half health) → combat. There were zero console errors.
- The creature's parts now read on screen: spines, eye stalks, lure bulbs and tentacles. The kelp ribbons' black backfaces and the blown-out bulbs were fixed in the third build.

**Not yet verified:**

- Defeating the Matriarch, her infection card and the stage-clear pull-back were not played to completion by hand (only the code path exists).
- No real-device (Android) testing has happened yet, and frame rate in the browser pane isn't meaningful.
- An Android APK build hasn't been run on this machine. It has 10 GB of RAM and the first WebGL build ran out of memory while other Unity jobs were running.

**Known gaps (next):**

- Seed entry/sharing UI.
- A real LevelPlay/IAP provider, which needs account keys.
- Stage 2+.
- Cloud save: the Unity Cloud link failed because no default org is set.
