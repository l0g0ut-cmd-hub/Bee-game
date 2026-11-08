using System.Collections.Generic;
using Swarm.Presentation;
using Swarm.Simulation;
using UnityEngine;

namespace Swarm.UI
{
    public enum UiAction { None, Play, Pause, Resume, Restart, ToMenu, OpenHive, CloseHive, ToggleSound, ToggleHaptics, ToggleShake }

    /// <summary>Экраны: меню, HUD, улей (улучшения), пауза, итоги. Только отрисовка и клики — логика в GameRoot.</summary>
    public sealed class GameUI
    {
        readonly PixelUI _ui;
        readonly TextBuf _b = new TextBuf();
        readonly Dictionary<string, string[]> _wrapCache = new Dictionary<string, string[]>();

        // баннер волны
        float _bannerTime;
        string _bannerTitle, _bannerSub;
        Color32 _bannerColor;

        public int TutorialStep;
        float _hintAlpha;

        public GameUI(PixelUI ui) { _ui = ui; }

        /// <summary>Светлый кремовый для вторичного текста на коричневых панелях.</summary>
        static readonly Color32 SubText = new Color32(255, 230, 180, 255);

        public void ShowWave(WaveMessage m)
        {
            _bannerTime = 3f;
            switch (m)
            {
                case WaveMessage.Scouts: _bannerTitle = Strings.WaveScouts; _bannerSub = Strings.WaveSubRaid; _bannerColor = PixelUI.Honey; break;
                case WaveMessage.Wasps: _bannerTitle = Strings.WaveWasps; _bannerSub = Strings.WaveSubRaid; _bannerColor = PixelUI.Honey; break;
                case WaveMessage.Hornet: _bannerTitle = Strings.WaveHornet; _bannerSub = Strings.WaveSubHornet; _bannerColor = PixelUI.Red; break;
                default: _bannerTitle = Strings.WaveInvasion; _bannerSub = null; _bannerColor = PixelUI.Red; break;
            }
        }

        public void ClearBanner() { _bannerTime = 0f; _lateSummerShown = false; _ballHintTime = 0f; }

        public void ShowBanner(string title, string sub, Color32 color)
        {
            _bannerTime = 3f;
            _bannerTitle = title;
            _bannerSub = sub;
            _bannerColor = color;
        }

        bool _lateSummerShown;
        float _ballHintTime;

        /// <summary>Для съёмки кадров в CI: плавные проявления сразу в конечном состоянии.</summary>
        public void SnapFades() { _hintAlpha = 1f; }

        string[] Wrap(string s, int maxW)
        {
            if (_wrapCache.TryGetValue(s, out var lines)) return lines;
            var words = s.Split(' ');
            var list = new List<string>();
            string cur = "";
            foreach (var w in words)
            {
                string t = cur.Length == 0 ? w : cur + " " + w;
                if (PixelFont.Measure(t) > maxW && cur.Length > 0) { list.Add(cur); cur = w; }
                else cur = t;
            }
            if (cur.Length > 0) list.Add(cur);
            lines = list.ToArray();
            _wrapCache[s] = lines;
            return lines;
        }

        void TextWrapped(string s, float cx, float topY, int maxW, Color32 c)
        {
            var lines = Wrap(s, maxW);
            for (int i = 0; i < lines.Length; i++)
                _ui.Text(lines[i], cx, topY - PixelFont.LineHeight - i * 10, c, 1, 1);
        }

