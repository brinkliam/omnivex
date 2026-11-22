using System;
using System.Collections.Generic;

namespace OMX.Core
{
    public interface IFileStore
    {
        bool Exists(string path);
        string Read(string path);
        void Write(string path, string text);
        void Delete(string path);
    }

    public sealed class MemoryFileStore : IFileStore
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public bool Exists(string path) => Files.ContainsKey(path);
        public string Read(string path) => Files[path];
        public void Write(string path, string text) => Files[path] = text;
        public void Delete(string path) => Files.Remove(path);
    }

    public enum LoadOutcome { Fresh, Loaded, RecoveredFromBackup, ResetAfterCorruption }

    /// <summary>
    /// Versioned, checksummed saves with a rolling backup. Format: "OMX|v|checksum|json". A save that fails its checksum
    /// or won't parse falls back to the backup; if both are bad the player starts fresh and the bad file is kept aside.
    /// </summary>
    public sealed class SaveStore
    {
        readonly IFileStore _fs;
        readonly string _main, _backup;
        readonly Func<SaveData, string> _toJson;
        readonly Func<string, SaveData> _fromJson;

        public SaveStore(IFileStore fs, string mainPath, Func<SaveData, string> toJson, Func<string, SaveData> fromJson)
        {
            _fs = fs; _main = mainPath; _backup = mainPath + ".bak"; _toJson = toJson; _fromJson = fromJson;
        }

        public static uint Checksum(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }

        public void Save(SaveData d)
        {
            d.version = SaveData.CurrentVersion;
            string json = _toJson(d);
            string blob = "OMX|" + d.version + "|" + Checksum(json).ToString("x8") + "|" + json;
            if (_fs.Exists(_main))
            {
                var prev = _fs.Read(_main);
                if (TryDecode(prev, out _)) _fs.Write(_backup, prev);   // only roll a known-good save into the backup
            }
            _fs.Write(_main, blob);
        }

        public SaveData Load(out LoadOutcome outcome)
        {
            if (!_fs.Exists(_main) && !_fs.Exists(_backup)) { outcome = LoadOutcome.Fresh; return new SaveData(); }
            if (_fs.Exists(_main) && TryDecode(_fs.Read(_main), out var d)) { outcome = LoadOutcome.Loaded; return d; }
            if (_fs.Exists(_backup) && TryDecode(_fs.Read(_backup), out var b))
            {
                if (_fs.Exists(_main)) _fs.Write(_main + ".corrupt", _fs.Read(_main));
                outcome = LoadOutcome.RecoveredFromBackup;
                return b;
            }
            if (_fs.Exists(_main)) _fs.Write(_main + ".corrupt", _fs.Read(_main));
            outcome = LoadOutcome.ResetAfterCorruption;
            return new SaveData();
        }

        bool TryDecode(string blob, out SaveData data)
        {
            data = null;
            try
            {
                if (string.IsNullOrEmpty(blob) || !blob.StartsWith("OMX|")) return false;
                int a = blob.IndexOf('|', 4); if (a < 0) return false;
                int b = blob.IndexOf('|', a + 1); if (b < 0) return false;
                int version = int.Parse(blob.Substring(4, a - 4));
                string sum = blob.Substring(a + 1, b - a - 1);
                string json = blob.Substring(b + 1);
                if (Checksum(json).ToString("x8") != sum) return false;
                if (version > SaveData.CurrentVersion) return false;   // written by a newer build; don't clobber it
                data = _fromJson(json);
                if (data == null) return false;
                Migrate(data, version);
                return true;
            }
            catch { data = null; return false; }
        }

        /// <summary>Upgrade older save layouts in place. Add a case per version bump.</summary>
        static void Migrate(SaveData d, int fromVersion)
        {
            if (d.lab == null) d.lab = new List<KeyLevel>();
            if (d.skins == null || d.skins.Count == 0) d.skins = new List<string> { "abyssal" };
            if (d.codex == null) d.codex = new List<string>();
            if (d.testReceipts == null) d.testReceipts = new List<string>();
            if (d.settings == null) d.settings = new SettingsData();
            if (string.IsNullOrEmpty(d.skin)) d.skin = "abyssal";
            d.version = SaveData.CurrentVersion;
        }
    }
}
