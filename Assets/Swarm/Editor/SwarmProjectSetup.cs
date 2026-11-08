using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Swarm.EditorTools
{
    /// <summary>
    /// Настройка проекта без ручных шагов: при открытии создаёт сцену Main, прописывает её в Build Settings
    /// и выставляет Player Settings под Android (портрет, IL2CPP, ARM64+ARMv7, Gamma).
    /// Меню SWARM → Build Android APK и метод для батч-сборки из командной строки.
    /// </summary>
    [InitializeOnLoad]
    public static class SwarmProjectSetup
    {
        public const string ScenePath = "Assets/Swarm/Scenes/Main.unity";
        public const string ApkPath = "Builds/Android/Swarm.apk";
        const string AppId = "com.hellfire.swarm";

        static SwarmProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode) return;
                if (!File.Exists(ScenePath) || EditorBuildSettings.scenes.Length == 0)
                {
                    Setup();
                }
            };
        }

        [MenuItem("SWARM/Setup Project", priority = 0)]
        public static void Setup()
        {
            EnsureScene();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
            Debug.Log("[SWARM] Проект настроен: сцена " + ScenePath + ", Android/портрет/IL2CPP. Жми Play.");
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                // пустая сцена: всё собирается кодом в GameBootstrap
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.Refresh();
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "HellFire";
            PlayerSettings.productName = "РОЙ";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.runInBackground = false;

            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, AppId);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.renderOutsideSafeArea = true;
        }

        [MenuItem("SWARM/Build Android APK", priority = 20)]
        public static void BuildAndroidMenu()
        {
            var report = BuildAndroid();
            if (report != null && report.summary.result == BuildResult.Succeeded)
                EditorUtility.RevealInFinder(ApkPath);
        }

        /// <summary>
        /// Командная строка:
        /// Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod Swarm.EditorTools.SwarmProjectSetup.BuildAndroidBatch -logFile build.log
        /// </summary>
        public static void BuildAndroidBatch()
        {
            var report = BuildAndroid();
            bool ok = report != null && report.summary.result == BuildResult.Succeeded;
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static BuildReport BuildAndroid()
        {
            EnsureScene();
            ConfigurePlayer();
            EditorUserBuildSettings.buildAppBundle = false;
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[SWARM] Android build: {s.result}, {s.totalSize / (1024 * 1024f):F1} MB, {s.totalErrors} errors, {s.totalTime}");
            return report;
        }
    }
}