        // ================================================================ МЕНЮ
        public UiAction DrawMenu(float time, float bestWin, bool muted)
        {
            var a = UiAction.None;
            int W = _ui.W, H = _ui.H;
            // затемнение под логотипом для читаемости
            float top = H - _ui.SafeTop;
            for (int i = 0; i < 10; i++)
                _ui.Fill(new Rect(0, top - 130 + i * 13, W, 13), new Color32(23, 18, 28, (byte)(20 + i * 14)));

            float bob = Mathf.Round(Mathf.Sin(time * 1.6f) * 2f);
            _ui.Icon(Spr.Logo, W * 0.5f, top - 52 + bob);
            _ui.Text(Strings.Subtitle, W * 0.5f, top - 92, PixelUI.Honey, 1, 1);

            float ty = H * 0.42f;
            _ui.Fill(new Rect(8, ty - 16, W - 16, 44), new Color32(23, 18, 28, 150));
            _ui.Text(Strings.Tagline1, W * 0.5f, ty + 16, PixelUI.White, 1, 1);
            _ui.Text(Strings.Tagline2, W * 0.5f, ty + 4, PixelUI.Muted, 1, 1);
            _ui.Text(Strings.Tagline3, W * 0.5f, ty - 8, PixelUI.Muted, 1, 1);

            float pulse = Mathf.Sin(time * 4f) > 0.7f ? 1f : 0f;
            var playRect = new Rect(Mathf.Round(W * 0.5f - 52), Mathf.Round(H * 0.27f) + pulse, 104, 30);
            if (_ui.Button(1, playRect, Strings.Play, true, 2)) a = UiAction.Play;

            float by = H * 0.27f - 34;
            if (bestWin > 0f)
            {
                _b.Clear().Append(Strings.Record).AppendTime(bestWin);
                _ui.Text(_b, W * 0.5f, by + 14, PixelUI.Honey, 1, 1);
            }
            var sRect = new Rect(Mathf.Round(W * 0.5f - 40), by - 14, 80, 18);
            if (_ui.Button(2, sRect, muted ? Strings.SoundOff : Strings.SoundOn)) a = UiAction.ToggleSound;

            _ui.Text(Strings.Version, W * 0.5f, 4 + _ui.SafeBottom, new Color32(196, 186, 168, 160), 1, 1);
            return a;
        }

