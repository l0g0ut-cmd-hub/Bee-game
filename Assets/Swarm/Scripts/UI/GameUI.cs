using System.Collections.Generic;
using Swarm.Game;
using Swarm.Presentation;
using Swarm.Simulation;
using UnityEngine;

namespace Swarm.UI
{
    public enum UiAction
    {
        None, OpenCampaign, StartLevel, PlayEndless, PlayDaily, OpenAchievements, Back,
        Pause, Resume, Restart, ToMenu, OpenHive, CloseHive, ChoosePerk,
        ToggleSound, ToggleHaptics, ToggleShake, PostSquad, ToggleAuto
    }

    /// <summary>
    /// Экраны: меню режимов, выбор сезона, достижения, HUD, улей, выбор перка, пауза, итоги.
    /// Только отрисовка и клики — решения принимает GameRoot.
    /// </summary>
    public sealed class GameUI
    {
        readonly PixelUI _ui;
        readonly TextBuf _b = new TextBuf();
        readonly Dictionary<string, string[]> _wrapCache = new Dictionary<string, string[]>();

        float _bannerTime;
        string _bannerTitle, _bannerSub;
        Color32 _bannerColor;

        public int TutorialStep;
        public int SelectedLevel;     // для UiAction.StartLevel
        public int PerkChoice;        // для UiAction.ChoosePerk
        public string VersionText = "";
        float _hintAlpha;
        bool _lateSummerShown;
        float _ballHintTime, _birdHintTime, _thirstHintTime, _squadHintTime, _webHintTime;

        static readonly Color32 SubText = new Color32(255, 230, 180, 255);
        static readonly Color32 Brown = new Color32(90, 60, 40, 255);

        public GameUI(PixelUI ui) { _ui = ui; }

        // ================================================================ баннеры
        public void ShowWave(WaveMessage m)
        {
            switch (m)
            {
                case WaveMessage.Scouts: ShowBanner(Strings.WaveScouts, Strings.WaveSubRaid, PixelUI.Honey); break;
                case WaveMessage.Wasps: ShowBanner(Strings.WaveWasps, Strings.WaveSubRaid, PixelUI.Honey); break;
                case WaveMessage.Hornet: ShowBanner(Strings.WaveHornet, Strings.WaveSubHornet, PixelUI.Red); break;
                case WaveMessage.Bear: ShowBanner(Strings.BearTitle, Strings.BearSub, PixelUI.Red); break;
                case WaveMessage.Ants: ShowBanner(Strings.AntsTitle, Strings.AntsSub, PixelUI.Honey); break;
                default: ShowBanner(Strings.WaveInvasion, null, PixelUI.Red); break;
            }
        }

        public void ShowBanner(string title, string sub, Color32 color)
        {
            _bannerTime = 3f;
            _bannerTitle = title;
            _bannerSub = sub;
            _bannerColor = color;
        }

        public void ClearBanner()
        {
            _bannerTime = 0f;
            _lateSummerShown = false;
            _ballHintTime = _birdHintTime = _thirstHintTime = _squadHintTime = _webHintTime = 0f;
        }

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

        void TextWrapped(string s, float cx, float topY, int maxW, Color32 c, bool shadow = true)
        {
            var lines = Wrap(s, maxW);
            for (int i = 0; i < lines.Length; i++)
                _ui.Text(lines[i], cx, topY - PixelFont.LineHeight - i * 10, c, 1, 1, shadow);
        }

        void Stars(float cx, float y, int stars, int scale = 2)
        {
            int step = 11 * scale;
            for (int i = 0; i < 3; i++)
                _ui.Icon(i < stars ? Spr.IconStar : Spr.IconStarEmpty, cx + (i - 1) * step, y, null, scale);
        }

