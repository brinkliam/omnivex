using System;
using System.Collections.Generic;
using System.Text;

namespace OMX.Core
{
    public sealed class AnalyticsEvent
    {
        public string name;
        public double t;                  // seconds since session start
        public string utc;
        public string session;
        public Dictionary<string, string> p = new Dictionary<string, string>();

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"name\":\"").Append(Esc(name)).Append("\",\"t\":").Append(t.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"utc\":\"").Append(utc).Append("\",\"session\":\"").Append(session).Append("\",\"p\":{");
            bool first = true;
            foreach (var kv in p)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(Esc(kv.Key)).Append("\":\"").Append(Esc(kv.Value)).Append('"');
            }
            return sb.Append("}}").ToString();
        }

        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
    }

    public interface IAnalyticsSink { void Write(AnalyticsEvent e); void Flush(); }

    /// <summary>
    /// Consent-aware analytics. Nothing is recorded until the player consents, no personal data is collected, and the
    /// session id is random per launch. Sinks decide where events go (local file now; a provider later).
    /// </summary>
    public sealed class AnalyticsService
    {
        readonly IClock _clock;
        readonly List<IAnalyticsSink> _sinks = new List<IAnalyticsSink>();
        public bool Consent { get; private set; }
        public string SessionId { get; }
        public readonly Dictionary<string, int> Counters = new Dictionary<string, int>();
        public readonly Dictionary<string, double> RevenueByPlacement = new Dictionary<string, double>();
        public double EstimatedRevenueUsd { get; private set; }
        public readonly List<AnalyticsEvent> Recent = new List<AnalyticsEvent>();
        public string Platform = "unknown";

        public AnalyticsService(IClock clock, bool consent, string sessionId)
        {
            _clock = clock; Consent = consent; SessionId = sessionId;
        }

        public void AddSink(IAnalyticsSink s) => _sinks.Add(s);

        public void SetConsent(bool consent)
        {
            Consent = consent;
            if (!consent) Recent.Clear();
        }

        public void Track(string name, params (string key, string value)[] parameters)
        {
            if (!Consent) return;
            var e = new AnalyticsEvent { name = name, t = _clock.Now, utc = _clock.UtcNow.ToString("o"), session = SessionId };
            e.p["platform"] = Platform;
            foreach (var (k, v) in parameters) e.p[k] = v ?? "";
            Counters[name] = (Counters.TryGetValue(name, out var n) ? n : 0) + 1;
            Recent.Add(e);
            if (Recent.Count > 200) Recent.RemoveAt(0);
            foreach (var s in _sinks) s.Write(e);
        }

        public void TrackRevenue(string placement, double usd)
        {
            if (!Consent) return;
            EstimatedRevenueUsd += usd;
            RevenueByPlacement[placement] = (RevenueByPlacement.TryGetValue(placement, out var v) ? v : 0) + usd;
            Track("ad_revenue_estimate", ("placement", placement), ("usd", usd.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture)));
        }

        public int Count(string name) => Counters.TryGetValue(name, out var n) ? n : 0;

        public void Flush() { foreach (var s in _sinks) s.Flush(); }
    }

    public sealed class MemorySink : IAnalyticsSink
    {
        public readonly List<AnalyticsEvent> Events = new List<AnalyticsEvent>();
        public void Write(AnalyticsEvent e) => Events.Add(e);
        public void Flush() { }
    }
}