        // ================================================================ HUD
        public UiAction DrawHud(GameSim sim, PixelScreen screen, float time, float dt)
        {
            var a = UiAction.None;
            int W = _ui.W, H = _ui.H;
            var cfg = sim.Cfg;
            var sw = sim.Swarm;
            var col = sim.Colony;
            float top = H - _ui.SafeTop;

            // --- индикаторы за краем экрана (под панелями)
            DrawIndicators(sim, screen, time, top - 46);

            // --- верхняя панель
            var bar = new Rect(-3, top - 21, W + 6, 24);
            _ui.Blocker(bar);
            _ui.Panel(bar, true);
            float ty = top - 14;
            _ui.Icon(Spr.IconBee, 10, ty + 3);
            _b.Clear().Append(sw.Count).Append('/').Append(col.MaxBees);
            _ui.Text(_b, 17, ty, sw.Count >= col.MaxBees ? PixelUI.Honey : PixelUI.White);

            float hx = Mathf.Round(W * 0.38f);
            _ui.Icon(Spr.IconHoney, hx, ty + 3);
            _b.Clear().Append(Mathf.FloorToInt(col.Honey));
            _ui.Text(_b, hx + 7, ty, PixelUI.Honey);

            float tx = Mathf.Round(W * 0.62f);
            float left = sim.TimeLeft;
            bool hurry = left < 60f && Mathf.Repeat(time, 1f) < 0.5f;
            _ui.Icon(Spr.IconSun, tx, ty + 3);
            _b.Clear().AppendTime(left);
            _ui.Text(_b, tx + 7, ty, hurry ? PixelUI.Red : PixelUI.White);

            if (_ui.Button(10, new Rect(W - 21, top - 19, 18, 18), null, true, 1, Spr.IconPause)) a = UiAction.Pause;

            // --- позднее лето: один раз баннер «копи мёд»
            if (!_lateSummerShown && sim.TimeLeft < 120f && sim.TimeLeft > 0f)
            {
                _lateSummerShown = true;
                ShowBanner(Strings.LateSummer, Strings.LateSummerSub, PixelUI.Honey);
            }
            // --- цель
            float gy = top - 33;
            _ui.Fill(new Rect(4, gy - 3, W - 8, 13), PixelUI.InkSoft);
            float half = (W - 8) * 0.5f;
            DrawGoal(4 + 2, gy, half - 6, Spr.IconBee, sw.Count, cfg.GoalBees);
            bool honeyBlink = sim.TimeLeft < 120f && !sim.GoalHoneyMet && Mathf.Repeat(time * 2f, 1f) < 0.3f;
            if (!honeyBlink) DrawGoal(4 + half + 2, gy, half - 6, Spr.IconHoney, Mathf.FloorToInt(col.Honey), cfg.GoalHoney);

            // --- баннер волны
            if (_bannerTime > 0f)
            {
                _bannerTime -= dt;
                bool blink = _bannerTime > 2.2f && Mathf.Repeat(time * 6f, 1f) < 0.35f;
                float by = Mathf.Round(H * 0.66f);
                float k = Mathf.Clamp01(_bannerTime * 2f);
                _ui.Fill(new Rect(0, by - 14, W, 34), new Color32(23, 18, 28, (byte)(150 * k)));
                if (!blink)
                {
                    var c = _bannerColor; c.a = (byte)(255 * k);
                    _ui.Text(_bannerTitle, W * 0.5f, by + 2, c, 2, 1);
                }
                if (_bannerSub != null)
                    _ui.Text(_bannerSub, W * 0.5f, by - 10, new Color32(255, 250, 240, (byte)(220 * k)), 1, 1);
            }

            // --- нектар в рое
            float cap = cfg.CarryBase * col.CarryMul * Mathf.Max(1, sw.SwarmCount);
            float frac = sw.FieldCarry / cap;
            float bottom = _ui.SafeBottom + 6;
            var cr = new Rect(6, bottom, 74, 18);
            _ui.Fill(cr, PixelUI.InkSoft);
            _ui.Icon(Spr.IconNectar, cr.x + 7, cr.y + 9);
            _ui.Bar(new Rect(cr.x + 14, cr.y + 7, 54, 4), frac, PixelUI.Honey, new Color32(70, 50, 40, 255));
            if (frac > 0.85f && !sim.SwarmAtHive && Mathf.Repeat(time * 2f, 1f) < 0.6f)
                _ui.Text(Strings.CarryFull, cr.x, cr.yMax + 4, PixelUI.Honey);

            // --- тепловой шар: статус
            if (sw.Balling && Mathf.Repeat(time * 5f, 1f) < 0.75f)
                _ui.Text(Strings.Balling, W * 0.5f, bottom + 34, new Color32(255, 150, 60, 255), 1, 1);

            // --- кнопка улья
            if (sim.SwarmAtHive)
            {
                float p = Mathf.Sin(time * 5f) > 0f ? 1f : 0f;
                var hr = new Rect(W - 76, bottom - 4 + p, 70, 26);
                if (_ui.Button(20, hr, Strings.Hive, true, 1, Spr.IconComb)) a = UiAction.OpenHive;
            }

            // --- подсказки обучения
            string hint = TutorialHint(sim);
            _hintAlpha = Mathf.MoveTowards(_hintAlpha, hint != null ? 1f : 0f, dt * 3f);
            if (hint != null && _hintAlpha > 0.01f)
            {
                var lines = Wrap(hint, W - 24);
                float hy = bottom + 40 + lines.Length * 10;
                _ui.Fill(new Rect(8, hy - lines.Length * 10 - 4, W - 16, lines.Length * 10 + 6), new Color32(23, 18, 28, (byte)(160 * _hintAlpha)));
                TextWrapped(hint, W * 0.5f, hy, W - 24, new Color32(255, 250, 240, (byte)(255 * _hintAlpha)));
            }
            return a;
        }

        void DrawGoal(float x, float y, float w, Spr icon, int value, int goal)
        {
            bool met = value >= goal;
            _ui.Icon(met ? Spr.IconCheck : icon, x + 4, y + 3);
            _ui.Bar(new Rect(x + 11, y + 1, w - 54, 4), value / (float)goal, met ? PixelUI.Green : PixelUI.Honey, new Color32(70, 50, 40, 255));
            _b.Clear().Append(Mathf.Min(value, goal)).Append('/').Append(goal);
            _ui.Text(_b, x + w + 2, y, met ? PixelUI.Green : PixelUI.White, 1, 2, false);
        }