        // ================================================================ МЕНЮ
        public UiAction DrawMenu(float time, Profile profile, bool muted)
        {
            var a = UiAction.None;
            int W = _ui.W, H = _ui.H;
            float top = H - _ui.SafeTop;
            for (int i = 0; i < 10; i++)
                _ui.Fill(new Rect(0, top - 130 + i * 13, W, 13), new Color32(23, 18, 28, (byte)(20 + i * 14)));

            float bob = Mathf.Round(Mathf.Sin(time * 1.6f) * 2f);
            _ui.Icon(Spr.Logo, W * 0.5f, top - 52 + bob);
            _ui.Text(Strings.Subtitle, W * 0.5f, top - 92, PixelUI.Honey, 1, 1);

            float ty = H * 0.47f;
            _ui.Fill(new Rect(8, ty - 16, W - 16, 44), new Color32(23, 18, 28, 150));
            _ui.Text(Strings.Tagline1, W * 0.5f, ty + 16, PixelUI.White, 1, 1);
            _ui.Text(Strings.Tagline2, W * 0.5f, ty + 4, PixelUI.Muted, 1, 1);
            _ui.Text(Strings.Tagline3, W * 0.5f, ty - 8, PixelUI.Muted, 1, 1);

            float bw = 132, bx = Mathf.Round(W * 0.5f - bw * 0.5f), by = Mathf.Round(H * 0.32f);
            float pulse = Mathf.Sin(time * 4f) > 0.7f ? 1f : 0f;
            if (_ui.Button(1, new Rect(bx, by + pulse, bw, 26), Strings.ModeCampaign, true, 1, Spr.IconStar)) a = UiAction.OpenCampaign;
            if (_ui.Button(2, new Rect(bx, by - 30, bw, 24), Strings.ModeEndless, true, 1, Spr.IconSkull)) a = UiAction.PlayEndless;
            if (_ui.Button(3, new Rect(bx, by - 58, bw, 24), Strings.ModeDaily, true, 1, Spr.IconSun)) a = UiAction.PlayDaily;

            float sy = by - 88;
            if (_ui.Button(4, new Rect(bx, sy, bw * 0.62f, 20), Strings.AchievementsTitle, true, 1, Spr.IconTrophy)) a = UiAction.OpenAchievements;
            if (_ui.Button(5, new Rect(bx + bw * 0.62f + 4, sy, bw * 0.38f - 4, 20), muted ? "ВЫКЛ" : "ЗВУК")) a = UiAction.ToggleSound;

            if (profile.EndlessBest > 0f)
            {
                _b.Clear().Append(Strings.ModeEndless).Append(": ").AppendTime(profile.EndlessBest);
                _ui.Text(_b, W * 0.5f, sy - 14, PixelUI.Muted, 1, 1);
            }
            _b.Clear().Append(Strings.Version).Append(VersionText);
            _ui.Text(_b, W * 0.5f, 4 + _ui.SafeBottom, new Color32(196, 186, 168, 190), 1, 1);
            return a;
        }

        // ================================================================ ВЫБОР СЕЗОНА
        public UiAction DrawLevels(Profile profile, float time)
        {
            var a = UiAction.None;
            int W = _ui.W, H = _ui.H;
            _ui.Fill(new Rect(0, 0, W, H), new Color32(23, 18, 28, 150));
            _ui.Blocker(new Rect(0, 0, W, H));
            float top = H - _ui.SafeTop;
            _ui.Text(Strings.ModeCampaign, W * 0.5f, top - 28, PixelUI.Honey, 2, 1);
            _b.Clear().Append(profile.CampaignStars).Append(" / 9");
            _ui.Icon(Spr.IconStar, W * 0.5f - 20, top - 41);
            _ui.Text(_b, W * 0.5f - 12, top - 44, PixelUI.White);

            float cw = W - 24, ch = 64;
            for (int i = 0; i < Levels.Campaign.Length; i++)
            {
                var d = Levels.Campaign[i];
                bool locked = profile.CampaignStars < d.UnlockStars;
                var r = new Rect(12, top - 120 - i * (ch + 10), cw, ch);
                if (_ui.Button(20 + i, r, null, !locked)) { SelectedLevel = i; a = UiAction.StartLevel; }
                Color32 tc = locked ? new Color32(60, 60, 70, 255) : PixelUI.Ink;
                _ui.Text(d.Name, r.x + 10, r.yMax - 18, tc, 2, 0, false);
                _ui.Text(d.Sub, r.x + 10, r.yMax - 30, locked ? tc : Brown, 1, 0, false);
                _b.Clear().Append("ЦЕЛЬ ").Append(d.GoalBees).Append(" ПЧЁЛ, ").Append(d.GoalHoney).Append(" МЁДА");
                _ui.Text(_b, r.x + 10, r.y + 8, locked ? tc : Brown, 1, 0, false);
                if (locked)
                {
                    _ui.Icon(Spr.IconLock, r.xMax - 22, r.center.y + 6, null, 2);
                    _b.Clear().Append(Strings.NeedStars).Append(d.UnlockStars);
                    _ui.Text(_b, r.xMax - 10, r.y + 8, tc, 1, 2, false);
                }
                else Stars(r.xMax - 36, r.yMax - 14, profile.LevelStars[i], 1);
            }
            if (_ui.Button(29, new Rect(Mathf.Round(W * 0.5f - 46), _ui.SafeBottom + 10, 92, 22), Strings.Back)) a = UiAction.Back;
            return a;
        }

