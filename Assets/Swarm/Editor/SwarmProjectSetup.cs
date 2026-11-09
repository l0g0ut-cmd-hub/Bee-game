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

        public static void EnsureScene()
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

        public static void ConfigurePlayer()
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
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.Android.useCustomKeystore = false;     // debug-ключ: APK ставится на телефон как есть
            EditorUserBuildSettings.androidBuildType = AndroidBuildType.Release;
            ForceLegacyInputManager();
            ApplyIcon();
        }

        /// <summary>Значок приложения — пухлая пчёлка (tools/art/gen_icon.py → Assets/Swarm/Editor/AppIcon.png).</summary>
        static void ApplyIcon()
        {
            const string path = "Assets/Swarm/Editor/AppIcon.png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (icon == null) { Debug.LogWarning("[SWARM] значок не найден: " + path); return; }
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);   // значок по умолчанию
            int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Android, IconKind.Application);
            var arr = new Texture2D[sizes.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = icon;
            PlayerSettings.SetIcons(NamedBuildTarget.Android, arr, IconKind.Application);
            Debug.Log("[SWARM] значок приложения установлен (" + sizes.Length + " размеров)");
        }

        /// <summary>
        /// Игра читает касания через классический Input. Если проект создан с «новым» вводом,
        /// Input бросает исключение — переключаем Active Input Handling на Input Manager (Old).
        /// </summary>
        static void ForceLegacyInputManager()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) { Debug.LogWarning("[SWARM] ProjectSettings.asset не найден"); return; }
            var so = new SerializedObject(assets[0]);
            SerializedProperty p = so.FindProperty("activeInputHandler");
            if (p == null) { Debug.LogWarning("[SWARM] activeInputHandler не найден"); return; }
            if (p.intValue != 0)
            {
                p.intValue = 0; // 0 = Input Manager (Old), 1 = Input System, 2 = Both
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log("[SWARM] Active Input Handling → Input Manager (Old)");
            }
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
