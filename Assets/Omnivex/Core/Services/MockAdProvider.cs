using System;
using System.Collections.Generic;

namespace OMX.Core
{
    /// <summary>
    /// Development-only ad provider. Never shows real ads and never earns money. The Unity layer can attach a
    /// presenter that draws a clearly-labelled "TEST AD" overlay; without one, ads complete instantly.
    /// Behaviour switches let tests simulate no-fill, failures, early closes and misbehaving SDK callbacks.
    /// </summary>
    public sealed class MockAdProvider : IAdProvider
    {
        public string Name => "Mock (test ads only)";
        public bool IsTestMode => true;
        public bool Fill = true;
        public bool FailNextShow;
        public bool SkipNextShow;
        public bool DuplicateCallbacks;
        public bool NeverCallBack;
        public bool InitSucceeds = true;
        readonly HashSet<string> _loaded = new HashSet<string>();
        public int ShowCount { get; private set; }

        /// <summary>Optional UI presenter: (placement, format, onClosed(result, rewardEarned)).</summary>
        public Action<string, AdFormat, Action<AdResult, bool>> Presenter;

        public void Initialize(Action<bool> done) => done?.Invoke(InitSucceeds);

        public bool IsReady(string placementId, AdFormat format) => Fill && _loaded.Contains(Key(placementId, format));

        public void Load(string placementId, AdFormat format) { if (Fill) _loaded.Add(Key(placementId, format)); }

        static string Key(string id, AdFormat f) => id + ":" + f;

        public void Show(string placementId, AdFormat format, Action<AdResult, bool, double> onClosed)
        {
            ShowCount++;
            _loaded.Remove(Key(placementId, format));
            if (NeverCallBack) return;
            if (FailNextShow) { FailNextShow = false; onClosed(AdResult.Failed, false, 0); return; }
            if (SkipNextShow) { SkipNextShow = false; onClosed(AdResult.Skipped, false, 0); return; }
            void Finish(AdResult r, bool earned)
            {
                onClosed(r, earned, 0);
                if (DuplicateCallbacks) onClosed(r, earned, 0);
            }
            if (Presenter != null) Presenter(placementId, format, Finish);
            else Finish(AdResult.Completed, format == AdFormat.Rewarded);
        }
    }
}