        // ================================================================ ДОСТИЖЕНИЯ
        public UiAction DrawAchievements(Profile profile)
        {
            var a = UiAction.None;
            int W = _ui.W, H = _ui.H;
            _ui.Fill(new Rect(0, 0, W, H), new Color32(23, 18, 28, 170));
            _ui.Blocker(new Rect(0, 0, W, H));
            float top = H - _ui.SafeTop;
            _ui.Text(Strings.AchievementsTitle, W * 0.5f, top - 26, PixelUI.Honey, 2, 1);
            float y = top - 50;
            float rowH = Mathf.Min(26f, (y - _ui.SafeBottom - 44f) / (int)Achievement.Count);
            for (int i = 0; i < (int)Achievement.Count; i++)
            {
                var ach = (Achievement)i;
                bool has = profile.Has(ach);
                y -= rowH;
                _ui.Fill(new Rect(10, y, W - 20, rowH - 3), has ? new Color32(119, 70, 29, 200) : new Color32(40, 34, 44, 200));
                _ui.Icon(Spr.IconTrophy, 22, y + rowH * 0.5f - 1, has ? (Color32?)null : new Color32(90, 90, 100, 255));
                _ui.Text(Achievements_.Name(ach), 32, y + rowH - 12, has ? PixelUI.Honey : PixelUI.Muted, 1, 0, false);
                if (rowH >= 22f) _ui.Text(Achievements_.Desc(ach), 32, y + 3, has ? SubText : new Color32(140, 135, 125, 255), 1, 0, false);
            }
            if (_ui.Button(39, new Rect(Mathf.Round(W * 0.5f - 46), _ui.SafeBottom + 10, 92, 22), Strings.Back)) a = UiAction.Back;
            return a;
        }

