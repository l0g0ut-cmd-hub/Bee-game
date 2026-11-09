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
            var profile = new Game.Profile();
            profile.LevelStars[0] = 3; profile.LevelStars[1] = 1;
            profile.EndlessBest = 1234f;
            profile.Achievements = (1 << (int)Game.Achievement.FirstHoney) | (1 << (int)Game.Achievement.Cook10) | (1 << (int)Game.Achievement.ThreeStars);
            { var bi = Resources.Load<TextAsset>("BuildInfo"); _gui.VersionText = bi != null ? bi.text.Trim() : "?"; }

            // 1. Главное меню: витрина с роем на фоне, версия внизу
            var attract = new GameSim(cfg, 4242u) { AttractMode = true };
            Snap(attract);
            Play(attract, 7f, s => NearestFlower(s), menu: true);
            Shot("01_menu", attract, () => _gui.DrawMenu(_t, profile, false), menu: true);
            Shot("02_levels", attract, () => _gui.DrawLevels(profile, _t), menu: true);
            Shot("03_achievements", attract, () => _gui.DrawAchievements(profile), menu: true);

            // 4. Старт партии (лето, сад): подсказка обучения, 
            var sim = new GameSim(Levels.Make(GameMode.Campaign, 1), 777u);
            _gui.TutorialStep = 0;
            _fx.Clear();
            Snap(sim);
            Play(sim, 1.2f, s => s.Swarm.LeaderPos);
            Shot("04_start", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));

            // 5. Сбор: рой 70 пчёл — «облако»
            Grow(sim, 70);
            _gui.TutorialStep = 4;
            Play(sim, 7f, s => NearestFlower(s));
            Shot("05_cloud_gather", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));
            _log.Append($"PROBE|cloud|R {sim.Swarm.Radius:F1} speed01 {sim.Swarm.Speed01:F2} gather {sim.Swarm.GatheringCount} scout {sim.Swarm.ScoutingCount}\n");

            // 6. Полёт: «комета»
            Vector2 far = new Vector2(sim.World.Width - sim.Swarm.LeaderPos.x, sim.World.Height - sim.Swarm.LeaderPos.y);
            Play(sim, 1.6f, s => far);
            Shot("06_comet", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));

            // 7. Телеграф ос
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
            Shot("07_windup", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));
            _log.Append($"PROBE|windup|seen {windup} after {waited:F2}s\n");
            Play(sim, 3f, s => s.Swarm.LeaderPos);

            // 8. Тепловой шар
            sim.Enemies.Clear();
            Grow(sim, 80);
            lp = sim.Swarm.LeaderPos;
            sim.Enemies.Spawn(EnemyType.Hornet, lp + new Vector2(40f, 50f), sim.Rng);
            sim.Enemies.DashCd[0] = 99f;
            _gui.ShowWave(WaveMessage.Hornet);
            Play(sim, 0.9f, s => s.Enemies.Count > 0 ? s.Enemies.Pos[0] : s.Swarm.LeaderPos, hold: true);
            Shot("08_heat_ball", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));
            _log.Append($"PROBE|ball|balling {sim.Swarm.Balling} inBall {sim.Swarm.BallCount} hornets {sim.Enemies.Count} " +
                        $"heatLost {sim.Stats.LostToHeat}\n");
            Play(sim, 2.5f, s => s.Enemies.Count > 0 ? s.Enemies.Pos[0] : s.Swarm.LeaderPos, hold: true);
            _log.Append($"PROBE|ball_end|hornets {sim.Enemies.Count} killed {sim.Stats.HornetsKilled} heatLost {sim.Stats.LostToHeat}\n");

            var hz = sim.Hazards;
            sim.Enemies.Clear();

            // 10. Паутина
            if (hz.WebCount > 0)
            {
                Vector2 wp = hz.WPos[0];
                Play(sim, 3f, s => wp);
                Shot("09_web", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));
            }

            // 11-12. Улей: матка, защитники, меню
            Play(sim, 6f, s => s.World.HivePos + BeeSwarm.HiveEntranceOffset);
            sim.Colony.Honey = Mathf.Max(sim.Colony.Honey, 260f);
            sim.HireDefenders(); sim.HireDefenders(); sim.HireDefenders();
            Play(sim, 1f, s => s.World.HivePos + BeeSwarm.HiveEntranceOffset);
            _log.Append($"PROBE|hive|atHive {sim.SwarmAtHive} defenders {sim.Swarm.GuardCount} honey {sim.Colony.Honey:F0}\n");
            Shot("11_hive_menu", sim, () => _gui.DrawHive(sim, _t));
            Play(sim, 3f, s => NearestFlower(s));
            Shot("12_defenders", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));

            // 12b. Пост: отряд оставлен у цветов, рой улетел
            Vector2 postAt = NearestFlower(sim);
            Play(sim, 4f, s => postAt);
            sim.PostSquad();
            Vector2 away = postAt + new Vector2(70f, 40f);
            Play(sim, 3f, s => away);
            _log.Append($"PROBE|post|active {sim.Swarm.SquadActive} squad {sim.Swarm.SquadCount}\n");
            Shot("12b_post", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));
            sim.RecallSquad();

            // 13. Выбор перка
            sim.PerkOffer[0] = (PerkId)0; sim.PerkOffer[1] = (PerkId)3; sim.PerkOffer[2] = (PerkId)6;
            Shot("13_perk", sim, () => _gui.DrawPerk(sim, _t));

            // 14. Медведь у улья
            sim.Enemies.Spawn(EnemyType.Bear, sim.World.HivePos + new Vector2(60f, -40f), sim.Rng);
            _gui.ShowWave(WaveMessage.Bear);
            Play(sim, 3f, s => s.World.HivePos + new Vector2(30f, -30f));
            Shot("14_bear", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));
            sim.Enemies.Clear();

            // 15. Дождь
            sim.Sky.Current = Weather.Rain;
            sim.Sky.WeatherBlend = 1f;
            sim.Sky.WeatherTimer = 99f;
            Grow(sim, 170);
            Play(sim, 3f, s => NearestFlower(s));
            Shot("15_rain_big_swarm", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));
            sim.Sky.Current = Weather.Clear;
            sim.Sky.WeatherBlend = 0f;

            // 16. Ночь
            sim.Time = sim.Sky.DayLength * 2f - sim.Sky.NightLength * 0.5f;
            Play(sim, 1.5f, s => NearestFlower(s));
            Shot("16_night", sim, () => _gui.DrawHud(sim, _screen, _t, Dt));

            // 17. Пауза
            Shot("17_pause", sim, () => _gui.DrawPause(false, true, true, false));

            // 18. Итоги с новым достижением
            sim.Result = SimResult.Wintered;
            sim.Stars = 2;
            var fresh = new System.Collections.Generic.List<Game.Achievement> { Game.Achievement.BearRepeller };
            Shot("18_result", sim, () => _gui.DrawResult(sim, _t, false, 0f, fresh));

            // 20-22. Автопилот: виртуальный палец сам играет через настоящий UI
            RunAutopilot();

            // 19. Выживание: HUD с кругами
            var end = new GameSim(Levels.Make(GameMode.Endless, 0), 31u);
            _fx.Clear();
            Snap(end);
            Grow(end, 60);
            Play(end, 4f, s => NearestFlower(s));
            end.Time = 700f;
            Play(end, 0.2f, s => NearestFlower(s));
            Shot("19_endless", end, () => _gui.DrawHud(end, _screen, _t, Dt));
        }

        static void RunAutopilot()
        {
            var sim = new GameSim(Levels.Make(GameMode.Campaign, 1), 2024u);
            var auto = new Game.AutoPilot();
            auto.Reset(_screen);
            _fx.Clear();
            _gui.ClearBanner();
            _gui.TutorialStep = 4;
            Snap(sim);
            int state = 0;            // 0 поле, 1 улей, 2 дар
            int hiveVisits = 0, posts = 0, perks = 0, presses = 0;
            bool shotHive = false, shotPerk = false, shotPost = false;
            int steps = Mathf.RoundToInt(240f / Dt);
            for (int i = 0; i < steps && sim.Result == SimResult.Running; i++)
            {
                var ctx = state == 0 ? Game.AutoContext.Playing : state == 1 ? Game.AutoContext.Hive : Game.AutoContext.Perk;
                auto.Tick(sim, ctx, _ui, _screen, Dt);
                _ui.Begin(_screen, auto.Cursor, auto.Down, auto.Held, auto.Up);
                UiAction act = state == 0 ? _gui.DrawHud(sim, _screen, _t, Dt) : state == 1 ? _gui.DrawHive(sim, _t) : _gui.DrawPerk(sim, _t);
                bool wasSquad = sim.Swarm.SquadActive;
                switch (act)
                {
                    case UiAction.OpenHive: state = 1; hiveVisits++; break;
                    case UiAction.CloseHive: state = 0; break;
                    case UiAction.ChoosePerk: sim.ChoosePerk(_gui.PerkChoice); state = 0; perks++; break;
                    case UiAction.PostSquad: if (wasSquad) sim.RecallSquad(); else { sim.PostSquad(); posts++; } break;
                }
                if (auto.Up) presses++;
                if (state == 0)
                {
                    var inp = new SimInput { Active = auto.Steering && !_ui.Captured, Target = _screen.PixelToWorld(auto.Cursor), Hold = auto.WantHold && auto.HoldStill >= Game.AutoPilot.HoldTime };
                    sim.Step(Dt, inp);
                    if (sim.PerkPending) state = 2;
                }
                if (state == 2) auto.DrawCursor(_ui, _t);
                _ui.End();
                _t += Dt;
                _fx.Consume(sim, _world, null);
                _fx.Update(sim, Dt);
                sim.Events.Clear();
                Follow(sim, false);
                _world.Draw(sim, _screen, _t, Dt);
                if (state == 1 && !shotHive && hiveVisits >= 2) { shotHive = true; Shot("20_auto_hive", sim, () => { _gui.DrawHive(sim, _t); auto.DrawCursor(_ui, _t); }); }
                if (state == 2 && !shotPerk) { shotPerk = true; Shot("21_auto_perk", sim, () => { _gui.DrawPerk(sim, _t); auto.DrawCursor(_ui, _t); }); }
                if (state == 0 && sim.Swarm.SquadActive && !shotPost && i % 60 == 0) { shotPost = true; Shot("22_auto_post", sim, () => { _gui.DrawHud(sim, _screen, _t, Dt); auto.DrawCursor(_ui, _t); }); }
            }
            Shot("23_auto_field", sim, () => { _gui.DrawHud(sim, _screen, _t, Dt); auto.DrawCursor(_ui, _t); });
            var col = sim.Colony;
            _log.Append($"PROBE|auto|t {sim.Time:F0} result {sim.Result} bees {sim.Swarm.SwarmCount}/{col.MaxBees} honey {col.Honey:F0} delivered {sim.Stats.HoneyDelivered:F0} " +
                        $"hive {hiveVisits} presses {presses} posts {posts} perks {perks} brood {sim.BroodIndex} def {sim.Swarm.GuardCount} " +
                        $"lv spd{col.Level[0]} car{col.Level[1]} sti{col.Level[2]} comb{col.Level[3]} cooked {sim.Stats.HornetsCooked}\n");
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