        string TutorialHint(GameSim sim)
        {
            // контекстная подсказка: шершень рядом, а шар не включён (первые ~15 с за партию)
            if (_ballHintTime < 15f && !sim.Swarm.Balling && sim.Swarm.SwarmCount >= sim.Cfg.BallMinBees)
            {
                var en = sim.Enemies;
                for (int i = 0; i < en.Count; i++)
                    if (en.Type[i] == EnemyType.Hornet && (en.Pos[i] - sim.Swarm.LeaderPos).sqrMagnitude < 150f * 150f)
                    {
                        _ballHintTime += 1f / 60f;
                        return Strings.HintBall;
                    }
            }
            switch (TutorialStep)
            {
                case 0:
                    if ((sim.Swarm.LeaderPos - (sim.World.HivePos + new Vector2(0f, -30f))).magnitude > 40f) TutorialStep = 1;
                    return Strings.HintMove;
                case 1:
                    if (sim.Swarm.TotalCarry > 3f) TutorialStep = 2;
                    return Strings.HintFlowers;
                case 2:
                    if (sim.Stats.HoneyDelivered > 4f) TutorialStep = 3;
                    return Strings.HintDeliver;
                case 3:
                    if (sim.Stats.BeesHatched > 0 || sim.Time > 120f) TutorialStep = 4;
                    return sim.SwarmAtHive ? Strings.HintHatch : null;
                default:
                    return null;
            }
        }

        void DrawIndicators(GameSim sim, PixelScreen screen, float time, float maxY)
        {
            int W = _ui.W;
            float minY = _ui.SafeBottom + 30f;
            // улей
            Vector2 hive = sim.World.HivePos + new Vector2(0f, 20f);
            EdgeArrow(screen, hive, W, minY, maxY, PixelUI.Honey, Spr.IconComb, time, 0f);
            // враги
            var en = sim.Enemies;
            for (int i = 0; i < en.Count; i++)
                EdgeArrow(screen, en.Pos[i], W, minY, maxY, PixelUI.Red,
                    en.Type[i] == EnemyType.Hornet ? Spr.IconSkull : Spr.IconWasp, time, i * 0.7f);
        }

        void EdgeArrow(PixelScreen screen, Vector2 world, int W, float minY, float maxY, Color32 c, Spr icon, float time, float ph)
        {
            Vector2 p = screen.WorldToPixel(world);
            const float m = 9f;
            if (p.x > m && p.x < W - m && p.y > minY && p.y < maxY) return;
            Vector2 center = new Vector2(W * 0.5f, (minY + maxY) * 0.5f);
            Vector2 d = p - center;
            float ang = Mathf.Atan2(d.y, d.x);
            // пересечение луча с прямоугольником видимости
            float hx = W * 0.5f - m, hy = (maxY - minY) * 0.5f - m;
            float sx = Mathf.Abs(d.x) > 0.001f ? hx / Mathf.Abs(d.x) : float.MaxValue;
            float sy = Mathf.Abs(d.y) > 0.001f ? hy / Mathf.Abs(d.y) : float.MaxValue;
            float s = Mathf.Min(sx, sy);
            Vector2 e = center + d * s;
            float pulse = Mathf.Sin(time * 6f + ph) * 1.5f;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 ap = e + dir * pulse;
            _ui.Icon(Spr.Dot5, ap.x - dir.x * 6f, ap.y - dir.y * 6f, new Color32(23, 18, 28, 200), 2);
            _ui.Icon(icon, Mathf.Round(ap.x - dir.x * 6f), Mathf.Round(ap.y - dir.y * 6f));
            _ui.Rotated(Spr.IconArrow, ap.x + dir.x * 2f, ap.y + dir.y * 2f, ang, c);
        }