        // ================================================================ ВЫБОР ПЕРКА
        public UiAction DrawPerk(GameSim sim, float time)
        {
            var a = UiAction.None;
            int W = _ui.W, H = _ui.H;
            _ui.Fill(new Rect(0, 0, W, H), new Color32(23, 18, 28, 175));
            _ui.Blocker(new Rect(0, 0, W, H));
            float cy = H * 0.5f;
            _ui.Text(Strings.PerkTitle, W * 0.5f, cy + 92, PixelUI.Honey, 2, 1);
            _ui.Text(Strings.PerkSub, W * 0.5f, cy + 78, PixelUI.Muted, 1, 1);
            for (int k = 0; k < 3; k++)
            {
                var p = sim.PerkOffer[k];
                var r = new Rect(14, cy + 40 - k * 52, W - 28, 46);
                if (_ui.Button(60 + k, r, null)) { PerkChoice = k; a = UiAction.ChoosePerk; }
                _ui.Text(Perks.Name(p), r.center.x, r.yMax - 18, PixelUI.Ink, 1, 1, false);
                TextWrapped(Perks.Desc(p), r.center.x, r.yMax - 22, (int)r.width - 12, Brown, false);
            }
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

            DrawIndicators(sim, screen, time, top - 46);

            // --- верхняя панель
            var bar = new Rect(-3, top - 21, W + 6, 24);
            _ui.Blocker(bar);
            _ui.Panel(bar, true);
            float ty = top - 14;
            _ui.Icon(Spr.IconBee, 9, ty + 3);
            _b.Clear().Append(sw.SwarmCount).Append('/').Append(col.MaxBees);
            _ui.Text(_b, 15, ty, sw.SwarmCount >= col.MaxBees ? PixelUI.Honey : PixelUI.White);

            float hx = Mathf.Round(W * 0.43f);
            _ui.Icon(Spr.IconHoney, hx, ty + 3);
            _b.Clear().Append(Mathf.FloorToInt(col.Honey));
            _ui.Text(_b, hx + 6, ty, PixelUI.Honey);

            float tx = Mathf.Round(W * 0.58f);
            bool night = sim.Sky.IsNight(sim.Time);
            _ui.Icon(night ? Spr.IconMoon : WeatherIcon(sim.Sky), tx, ty + 3);
            _b.Clear();
            if (cfg.Endless) _b.AppendTime(sim.Time);
            else
            {
                float left = sim.TimeLeft;
                _b.AppendTime(left);
            }
            bool hurry = !cfg.Endless && sim.TimeLeft < 60f && Mathf.Repeat(time, 1f) < 0.5f;
            _ui.Text(_b, tx + 6, ty, hurry ? PixelUI.Red : PixelUI.White);

            if (_ui.Button(10, new Rect(W - 21, top - 19, 18, 18), null, true, 1, Spr.IconPause)) a = UiAction.Pause;
            bool auto = AutoPilot.Enabled;
            if (_ui.Button(11, new Rect(W - 41, top - 19, 18, 18), "А", true, 1, Spr.Count, auto ? new Color32(40, 120, 30, 255) : (Color32?)null)) a = UiAction.ToggleAuto;
            if (auto && Mathf.Repeat(time, 2f) < 1.6f)
                _ui.Text(Strings.AutoOn, W * 0.5f, top - 59, new Color32(255, 240, 160, 255), 1, 1);

            // --- вторая строка: цель / выживание
            float gy = top - 33;
            _ui.Fill(new Rect(4, gy - 3, W - 8, 13), PixelUI.InkSoft);
            if (cfg.Endless)
            {
                _b.Clear().Append(Strings.Round).Append(sim.Waves.Cycle + 1).Append("  ").Append(Strings.Survive).Append(' ');
                _ui.Text(_b, 8, gy, PixelUI.White, 1, 0, false);
                int st = sim.EndlessStars();
                float next = st >= 3 ? 1800f : (st + 1) * 600f;
                float sx = 8 + PixelFont.Measure(_b) + 2;
                _ui.Bar(new Rect(sx, gy + 1, W - sx - 46, 4), sim.Time / next, PixelUI.Honey, new Color32(70, 50, 40, 255));
                Stars(W - 26, gy + 3, st, 1);
            }
            else
            {
                if (!_lateSummerShown && sim.TimeLeft < 120f && sim.TimeLeft > 0f)
                {
                    _lateSummerShown = true;
                    ShowBanner(Strings.LateSummer, Strings.LateSummerSub, PixelUI.Honey);
                }
                float half = (W - 8) * 0.5f;
                DrawGoal(4 + 2, gy, half - 6, Spr.IconBee, sw.SwarmCount, cfg.GoalBees);
                bool honeyBlink = sim.TimeLeft < 120f && !sim.GoalHoneyMet && Mathf.Repeat(time * 2f, 1f) < 0.3f;
                if (!honeyBlink) DrawGoal(4 + half + 2, gy, half - 6, Spr.IconHoney, Mathf.FloorToInt(col.Honey), cfg.GoalHoney);
            }

            // --- день и жажда
            if (cfg.SkyEnabled)
            {
                _b.Clear().Append(Strings.Day).Append(sim.Sky.Day(sim.Time));
                _ui.Text(_b, W - 6, gy - 13, night ? new Color32(170, 190, 255, 255) : PixelUI.Muted, 1, 2);
            }
            // защитники и сборщицы — под строкой цели слева
            {
                float cx = 8f;
                if (sw.GuardCount > 0)
                {
                    _ui.Icon(Spr.IconGuard, cx + 2, gy - 10);
                    _b.Clear().Append(sw.GuardCount);
                    _ui.Text(_b, cx + 7, gy - 13, new Color32(255, 170, 150, 255));
                    cx += 14 + PixelFont.Measure(_b);
                }
                if (sw.WorkerCount > 0)
                {
                    _ui.Icon(Spr.IconNectar, cx + 2, gy - 10);
                    _b.Clear().Append(sw.WorkerCount);
                    _ui.Text(_b, cx + 7, gy - 13, new Color32(200, 240, 160, 255));
                }
            }
            if (sim.Sky.Thirst > 0.05f)
            {
                _ui.Icon(Spr.IconWater, 10, gy - 21);
                _ui.Bar(new Rect(17, gy - 23, 50, 3), sim.Sky.Thirst, new Color32(110, 170, 230, 255), new Color32(40, 50, 70, 255));
            }

            // --- баннер
            if (_bannerTime > 0f)
            {
                _bannerTime -= dt;
                bool blink = _bannerTime > 2.2f && Mathf.Repeat(time * 6f, 1f) < 0.35f;
                float by = Mathf.Round(H * 0.66f);
                float k = Mathf.Clamp01(_bannerTime * 2f);
                int subLines = _bannerSub != null ? Wrap(_bannerSub, W - 8).Length : 0;
                float extra = Mathf.Max(0, subLines - 1) * 10f;
                _ui.Fill(new Rect(0, by - 14 - extra, W, 34 + extra), new Color32(23, 18, 28, (byte)(150 * k)));
                if (!blink)
                {
                    var c = _bannerColor; c.a = (byte)(255 * k);
                    int scale = PixelFont.Measure(_bannerTitle, 2) > W - 10 ? 1 : 2;
                    _ui.Text(_bannerTitle, W * 0.5f, by + (scale == 2 ? 2 : 5), c, scale, 1);
                }
                if (_bannerSub != null)
                {
                    var lines = Wrap(_bannerSub, W - 8);   // длинные подписи переносятся, а не уходят за край
                    for (int li = 0; li < lines.Length; li++)
                        _ui.Text(lines[li], W * 0.5f, by - 10 - li * 10, new Color32(255, 250, 240, (byte)(220 * k)), 1, 1);
                }
            }

            // --- нектар в рое
            float cap = cfg.CarryBase * col.CarryMul * sim.Mods.Carry * Mathf.Max(1, sw.SwarmCount - sw.SquadCount);
            float frac = sw.FieldCarry / cap;
            float bottom = _ui.SafeBottom + 6;
            var cr = new Rect(6, bottom, 74, 18);
            _ui.Fill(cr, PixelUI.InkSoft);
            _ui.Icon(Spr.IconNectar, cr.x + 7, cr.y + 9);
            _ui.Bar(new Rect(cr.x + 14, cr.y + 7, 54, 4), frac, PixelUI.Honey, new Color32(70, 50, 40, 255));
            if (frac > 0.85f && !sim.SwarmAtHive && Mathf.Repeat(time * 2f, 1f) < 0.6f)
                _ui.Text(Strings.CarryFull, cr.x, cr.yMax + 4, PixelUI.Honey);

            if (sw.Balling && Mathf.Repeat(time * 5f, 1f) < 0.75f)
                _ui.Text(Strings.Balling, W * 0.5f, bottom + 34, new Color32(255, 150, 60, 255), 1, 1);
            // кнопка поста: оставить отряд стеречь место / вернуть его в рой
            {
                var pr = new Rect(Mathf.Round(W * 0.5f - 29), bottom - 2, 58, 22);
                if (sw.SquadActive)
                {
                    if (_ui.Button(21, pr, Strings.PostRecall)) a = UiAction.PostSquad;
                    _ui.Icon(Spr.IconSquad, pr.center.x - 8, pr.yMax + 7);
                    _b.Clear().Append(sw.SquadCount);
                    _ui.Text(_b, pr.center.x - 1, pr.yMax + 4, new Color32(200, 228, 255, 255));
                }
                else if (_ui.Button(21, pr, Strings.PostName, sim.CanPost, 1, Spr.IconSquad)) a = UiAction.PostSquad;
            }

            if (sim.SwarmAtHive)
            {
                float p = Mathf.Sin(time * 5f) > 0f ? 1f : 0f;
                var hr = new Rect(W - 76, bottom - 4 + p, 70, 26);
                if (_ui.Button(20, hr, Strings.Hive, true, 1, Spr.IconComb)) a = UiAction.OpenHive;
            }

            string hint = TutorialHint(sim, dt);
            _hintAlpha = Mathf.MoveTowards(_hintAlpha, hint != null ? 1f : 0f, dt * 3f);
            if (hint != null && _hintAlpha > 0.01f)
            {
                var lines = Wrap(hint, W - 24);
                float hy = bottom + 50 + lines.Length * 10;
                _ui.Fill(new Rect(8, hy - lines.Length * 10 - 4, W - 16, lines.Length * 10 + 6), new Color32(23, 18, 28, (byte)(160 * _hintAlpha)));
                TextWrapped(hint, W * 0.5f, hy, W - 24, new Color32(255, 250, 240, (byte)(255 * _hintAlpha)));
            }
            return a;
        }

