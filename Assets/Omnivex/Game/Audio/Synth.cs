using System;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>Offline synthesiser. Every sound and every bar of music in Omnivex is generated here: wholly original audio.</summary>
    public static class Synth
    {
        public const int Rate = 22050;
        const float Tau = Mathf.PI * 2;

        sealed class R { uint s; public R(uint seed) { s = seed * 2654435761u + 7; } public float N() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xffffff) / (float)0x800000 - 1f; } }

        static float[] Buf(float sec) => new float[Mathf.CeilToInt(sec * Rate)];

        static AudioClip Clip(string name, float[] d, bool loop = false, float peak = 0.9f)
        {
            float m = 1e-4f; foreach (var x in d) m = Mathf.Max(m, Mathf.Abs(x));
            float k = peak / m; for (int i = 0; i < d.Length; i++) d[i] *= k;
            if (loop)
            {
                int n = Mathf.Min(Rate / 10, d.Length / 4);
                for (int i = 0; i < n; i++) { float u = i / (float)n; d[i] = d[i] * u + d[d.Length - n + i] * (1 - u); }
            }
            var c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static void LowPass(float[] d, float cutoff) { float a = 1 - Mathf.Exp(-Tau * cutoff / Rate), y = 0; for (int i = 0; i < d.Length; i++) { y += a * (d[i] - y); d[i] = y; } }
        static void HighPass(float[] d, float cutoff) { float a = 1 - Mathf.Exp(-Tau * cutoff / Rate), y = 0; for (int i = 0; i < d.Length; i++) { y += a * (d[i] - y); d[i] -= y; } }

        // ------------------------------------------------------------------ effects

        /// <summary>Wet organic swallow: a resonant bloop that drops in pitch, plus a slurp of filtered noise.</summary>
        public static AudioClip Gulp(int variant)
        {
            var d = Buf(0.32f); var r = new R((uint)(variant + 3)); float ph = 0;
            float f0 = 300 + variant * 45, f1 = 85 + variant * 10;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(f0, f1, Mathf.Pow(t / 0.32f, 0.5f)) * (1 + Mathf.Sin(t * 70) * 0.05f);
                ph += f * Tau / Rate;
                float env = Mathf.Min(1, t * 120) * Mathf.Exp(-t * 11);
                d[i] = (Mathf.Sin(ph) + 0.35f * Mathf.Sin(ph * 2.01f)) * env + r.N() * 0.18f * Mathf.Exp(-t * 30);
            }
            LowPass(d, 1800);
            return Clip("gulp" + variant, d, false, 0.8f);
        }

        public static AudioClip Squelch()
        {
            var d = Buf(0.22f); var r = new R(11);
            for (int i = 0; i < d.Length; i++) { float t = i / (float)Rate; d[i] = r.N() * Mathf.Exp(-t * 18) * (0.6f + 0.4f * Mathf.Sin(t * 400)); }
            LowPass(d, 1200); LowPass(d, 2400);
            return Clip("squelch", d, false, 0.7f);
        }

        public static AudioClip Whoosh()
        {
            var d = Buf(0.42f); var r = new R(5);
            float y = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate, u = t / 0.42f;
                float cut = Mathf.Lerp(300, 2600, Mathf.Sin(u * Mathf.PI));
                float a = 1 - Mathf.Exp(-Tau * cut / Rate);
                y += a * (r.N() - y);
                d[i] = y * Mathf.Sin(u * Mathf.PI);
            }
            return Clip("whoosh", d, false, 0.6f);
        }

        public static AudioClip Hurt()
        {
            var d = Buf(0.35f); var r = new R(9); float ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                ph += Mathf.Lerp(160, 60, t / 0.35f) * Tau / Rate;
                d[i] = (Mathf.Sin(ph) * 0.9f + r.N() * 0.5f * Mathf.Exp(-t * 25)) * Mathf.Exp(-t * 9);
            }
            LowPass(d, 1600);
            return Clip("hurt", d, false, 0.8f);
        }

        public static AudioClip Zap()
        {
            var d = Buf(0.3f); var r = new R(13);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float buzz = Mathf.Sign(Mathf.Sin(t * Tau * (110 + r.N() * 40))) * 0.4f;
                float crackle = r.N() > 0.86f ? r.N() : 0;
                d[i] = (buzz + crackle) * Mathf.Exp(-t * 10);
            }
            HighPass(d, 300);
            return Clip("zap", d, false, 0.55f);
        }

        public static AudioClip Spit()
        {
            var d = Buf(0.18f); var r = new R(17); float ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                ph += Mathf.Lerp(900, 300, t / 0.18f) * Tau / Rate;
                d[i] = (Mathf.Sin(ph) * 0.6f + r.N() * 0.6f) * Mathf.Exp(-t * 26);
            }
            LowPass(d, 2600);
            return Clip("spit", d, false, 0.6f);
        }

        public static AudioClip Evolve()
        {
            var d = Buf(2.2f); var r = new R(23);
            float[] chord = { 220, 277.18f, 329.63f, 415.3f, 554.37f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1, t * 2.2f) * Mathf.Exp(-Mathf.Max(0, t - 0.8f) * 2.2f);
                float s = 0;
                for (int k = 0; k < chord.Length; k++)
                    s += Mathf.Sin(t * Tau * chord[k] * (1 + t * 0.06f) + Mathf.Sin(t * 5 + k) * 0.4f) * (1f / (k + 1.5f));
                float shimmer = Mathf.Sin(t * Tau * 1760 * (1 + t * 0.2f)) * Mathf.Max(0, Mathf.Sin(t * Tau * 11)) * 0.12f;
                d[i] = (s + shimmer) * env + r.N() * 0.04f * env;
            }
            for (int i = 4000; i < d.Length; i++) d[i] += d[i - 4000] * 0.3f;
            return Clip("evolve", d, false, 0.7f);
        }

        public static AudioClip Sting()
        {
            var d = Buf(2.6f);
            float[] notes = { 110, 164.81f, 220, 261.63f, 329.63f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float s = 0;
                for (int k = 0; k < notes.Length; k++)
                {
                    float on = k * 0.06f;
                    if (t < on) continue;
                    float tt = t - on;
                    s += (Mathf.Sin(tt * Tau * notes[k]) + 0.3f * Mathf.Sin(tt * Tau * notes[k] * 2)) * Mathf.Exp(-tt * 1.6f) * Mathf.Min(1, tt * 60);
                }
                d[i] = s;
            }
            for (int i = 5000; i < d.Length; i++) d[i] += d[i - 5000] * 0.35f;
            return Clip("sting", d, false, 0.7f);
        }

        public static AudioClip Roar()
        {
            var d = Buf(2.4f); var r = new R(29); float p1 = 0, p2 = 0, p3 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate, u = t / 2.4f;
                float f = 55 + Mathf.Sin(u * Mathf.PI) * 30 + Mathf.Sin(t * 13) * 4;
                p1 += f * Tau / Rate; p2 += f * 1.507f * Tau / Rate; p3 += f * 0.5f * Tau / Rate;
                float saw = (p1 / Tau % 1) * 2 - 1;
                float env = Mathf.Min(1, t * 3) * Mathf.Pow(1 - u, 1.2f);
                d[i] = (saw * 0.5f + Mathf.Sin(p2) * 0.4f + Mathf.Sin(p3) * 0.7f + r.N() * 0.35f) * env;
            }
            LowPass(d, 900); LowPass(d, 1400);
            return Clip("roar", d, false, 0.95f);
        }

        public static AudioClip Boom()
        {
            var d = Buf(1.6f); var r = new R(31); float ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                ph += Mathf.Lerp(90, 28, Mathf.Min(1, t * 2)) * Tau / Rate;
                d[i] = Mathf.Sin(ph) * Mathf.Exp(-t * 3) + r.N() * Mathf.Exp(-t * 7) * 0.6f;
            }
            LowPass(d, 700);
            return Clip("boom", d, false, 0.95f);
        }

        public static AudioClip Charge()
        {
            var d = Buf(1.0f); float ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                ph += Mathf.Lerp(120, 520, t * t) * Tau / Rate;
                d[i] = (Mathf.Sin(ph) + 0.5f * Mathf.Sin(ph * 1.5f)) * Mathf.Min(1, t * 4) * (1 - t * 0.3f) * (0.7f + 0.3f * Mathf.Sin(t * Tau * 14));
            }
            return Clip("charge", d, false, 0.5f);
        }

        public static AudioClip Hiss()
        {
            var d = Buf(0.9f); var r = new R(37);
            for (int i = 0; i < d.Length; i++) { float t = i / (float)Rate; d[i] = r.N() * Mathf.Min(1, t * 30) * Mathf.Exp(-t * 3.5f); }
            HighPass(d, 600); LowPass(d, 5000);
            return Clip("hiss", d, false, 0.55f);
        }

        public static AudioClip Blip(string name, float f, float len, float peak)
        {
            var d = Buf(len); float ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                ph += f * (1 + Mathf.Exp(-t * 60) * 0.5f) * Tau / Rate;
                d[i] = (Mathf.Sin(ph) + 0.25f * Mathf.Sin(ph * 2)) * Mathf.Exp(-t * (8 / len));
            }
            return Clip(name, d, false, peak);
        }

        public static AudioClip Arp(string name, float[] f, float step, float peak)
        {
            var d = Buf(step * f.Length + 0.6f);
            for (int n = 0; n < f.Length; n++)
            {
                int s0 = (int)(n * step * Rate);
                for (int i = 0; s0 + i < d.Length; i++)
                {
                    float t = i / (float)Rate;
                    d[s0 + i] += (Mathf.Sin(t * Tau * f[n]) + 0.3f * Mathf.Sin(t * Tau * f[n] * 2)) * Mathf.Exp(-t * 7) * Mathf.Min(1, t * 400);
                }
            }
            return Clip(name, d, false, peak);
        }

        // ------------------------------------------------------------------ ambience & music

        /// <summary>The pool breathing: low filtered rumble, slow beating, distant bubbles.</summary>
        public static AudioClip Deep()
        {
            var d = Buf(12); var r = new R(41);
            for (int i = 0; i < d.Length; i++) d[i] = r.N();
            LowPass(d, 160); LowPass(d, 220);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] = d[i] * 3 * (0.7f + 0.3f * Mathf.Sin(t * Tau / 6)) + Mathf.Sin(t * Tau * 41.2f) * 0.08f * Mathf.Sin(t * Tau / 12 * 1) + Mathf.Sin(t * Tau * 61.7f) * 0.05f;
            }
            for (int k = 0; k < 40; k++)
            {
                int at = (int)((r.N() * 0.5f + 0.5f) * (d.Length - 3000)); float f = 500 + (r.N() * 0.5f + 0.5f) * 900;
                for (int i = 0; i < 2600; i++) { float t = i / (float)Rate; d[at + i] += Mathf.Sin(t * Tau * f * (1 + t * 6)) * Mathf.Exp(-t * 40) * 0.08f; }
            }
            return Clip("deep", d, true, 0.6f);
        }

        // A Phrygian-dominant flavoured scale on E: alien but tonal.
        static readonly float[] Scale = { 164.81f, 174.61f, 207.65f, 220f, 246.94f, 261.63f, 293.66f, 329.63f, 349.23f, 415.3f };
        const float Bpm = 96f;

        /// <summary>
        /// Three stems that share tempo and length so they can be layered: 0 = calm pads + glassy plucks,
        /// 1 = hunting pulse (bass + ticking), 2 = boss drive (drums + growl bass).
        /// </summary>
        public static AudioClip Stem(int layer, uint seed)
        {
            float beat = 60f / Bpm;
            float seconds = beat * 32;
            var d = Buf(seconds); var r = new R(seed + (uint)layer * 101);
            float[][] chords = { new[] { 82.41f, 123.47f, 164.81f, 207.65f }, new[] { 87.31f, 130.81f, 174.61f, 220f }, new[] { 73.42f, 110f, 146.83f, 174.61f }, new[] { 82.41f, 123.47f, 155.56f, 207.65f } };
            if (layer == 0)
            {
                for (int c = 0; c < 4; c++)
                {
                    int s0 = (int)(c * beat * 8 * Rate), sl = (int)(beat * 8 * Rate);
                    foreach (var f in chords[c])
                        for (int i = 0; i < sl + Rate && s0 + i < d.Length; i++)
                        {
                            float t = i / (float)Rate;
                            float env = Mathf.Min(1, t / 1.8f) * (i < sl ? 1 : Mathf.Max(0, 1 - (i - sl) / (float)Rate));
                            d[s0 + i] += (Mathf.Sin(t * Tau * f * 2) * 0.5f + Mathf.Sin(t * Tau * f * 4.01f) * 0.15f + Mathf.Sin(t * Tau * f * 3) * 0.1f) * env * 0.12f * (1 + 0.2f * Mathf.Sin(t * 1.3f));
                        }
                }
                int idx = 5;
                for (int n = 0; n < 64; n++)
                {
                    if (r.N() < -0.2f) continue;
                    idx = Mathf.Clamp(idx + Mathf.RoundToInt(r.N() * 2.5f), 0, Scale.Length - 1);
                    float f = Scale[idx] * 2;
                    int s0 = (int)(n * beat * 0.5f * Rate);
                    for (int i = 0; i < Rate && s0 + i < d.Length; i++)
                    {
                        float t = i / (float)Rate;
                        d[s0 + i] += (Mathf.Sin(t * Tau * f) + 0.4f * Mathf.Sin(t * Tau * f * 2.76f)) * Mathf.Exp(-t * 5) * Mathf.Min(1, t * 500) * 0.13f;
                    }
                }
                int delay = (int)(beat * 0.75f * Rate);
                for (int i = delay; i < d.Length; i++) d[i] += d[i - delay] * 0.35f;
            }
            else if (layer == 1)
            {
                for (int s = 0; s < 64; s++)
                {
                    float f = chords[s / 16][0];
                    int s0 = (int)(s * beat * 0.5f * Rate);
                    bool accent = s % 4 == 0;
                    for (int i = 0; i < beat * 0.5f * Rate && s0 + i < d.Length; i++)
                    {
                        float t = i / (float)Rate;
                        float saw = ((t * f) % 1) * 2 - 1;
                        d[s0 + i] += (saw * 0.35f + Mathf.Sin(t * Tau * f) * 0.6f) * Mathf.Exp(-t * (accent ? 5 : 9)) * (accent ? 0.5f : 0.32f);
                    }
                    // tick
                    for (int i = 0; i < 600 && s0 + i < d.Length; i++) d[s0 + i] += r.N() * Mathf.Exp(-i / 90f) * 0.08f;
                }
                LowPass(d, 1400);
            }
            else
            {
                for (int s = 0; s < 64; s++)
                {
                    int s0 = (int)(s * beat * 0.5f * Rate);
                    bool kick = s % 4 == 0 || s % 16 == 10;
                    bool snare = s % 8 == 4;
                    if (kick) for (int i = 0; i < Rate * 0.3f && s0 + i < d.Length; i++) { float t = i / (float)Rate; d[s0 + i] += Mathf.Sin(t * Tau * Mathf.Lerp(120, 40, Mathf.Min(1, t * 10))) * Mathf.Exp(-t * 9) * 0.9f; }
                    if (snare) for (int i = 0; i < Rate * 0.2f && s0 + i < d.Length; i++) { float t = i / (float)Rate; d[s0 + i] += (r.N() * 0.6f + Mathf.Sin(t * Tau * 190) * 0.3f) * Mathf.Exp(-t * 16) * 0.6f; }
                    for (int i = 0; i < 400 && s0 + i < d.Length; i++) d[s0 + i] += r.N() * Mathf.Exp(-i / 50f) * 0.12f;
                    float f = chords[s / 16][0] * 0.5f;
                    for (int i = 0; i < beat * 0.5f * Rate && s0 + i < d.Length; i++)
                    {
                        float t = i / (float)Rate;
                        float sq = Mathf.Sign(Mathf.Sin(t * Tau * f)) * 0.3f + Mathf.Sin(t * Tau * f * 1.01f) * 0.4f;
                        d[s0 + i] += sq * Mathf.Exp(-t * 3) * 0.35f;
                    }
                }
            }
            return Clip("stem" + layer, d, true, layer == 2 ? 0.8f : 0.6f);
        }
    }
}
