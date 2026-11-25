using System;
using System.IO;
using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>Disk-backed store on devices; PlayerPrefs on WebGL (where the filesystem isn't durable without syncfs).</summary>
    public sealed class DeviceFileStore : IFileStore
    {
        static bool UsePrefs => Application.platform == RuntimePlatform.WebGLPlayer;
        static string P(string path) => Path.Combine(Application.persistentDataPath, path);
        public bool Exists(string path) => UsePrefs ? PlayerPrefs.HasKey(path) : File.Exists(P(path));
        public string Read(string path) => UsePrefs ? PlayerPrefs.GetString(path) : File.ReadAllText(P(path));
        public void Write(string path, string text)
        {
            if (UsePrefs) { PlayerPrefs.SetString(path, text); PlayerPrefs.Save(); return; }
            var tmp = P(path) + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(P(path))) File.Delete(P(path));
            File.Move(tmp, P(path));   // write-then-rename: a crash mid-write never truncates the real save
        }
        public void Delete(string path) { if (UsePrefs) PlayerPrefs.DeleteKey(path); else if (File.Exists(P(path))) File.Delete(P(path)); }
    }

    /// <summary>Appends analytics as JSON lines locally until a real backend (consent-gated) is connected.</summary>
    public sealed class LocalFileSink : IAnalyticsSink
    {
        readonly string _path;
        readonly System.Text.StringBuilder _buf = new System.Text.StringBuilder();
        public LocalFileSink(string path) { _path = path; }
        public void Write(AnalyticsEvent e) { _buf.AppendLine(e.ToJson()); if (_buf.Length > 4096) Flush(); }
        public void Flush()
        {
            if (_buf.Length == 0 || Application.platform == RuntimePlatform.WebGLPlayer) { _buf.Clear(); return; }
            try { File.AppendAllText(_path, _buf.ToString()); } catch (Exception ex) { Debug.LogWarning("[OMX] analytics flush failed: " + ex.Message); }
            _buf.Clear();
        }
    }

    /// <summary>Owns save data, analytics, ads, purchases and remote config. Gameplay talks only to this.</summary>
    public sealed class ServicesHub
    {
        public SaveData Save { get; private set; }
        public LoadOutcome LoadOutcome { get; private set; }
        public readonly SaveStore Store;
        public readonly AnalyticsService Analytics;
        public readonly AdManager Ads;
        public readonly MockAdProvider MockAds;
        public readonly EntitlementStore Entitlements;
        public readonly IPurchaseProvider Purchases;
        public readonly IClock Clock = new SystemClock();
        public readonly ConsentGate Consent;

        public ServicesHub(IGameplayGate gate)
        {
            Store = new SaveStore(new DeviceFileStore(), "omnivex_save.json", d => JsonUtility.ToJson(d), j => JsonUtility.FromJson<SaveData>(j));
            Save = Store.Load(out var outcome);
            LoadOutcome = outcome;
            if (outcome != LoadOutcome.Loaded && outcome != LoadOutcome.Fresh) Debug.LogWarning("[OMX] Save load: " + outcome);

            Analytics = new AnalyticsService(Clock, Save.settings.analyticsConsent, Guid.NewGuid().ToString("N").Substring(0, 12));
            Analytics.Platform = Application.platform.ToString();
            Analytics.AddSink(new LocalFileSink(Path.Combine(Application.persistentDataPath, "omnivex_analytics.jsonl")));

            var cfg = AdConfig.Default();
            var remote = Resources.Load<TextAsset>("RemoteConfigDefaults");
            if (remote != null) { try { JsonUtility.FromJsonOverwrite(remote.text, cfg); } catch (Exception ex) { Debug.LogWarning("[OMX] remote config ignored: " + ex.Message); } }

            // Test store only. Real billing (Unity IAP / Google Play Billing) plugs in behind IPurchaseProvider.
            var store = new TestPurchaseProvider();
            foreach (var id in Save.testReceipts) store.Seed(new PurchaseReceipt { productId = id, transactionId = "TEST-restored-" + id, validated = true, isTest = true });
            Purchases = store;
            Entitlements = new EntitlementStore(cfg.testMode, Analytics);
            Purchases.Restore(list => { foreach (var r in list) Entitlements.Accept(r); });

            // Test ads only. LevelPlay plugs in behind IAdProvider once the account owner supplies app keys.
            MockAds = new MockAdProvider();
            Ads = new AdManager(cfg, MockAds, Clock, Entitlements, Analytics, gate);

            // Consent gate: no ad SDK init and no analytics until the player has chosen (and UMP/ATT/COPPA agree).
            // Until then the ad manager reports "ads not initialised", so no ads of any kind are shown.
            Consent = new ConsentGate(new StubConsentPlatform());
            Consent.OnResolved += d =>
            {
                Analytics.SetConsent(d.Analytics);
                if (!Ads.Initialized) Ads.Initialize(d);
                else Ads.UpdateConsent(d);
                Debug.Log($"[OMX] Consent resolved via {Consent.PlatformName}: analytics={d.Analytics} personalizedAds={d.PersonalizedAds} childDirected={d.ChildDirected}");
            };
            if (Save.settings.consentAsked) Consent.Resolve(Save.settings.analyticsConsent, Save.settings.personalizedAds);
            Analytics.Track("session_start", ("load", outcome.ToString()), ("runs", Save.runs.ToString()));
        }

        public void Persist() { try { Store.Save(Save); } catch (Exception ex) { Debug.LogError("[OMX] save failed: " + ex.Message); } }

        /// <summary>The first-launch choice: analytics and ad personalization together. Resolves the consent gate.</summary>
        public void SetConsent(bool yes) => SetConsent(yes, yes);

        public void SetConsent(bool analytics, bool personalizedAds)
        {
            Save.settings.consentAsked = true;
            Save.settings.analyticsConsent = analytics;
            Save.settings.personalizedAds = personalizedAds;
            Persist();
            Consent.Resolve(analytics, personalizedAds);
        }

        /// <summary>Settings toggles change one half of the choice and re-resolve.</summary>
        public void SetAnalyticsConsent(bool on) => SetConsent(on, Save.settings.personalizedAds);
        public void SetPersonalizedAds(bool on) => SetConsent(Save.settings.analyticsConsent, on);

        public void Buy(string productId, Action<bool> done)
        {
            Analytics.Track("iap_tap", ("product", productId));
            Purchases.Purchase(productId, (result, receipt) =>
            {
                bool ok = (result == PurchaseResult.Success || result == PurchaseResult.AlreadyOwned) && Entitlements.Accept(receipt) | Entitlements.Has(productId);
                if (ok && receipt != null && receipt.isTest && !Save.testReceipts.Contains(productId)) { Save.testReceipts.Add(productId); Persist(); }
                Analytics.Track("iap_result", ("product", productId), ("result", result.ToString()));
                done?.Invoke(ok);
            });
        }
    }
}
