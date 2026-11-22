using OMX.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OMX.Game
{
    /// <summary>Builds the entire game at runtime from one object in an otherwise empty scene.</summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        public static int CurrentQuality { get; private set; } = 2;
        static Bootstrap _inst;
        RunController _run; CameraRig _rig; PlayerCreature _player; Ecosystem _eco; Arena _arena; Matriarch _boss; UIRoot _ui;
        Volume _volume; Bloom _bloom; Vignette _vig; ColorAdjustments _ca; ChromaticAberration _chroma;
        Light _sun;
        bool _dev;
        float _hurtPulse;

        void Awake()
        {
            _inst = this;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            _dev = Application.isEditor || Debug.isDebugBuild || (Application.absoluteURL ?? "").Contains("dev=1");
        }

        void Start()
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var input = new GameObject("Input").AddComponent<InputRouter>();

            _run = new GameObject("Run").AddComponent<RunController>();
            var services = new ServicesHub(_run);
            _run.Services = services;

            _rig = new GameObject("CameraRig").AddComponent<CameraRig>();
            _rig.Init();
            var cd = _rig.Cam.GetUniversalAdditionalCameraData();
            cd.renderPostProcessing = true;
            cd.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            _rig.Cam.allowHDR = true;

            BuildLighting();
            BuildVolume();

            var fx = new GameObject("Fx").AddComponent<Fx>();
            var audio = new GameObject("Audio").AddComponent<AudioDirector>();
            audio.Init();
            audio.MusicVolume = services.Save.settings.music; audio.SfxVolume = services.Save.settings.sfx;
            _rig.ShakeEnabled = services.Save.settings.shake;

            int seed = services.Save.seed != 0 ? services.Save.seed : Random.Range(1, 999999);
            _arena = new GameObject("Arena").AddComponent<Arena>();
            _arena.Build(seed);

            _eco = new GameObject("Ecosystem").AddComponent<Ecosystem>();
            _eco.Arena = _arena; _eco.Init(seed);

            _player = new GameObject("Omnivex").AddComponent<PlayerCreature>();
            _player.Arena = _arena; _player.Eco = _eco; _player.Rig = _rig; _player.Audio = audio;
            _player.Init(Skins.Get(services.Save.skin));
            _eco.Player = _player;
            fx.Init(_player.transform);

            var hazards = new GameObject("Hazards").AddComponent<Hazards>();
            hazards.Arena = _arena; hazards.Eco = _eco; hazards.Player = _player; hazards.Audio = audio;
            hazards.Build();

            var proj = new GameObject("Projectiles").AddComponent<Projectiles>();
            proj.Eco = _eco; proj.Player = _player; proj.Arena = _arena; proj.Audio = audio; proj.Rig = _rig;
            proj.Init();

            _boss = new GameObject("Matriarch").AddComponent<Matriarch>();
            _boss.Player = _player; _boss.Eco = _eco; _boss.Rig = _rig; _boss.Audio = audio; _boss.Arena = _arena;
            _boss.Build();

            // Stage 2: the town, its military and its boss (built lazily when the run reaches them)
            var town = new GameObject("Harrowgate").AddComponent<Town>();
            town.Player = _player; town.Eco = _eco; town.Rig = _rig; town.Audio = audio;
            town.gameObject.SetActive(false);
            var military = new GameObject("Military").AddComponent<Military>();
            military.Eco = _eco; military.Player = _player; military.Town = town; military.Audio = audio;
            var bastion = new GameObject("Bastion").AddComponent<Bastion>();
            bastion.Player = _player; bastion.Eco = _eco; bastion.Rig = _rig; bastion.Audio = audio; bastion.Town = town;
            bastion.Build();

            _ui = new GameObject("UI").AddComponent<UIRoot>();
            _ui.Run = _run; _ui.Services = services; _ui.Player = _player; _ui.Boss = _boss; _ui.Rig = _rig; _ui.Audio = audio; _ui.Eco = _eco;
            _ui.Init();

            _run.Player = _player; _run.Eco = _eco; _run.Arena = _arena; _run.Hazards = hazards; _run.Boss = _boss;
            _run.Rig = _rig; _run.Audio = audio; _run.UI = _ui; _run.Proj = proj;
            _run.Town = town; _run.Military = military; _run.Bastion = bastion;
            _run.OnStageChanged = ApplyMood;
            _run.Init();

            int q = services.Save.settings.quality;
            ApplyQuality(q >= 0 ? q : (Application.isMobilePlatform ? 1 : 2));

            // the lair: the hatchling idles in the shallows while the pool lives around it
            _rig.Target = _player.transform; _rig.SnapToTarget();
            _run.EnterLair();
            Debug.Log("[OMX] Omnivex ready. Seed " + seed + (_dev ? " (dev keys: F8 heal, F9 +biomass, F10 boss)" : ""));
        }

        void BuildLighting()
        {
            var sunGo = new GameObject("SurfaceLight");
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.color = Palette.KeyLight; _sun.intensity = 1.05f;
            _sun.shadows = LightShadows.Soft; _sun.shadowStrength = 0.75f;
            sunGo.transform.rotation = Quaternion.Euler(62, -35, 0);
            RenderSettings.sun = _sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.AmbSky;
            RenderSettings.ambientEquatorColor = Color.Lerp(Palette.AmbSky, Palette.AmbGround, 0.5f);
            RenderSettings.ambientGroundColor = Palette.AmbGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Palette.Fog;
            RenderSettings.fogDensity = 0.02f;
            RenderSettings.skybox = null;
            Shader.SetGlobalColor("_OMX_AmbSky", Palette.AmbSky * 0.9f);
            Shader.SetGlobalColor("_OMX_AmbGround", Palette.AmbGround);
        }

        /// <summary>Stage moods. 1: cold surface light over a teal abyss. 2: Harrowgate at dusk, sodium lamps and lit windows.</summary>
        void ApplyMood(int stage)
        {
            if (stage == 2)
            {
                _sun.color = Palette.Hex(0xB8C4FF); _sun.intensity = 0.75f;
                _sun.transform.rotation = Quaternion.Euler(48, 140, 0);
                RenderSettings.ambientSkyColor = Palette.Hex(0x3A3466);
                RenderSettings.ambientEquatorColor = Palette.Hex(0x2A2440);
                RenderSettings.ambientGroundColor = Palette.Hex(0x14101C);
                RenderSettings.fogColor = Palette.Hex(0x1C1430);
                Shader.SetGlobalColor("_OMX_AmbSky", Palette.Hex(0x3A3466));
                Shader.SetGlobalColor("_OMX_AmbGround", Palette.Hex(0x1A1420));
                _rig.Cam.backgroundColor = Palette.Hex(0x120C20);
                _ca.saturation.value = 10; _ca.postExposure.value = 0.25f;
            }
            else
            {
                _sun.color = Palette.KeyLight; _sun.intensity = 1.05f;
                _sun.transform.rotation = Quaternion.Euler(62, -35, 0);
                RenderSettings.ambientSkyColor = Palette.AmbSky;
                RenderSettings.ambientEquatorColor = Color.Lerp(Palette.AmbSky, Palette.AmbGround, 0.5f);
                RenderSettings.ambientGroundColor = Palette.AmbGround;
                RenderSettings.fogColor = Palette.Fog;
                Shader.SetGlobalColor("_OMX_AmbSky", Palette.AmbSky * 0.9f);
                Shader.SetGlobalColor("_OMX_AmbGround", Palette.AmbGround);
                _rig.Cam.backgroundColor = Palette.Abyss;
                _ca.saturation.value = 14; _ca.postExposure.value = 0.1f;
            }
        }

        void BuildVolume()
        {
            var go = new GameObject("PostFX");
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true; _volume.priority = 10;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tm = profile.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            _bloom = profile.Add<Bloom>(true); _bloom.intensity.Override(0.6f); _bloom.threshold.Override(1.0f); _bloom.scatter.Override(0.72f);
            _bloom.tint.Override(Color.Lerp(Color.white, Palette.Accent, 0.15f));
            _vig = profile.Add<Vignette>(true); _vig.intensity.Override(0.26f); _vig.smoothness.Override(0.45f); _vig.color.Override(Palette.Abyss);
            _ca = profile.Add<ColorAdjustments>(true); _ca.saturation.Override(14); _ca.contrast.Override(10); _ca.postExposure.Override(0.1f);
            _chroma = profile.Add<ChromaticAberration>(true); _chroma.intensity.Override(0f);
            var wb = profile.Add<WhiteBalance>(true); wb.temperature.Override(-6);
            _volume.sharedProfile = profile;
        }

        public static void ApplyQuality(int q)
        {
            CurrentQuality = Mathf.Clamp(q, 0, 2);
            var names = QualitySettings.names;
            int mobile = System.Array.FindIndex(names, n => n.ToLowerInvariant().Contains("mobile"));
            int pc = System.Array.FindIndex(names, n => n.ToLowerInvariant().Contains("pc"));
            int level = CurrentQuality == 2 ? (pc >= 0 ? pc : names.Length - 1) : (mobile >= 0 ? mobile : 0);
            QualitySettings.SetQualityLevel(level, true);
            if (Fx.I != null) Fx.I.Quality = CurrentQuality == 0 ? 0.45f : CurrentQuality == 1 ? 0.75f : 1f;
            if (_inst != null)
            {
                if (_inst._sun != null) _inst._sun.shadows = CurrentQuality == 0 ? LightShadows.None : LightShadows.Soft;
                if (_inst._bloom != null) _inst._bloom.active = CurrentQuality > 0;
                var cd = _inst._rig?.Cam.GetUniversalAdditionalCameraData();
                if (cd != null) cd.antialiasing = CurrentQuality == 2 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.FastApproximateAntialiasing;
            }
            Debug.Log($"[OMX] Quality {CurrentQuality} -> tier '{names[level]}'");
        }

        float _hpLast = -1;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            // keep depth fog proportional to the camera distance so growth never fogs the creature out
            if (_rig != null && _rig.Cam != null && _player != null)
            {
                float camDist = Vector3.Distance(_rig.transform.position, _player.transform.position);
                RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, 1.25f / (camDist * 1.8f + 18f), dt * 2);
            }
            if (_run != null && _run.State == RunState.Lair && _rig != null)
            {
                _rig.Yaw += dt * 5f;
                _rig.Cinematic(_player.transform.position + Vector3.up * 0.3f, 6.5f, 1, 32);
            }
            else if (_rig != null && _run != null && _run.State != RunState.StageClear && _run.State != RunState.BossIntro && !_boss.Active) _rig.Yaw = Mathf.MoveTowardsAngle(_rig.Yaw, 0, dt * 90);
            else if (_rig != null && _boss.Active) _rig.Yaw = Mathf.MoveTowardsAngle(_rig.Yaw, 0, dt * 90);

            // damage feedback: chromatic pulse and vignette tint
            if (_player != null && _player.Genome != null)
            {
                if (_hpLast >= 0 && _player.Health < _hpLast - 0.5f) _hurtPulse = 1;
                _hpLast = _player.Health;
                _hurtPulse = Mathf.MoveTowards(_hurtPulse, 0, dt * 2.5f);
                float low = _player.IsAlive ? Mathf.Clamp01(1 - _player.Health / _player.MaxHealth * 3f) : 1;
                _chroma.intensity.value = _hurtPulse * 0.6f;
                _vig.intensity.value = 0.26f + Mathf.Max(_hurtPulse * 0.2f, low * 0.15f);
                _vig.color.value = Color.Lerp(Palette.Abyss, Palette.Danger * 0.5f, Mathf.Max(_hurtPulse, low * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6))));
            }

            if (_dev) DevKeys();
        }

        void DevKeys()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || _player.Genome == null) return;
            if (kb.f9Key.wasPressedThisFrame) { _player.Genome.Feed(25, Dna.Flesh); Debug.Log("[OMX] dev +25 biomass"); }
            if (kb.f8Key.wasPressedThisFrame) _player.Revive(1f);
            if (kb.f10Key.wasPressedThisFrame) { int target = _run.Stage == 2 ? 8 : 4; while (_player.Genome.Tier < target) _player.Genome.Feed(20, _run.Stage == 2 ? Dna.Alloy : Dna.Flora); Debug.Log("[OMX] dev: grown to boss tier"); }
            if (kb.f11Key.wasPressedThisFrame) { _run.DevJumpToStage2(); Debug.Log("[OMX] dev: jumped to stage 2"); }
        }
    }
}
