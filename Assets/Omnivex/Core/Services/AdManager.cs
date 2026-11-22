using System;
using System.Collections.Generic;

namespace OMX.Core
{
    public sealed class RewardGrant
    {
        public string token;
        public string rewardId;
        public string placementId;
    }

    /// <summary>
    /// The single gateway for every advertisement in the game. Enforces cooldowns, frequency caps, critical-moment
    /// protection, ad-free entitlements and exactly-once reward delivery. Gameplay never talks to an SDK directly.
    /// </summary>
    public sealed class AdManager
    {
        readonly AdConfig _config;
        readonly IAdProvider _provider;
        readonly IClock _clock;
        readonly EntitlementStore _entitlements;
        readonly AnalyticsService _analytics;
        readonly IGameplayGate _gate;

        readonly Dictionary<string, double> _lastShown = new Dictionary<string, double>();
        readonly Dictionary<string, int> _sessionCount = new Dictionary<string, int>();
        readonly Dictionary<string, int> _dayCount = new Dictionary<string, int>();
        readonly HashSet<string> _consumedTokens = new HashSet<string>();
        string _dayKey = "";
        double _lastInterstitial = double.NegativeInfinity;
        double _lastRewarded = double.NegativeInfinity;
        public double RewardedToInterstitialGapSeconds = 90;
        int _interstitialsThisSession;
        bool _gameplaySinceInterstitial = true;
        bool _inFlight;
        int _tokenSeq;
        public bool Initialized { get; private set; }
        public string ProviderName => _provider.Name;
        public bool IsTestMode => _provider.IsTestMode;
        public AdConfig Config => _config;

        public event Action<RewardGrant> OnRewardGranted;

        public AdManager(AdConfig config, IAdProvider provider, IClock clock, EntitlementStore entitlements, AnalyticsService analytics, IGameplayGate gate)
        {
            _config = config; _provider = provider; _clock = clock; _entitlements = entitlements; _analytics = analytics; _gate = gate;
        }

        public void Initialize(Action<bool> done = null)
        {
            _provider.Initialize(ok =>
            {
                Initialized = ok;
                if (ok)
                    foreach (var p in _config.placements) if (p.enabled) _provider.Load(p.id, p.format);
                done?.Invoke(ok);
            });
        }

        /// <summary>Call when the player does something meaningful; prevents back-to-back interstitials.</summary>
        public void NotifyGameplay() => _gameplaySinceInterstitial = true;

        void RollDay()
        {
            string key = _clock.UtcNow.ToString("yyyy-MM-dd");
            if (key != _dayKey) { _dayKey = key; _dayCount.Clear(); }
        }

        int Count(Dictionary<string, int> d, string id) => d.TryGetValue(id, out var n) ? n : 0;

        public bool CanShow(string placementId, out string reason)
        {
            RollDay();
            reason = "";
            var p = _config.Get(placementId);
            if (p == null) { reason = "unknown placement"; return false; }
            if (!_config.adsEnabled || !p.enabled) { reason = "disabled"; return false; }
            if (!Initialized) { reason = "ads not initialised"; return false; }
            if (_inFlight) { reason = "another ad is showing"; return false; }
            if (_gate != null && !_gate.IsOnline) { reason = "offline"; return false; }
            if (_lastShown.TryGetValue(placementId, out var last) && _clock.Now - last < p.cooldownSeconds) { reason = "cooling down"; return false; }
            if (p.maxPerSession > 0 && Count(_sessionCount, placementId) >= p.maxPerSession) { reason = "session limit"; return false; }
            if (p.maxPerDay > 0 && Count(_dayCount, placementId) >= p.maxPerDay) { reason = "daily limit"; return false; }

            if (p.format == AdFormat.Interstitial)
            {
                if (_entitlements != null && _entitlements.Has(Products.RemoveAds)) { reason = "ad-free"; return false; }
                if (_gate != null && _gate.IsCriticalMoment) { reason = "critical moment"; return false; }
                if (_clock.Now < _config.interstitialMinSessionSeconds) { reason = "too early in session"; return false; }
                if (_clock.Now - _lastInterstitial < _config.interstitialGlobalCooldownSeconds) { reason = "interstitial cooldown"; return false; }
                if (_interstitialsThisSession >= _config.interstitialMaxPerSession) { reason = "interstitial session cap"; return false; }
                if (!_gameplaySinceInterstitial) { reason = "no consecutive interstitials"; return false; }
                if (_clock.Now - _lastRewarded < RewardedToInterstitialGapSeconds) { reason = "just watched a rewarded ad"; return false; }
            }

            if (!_provider.IsReady(placementId, p.format))
            {
                _provider.Load(placementId, p.format);
                reason = "no ad ready";
                return false;
            }
            return true;
        }

