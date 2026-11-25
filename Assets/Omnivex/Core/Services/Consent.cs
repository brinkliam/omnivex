using System;

namespace OMX.Core
{
    /// <summary>What the player (and the platform) allowed. Persisted in SettingsData.</summary>
    public sealed class ConsentDecision
    {
        public bool Analytics;
        public bool PersonalizedAds;     // false = non-personalized (contextual) ads only
        public bool ChildDirected;       // COPPA / under-age: forces non-personalized and no analytics
        public bool AttAuthorized;       // iOS App Tracking Transparency result (always false off iOS)
    }

    /// <summary>
    /// Platform consent hooks. Real builds plug Google UMP (GDPR/US states), Apple ATT and an age gate in here.
    /// The stub returns "no platform requirement" so the in-game choice decides.
    /// </summary>
    public interface IConsentPlatform
    {
        string Name { get; }
        /// <summary>Google UMP form. Calls back with whether personalized ads are allowed (null = UMP not required here).</summary>
        void RequestUmp(Action<bool?> done);
        /// <summary>Apple ATT prompt. Calls back with whether tracking was authorized (false when not iOS).</summary>
        void RequestAtt(Action<bool> done);
        /// <summary>True when the audience is a child (COPPA / age gate / Play Families).</summary>
        bool IsChildDirected { get; }
    }

    public sealed class StubConsentPlatform : IConsentPlatform
    {
        public string Name => "Stub (no UMP/ATT SDK yet)";
        public bool ChildDirected;
        public bool? UmpAnswer;          // tests can simulate a UMP region that forbids personalization
        public bool AttAnswer;
        public void RequestUmp(Action<bool?> done) => done(UmpAnswer);
        public void RequestAtt(Action<bool> done) => done(AttAnswer);
        public bool IsChildDirected => ChildDirected;
    }

    /// <summary>
    /// Nothing that could track the player starts until consent is resolved: the ad SDK stays uninitialised
    /// (no ads at all) and analytics stay off. Resolve() combines the player's in-game choice with UMP, ATT and
    /// COPPA, always choosing the more private option, and only then fires OnResolved.
    /// </summary>
    public sealed class ConsentGate
    {
        readonly IConsentPlatform _platform;
        public bool Resolved { get; private set; }
        public ConsentDecision Decision { get; private set; } = new ConsentDecision();
        public event Action<ConsentDecision> OnResolved;
        public string PlatformName => _platform.Name;

        public ConsentGate(IConsentPlatform platform) { _platform = platform; }

        public void Resolve(bool analyticsChoice, bool personalizedChoice)
        {
            _platform.RequestUmp(ump =>
            {
                _platform.RequestAtt(att =>
                {
                    bool child = _platform.IsChildDirected;
                    Decision = new ConsentDecision
                    {
                        ChildDirected = child,
                        Analytics = analyticsChoice && !child,
                        AttAuthorized = att,
                        PersonalizedAds = personalizedChoice && !child && (ump ?? true),
                    };
                    Resolved = true;
                    OnResolved?.Invoke(Decision);
                });
            });
        }
    }
}