        static Spr WeatherIcon(SkySystem sky)
        {
            if (sky.WeatherBlend < 0.3f) return Spr.IconSun;
            switch (sky.Current)
            {
                case Weather.Rain: return Spr.IconRain;
                case Weather.Wind: return Spr.IconWind;
                case Weather.Heat: return Spr.IconHeat;
                default: return Spr.IconSun;
            }
        }

        void DrawGoal(float x, float y, float w, Spr icon, int value, int goal)
        {
            bool met = value >= goal;
            _ui.Icon(met ? Spr.IconCheck : icon, x + 4, y + 3);
            _ui.Bar(new Rect(x + 11, y + 1, w - 54, 4), value / (float)goal, met ? PixelUI.Green : PixelUI.Honey, new Color32(70, 50, 40, 255));
            _b.Clear().Append(Mathf.Min(value, goal)).Append('/').Append(goal);
            _ui.Text(_b, x + w + 2, y, met ? PixelUI.Green : PixelUI.White, 1, 2, false);
        }

        string TutorialHint(GameSim sim, float dt)
        {
            var sw = sim.Swarm;
            // птица: тень рядом с роем
            if (_birdHintTime < 8f)
            {
                var hz = sim.Hazards;
                for (int b = 0; b < HazardSystem.MaxBirds; b++)
                    if (hz.BState[b] == BirdState.Shadow && (hz.BTarget[b] - sw.LeaderPos).sqrMagnitude < 60f * 60f)
                    { _birdHintTime += dt; return Strings.HintBird; }
            }
            // шершень рядом, шар не включён
            if (_ballHintTime < 15f && !sw.Balling && sw.SwarmCount >= sim.Cfg.BallMinBees)
            {
                var en = sim.Enemies;
                for (int i = 0; i < en.Count; i++)
                    if (en.Type[i] == EnemyType.Hornet && (en.Pos[i] - sw.LeaderPos).sqrMagnitude < 150f * 150f)
                    { _ballHintTime += dt; return Strings.HintBall; }
            }
            if (_thirstHintTime < 8f && sim.Sky.Thirst > 0.5f) { _thirstHintTime += dt; return Strings.HintThirst; }
            if (_webHintTime < 6f)
                for (int i = 0; i < sw.Count; i += 3)
                    if (sw.Stuck[i] > 1f) { _webHintTime += dt; return Strings.HintWeb; }
            switch (TutorialStep)
            {
                case 0:
                    if ((sw.LeaderPos - (sim.World.HivePos + new Vector2(0f, -30f))).magnitude > 40f) TutorialStep = 1;
                    return Strings.HintMove;
                case 1:
                    if (sw.TotalCarry > 3f) TutorialStep = 2;
                    return Strings.HintFlowers;
                case 2:
                    if (sim.Stats.HoneyDelivered > 4f) TutorialStep = 3;
                    return Strings.HintDeliver;
                case 3:
                    if (sim.Time > 100f) TutorialStep = 4;
                    return sim.SwarmAtHive ? Strings.HintHatch : null;
                default:
                    if (_squadHintTime < 7f && sim.Time > 120f && sw.SwarmCount >= 40 && !sw.SquadActive) { _squadHintTime += dt; return Strings.HintSquad; }
                    return null;
            }
        }

