using System.Collections.Generic;
using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>A living thing in the pool. Plain class driven by the Ecosystem's single update loop (no per-critter MonoBehaviour).</summary>
    public sealed class Critter
    {
        public CritterKind Kind;
        public GameObject Go;
        public Transform Root, Tail, JawL, JawR, Glow;
        public Transform[] Segments;
        public Renderer[] Renderers;
        public float Radius, Hp, MaxHp;
        public Vector3 Pos, Vel;
        public float Heading, Wander, T, Cooldown, StateTimer, Flash, Life;
        public int State;                 // brain-specific
        public Vector3 Aim;
        public bool Alive;
        public float Digest = -1;         // >= 0 while being swallowed
        public Vector3 DigestFrom;
        public Dna Dna;                   // morsels inherit their victim's DNA
        public float Value;               // biomass override (morsels)
        public bool Elite;

        public bool Hostile => Kind.Brain == Brain.Predator || Kind.Brain == Brain.Lancer;
    }

    /// <summary>Builds critter bodies from primitives. Silhouette first: every kind reads when filled flat black.</summary>
    public static class CritterFactory
    {
        static Mesh _sphere, _lump, _cone, _fin, _frond;
        static Mesh Sphere => _sphere ??= MeshKit.Blob(14, 10, Color.white, new Color(0.55f, 0.55f, 0.6f));
        static Mesh Lump => _lump ??= MeshKit.Blob(12, 9, Color.white, new Color(0.5f, 0.5f, 0.55f), 0.7f, 3);
        static Mesh ConeM => _cone ??= MeshKit.Cone(7, new Color(0.8f, 0.8f, 0.8f), Color.white);
        static Mesh Fin => _fin ??= MeshKit.Cone(4, new Color(0.7f, 0.7f, 0.7f), Color.white, 0.12f);
        static Mesh Frond => _frond ??= MeshKit.Ribbon(7, 0.18f, new Color(0.4f, 0.4f, 0.45f), Color.white, 3f, 2);

        static Material Body(CritterKind k, Color dna, float veins, float wobble = 0.05f) =>
            Mats.Bio("critter_" + k.Id, Palette.Hdr(dna, 0.9f), 0.9f, Palette.Hdr(dna, 2.2f), veins, wobble, 0.55f, 2.5f, 5f);

        static Transform Part(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 euler, Vector3 scale, Color tint)
        {
            var go = new GameObject("part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localEulerAngles = euler; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (tint != Color.white) { var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", tint); mr.SetPropertyBlock(mpb); }
            return go.transform;
        }

        public static Critter Build(CritterKind k, Transform container)
        {
            var c = new Critter { Kind = k, Dna = k.Dna };
            var go = new GameObject(k.Id); go.transform.SetParent(container, false);
            c.Go = go; c.Root = go.transform;
            var col = Palette.Of(k.Dna);
            var dark = Color.Lerp(col, Palette.Abyss, 0.6f);
            var eye = Mats.Bio("eye", Color.black, 0, Color.black, 0, 0, 1f);
            eye.SetColor("_EmissionColor", Palette.Hdr(Palette.Hex(0xFFF6C8), 3f));
            Material body;
            switch (k.Brain)
            {
                case Brain.Morsel:
                    body = Mats.Bio("morsel", Color.white, 0.4f, Color.white, 0, 0.12f, 0.9f);
                    var m = Part(c.Root, Sphere, body, Vector3.zero, Vector3.zero, Vector3.one, Color.white);
                    c.Glow = m;
                    break;
                default:
                    body = Body(k, col, k == Kinds.Gulper ? 1f : 0.6f);
                    break;
            }

            if (k == Kinds.Driftspore)
            {
                Part(c.Root, Sphere, body, Vector3.zero, Vector3.zero, Vector3.one, Color.Lerp(col, Color.white, 0.2f));
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 60 * Mathf.Deg2Rad;
                    Part(c.Root, Sphere, body, new Vector3(Mathf.Cos(a) * 0.5f, 0.05f, Mathf.Sin(a) * 0.5f), Vector3.zero, Vector3.one * 0.22f, Color.white);
                }
            }
            else if (k == Kinds.Glimmermite || k == Kinds.Spawnling)
            {
                var tint = k == Kinds.Spawnling ? Palette.Accent : col;
                if (k == Kinds.Spawnling) body = Mats.Bio("critter_spawnling", Palette.Hdr(Palette.Accent, 1.2f), 1f, Palette.Hdr(Palette.Accent, 2.5f), 0.8f, 0.05f, 0.6f, 4f, 6f);
                Part(c.Root, Sphere, body, Vector3.zero, Vector3.zero, new Vector3(0.75f, 0.6f, 1.15f), tint);
                c.Tail = Part(c.Root, Fin, body, new Vector3(0, 0, -0.5f), new Vector3(0, 180, 0), new Vector3(0.5f, 0.15f, 0.6f), tint);
                Part(c.Root, Sphere, eye, new Vector3(0.18f, 0.18f, 0.42f), Vector3.zero, Vector3.one * 0.16f, Color.white);
                Part(c.Root, Sphere, eye, new Vector3(-0.18f, 0.18f, 0.42f), Vector3.zero, Vector3.one * 0.16f, Color.white);
            }
            else if (k == Kinds.Shellbit)
            {
                Part(c.Root, Lump, body, Vector3.zero, Vector3.zero, new Vector3(1, 0.6f, 1), dark);
                var shell = Mats.Bio("shellplate", Palette.Hdr(Palette.Of(Dna.Mineral), 1.4f), 1.1f, Palette.Hdr(Palette.Of(Dna.Mineral), 1.8f), 0.4f, 0, 0.9f);
                for (int i = 0; i < 5; i++)
                {
                    float a = i * 72 * Mathf.Deg2Rad;
                    Part(c.Root, Sphere, shell, new Vector3(Mathf.Cos(a) * 0.25f, 0.22f, Mathf.Sin(a) * 0.25f), new Vector3(Mathf.Sin(a) * 25, i * 72, Mathf.Cos(a) * 25), new Vector3(0.5f, 0.18f, 0.42f), col);
                }
            }
            else if (k == Kinds.Voltling)
            {
                Part(c.Root, Sphere, body, Vector3.zero, Vector3.zero, Vector3.one * 0.85f, col);
                for (int i = 0; i < 4; i++) Part(c.Root, ConeM, body, Vector3.zero, new Vector3(i % 2 == 0 ? -25 : 25, i * 90, 0), new Vector3(0.2f, 0.2f, 0.75f), Color.white);
                c.Glow = c.Root;
            }
            else if (k == Kinds.Toxbloom)
            {
                Part(c.Root, Lump, body, new Vector3(0, -0.15f, 0), Vector3.zero, new Vector3(1, 0.5f, 1), dark);
                for (int i = 0; i < 7; i++)
                {
                    float a = i / 7f * 360;
                    Part(c.Root, Frond, body, new Vector3(0, 0.05f, 0), new Vector3(28, a, 0), new Vector3(0.9f, 0.9f, 0.9f), col);
                }
            }
            else if (k == Kinds.Snapjaw || k == Kinds.Brood)
            {
                var hide = k == Kinds.Brood ? Palette.Of(Dna.Void) : Palette.Hex(0xC23A50);
                Part(c.Root, Lump, body, Vector3.zero, Vector3.zero, new Vector3(0.8f, 0.6f, 1.15f), hide);
                c.JawL = Part(c.Root, ConeM, body, new Vector3(0.2f, -0.02f, 0.38f), new Vector3(0, 18, 0), new Vector3(0.22f, 0.16f, 0.55f), Palette.Hex(0xFFE8D0));
                c.JawR = Part(c.Root, ConeM, body, new Vector3(-0.2f, -0.02f, 0.38f), new Vector3(0, -18, 0), new Vector3(0.22f, 0.16f, 0.55f), Palette.Hex(0xFFE8D0));
                c.Tail = Part(c.Root, Fin, body, new Vector3(0, 0.05f, -0.52f), new Vector3(0, 180, 0), new Vector3(0.55f, 0.3f, 0.5f), hide);
                var red = Mats.Bio("eye_red", Color.black, 0, Color.black, 0, 0, 1f);
                red.SetColor("_EmissionColor", Palette.Hdr(Palette.Warning, 4f));
                Part(c.Root, Sphere, red, new Vector3(0.2f, 0.24f, 0.3f), Vector3.zero, Vector3.one * 0.13f, Color.white);
                Part(c.Root, Sphere, red, new Vector3(-0.2f, 0.24f, 0.3f), Vector3.zero, Vector3.one * 0.13f, Color.white);
                for (int i = 0; i < 3; i++) Part(c.Root, ConeM, body, new Vector3(0, 0.26f, 0.1f - i * 0.22f), new Vector3(-70, 0, 0), new Vector3(0.12f, 0.12f, 0.3f), Color.white);
            }
            else if (k == Kinds.Lancer)
            {
                Part(c.Root, Sphere, body, Vector3.zero, Vector3.zero, new Vector3(0.55f, 0.45f, 1.1f), Palette.Hex(0x3A3410));
                Part(c.Root, ConeM, body, new Vector3(0, 0, 0.4f), Vector3.zero, new Vector3(0.25f, 0.2f, 0.7f), col);
                var yeye = Mats.Bio("eye_volt", Color.black, 0, Color.black, 0, 0, 1f);
                yeye.SetColor("_EmissionColor", Palette.Hdr(Palette.Of(Dna.Volt), 4f));
                Part(c.Root, Sphere, yeye, new Vector3(0.16f, 0.15f, 0.2f), Vector3.zero, Vector3.one * 0.12f, Color.white);
                Part(c.Root, Sphere, yeye, new Vector3(-0.16f, 0.15f, 0.2f), Vector3.zero, Vector3.one * 0.12f, Color.white);
                c.Segments = new Transform[6];
                for (int i = 0; i < 6; i++)
                {
                    var seg = new GameObject("seg"); seg.transform.SetParent(container, false);
                    seg.transform.SetParent(go.transform, true);
                    var s = Part(seg.transform, Sphere, body, Vector3.zero, Vector3.zero, Vector3.one, i % 2 == 0 ? Palette.Hex(0x3A3410) : col);
                    c.Segments[i] = seg.transform;
                }
            }
            else if (k == Kinds.Gulper)
            {
                Part(c.Root, Sphere, body, Vector3.zero, Vector3.zero, new Vector3(1, 0.8f, 1.1f), Palette.Hex(0x3A2466));
                Part(c.Root, Sphere, eye, new Vector3(0, 0.35f, 0.35f), Vector3.zero, Vector3.one * 0.18f, Color.white);
                var halo = Mats.Aura(Palette.Hdr(Palette.Of(Dna.Void), 2f));
                var h = Part(c.Root, Sphere, halo, Vector3.zero, Vector3.zero, Vector3.one * 1.25f, Color.white);
                c.Glow = h;
                for (int i = 0; i < 5; i++) Part(c.Root, Frond, body, new Vector3(0, -0.2f, -0.2f), new Vector3(150, i * 30 - 60, 0), new Vector3(0.7f, 1.2f, 0.7f), Palette.Of(Dna.Void));
            }

            var list = new List<Renderer>();
            go.GetComponentsInChildren(true, list);
            c.Renderers = list.ToArray();
            return c;
        }
    }
}