        // ================================================================ УЛЕЙ
        public UiAction DrawHive(GameSim sim, float time)
        {
            var a = UiAction.None;
            int W = _ui.W, H = _ui.H;
            var col = sim.Colony;
            var cfg = sim.Cfg;
            _ui.Fill(new Rect(0, 0, W, H), new Color32(23, 18, 28, 140));

            float pw = Mathf.Min(W - 10, 220), ph = 298;
            var panel = new Rect(Mathf.Round((W - pw) * 0.5f), Mathf.Round((H - ph) * 0.5f), pw, ph);
            _ui.Blocker(new Rect(0, 0, W, H));
            _ui.Panel(panel);
            float x0 = panel.x + 8, x1 = panel.xMax - 8;
            float y = panel.yMax - 22;
            _ui.Text(Strings.HiveTitle, panel.center.x, y, PixelUI.Ink, 2, 1, false);
            y -= 14;
            _b.Clear().Append(Mathf.FloorToInt(col.Honey));
            int tw = PixelFont.Measure(_b);
            _ui.Icon(Spr.IconHoney, panel.center.x - tw * 0.5f - 6, y + 3);
            _ui.Text(_b, panel.center.x - tw * 0.5f + 1, y, PixelUI.Honey, 1, 0, true);

            y -= 10;
            float rowH = 36f;
            // 1. вывод пчёл
            {
                y -= rowH;
                RowFrame(x0, y, x1 - x0, rowH - 3);
                _ui.Icon(Spr.IconBee, x0 + 12, y + rowH * 0.5f, null, 2);
                _ui.Text(Strings.HatchName, x0 + 24, y + 19, PixelUI.Ink, 1, 0, false);
                bool full = sim.HatchAmount <= 0;
                if (full) _ui.Text(Strings.HatchFull, x0 + 24, y + 8, PixelUI.Red, 1, 0, false);
                else
                {
                    _b.Clear().Append('+').Append(sim.HatchAmount).Append("  ").Append(sim.Swarm.Count).Append('/').Append(col.MaxBees);
                    _ui.Text(_b, x0 + 24, y + 8, SubText, 1, 0, false);
                }
                _b.Clear().Append(sim.HatchCost);
                if (_ui.ButtonBuf(31, new Rect(x1 - 50, y + 6, 46, 20), _b, sim.CanHatch, Spr.IconHoney)) sim.TryHatch();
            }
            // 2-5. улучшения
            y = UpgradeRow(sim, UpgradeType.Comb, 32, Spr.IconComb, Strings.CombName, x0, x1, y, rowH, time);
            y = UpgradeRow(sim, UpgradeType.Carry, 33, Spr.IconCap, Strings.CarryName, x0, x1, y, rowH, time);
            y = UpgradeRow(sim, UpgradeType.Sting, 34, Spr.IconSting, Strings.StingName, x0, x1, y, rowH, time);
            y = UpgradeRow(sim, UpgradeType.Speed, 35, Spr.IconSpeed, Strings.SpeedName, x0, x1, y, rowH, time);
            // стражи: переключатель 0 → 5 → 10 → 20 (бесплатно)
            {
                y -= rowH;
                RowFrame(x0, y, x1 - x0, rowH - 3);
                _ui.Icon(Spr.IconGuard, x0 + 12, y + rowH * 0.5f, null, 2);
                _ui.Text(Strings.GuardName, x0 + 24, y + 19, PixelUI.Ink, 1, 0, false);
                _b.Clear().Append(Strings.GuardSub).Append(sim.Swarm.GuardCount).Append('/').Append(sim.GuardWanted);
                _ui.Text(_b, x0 + 24, y + 8, SubText, 1, 0, false);
                int next = sim.Cfg.GuardSteps[(sim.Colony.GuardIndex + 1) % sim.Cfg.GuardSteps.Length];
                _b.Clear().Append(next);
                if (_ui.ButtonBuf(36, new Rect(x1 - 50, y + 6, 46, 20), _b, true, Spr.IconGuard)) sim.CycleGuards();
            }

            if (_ui.Button(39, new Rect(Mathf.Round(panel.center.x - 46), panel.y + 6, 92, 22), Strings.Fly)) a = UiAction.CloseHive;
            return a;
        }

        void RowFrame(float x, float y, float w, float h)
        {
            _ui.Fill(new Rect(x, y, w, h), new Color32(119, 70, 29, 90));
            _ui.Fill(new Rect(x, y + h - 1, w, 1), new Color32(255, 240, 200, 60));
        }