        void DrawIndicators(GameSim sim, PixelScreen screen, float time, float maxY)
        {
            int W = _ui.W;
            float minY = _ui.SafeBottom + 30f;
            EdgeArrow(screen, sim.World.HivePos + new Vector2(0f, 20f), W, minY, maxY, PixelUI.Honey, Spr.IconComb, time, 0f);
            var en = sim.Enemies;
            for (int i = 0; i < en.Count; i++)
            {
                var t = en.Type[i];
                if (t == EnemyType.Nest && !sim.Fog.IsRevealed(en.Pos[i])) continue;
                if (t == EnemyType.Ant) continue;
                Spr icon = t == EnemyType.Hornet || t == EnemyType.Bear ? Spr.IconSkull : Spr.IconWasp;
                EdgeArrow(screen, en.Pos[i], W, minY, maxY, PixelUI.Red, icon, time, i * 0.7f);
            }
            if (sim.Swarm.SquadActive)
                EdgeArrow(screen, sim.Swarm.SquadTarget, W, minY, maxY, new Color32(200, 228, 255, 255), Spr.IconSquad, time, 3f);
            var fog = sim.Fog;
            for (int m = 0; m < FogMap.MaxMarks; m++)
                if (fog.MarkTime[m] > 0f)
                    EdgeArrow(screen, fog.MarkPos[m], W, minY, maxY, PixelUI.Honey, Spr.IconNectar, time, m);
            var fl = sim.Flowers;
            for (int f = fl.BaseCount; f < fl.Count; f++)
                if ((fl.Rare[f] || fl.Kind[f] == FlowerKind.Linden) && fl.IsBlooming(f) && (fl.Rare[f] || f == fl.Count - 1 || fl.Kind[f + 1] != FlowerKind.Linden))
                    EdgeArrow(screen, fl.Pos[f], W, minY, maxY, new Color32(255, 230, 120, 255), Spr.IconNectar, time, f);
        }

