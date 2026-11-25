# Omnivex

*Start as nothing.*

A mobile and tablet 3D evolution and devouring game built in Unity 6 (URP). You hatch as a speck in an alien tide pool and eat anything smaller than you. What you eat decides what you become. Five scale stages are planned, ending with you eating stars. This build covers Stage 1, **The Parasite**, and Stage 2, **The Monster**.

Play the web build at https://omnivex-one.vercel.app (add `?dev=1` for the test keys). Earlier design docs called the game "World Eater".

## Run it

- **Editor:** open with Unity **6000.6.9f1**. Run **Omnivex ▸ Setup Project** once. It creates `Assets/Scenes/Main.unity`, the UI panel settings, the player settings and the renderer settings. Then press Play.
- **Builds:** **Omnivex ▸ Build Web** (`Builds/Web`), **Build Android (APK)** (`Builds/Android/Omnivex.apk`), **Build Windows**.
- **Tests:** `unity test . --mode EditMode`

## Controls

| | Touch | Desktop |
|---|---|---|
| Swim | the thumbstick in the lower-left corner, or press anywhere and drag (floating stick) | WASD / arrows, or click-drag |
| Dash | DASH button, or tap with another finger on the right side of the screen | Space / Shift / right-click |
| Pause | II button | Esc / P |
| Photo mode | PHOTO button (hides the UI and saves a screenshot) | same |

Dev keys work in the Editor, development builds, and the web build with `?dev=1` (an on-screen line lists them): **F7** full heal, **F8** start the Bastion fight at boss-fight size, **F9** jump to Stage 2 at its normal starting size, **F10** grow one size tier. They auto-pick mutations along the way, so no chain of Evolve screens appears.

## What's in this build

- **Core loop:** eat smaller things to grow. The camera pulls back as you grow, so the world shrinks around you. Bigger things hunt you. Prey, predators and threats are always spawned relative to your size, so the snapjaw that chased you at minute one becomes your lunch by minute four.
- **You are what you eat:** every critter carries a DNA strand (Flesh, Flora, Mineral, Volt, Toxin, Void). Each evolution offers three mutations, weighted by your diet. The pool has five seeded feeding grounds (Kelp Thicket, Crystal Shelf, Vent Field, Bloom Reef, Rot Garden), so where you choose to hunt shapes your build.
- **15 mutations, each with a body part and a real effect:**
  - tentacles (reach)
  - second maw (biomass and bite)
  - jet siphons (speed and dash)
  - stalk eyes (view, plus a rare-prey compass)
  - chitin plates (armour)
  - barbed spines (thorns)
  - gorged mass (health)
  - acid sacs (auto-spit)
  - siren lure (pulls prey)
  - mending membrane (regen)
  - hive spores (ally spawnlings)
  - volt organ (discharge)
  - cilia (turning)
  - gravity gland (vortex)
  - the Matriarch's Crown (boss infection)

  Parts grow onto the creature with an elastic molt animation and stack visibly by level. Builds lean toward Predator, Titan, Parasite, Technophage or Cosmic, and hybrids get a combined title.
- **Ecosystem:** 11 critter kinds with distinct AI:
  - drifters and skittish fleers
  - armoured shellbits (you must be much bigger to eat them)
  - toxblooms (they burn going down unless you're toxin-adapted)
  - lunging snapjaws (wind-up telegraph), some elite
  - Lancer eels (they coil and glow, then charge in a straight line)
  - rare Gulpers worth triple biomass
  - Matriarch broodlings
  - your own spawnlings

  Predators eat smaller prey on their own. Kills burst into ichor morsels.
- **Stealth:** drift slowly inside the kelp and predators lose track of you.
- **Hazards:** thermal vents with filling telegraphs. They scald anything on them, including predators you lure across.
- **Boss: the Hollow Matriarch.** She has a cinematic rise from the trench and three attacks:
  - spore volleys with a gap to slip through
  - slam telegraphs that track where you're heading
  - brood summons

  After each slam her core is exposed and only then can you bite her. Below half health she enrages and adds a rotating spore spiral. Her death plays in slow motion with dissolve and a biomass shower. Then she *infects* you, unlocking a legendary mutation, and the camera pulls up out of the consumed pool.
- **Stage 2, Harrowgate (the town):** after the Matriarch, the camera pulls up out of the pool and you crawl out of a gutter into an alien town at dusk.
  - **Districts:** suburbs, midtown, downtown towers, a plaza, parks you can hide in, an industrial quarter and the Fort Harrow military base. All are seeded.
  - **Destruction:** every building chews apart in stages (crack, fail, collapse) with debris and dust, then bursts into biomass. Bigger buildings need a bigger creature.
  - **Military alert, 1 to 5 stars:** loud play raises it and hiding cools it. It brings patrol cars, rifle squads, gunships with homing missiles, siege tanks with telegraphed artillery, and hunter drones. Dashing makes you briefly invulnerable.
  - **Alloy DNA and 7 town mutations:** Plated Hull, Railspine, EMP Gland, Crusher Jaws, Seismic Tread, Chameleon Skin, and the boss's Reactor Heart.
  - **Boss: the Bastion,** a four-legged siege fortress. It sweeps a laser through the town, calls artillery barrages that leave its reactor overheated and open to bites, and stomps out shockwave rings you dash through. Below half health it adds drones.
- **Meta progression:** Essence from every run buys permanent upgrades in the **Genome Lab** (7 upgrades). You also get a daily gift with a streak, five cosmetic **Forms** (skins never carry stats), and a codex of discovered DNA and mutations.
- **Monetization foundation:** test ads and a test store only, no real money. See [docs/MONETIZATION.md](docs/MONETIZATION.md).
- **All original assets:** procedural meshes, custom URP shaders (living tissue with pulsing veins, a caustic seabed, glow telegraphs), and synthesised audio and adaptive music generated at startup.

Design rules, architecture and progress are in [docs/ROADMAP.md](docs/ROADMAP.md).
