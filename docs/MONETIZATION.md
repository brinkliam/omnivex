# Monetization

Status: **foundation only. Test ads and a test store, no real money.** Production needs the account owner's LevelPlay (or AdMob) app keys and Play Console products.

## Architecture

```
gameplay -> RunController / UIRoot -> AdManager (OMX.Core) -> IAdProvider
                                                    |-> MockAdProvider (now: draws a labelled TEST AD overlay)
                                                    |-> LevelPlayAdProvider (to add: com.unity.services.levelplay)
```

- Gameplay only ever names a **placement**. All rules live in `AdManager`: cooldowns, per-session and per-day caps, critical-moment protection, ad-free entitlement, the gap between a rewarded ad and an interstitial, and exactly-once reward delivery.
- A reward is granted only when the provider reports `Completed` with `rewardEarned`. Duplicate callbacks are ignored, with a one-time token per show. A hung provider blocks further ads instead of granting twice. Skips, failures, no-fill and offline all grant nothing and tell the player.
- Config comes from `AdConfig.Default()`. An optional `Resources/RemoteConfigDefaults.json` overrides any field. Wire Unity Remote Config, or LevelPlay's own A/B testing, to the same object for live experiments.

## Placements

| Placement | Type | Where | Reward | Caps (session/day) |
|---|---|---|---|---|
| `rw_double_essence` | rewarded | results screen | x2 run Essence | 8 / 20 |
| `rw_mutation_reroll` | rewarded | evolution screen (after the free rerolls) | a new set of 3 cards | 6 / 20 |
| `rw_second_chance` | rewarded | death screen, **once per run** | revive at 50% HP, hostile shots cleared | 4 / 12 |
| `rw_feeding_frenzy` | rewarded | lair ("hatch with frenzy") and pause menu | +50% biomass for 90 s | 2 / 6, 5 min cooldown |
| `rw_daily_boost` | rewarded | daily gift card | x2 daily gift | 1 / 1 |
| `rw_cosmetic_progress` | rewarded | Forms screen | +1 skin shard | 3 / 6 |
| `int_after_run` | interstitial | leaving the results screen | none | 2 per session, 5 min apart |
| `int_stage_complete` | interstitial | leaving a cleared stage's results | none | 1 per session |

Interstitials never appear:

- in the first 4 minutes of a session
- during play, evolution, boss fights, cinematics or the death screen (the run controller reports these as critical moments)
- within 90 s of a rewarded ad
- twice without gameplay in between
- for players who bought **Remove Forced Ads**

There are no banners. They would cheapen the look and sit near the controls.

## Purchases (test store now)

| Product | Proposed price | Effect |
|---|---|---|
| `omx_remove_ads` | $4.99 | removes interstitials (rewarded stays optional) |
| `omx_skin_crystal_devourer` | $2.99 | cosmetic form (also earnable with 8 shards) |
| `omx_skin_void_leviathan` | $2.99 | cosmetic form (also earnable with 12 shards) |
| `omx_skin_nuclear_titan` | $2.99 | cosmetic form |

Entitlements are granted only from validated receipts. Test-store receipts are honoured only while `testMode` is on, so a development unlock cannot leak into production. Nothing purchasable changes stats.

## Analytics

Analytics are consent-gated. On first launch the player gets a plain-language choice, and Settings has a toggle. Nothing is recorded without consent. No personal data is collected, and the session id is random per launch. Events:

- session and run: `session_start`, `run_start`, `run_end` (cleared, essence, time, tier), `tier_up`, `death`
- evolution: `evolution_offer`, `evolution_chosen`, `evolution_reroll`
- boss and revive: `boss_start`, `boss_defeated`, `revive`
- purchases: `lab_buy`, `iap_tap`, `iap_result`
- ads: `ad_*` (offer accepted, start, complete, skipped, failed, unavailable with reason, interstitial impression or skip with reason, duplicate callback), plus `ad_revenue_estimate` per placement

From these you can derive DAU, session length, retention (session_start by day), rewarded acceptance and completion rate, ARPDAU, eCPM estimate, ad failures, progression funnel, churn point (the last `death` tier) and ad-related abandonment (session end within 30 s of an interstitial). Events currently go to `persistentDataPath/omnivex_analytics.jsonl`. Plugging in Unity Analytics or Firebase means adding an `IAnalyticsSink`.

## To go live (account owner)

1. Create the LevelPlay app (Android, then iOS) and add the `com.unity.services.levelplay` package. Implement `LevelPlayAdProvider : IAdProvider`, using the rewarded callback's completion flag as `rewardEarned`.
2. Add the UMP / LevelPlay consent flow for GDPR, US states and COPPA (the game is not directed at children; set that in the Play Console).
3. Create the Play Console products above, add Unity IAP, implement `IPurchaseProvider` with server or Play receipt validation, and set `AdConfig.testMode = false` **only** in release builds.
4. Keep test ad units in every non-release build.