        void EdgeArrow(PixelScreen screen, Vector2 world, int W, float minY, float maxY, Color32 c, Spr icon, float time, float ph)
        {
            Vector2 p = screen.WorldToPixel(world);
            const float m = 9f;
            if (p.x > m && p.x < W - m && p.y > minY && p.y < maxY) return;
            Vector2 center = new Vector2(W * 0.5f, (minY + maxY) * 0.5f);
            Vector2 d = p - center;
            float ang = Mathf.Atan2(d.y, d.x);
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
            _ui.Fill(new Rect(0, 0, W, H), new Color32(23, 18, 28, 140));

            float pw = Mathf.Min(W - 10, 220), ph = 360;
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

            y -= 2;
            TextWrapped(Strings.QueenHelp, panel.center.x, y, (int)pw - 16, PixelUI.Ink, false);
            y -= Wrap(Strings.QueenHelp, (int)pw - 16).Length * 10 + 2;
            float rowH = 34f;
            // 1. матка: доля мёда на расплод
            {
                y -= rowH;
                RowFrame(x0, y, x1 - x0, rowH - 3);
                _ui.Icon(Spr.IconQueen, x0 + 12, y + rowH * 0.5f, null, 2);
                _ui.Text(Strings.QueenName, x0 + 24, y + 19, PixelUI.Ink, 1, 0, false);
                bool full = sim.Swarm.SwarmCount >= col.MaxBees;
                if (full) _ui.Text(Strings.HatchFull, x0 + 24, y + 8, PixelUI.Red, 1, 0, false);
                else if (sim.BroodShare <= 0f) _ui.Text(Strings.QueenStopped, x0 + 24, y + 8, SubText, 1, 0, false);
                else
                {
                    // +N пчёл/мин по X мёда за каждую
                    _b.Clear().Append('+').Append(Mathf.RoundToInt(sim.BroodPerMinute)).Append("/МИН ПО ").AppendDecimal(sim.BroodCostPerBee, 1);
                    _ui.Text(_b, x0 + 24, y + 8, SubText, 1, 0, false);
                    _ui.Icon(Spr.IconHoney, x0 + 26 + PixelFont.Measure(_b) + 3, y + 11);
                }
                if (_ui.Button(31, new Rect(x1 - 50, y + 6, 46, 20), Strings.BroodNames[sim.BroodIndex])) sim.CycleBrood();
            }
            // 2. защитники
            {
                y -= rowH;
                RowFrame(x0, y, x1 - x0, rowH - 3);
                _ui.Icon(Spr.SoldierA, x0 + 12, y + rowH * 0.5f);
                _ui.Text(Strings.DefName, x0 + 24, y + 19, PixelUI.Ink, 1, 0, false);
                _b.Clear().Append(Strings.DefSub).Append(sim.Swarm.GuardCount).Append('/').Append(sim.Cfg.DefenderMax);
                _ui.Text(_b, x0 + 24, y + 8, SubText, 1, 0, false);
                if (sim.DefenderRoom <= 0) _ui.Button(32, new Rect(x1 - 50, y + 6, 46, 20), Strings.Max, false);
                else
                {
                    _b.Clear().Append('+').Append(sim.DefenderRoom).Append(' ').Append(sim.DefenderCost);
                    if (_ui.ButtonBuf(32, new Rect(x1 - 50, y + 6, 46, 20), _b, sim.CanHireDefenders, Spr.IconHoney)) sim.HireDefenders();
                }
            }
            // 3. сборщицы
            {
                y -= rowH;
                RowFrame(x0, y, x1 - x0, rowH - 3);
                _ui.Icon(Spr.BeeLoadA, x0 + 12, y + rowH * 0.5f, new Color32(200, 240, 160, 255), 2);
                _ui.Text(Strings.WorkerName, x0 + 24, y + 18, PixelUI.Ink, 1, 0, false);
                _b.Clear().Append(Strings.WorkerSub).Append(sim.Swarm.WorkerCount).Append('/').Append(sim.Cfg.WorkerMax);
                _ui.Text(_b, x0 + 24, y + 7, SubText, 1, 0, false);
                if (sim.WorkerRoom <= 0) _ui.Button(37, new Rect(x1 - 50, y + 5, 46, 20), Strings.Max, false);
                else
                {
                    _b.Clear().Append('+').Append(sim.WorkerRoom).Append(' ').Append(sim.WorkerCost);
                    if (_ui.ButtonBuf(37, new Rect(x1 - 50, y + 5, 46, 20), _b, sim.CanHireWorkers, Spr.IconHoney)) sim.HireWorkers();
                }
            }
            y = UpgradeRow(sim, UpgradeType.Comb, 33, Spr.IconComb, Strings.CombName, x0, x1, y, rowH);
            y = UpgradeRow(sim, UpgradeType.Carry, 34, Spr.IconCap, Strings.CarryName, x0, x1, y, rowH);
            y = UpgradeRow(sim, UpgradeType.Sting, 35, Spr.IconSting, Strings.StingName, x0, x1, y, rowH);
            y = UpgradeRow(sim, UpgradeType.Speed, 36, Spr.IconSpeed, Strings.SpeedName, x0, x1, y, rowH);

            if (_ui.Button(39, new Rect(Mathf.Round(panel.center.x - 46), panel.y + 6, 92, 22), Strings.Fly)) a = UiAction.CloseHive;
            return a;
        }

        void RowFrame(float x, float y, float w, float h)
        {
            _ui.Fill(new Rect(x, y, w, h), new Color32(119, 70, 29, 90));
            _ui.Fill(new Rect(x, y + h - 1, w, 1), new Color32(255, 240, 200, 60));
        }

        float UpgradeRow(GameSim sim, UpgradeType t, int id, Spr icon, string name, float x0, float x1, float y, float rowH)
        {
            var col = sim.Colony;
            var cfg = sim.Cfg;
            y -= rowH;
            RowFrame(x0, y, x1 - x0, rowH - 3);
            _ui.Icon(icon, x0 + 12, y + rowH * 0.5f, null, 2);
            _ui.Text(name, x0 + 24, y + 19, PixelUI.Ink, 1, 0, false);
            int lv = col.Level[(int)t];
            int max = col.MaxLevel(t);
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
            if (col.IsMaxed(t)) _ui.Button(id, br, Strings.Max, false);
            else
            {
                _b.Clear().Append(col.CostOf(t));
                if (_ui.ButtonBuf(id, br, _b, sim.CanUpgrade(t), Spr.IconHoney)) sim.TryUpgrade(t);
            }
            return y;
        }

