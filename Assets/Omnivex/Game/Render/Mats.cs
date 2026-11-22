using System.Collections.Generic;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>Material factory over the Omnivex shaders. A null Shader.Find is an error, never a silent pink.</summary>
    public static class Mats
    {
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        static Shader _bio, _seabed, _glow, _particle;

        static Shader Find(ref Shader s, string name)
        {
            if (s != null) return s;
            s = Shader.Find(name);
            if (s == null) Debug.LogError("[OMX] Missing shader " + name);
            return s;
        }

        public static Shader BioShader => Find(ref _bio, "Omnivex/Bio");

        public static Material Bio(string key, Color rim, float rimStrength, Color glow, float veins, float wobble = 0.04f, float gloss = 0.35f, float pulse = 2f, float veinScale = 4f)
        {
            if (Cache.TryGetValue(key, out var m)) return m;
            m = new Material(Find(ref _bio, "Omnivex/Bio")) { name = key, enableInstancing = true };
            m.SetColor("_RimColor", rim); m.SetFloat("_Rim", rimStrength);
            m.SetColor("_GlowColor", glow); m.SetFloat("_VeinAmount", veins); m.SetFloat("_VeinScale", veinScale);
            m.SetFloat("_Wobble", wobble); m.SetFloat("_Gloss", gloss); m.SetFloat("_Pulse", pulse);
            Cache[key] = m;
            return m;
        }

        /// <summary>A fresh (uncached) bio material, for things whose glow animates on their own.</summary>
        public static Material BioUnique(string name, Color rim, float rimStrength, Color glow, float veins, float wobble = 0.04f)
        {
            var m = new Material(Find(ref _bio, "Omnivex/Bio")) { name = name };
            m.SetColor("_RimColor", rim); m.SetFloat("_Rim", rimStrength);
            m.SetColor("_GlowColor", glow); m.SetFloat("_VeinAmount", veins); m.SetFloat("_Wobble", wobble);
            return m;
        }

        public static Material Seabed()
        {
            if (Cache.TryGetValue("seabed", out var m)) return m;
            m = new Material(Find(ref _seabed, "Omnivex/Seabed")) { name = "seabed" };
            m.SetColor("_CausticColor", Palette.Hdr(Palette.Hex(0x7FFFF0), 0.55f));
            m.SetColor("_ZoneGlow", Palette.Hdr(Palette.Accent, 0.6f));
            Cache["seabed"] = m;
            return m;
        }

        public static Material Glow(Color hdr, float shape = 0, float thickness = 0.15f, bool unique = false)
        {
            string key = "glow" + hdr + shape + thickness;
            if (!unique && Cache.TryGetValue(key, out var m)) return m;
            m = new Material(Find(ref _glow, "Omnivex/Glow")) { name = "glow" };
            m.SetColor("_Color", hdr); m.SetFloat("_Shape", shape); m.SetFloat("_Thickness", thickness);
            if (!unique) Cache[key] = m;
            return m;
        }

        public static Material Aura(Color hdr)
        {
            var m = new Material(Find(ref _glow, "Omnivex/Glow")) { name = "aura" };
            m.SetColor("_Color", hdr); m.SetFloat("_Fresnel", 1);
            return m;
        }

        public static Material Particle(bool additive, float softness = 0.6f, float shape = 0)
        {
            string key = "p" + additive + softness + shape;
            if (Cache.TryGetValue(key, out var m)) return m;
            m = new Material(Find(ref _particle, "Omnivex/Particle")) { name = key };
            m.SetFloat("_Softness", softness); m.SetFloat("_Shape", shape);
            m.SetFloat("_SrcBlend", additive ? 1 : 5); m.SetFloat("_DstBlend", additive ? 1 : 10);
            m.SetFloat("_FogAmount", additive ? 0.3f : 1f);
            Cache[key] = m;
            return m;
        }
    }
}
