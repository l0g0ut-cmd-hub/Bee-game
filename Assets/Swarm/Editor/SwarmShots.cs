using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Swarm.Presentation;
using Swarm.Simulation;
using Swarm.UI;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Swarm.EditorTools
{
    /// <summary>
    /// Съёмка настоящих кадров игры в CI (без телефона): те же классы симуляции, рендера, FX и UI,
    /// что и в билде, прогоняются в редакторе и рендерятся в PNG (build/shots).
    /// Плюс замер скорости симуляции в рантайме Unity (build/probe.txt).
    /// </summary>
    public static class SwarmShots
    {
        const int ScreenW = 1080, ScreenH = 2340;   // типичный современный Android-портрет
        const float Dt = 1f / 60f;

        static PixelScreen _screen;
        static WorldRenderer _world;
        static FxSystem _fx;
        static PixelUI _ui;
        static GameUI _gui;
        static TouchInput _input;
        static Vector2 _cam;
        static float _t;
        static string _dir;
        static StringBuilder _log;

        public static void Run(string dir, StringBuilder log)
        {
            _dir = dir;
            _log = log;
            log.Append("PROBE|gfx|" + SystemInfo.graphicsDeviceType + "|" + SystemInfo.graphicsDeviceName + "\n");
            Benchmark(log);
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                log.Append("PROBE|shots|нет графического устройства — кадров не будет\n");
                return;
            }
            Directory.CreateDirectory(dir);
            var root = new GameObject("SWARM_SHOTS");
            try
            {
                SpriteAtlas.Init();
                _screen = new PixelScreen(root.transform, ScreenW, ScreenH);
                _world = new WorldRenderer(root.transform);
                _fx = new FxSystem(root.transform);
                _ui = new PixelUI(root.transform);
                _gui = new GameUI(_ui);
                _input = new TouchInput();
                log.Append($"PROBE|shots|rt {_screen.Width}x{_screen.Height} scale {_screen.Scale}\n");
                Scenarios();
            }
            catch (Exception e)
            {
                log.Append("PROBE|shots|error " + e + "\n");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void Scenarios()
        {
            var cfg = SimConfig.Default();

            // 1. Главное меню: витрина с роем на фоне
            var attract = new GameSim(cfg, 4242u) { AttractMode = true };
            Snap(attract);
            Play(attract, 7f, s => NearestFlower(s), menu: true);
            Shot("01_menu", attract, () => _gui.DrawMenu(_t, 0f, false), menu: true);

            // 2. Старт партии: подсказка обучения
            var sim = new GameSim(cfg, 777u);
            _gui.TutorialStep = 0;
            _fx.Clear();
            Snap(sim);
            Play(sim, 1.2f, s => s.Swarm.LeaderPos);
            Shot("02_start", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));

            // 3. Сбор: рой 70 пчёл остановился на лугу — «облако»
            Grow(sim, 70);
            _gui.TutorialStep = 4;
            Play(sim, 7f, s => NearestFlower(s));
            Shot("03_cloud_gather", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));
            _log.Append($"PROBE|cloud|R {sim.Swarm.Radius:F1} speed01 {sim.Swarm.Speed01:F2} gather {sim.Swarm.GatheringCount} scout {sim.Swarm.ScoutingCount}\n");

            // 4. Полёт: «комета» со шлейфом
            Vector2 far = new Vector2(sim.World.Width - sim.Swarm.LeaderPos.x, sim.World.Height - sim.Swarm.LeaderPos.y);
            Play(sim, 1.6f, s => far);
            Shot("04_comet", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));
            _log.Append($"PROBE|comet|speed01 {sim.Swarm.Speed01:F2} heading {sim.Swarm.Heading}\n");

            // 5. Телеграф: осы замирают и дрожат перед рывком
            Vector2 lp = sim.Swarm.LeaderPos;
            sim.Enemies.Spawn(EnemyType.Wasp, lp + new Vector2(55f, 30f), sim.Rng);
            sim.Enemies.Spawn(EnemyType.Wasp, lp + new Vector2(-50f, 35f), sim.Rng);
            for (int e = 0; e < sim.Enemies.Count; e++) sim.Enemies.DashCd[e] = 0f;
            _gui.ShowWave(WaveMessage.Wasps);
            float waited = 0f;
            bool windup = false;
            while (waited < 3f && !windup)
            {
                Play(sim, 0.05f, s => s.Swarm.LeaderPos);
                waited += 0.05f;
                for (int e = 0; e < sim.Enemies.Count; e++) windup |= sim.Enemies.Act[e] == EnemyAct.Windup;
            }
            Play(sim, 0.15f, s => s.Swarm.LeaderPos);
            Shot("05_windup", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));
            _log.Append($"PROBE|windup|seen {windup} after {waited:F2}s\n");
            Play(sim, 3f, s => s.Swarm.LeaderPos);

            // 6. Тепловой шар: палец удерживается на шершне
            sim.Enemies.Clear();
            Grow(sim, 80);
            lp = sim.Swarm.LeaderPos;
            sim.Enemies.Spawn(EnemyType.Hornet, lp + new Vector2(40f, 50f), sim.Rng);
            sim.Enemies.DashCd[0] = 99f;
            _gui.ShowWave(WaveMessage.Hornet);
            Play(sim, 0.9f, s => s.Enemies.Count > 0 ? s.Enemies.Pos[0] : s.Swarm.LeaderPos, hold: true);
            Shot("06_heat_ball", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));
            _log.Append($"PROBE|ball|balling {sim.Swarm.Balling} inBall {sim.Swarm.BallCount} hornets {sim.Enemies.Count} " +
                        $"heatLost {sim.Stats.LostToHeat} hornetHp {(sim.Enemies.Count > 0 ? sim.Enemies.Hp[0] : 0f):F1}\n");
            Play(sim, 2.5f, s => s.Enemies.Count > 0 ? s.Enemies.Pos[0] : s.Swarm.LeaderPos, hold: true);
            _log.Append($"PROBE|ball_end|hornets {sim.Enemies.Count} killed {sim.Stats.HornetsKilled} heatLost {sim.Stats.LostToHeat}\n");

            // 7-8. Улей: стражи и меню
            sim.Enemies.Clear();
            sim.CycleGuards(); sim.CycleGuards();   // 10 стражей
            Play(sim, 6f, s => s.World.HivePos + BeeSwarm.HiveEntranceOffset);
            sim.Colony.Honey = Mathf.Max(sim.Colony.Honey, 180f);
            Play(sim, 1f, s => s.World.HivePos + BeeSwarm.HiveEntranceOffset);
            _log.Append($"PROBE|hive|atHive {sim.SwarmAtHive} guards {sim.Swarm.GuardCount}/{sim.Swarm.GuardWanted}\n");
            Shot("07_hive_menu", sim, () => _gui.DrawHive(sim, _t));
            Play(sim, 4f, s => NearestFlower(s));
            Shot("08_guards_stay", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));

            // 9. Большой рой над лугом
            Grow(sim, 170);
            Play(sim, 6f, s => NearestFlower(s));
            Shot("09_big_swarm", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));

            // 10. Пауза с настройками
            Shot("10_pause", sim, () => _gui.DrawPause(false, true, true));

            // 11. Итоги: зимовка на две звезды
            sim.Result = SimResult.Wintered;
            sim.Stars = 2;
            Shot("11_result", sim, () => _gui.DrawResult(sim, _t, false, 0f));
        }

        // ------------------------------------------------------------------ прогон
        static void Snap(GameSim sim) => _cam = sim.Swarm.LeaderPos;

        static void Grow(GameSim sim, int n)
        {
            while (sim.Swarm.Count < n && sim.Swarm.Spawn(sim.Swarm.LeaderPos, sim.Rng)) { }
        }

        static Vector2 NearestFlower(GameSim s)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int f = 0; f < s.Flowers.Count; f++)
            {
                if (!s.Flowers.IsBlooming(f)) continue;
                float d = (s.Flowers.Pos[f] - s.World.HivePos).sqrMagnitude;
                if (d < bd && d > 80f * 80f) { bd = d; best = f; }
            }
            return best >= 0 ? s.Flowers.Pos[best] : s.World.HivePos;
        }

        static void Play(GameSim sim, float seconds, Func<GameSim, Vector2> target, bool menu = false, bool hold = false)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(seconds / Dt));
            for (int i = 0; i < steps; i++)
            {
                var inp = new SimInput { Active = true, Target = target(sim), Hold = hold };
                sim.Step(Dt, inp);
                _t += Dt;
                _fx.Consume(sim, _world, null);
                _fx.Update(sim, Dt);
                Follow(sim, menu);
                _world.Draw(sim, _screen, _t, Dt); // держит анимацию крон/частиц в актуальном состоянии
            }
        }

        static void Follow(GameSim sim, bool menu)
        {
            Vector2 target = sim.Swarm.LeaderPos + sim.Swarm.LeaderVel * 0.45f;
            if (menu) target.y += _screen.Height * 0.18f;
            _cam += (target - _cam) * (1f - Mathf.Exp(-Dt * 4f));
            float hw = _screen.Width * 0.5f, hh = _screen.Height * 0.5f;
            _cam.x = Mathf.Clamp(_cam.x, hw - 8f, sim.World.Width - hw + 8f);
            _cam.y = Mathf.Clamp(_cam.y, hh - 24f, sim.World.Height - hh + 24f);
            _screen.PlaceWorldCamera(_cam);
        }

        // ------------------------------------------------------------------ кадр
        static void Shot(string name, GameSim sim, Action drawUi, bool menu = false)
        {
            Follow(sim, menu);
            _gui.SnapFades();
            _ui.Begin(_screen, _input);
            drawUi();
            _ui.End();
            _world.Draw(sim, _screen, _t, Dt);
            _fx.Draw(_screen.WorldViewRect(16f));
            _screen.WorldCam.Render();
            _screen.UiCam.Render();

            var rt = _screen.Target;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            // увеличение x3 «ближайшим соседом», чтобы кадр было удобно смотреть
            const int k = 3;
            Color32[] src = tex.GetPixels32();
            var big = new Texture2D(rt.width * k, rt.height * k, TextureFormat.RGBA32, false);
            var dst = new Color32[src.Length * k * k];
            int bw = rt.width * k;
            for (int y = 0; y < rt.height; y++)
                for (int x = 0; x < rt.width; x++)
                {
                    Color32 c = src[y * rt.width + x];
                    c.a = 255;
                    for (int dy = 0; dy < k; dy++)
                    {
                        int row = (y * k + dy) * bw + x * k;
                        for (int dx = 0; dx < k; dx++) dst[row + dx] = c;
                    }
                }
            big.SetPixels32(dst);
            big.Apply();
            File.WriteAllBytes(Path.Combine(_dir, name + ".png"), big.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(big);
            _log.Append($"PROBE|shot|{name}|bees {sim.Swarm.Count} enemies {sim.Enemies.Count} honey {sim.Colony.Honey:F0} carry {sim.Swarm.TotalCarry:F1}\n");
        }

        // ------------------------------------------------------------------ замер
        static void Benchmark(StringBuilder log)
        {
            var cfg = SimConfig.Default();
            var sim = new GameSim(cfg, 99u);
            while (sim.Swarm.Count < 170 && sim.Swarm.Spawn(sim.Swarm.LeaderPos, sim.Rng)) { }
            for (int e = 0; e < 8; e++)
                sim.Enemies.Spawn(e % 4 == 0 ? EnemyType.Hornet : EnemyType.Wasp, sim.World.HivePos + new Vector2(150f + e * 10f, 0f), sim.Rng);
            var sw = Stopwatch.StartNew();
            int steps = 1800;
            for (int i = 0; i < steps; i++)
            {
                Vector2 tgt = sim.World.HivePos + new Vector2(Mathf.Cos(i * 0.01f), Mathf.Sin(i * 0.01f)) * 120f;
                sim.Step(Dt, new SimInput { Active = true, Target = tgt });
                sim.Events.Clear();
            }
            sw.Stop();
            log.Append($"PROBE|bench|{steps} шагов, старт 170 пчёл + 8 врагов: {sw.Elapsed.TotalMilliseconds / steps:F3} мс/шаг (редактор, Mono), " +
                       $"в конце пчёл {sim.Swarm.Count}, врагов {sim.Enemies.Count}\n");
        }
    }
}
