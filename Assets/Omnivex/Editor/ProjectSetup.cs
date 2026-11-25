using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace OMX.EditorTools
{
    /// <summary>One-click configuration and builds. Safe to run repeatedly.</summary>
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string UiDir = "Assets/Omnivex/Resources/UI";

        [MenuItem("Omnivex/Setup Project")]
        public static void Run()
        {
            Directory.CreateDirectory(UiDir);
            string tss = UiDir + "/OMX_Theme.tss";
            if (!File.Exists(tss)) { File.WriteAllText(tss, "@import url(\"unity-theme://default\");\n"); AssetDatabase.ImportAsset(tss); }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tss);
            string psPath = UiDir + "/OMX_PanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(psPath);
            if (ps == null) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, psPath); }
            ps.themeStyleSheet = theme;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(720, 720);
            ps.match = 1;
            EditorUtility.SetDirty(ps);

            // Forward rendering; drop SSAO (its resources go missing in player builds and our look doesn't use it).
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                data.renderingMode = RenderingMode.Forward;
                var mapField = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var map = mapField?.GetValue(data) as System.Collections.Generic.List<long>;
                for (int i = data.rendererFeatures.Count - 1; i >= 0; i--)
                {
                    var f = data.rendererFeatures[i];
                    if (f != null && f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                    data.rendererFeatures.RemoveAt(i);
                    if (map != null && i < map.Count) map.RemoveAt(i);
                    if (f != null) { AssetDatabase.RemoveObjectFromAsset(f); Object.DestroyImmediate(f, true); }
                }
                data.SetDirty();
                EditorUtility.SetDirty(data);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (a == null) continue;
                a.supportsHDR = true;                 // bloom thresholds above 1 need HDR
                a.shadowDistance = 60;
                a.maxAdditionalLightsCount = 8;
                EditorUtility.SetDirty(a);
            }

            PlayerSettings.companyName = "Omnivex";
            PlayerSettings.productName = "Omnivex";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.omnivex.game");
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, "com.omnivex.game");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.runInBackground = false;

            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Omnivex").AddComponent<OMX.Game.Bootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[OMX] Project setup complete.");
        }

        [MenuItem("Omnivex/Build Web")]
        public static void BuildWeb()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            Run();
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.template = "PROJECT:Omnivex";
            // content-hashed file names: a new deploy can never be mixed with a visitor's cached data from an older build
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            SetWebGLProp("initialMemorySize", 256);
            SetWebGLProp("maximumMemorySize", 2048);
            SetWebGLProp("memoryGrowthMode", "Geometric");
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.WebGL, UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);
            var ubs = System.Type.GetType("UnityEditor.WebGL.UserBuildSettings, UnityEditor.WebGL.Extensions");
            var prop = ubs?.GetProperty("codeOptimization", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (prop != null) prop.SetValue(null, System.Enum.Parse(prop.PropertyType, "BuildTimes"));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = "Builds/Web", target = BuildTarget.WebGL });
            Debug.Log("[OMX] Web build: " + report.summary.result + " " + report.summary.totalSize / (1024 * 1024) + " MB");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        [MenuItem("Omnivex/Build Android (APK)")]
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Run();
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = "Builds/Android/Omnivex.apk", target = BuildTarget.Android });
            Debug.Log("[OMX] Android build: " + report.summary.result + " " + report.summary.totalSize / (1024 * 1024) + " MB");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        [MenuItem("Omnivex/Build Windows")]
        public static void BuildWindows()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            Run();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = "Builds/Windows/Omnivex.exe", target = BuildTarget.StandaloneWindows64 });
            Debug.Log("[OMX] Windows build: " + report.summary.result);
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        static void SetWebGLProp(string name, object value)
        {
            var p = typeof(PlayerSettings.WebGL).GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (p == null) { Debug.LogWarning("[OMX] PlayerSettings.WebGL." + name + " not found"); return; }
            var v = p.PropertyType.IsEnum ? System.Enum.Parse(p.PropertyType, value.ToString()) : System.Convert.ChangeType(value, p.PropertyType);
            p.SetValue(null, v);
        }
    }
}