        /// <summary>Offer a voluntary rewarded ad. The reward callback fires at most once, only on verified completion.</summary>
        public void ShowRewarded(string placementId, Action<RewardGrant> onReward, Action<AdResult, string> onFinished = null)
        {
            var p = _config.Get(placementId);
            _analytics?.Track("ad_rewarded_offer_accepted", ("placement", placementId));
            if (p == null || p.format != AdFormat.Rewarded || !CanShow(placementId, out var reason))
            {
                string why = p == null ? "unknown placement" : (p.format != AdFormat.Rewarded ? "not a rewarded placement" : ReasonFor(placementId));
                _analytics?.Track("ad_unavailable", ("placement", placementId), ("reason", why));
                onFinished?.Invoke(why == "offline" || why == "no ad ready" ? AdResult.Unavailable : AdResult.Blocked, why);
                return;
            }
            string token = $"{placementId}#{++_tokenSeq}";
            bool closed = false;
            _inFlight = true;
            _analytics?.Track("ad_rewarded_start", ("placement", placementId));
            _provider.Show(placementId, AdFormat.Rewarded, (result, earned, revenueUsd) =>
            {
                if (closed) { _analytics?.Track("ad_duplicate_callback", ("placement", placementId)); return; }
                closed = true;
                _inFlight = false;
                MarkShown(placementId);
                _lastRewarded = _clock.Now;
                if (result == AdResult.Completed && earned && _consumedTokens.Add(token))
                {
                    _analytics?.Track("ad_rewarded_complete", ("placement", placementId), ("reward", p.rewardId));
                    _analytics?.TrackRevenue(placementId, revenueUsd > 0 ? revenueUsd : p.estimatedEcpmUsd / 1000.0);
                    var grant = new RewardGrant { token = token, rewardId = p.rewardId, placementId = placementId };
                    onReward?.Invoke(grant);
                    OnRewardGranted?.Invoke(grant);
                }
                else
                {
                    _analytics?.Track(result == AdResult.Skipped ? "ad_rewarded_skipped" : "ad_rewarded_failed", ("placement", placementId));
                }
                onFinished?.Invoke(result, result == AdResult.Completed ? "" : result.ToString());
                _provider.Load(placementId, AdFormat.Rewarded);
            });
        }

        string ReasonFor(string placementId) { CanShow(placementId, out var r); return r; }

        /// <summary>Try to show an interstitial at a natural break. Returns false (and does nothing) if any rule says no.</summary>
        public bool TryShowInterstitial(string placementId, Action<AdResult> onFinished = null)
        {
            var p = _config.Get(placementId);
            if (p == null || p.format != AdFormat.Interstitial) return false;
            if (!CanShow(placementId, out var reason))
            {
                _analytics?.Track("ad_interstitial_skipped", ("placement", placementId), ("reason", reason));
                return false;
            }
            bool closed = false;
            _inFlight = true;
            _provider.Show(placementId, AdFormat.Interstitial, (result, _, revenueUsd) =>
            {
                if (closed) return;
                closed = true;
                _inFlight = false;
                MarkShown(placementId);
                _lastInterstitial = _clock.Now;
                _interstitialsThisSession++;
                _gameplaySinceInterstitial = false;
                if (result == AdResult.Completed || result == AdResult.Skipped)
                {
                    _analytics?.Track("ad_interstitial_impression", ("placement", placementId));
                    _analytics?.TrackRevenue(placementId, revenueUsd > 0 ? revenueUsd : p.estimatedEcpmUsd / 1000.0);
                }
                onFinished?.Invoke(result);
                _provider.Load(placementId, AdFormat.Interstitial);
            });
            return true;
        }

        void MarkShown(string id)
        {
            RollDay();
            _lastShown[id] = _clock.Now;
            _sessionCount[id] = Count(_sessionCount, id) + 1;
            _dayCount[id] = Count(_dayCount, id) + 1;
        }
    }
}
