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

### Phase 2: The Monster (human-scale town). Next

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

See the bottom of this file; it's updated after each verification pass.