        float UpgradeRow(GameSim sim, UpgradeType t, int id, Spr icon, string name, float x0, float x1, float y, float rowH, float time)
        {
            var col = sim.Colony;
            var cfg = sim.Cfg;
            y -= rowH;
            RowFrame(x0, y, x1 - x0, rowH - 3);
            _ui.Icon(icon, x0 + 12, y + rowH * 0.5f, null, 2);
            _ui.Text(name, x0 + 24, y + 19, PixelUI.Ink, 1, 0, false);
            int lv = col.Level[(int)t];
            int max = col.MaxLevel(t);
            // пипсы уровня
            for (int i = 0; i < max; i++)
                _ui.Fill(new Rect(x0 + 24 + PixelFont.Measure(name) + 5 + i * 5, y + 20, 3, 5), i < lv ? PixelUI.Honey : new Color32(46, 26, 14, 255));

            _b.Clear();
            switch (t)
            {
                case UpgradeType.Comb:
                    _b.Append(Strings.CombSub).Append(cfg.CombCap[lv]);
                    if (lv < max) _b.Append(" > ").Append(cfg.CombCap[lv + 1]);
                    break;
                case UpgradeType.Carry:
                    _b.Append(Strings.CarrySub).AppendDecimal(cfg.CarryMul[lv], 2);
                    if (lv < max) _b.Append(" > ").AppendDecimal(cfg.CarryMul[lv + 1], 2);
                    break;
                case UpgradeType.Sting:
                    _b.Append(Strings.StingSub).AppendDecimal(cfg.StingMul[lv], 2);
                    if (lv < max) _b.Append(" > ").AppendDecimal(cfg.StingMul[lv + 1], 2);
                    break;
                default:
                    _b.Append(Strings.SpeedSub).AppendDecimal(cfg.SpeedMul[lv], 2);
                    if (lv < max) _b.Append(" > ").AppendDecimal(cfg.SpeedMul[lv + 1], 2);
                    break;
            }
            _ui.Text(_b, x0 + 24, y + 8, SubText, 1, 0, false);

            var br = new Rect(x1 - 50, y + 6, 46, 20);
            if (col.IsMaxed(t))
            {
                _ui.Button(id, br, Strings.Max, false);
            }
            else
            {
                _b.Clear().Append(col.CostOf(t));
                if (_ui.ButtonBuf(id, br, _b, sim.CanUpgrade(t), Spr.IconHoney)) sim.TryUpgrade(t);
            }
            return y;
        }

        // ================================================================ ПАУЗА
        public UiAction DrawPause(bool muted, bool haptics, bool shake)
        {
            var a = UiAction.None;
            int W = _ui.W, H = _ui.H;
            _ui.Fill(new Rect(0, 0, W, H), new Color32(23, 18, 28, 170));
            _ui.Blocker(new Rect(0, 0, W, H));
            var panel = new Rect(Mathf.Round(W * 0.5f - 70), Mathf.Round(H * 0.5f - 106), 140, 212);
            _ui.Panel(panel);
            _ui.Text(Strings.Pause, panel.center.x, panel.yMax - 24, PixelUI.Ink, 2, 1, false);
            float bx = panel.center.x - 55, by = panel.yMax - 54;
            if (_ui.Button(41, new Rect(bx, by, 110, 22), Strings.Resume)) a = UiAction.Resume;
            if (_ui.Button(42, new Rect(bx, by - 26, 110, 22), Strings.Restart)) a = UiAction.Restart;
            if (_ui.Button(43, new Rect(bx, by - 52, 110, 22), muted ? Strings.SoundOff : Strings.SoundOn)) a = UiAction.ToggleSound;
            if (_ui.Button(45, new Rect(bx, by - 78, 110, 22), haptics ? Strings.HapticsOn : Strings.HapticsOff)) a = UiAction.ToggleHaptics;
            if (_ui.Button(46, new Rect(bx, by - 104, 110, 22), shake ? Strings.ShakeOn : Strings.ShakeOff)) a = UiAction.ToggleShake;
            if (_ui.Button(44, new Rect(bx, by - 130, 110, 22), Strings.ToMenu)) a = UiAction.ToMenu;
            return a;
        }

