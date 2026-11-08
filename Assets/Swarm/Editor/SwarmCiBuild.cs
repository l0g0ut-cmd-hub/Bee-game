using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Swarm.EditorTools
{
    /// <summary>
    /// Сборка для CI (GameCI): -executeMethod Swarm.EditorTools.SwarmCiBuild.BuildAndroid.
    /// GameCI передаёт -customBuildPath и -customBuildName; успех подтверждается строкой «Build succeeded!».
    /// Перед сборкой снимает кадры игры и замер симуляции (build/shots, build/probe.txt).
    /// </summary>
    public static class SwarmCiBuild
    {
        public static void BuildAndroid()
        {
            var probe = new StringBuilder();
            try
            {
                string name = GetArg("-customBuildName");
                if (string.IsNullOrEmpty(name)) name = "Swarm";
                string path = GetArg("-customBuildPath");
                if (string.IsNullOrEmpty(path)) path = "build/Android/" + name + ".apk";
                else if (!path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, name + ".apk");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                Debug.Log("SWARM: сборка APK → " + path);

                SwarmProjectSetup.EnsureScene();
                SwarmProjectSetup.ConfigurePlayer();
                // версия: из BuildInfo.txt (его пишет CI; работает и когда Unity запущена в контейнере без env)
                string ver = null, code = null;
                string infoPath = Path.Combine(Application.dataPath, "Swarm/Resources/BuildInfo.txt");
                if (File.Exists(infoPath))
                {
                    string first = File.ReadAllText(infoPath).Trim().Split(' ')[0];   // «0.2.7»
                    ver = first;
                    code = first.Substring(first.LastIndexOf('.') + 1);
                }
                if (string.IsNullOrEmpty(ver)) ver = Environment.GetEnvironmentVariable("SWARM_VERSION");
                if (!string.IsNullOrEmpty(ver)) PlayerSettings.bundleVersion = ver;
                if (int.TryParse(code, out int vc) && vc > 0) PlayerSettings.Android.bundleVersionCode = vc;
                AssetDatabase.Refresh();   // подхватить BuildInfo.txt, записанный CI перед сборкой
                probe.Append("PROBE|version|" + PlayerSettings.bundleVersion + " code " + PlayerSettings.Android.bundleVersionCode + "\n");

                string buildDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../build"));
                Directory.CreateDirectory(buildDir);
                try { SwarmShots.Run(Path.Combine(buildDir, "shots"), probe); }
                catch (Exception e) { probe.Append("PROBE|shots|fatal " + e + "\n"); }
                File.WriteAllText(Path.Combine(buildDir, "probe.txt"), probe.ToString());
                Debug.Log(probe.ToString());

                var opts = new BuildPlayerOptions
                {
                    scenes = new[] { SwarmProjectSetup.ScenePath },
                    locationPathName = path,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                };
                BuildReport report = BuildPipeline.BuildPlayer(opts);
                BuildSummary sum = report.summary;
                Debug.Log("SWARM: результат=" + sum.result + ", ошибок=" + sum.totalErrors + ", размер=" + sum.totalSize + " байт");
                if (sum.result == BuildResult.Succeeded)
                {
                    Debug.Log("Build succeeded!");
                    EditorApplication.Exit(0);
                }
                else
                {
                    Debug.LogError("SWARM: сборка не удалась: " + sum.result);
                    EditorApplication.Exit(2);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("SWARM: исключение в сборке: " + e);
                EditorApplication.Exit(1);
            }
        }

        static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