        // ================================================================ ПАУЗА
        public UiAction DrawPause(bool muted, bool haptics, bool shake, bool auto)
        {
            var a = UiAction.None;
            int W = _ui.W, H = _ui.H;
            _ui.Fill(new Rect(0, 0, W, H), new Color32(23, 18, 28, 170));
            _ui.Blocker(new Rect(0, 0, W, H));
            var panel = new Rect(Mathf.Round(W * 0.5f - 70), Mathf.Round(H * 0.5f - 119), 140, 238);
            _ui.Panel(panel);
            _ui.Text(Strings.Pause, panel.center.x, panel.yMax - 24, PixelUI.Ink, 2, 1, false);
            float bx = panel.center.x - 55, by = panel.yMax - 54;
            if (_ui.Button(41, new Rect(bx, by, 110, 22), Strings.Resume)) a = UiAction.Resume;
            if (_ui.Button(42, new Rect(bx, by - 26, 110, 22), Strings.Restart)) a = UiAction.Restart;
            if (_ui.Button(43, new Rect(bx, by - 52, 110, 22), muted ? Strings.SoundOff : Strings.SoundOn)) a = UiAction.ToggleSound;
            if (_ui.Button(45, new Rect(bx, by - 78, 110, 22), haptics ? Strings.HapticsOn : Strings.HapticsOff)) a = UiAction.ToggleHaptics;
            if (_ui.Button(46, new Rect(bx, by - 104, 110, 22), shake ? Strings.ShakeOn : Strings.ShakeOff)) a = UiAction.ToggleShake;
            if (_ui.Button(47, new Rect(bx, by - 130, 110, 22), auto ? Strings.AutoBtnOn : Strings.AutoBtnOff, true, 1, auto ? Spr.IconCheck : Spr.Count)) a = UiAction.ToggleAuto;
            if (_ui.Button(44, new Rect(bx, by - 156, 110, 22), Strings.ToMenu)) a = UiAction.ToMenu;
            return a;
        }

        // ================================================================ ИТОГИ
        public UiAction DrawResult(GameSim sim, float time, bool newRecord, float best, List<Achievement> fresh)
        {
            var a = UiAction.None;
            int W = _ui.W, H = _ui.H;
            _ui.Fill(new Rect(0, 0, W, H), new Color32(23, 18, 28, 175));
            _ui.Blocker(new Rect(0, 0, W, H));
            var panel = new Rect(Mathf.Round(W * 0.5f - Mathf.Min(100, W * 0.5f - 6)), Mathf.Round(H * 0.5f - 130), Mathf.Min(200, W - 12), 260);
            _ui.Panel(panel);

            string title, sub;
            Color32 tc;
            bool endless = sim.Cfg.Endless;
            switch (sim.Result)
            {
                case SimResult.Won: title = Strings.Won; sub = Strings.WonSub; tc = PixelUI.Green; break;
                case SimResult.Wintered: title = Strings.Wintered; sub = Strings.WinteredSub; tc = PixelUI.Honey; break;
                case SimResult.LostSwarm:
                    title = endless ? Strings.ModeEndless : Strings.LostSwarm;
                    sub = Strings.LostSwarmSub;
                    tc = endless ? PixelUI.Honey : PixelUI.Red;
                    break;
                default: title = Strings.LostSummer; sub = Strings.LostSummerSub; tc = PixelUI.Red; break;
            }
            float y = panel.yMax - 26;
            _ui.Text(title, panel.center.x, y, tc, 2, 1, true);
            y -= 13;
            _ui.Text(sub, panel.center.x, y, SubText, 1, 1, true);
            y -= 8;
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
            if (fresh != null && fresh.Count > 0)
            {
                int idx = (int)(time / 2f) % fresh.Count;
                _ui.Icon(Spr.IconTrophy, panel.x + 14, y + 3);
                _ui.Text(Achievements_.Name(fresh[idx]), panel.x + 22, y, PixelUI.Honey, 1, 0, true);
            }
            else if (newRecord && Mathf.Repeat(time * 2f, 1f) < 0.7f)
                _ui.Text(Strings.NewRecord, panel.center.x, y, PixelUI.Honey, 1, 1, true);
            else if (best > 0f)
            {
                _b.Clear().Append(Strings.Record).AppendTime(best);
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