        // ================================================================ ИТОГИ
        public UiAction DrawResult(GameSim sim, float time, bool newRecord, float bestWin)
        {
            var a = UiAction.None;
            int W = _ui.W, H = _ui.H;
            _ui.Fill(new Rect(0, 0, W, H), new Color32(23, 18, 28, 175));
            _ui.Blocker(new Rect(0, 0, W, H));
            var panel = new Rect(Mathf.Round(W * 0.5f - Mathf.Min(100, W * 0.5f - 6)), Mathf.Round(H * 0.5f - 122), Mathf.Min(200, W - 12), 244);
            _ui.Panel(panel);

            string title, sub;
            Color32 tc;
            switch (sim.Result)
            {
                case SimResult.Won: title = Strings.Won; sub = Strings.WonSub; tc = PixelUI.Green; break;
                case SimResult.Wintered: title = Strings.Wintered; sub = Strings.WinteredSub; tc = PixelUI.Honey; break;
                case SimResult.LostSwarm: title = Strings.LostSwarm; sub = Strings.LostSwarmSub; tc = PixelUI.Red; break;
                default: title = Strings.LostSummer; sub = Strings.LostSummerSub; tc = PixelUI.Red; break;
            }
            float y = panel.yMax - 26;
            _ui.Text(title, panel.center.x, y, tc, 2, 1, true);
            y -= 13;
            _ui.Text(sub, panel.center.x, y, SubText, 1, 1, true);
            y -= 8;
            // звёзды: 3 — цель лета, 1–2 — зимовка
            for (int si = 0; si < 3; si++)
            {
                bool on = si < sim.Stars;
                float pop = on && Mathf.Repeat(time * 1.5f - si * 0.15f, 1f) < 0.08f ? 1f : 0f;
                _ui.Icon(on ? Spr.IconStar : Spr.IconStarEmpty, panel.center.x + (si - 1) * 22, y - 8 + pop, null, 2);
            }
            y -= 18;

            var st = sim.Stats;
            y = StatRow(panel, y, Strings.StatTime, -1, sim.Time);
            y = StatRow(panel, y, Strings.StatPeak, st.PeakBees, 0f);
            y = StatRow(panel, y, Strings.StatHoney, Mathf.FloorToInt(st.HoneyDelivered), 0f);
            y = StatRow(panel, y, Strings.StatLost, st.BeesLost, 0f);
            y = StatRow(panel, y, Strings.StatWasps, st.WaspsKilled, 0f);
            y = StatRow(panel, y, Strings.StatHornets, st.HornetsKilled, 0f);

            y -= 14;
            if (newRecord && Mathf.Repeat(time * 2f, 1f) < 0.7f)
                _ui.Text(Strings.NewRecord, panel.center.x, y, PixelUI.Honey, 1, 1, true);
            else if (bestWin > 0f)
            {
                _b.Clear().Append(Strings.Record).AppendTime(bestWin);
                _ui.Text(_b, panel.center.x, y, SubText, 1, 1, true);
            }

            float bw = (panel.width - 24) * 0.5f;
            if (_ui.Button(51, new Rect(panel.x + 8, panel.y + 8, bw, 24), Strings.Again)) a = UiAction.Restart;
            if (_ui.Button(52, new Rect(panel.xMax - 8 - bw, panel.y + 8, bw, 24), Strings.ToMenu)) a = UiAction.ToMenu;
            return a;
        }

        float StatRow(Rect panel, float y, string label, int value, float seconds)
        {
            y -= 13;
            _ui.Fill(new Rect(panel.x + 8, y - 3, panel.width - 16, 11), new Color32(119, 70, 29, 60));
            _ui.Text(label, panel.x + 12, y, PixelUI.Ink, 1, 0, false);
            _b.Clear();
            if (value < 0) _b.AppendTime(seconds); else _b.Append(value);
            _ui.Text(_b, panel.xMax - 12, y, PixelUI.Ink, 1, 2, false);
            return y;
        }

        public void Fade(float alpha)
        {
            if (alpha <= 0.001f) return;
            _ui.Fill(new Rect(0, 0, _ui.W, _ui.H), new Color32(10, 8, 14, (byte)(255 * Mathf.Clamp01(alpha))));
        }
    }
}
