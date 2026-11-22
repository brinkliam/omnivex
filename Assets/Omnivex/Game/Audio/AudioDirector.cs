using System.Collections.Generic;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>
    /// Plays the synthesised library. Music is three tempo-locked stems layered by intensity: calm, hunting
    /// (a threat is near), and boss. Separate music and SFX volumes.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> _pool = new List<AudioSource>();
        AudioSource[] _stems;
        AudioSource _amb;
        AudioClip[] _gulps;
        public float MusicVolume = 0.7f, SfxVolume = 0.9f;
        public float Intensity;    // 0 calm, 1 hunting, 2 boss
        float[] _stemVol = new float[3];
        int _voiceCursor;

        public void Init()
        {
            _gulps = new[] { Synth.Gulp(0), Synth.Gulp(1), Synth.Gulp(2), Synth.Gulp(3) };
            _clips["squelch"] = Synth.Squelch();
            _clips["dash"] = Synth.Whoosh();
            _clips["hurt"] = Synth.Hurt();
            _clips["zap"] = Synth.Zap();
            _clips["spit"] = Synth.Spit();
            _clips["evolve"] = Synth.Evolve();
            _clips["sting"] = Synth.Sting();
            _clips["roar"] = Synth.Roar();
            _clips["slam"] = Synth.Boom();
            _clips["charge"] = Synth.Charge();
            _clips["vent"] = Synth.Hiss();
            _clips["click"] = Synth.Blip("click", 880, 0.08f, 0.35f);
            _clips["hover"] = Synth.Blip("hover", 1320, 0.05f, 0.15f);
            _clips["select"] = Synth.Arp("select", new[] { 659.25f, 830.61f, 987.77f }, 0.06f, 0.4f);
            _clips["reward"] = Synth.Arp("reward", new[] { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f }, 0.07f, 0.45f);
            _clips["error"] = Synth.Blip("error", 180, 0.2f, 0.35f);

            for (int i = 0; i < 16; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 0;
                _pool.Add(s);
            }
            _amb = gameObject.AddComponent<AudioSource>();
            _amb.clip = Synth.Deep(); _amb.loop = true; _amb.volume = 0.5f; _amb.Play();

            _stems = new AudioSource[3];
            double start = AudioSettings.dspTime + 0.2;
            for (int i = 0; i < 3; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.clip = Synth.Stem(i, 7); s.loop = true; s.volume = 0; s.playOnAwake = false;
                s.PlayScheduled(start);
                _stems[i] = s;
            }
        }

        AudioSource Voice()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                var s = _pool[(_voiceCursor + i) % _pool.Count];
                if (!s.isPlaying) { _voiceCursor = (_voiceCursor + i + 1) % _pool.Count; return s; }
            }
            _voiceCursor = (_voiceCursor + 1) % _pool.Count;
            return _pool[_voiceCursor];
        }

        public void Play(string id, float vol = 1, float pitch = 1)
        {
            if (!_clips.TryGetValue(id, out var c)) return;
            var s = Voice();
            s.pitch = pitch; s.volume = vol * SfxVolume; s.clip = c; s.Play();
        }

        /// <summary>Distance-attenuated without a 3D source: quieter the further from the camera's focus.</summary>
        public void At(string id, Vector3 pos, float vol = 1)
        {
            var cam = Camera.main;
            float d = cam != null ? Vector3.Distance(cam.transform.position, pos) : 0;
            Play(id, vol * Mathf.Clamp01(1.4f - d / 60f), 1 + Random.Range(-0.06f, 0.06f));
        }

        /// <summary>Swallow sound pitched by how big the meal was relative to you.</summary>
        public void Gulp(float sizeRatio, bool morsel)
        {
            var s = Voice();
            s.clip = morsel ? _clips["squelch"] : _gulps[Random.Range(0, _gulps.Length)];
            s.pitch = Mathf.Clamp(1.45f - sizeRatio * 0.7f, 0.7f, 1.5f) * Random.Range(0.94f, 1.06f);
            s.volume = (0.35f + sizeRatio * 0.5f) * SfxVolume;
            s.Play();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float calm = 1f;
            float hunt = Mathf.Clamp01(Intensity);
            float boss = Mathf.Clamp01(Intensity - 1);
            float[] target = { calm * (1 - boss * 0.5f), hunt * (1 - boss * 0.3f), boss };
            for (int i = 0; i < 3; i++)
            {
                _stemVol[i] = Mathf.MoveTowards(_stemVol[i], target[i], dt * 0.5f);
                if (_stems != null) _stems[i].volume = _stemVol[i] * MusicVolume * (i == 2 ? 0.75f : 0.55f);
            }
            if (_amb != null) _amb.volume = 0.45f * SfxVolume;
        }
    }
}
