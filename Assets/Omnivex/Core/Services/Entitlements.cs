using System;
using System.Collections.Generic;

namespace OMX.Core
{
    public static class Products
    {
        public const string RemoveAds = "omx_remove_ads";            // removes interstitials + banners; rewarded stays optional
        public const string SkinVoidLeviathan = "omx_skin_void_leviathan";
        public const string SkinCrystalDevourer = "omx_skin_crystal_devourer";
        public const string SkinNuclearTitan = "omx_skin_nuclear_titan";

        public static string Price(string id) => id == RemoveAds ? "$4.99" : "$2.99";
    }

    public enum PurchaseResult { Success, Cancelled, Failed, AlreadyOwned, Unavailable }

    public sealed class PurchaseReceipt
    {
        public string productId;
        public string transactionId;
        public bool validated;     // set only after platform / server receipt validation
        public bool isTest;
    }

    /// <summary>Platform billing (Google Play Billing / StoreKit via Unity IAP on device; a test store in development).</summary>
    public interface IPurchaseProvider
    {
        string Name { get; }
        bool IsTestStore { get; }
        void Initialize(Action<bool> done);
        void Purchase(string productId, Action<PurchaseResult, PurchaseReceipt> done);
        void Restore(Action<List<PurchaseReceipt>> done);
        string PriceLabel(string productId);
    }

    /// <summary>
    /// Holds entitlements. Entitlements can only be granted from a validated receipt delivered by a purchase provider;
    /// there is no API to unlock something locally. Test-store receipts are honoured only while testMode is on, so a
    /// development unlock can never leak into a production build.
    /// </summary>
    public sealed class EntitlementStore
    {
        readonly HashSet<string> _owned = new HashSet<string>();
        readonly bool _testMode;
        readonly AnalyticsService _analytics;
        public event Action OnChanged;

        public EntitlementStore(bool testMode, AnalyticsService analytics = null)
        {
            _testMode = testMode; _analytics = analytics;
        }

        public bool Has(string productId) => _owned.Contains(productId);
        public IEnumerable<string> Owned => _owned;

        public bool Accept(PurchaseReceipt r)
        {
            if (r == null || !r.validated || string.IsNullOrEmpty(r.transactionId)) return false;
            if (r.isTest && !_testMode) return false;
            bool added = _owned.Add(r.productId);
            if (added) { _analytics?.Track("entitlement_granted", ("product", r.productId), ("test", r.isTest ? "1" : "0")); OnChanged?.Invoke(); }
            return added;
        }

        public void Revoke(string productId) { if (_owned.Remove(productId)) OnChanged?.Invoke(); }
    }

    /// <summary>Development store. Its receipts are marked as test receipts.</summary>
    public sealed class TestPurchaseProvider : IPurchaseProvider
    {
        public string Name => "Test Store (no real money)";
        public bool IsTestStore => true;
        public bool NextPurchaseCancels;
        readonly List<PurchaseReceipt> _history = new List<PurchaseReceipt>();
        int _seq;

        public void Initialize(Action<bool> done) => done?.Invoke(true);

        public void Purchase(string productId, Action<PurchaseResult, PurchaseReceipt> done)
        {
            if (NextPurchaseCancels) { NextPurchaseCancels = false; done(PurchaseResult.Cancelled, null); return; }
            foreach (var h in _history) if (h.productId == productId) { done(PurchaseResult.AlreadyOwned, h); return; }
            var r = new PurchaseReceipt { productId = productId, transactionId = "TEST-" + (++_seq), validated = true, isTest = true };
            _history.Add(r);
            done(PurchaseResult.Success, r);
        }

        public void Restore(Action<List<PurchaseReceipt>> done) => done(new List<PurchaseReceipt>(_history));

        public void Seed(PurchaseReceipt r) => _history.Add(r);

        public string PriceLabel(string productId) => Products.Price(productId) + " (test)";
    }
}
