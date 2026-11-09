using Swarm.Presentation;
using Swarm.Simulation;
using Swarm.UI;
using UnityEngine;

namespace Swarm.Game
{
    public enum AutoContext { Playing, Hive, Perk, Other }

    /// <summary>
    /// Автопилот играет «виртуальным пальцем»: курсор плавно ведёт рой, подлетает к кнопкам и нажимает их
    /// через тот же UI, что и игрок (улей, покупки, матка, отряд, дары). Между действиями — паузы,
    /// чтобы было видно, что он делает. Логика решений — та же, что у бота стенда tools/simtest.
    /// </summary>
    public sealed class AutoPilot
    {
        const string PrefKey = "swarm_autopilot";
        public static bool Enabled;
        public static void Load() => Enabled = PlayerPrefs.GetInt(PrefKey, 0) == 1;
        public static void Toggle()
        {
            Enabled = !Enabled;
            PlayerPrefs.SetInt(PrefKey, Enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        // --- виртуальный палец (пиксели RT)
        public Vector2 Cursor;
        public bool Down, Held, Up;
        public bool Steering;      // палец ведёт рой (а не жмёт кнопку)
        public bool WantHold;      // удержание у шершня — тепловой шар
        public float HoldStill;

        // нажатие кнопки: подвод → нажатие → отпускание
        enum Tap { None, Move, Press }
        Tap _tap;
        int _tapId;
        float _tapTimer;
        float _wait;

        // решения полёта
        float _think;
        int _flower = -1;
        bool _delivering;
        Vector2 _worldTarget;
        float _hiveCooldown, _postCooldown, _perkDelay = -1f;
        float _postAge;

        const float CursorSpeed = 260f, TapSpeed = 420f;
        public const float HoldTime = 0.3f;

        public void Reset(PixelScreen screen)
        {
            Cursor = new Vector2(screen.Width * 0.5f, screen.Height * 0.4f);
            _tap = Tap.None;
            _wait = 0.8f;
            _flower = -1;
            _delivering = false;
            _hiveCooldown = 0f;
            _postCooldown = 30f;
            _perkDelay = -1f;
            Down = Up = Held = Steering = WantHold = false;
            HoldStill = 0f;
        }

        /// <summary>Живой палец коснулся экрана — текущее «нажатие» автопилота отменяется.</summary>
        public void Interrupt()
        {
            _tap = Tap.None;
            _wait = Mathf.Max(_wait, 0.4f);
            Held = Steering = false;
        }

        public Vector2 WorldTarget => _worldTarget;

        public void Tick(GameSim sim, AutoContext ctx, PixelUI ui, PixelScreen screen, float dt)
        {
            Down = Up = false;
            Steering = false;
            _hiveCooldown -= dt;
            _postCooldown -= dt;
            if (sim.Swarm.SquadActive) _postAge += dt; else _postAge = 0f;

            if (_tap != Tap.None) { RunTap(ui, dt); return; }
            if (_wait > 0f) { _wait -= dt; Held = false; WantHold = false; return; }

            switch (ctx)
            {
                case AutoContext.Perk:
                    if (_perkDelay < 0f) _perkDelay = 1.6f;           // даём прочитать карточки
                    _perkDelay -= dt;
                    if (_perkDelay <= 0f) { _perkDelay = -1f; StartTap(60 + PickPerk(sim)); }
                    break;
                case AutoContext.Hive:
                    StartTap(NextHiveButton(sim));
                    break;
                case AutoContext.Playing:
                    PlayField(sim, ui, screen, dt);
                    break;
                default:
                    Held = false;
                    break;
            }
        }

        void StartTap(int id)
        {
            _tap = Tap.Move;
            _tapId = id;
            Held = false;
            WantHold = false;
        }

        void RunTap(PixelUI ui, float dt)
        {
            if (!ui.TryGetButton(_tapId, out Rect r)) { _tap = Tap.None; _wait = 0.3f; Held = false; return; }
            Vector2 c = r.center;
            if (_tap == Tap.Move)
            {
                Held = false;
                Cursor = Vector2.MoveTowards(Cursor, c, TapSpeed * dt);
                if ((Cursor - c).sqrMagnitude < 1f)
                {
                    Cursor = c;
                    _tap = Tap.Press;
                    _tapTimer = 0.16f;
                    Down = true;
                    Held = true;
                }
            }
            else
            {
                Cursor = c;
                _tapTimer -= dt;
                Held = true;
                if (_tapTimer <= 0f)
                {
                    Held = false;
                    Up = true;
                    _tap = Tap.None;
                    _wait = 0.55f;
                }
            }
        }

        // ------------------------------------------------------------------ поле
        void PlayField(GameSim sim, PixelUI ui, PixelScreen screen, float dt)
        {
            var sw = sim.Swarm;
            // 1. Улей: зайти, если есть что купить или поменять кладку
            if (sim.SwarmAtHive && sw.FieldCarry < 0.5f && _hiveCooldown <= 0f && HiveWanted(sim) && ui.TryGetButton(20, out _))
            {
                _hiveCooldown = 20f;
                StartTap(20);
                return;
            }
            // 2. Отряд: оставить на богатой поляне / вернуть, когда поляна пуста или отряд мал
            if (sw.SquadActive)
            {
                if (_postAge > 8f && (BloomingNear(sim, sw.SquadTarget, sim.Cfg.PostRadius) == 0 || sw.SquadCount < 5) && ui.TryGetButton(21, out _))
                {
                    _postCooldown = 25f;
                    StartTap(21);
                    return;
                }
            }
            else if (_postCooldown <= 0f && sim.CanPost && sw.SwarmCount >= 50 && sim.Time > 120f && !_delivering && _flower >= 0
                     && (sim.Flowers.Pos[_flower] - sw.LeaderPos).sqrMagnitude < 30f * 30f
                     && BloomingNear(sim, sw.LeaderPos, sim.Cfg.PostRadius) >= 3 && ui.TryGetButton(21, out _))
            {
                _postCooldown = 45f;
                StartTap(21);
                return;
            }

            // 3. Полёт
            _think -= dt;
            if (_think <= 0f) { _think = 0.25f; Decide(sim); }
            Vector2 want = screen.WorldToPixel(_worldTarget);
            float top = screen.Height - screen.SafeTop - 46f, bottom = screen.SafeBottom + 34f;
            want.x = Mathf.Clamp(want.x, 8f, screen.Width - 8f);
            want.y = Mathf.Clamp(want.y, bottom, top);
            Vector2 before = Cursor;
            Cursor = Vector2.MoveTowards(Cursor, want, CursorSpeed * dt);
            Held = true;
            Steering = true;
            if (WantHold && (Cursor - before).sqrMagnitude < 0.25f) HoldStill += dt; else HoldStill = 0f;
        }

        void Decide(GameSim sim)
        {
            var sw = sim.Swarm;
            var col = sim.Colony;
            var cfg = sim.Cfg;
            Vector2 hive = sim.World.HivePos + BeeSwarm.HiveEntranceOffset;
            float cap = cfg.CarryBase * col.CarryMul * sim.Mods.Carry * Mathf.Max(1, sw.SwarmCount);
            var en = sim.Enemies;
            int threat = -1, raider = -1;
            float threatD = 140f * 140f;
            for (int e = 0; e < en.Count; e++)
            {
                if (en.Type[e] == EnemyType.Nest || en.Type[e] == EnemyType.Bear && sw.SwarmCount < 60) continue;
                float d = (en.Pos[e] - sw.LeaderPos).sqrMagnitude;
                if (d < threatD) { threatD = d; threat = e; }
                if (en.Mode[e] == EnemyMode.Raid && (en.Pos[e] - hive).sqrMagnitude < 90f * 90f) raider = e;
            }
            WantHold = false;
            if (threat >= 0 && en.Type[threat] == EnemyType.Hornet && sw.SwarmCount >= 40)
            {
                _worldTarget = en.Pos[threat];
                WantHold = true;
            }
            else if (threat >= 0 && en.Type[threat] == EnemyType.Hornet && sw.SwarmCount < 70)
            {
                Vector2 away = sw.LeaderPos - en.Pos[threat];
                _worldTarget = sw.LeaderPos + away.normalized * 80f;
            }
            else if (threat >= 0 && threatD < 90f * 90f) _worldTarget = en.Pos[threat];
            else if (raider >= 0) _worldTarget = en.Pos[raider];
            else if (sim.Sky.Thirst > 0.6f && sim.World.Ponds.Count > 0)
            {
                Vector2 best = sim.World.Ponds[0];
                for (int i = 1; i < sim.World.Ponds.Count; i++)
                    if ((sim.World.Ponds[i] - sw.LeaderPos).sqrMagnitude < (best - sw.LeaderPos).sqrMagnitude) best = sim.World.Ponds[i];
                _worldTarget = best;
            }
            else if (sw.FieldCarry >= cap * 0.85f || (_delivering && sw.FieldCarry > 0.5f))
            {
                _delivering = true;
                _worldTarget = hive;
            }
            else
            {
                _delivering = false;
                var fl = sim.Flowers;
                if (_flower < 0 || _flower >= fl.Count || !fl.IsBlooming(_flower))
                {
                    _flower = -1;
                    float best = float.MaxValue;
                    for (int f = 0; f < fl.Count; f++)
                    {
                        if (!fl.IsBlooming(f)) continue;
                        if (sw.SquadActive && (fl.Pos[f] - sw.SquadTarget).sqrMagnitude < cfg.PostRadius * cfg.PostRadius) continue; // там работает отряд
                        float d = (fl.Pos[f] - sw.LeaderPos).magnitude + (fl.Pos[f] - hive).magnitude * 0.5f;
                        float score = d / (2f + fl.Nectar[f]);
                        if (score < best) { best = score; _flower = f; }
                    }
                }
                _worldTarget = _flower >= 0 ? fl.Pos[_flower] : hive;
            }
        }

        static int BloomingNear(GameSim sim, Vector2 c, float r)
        {
            var fl = sim.Flowers;
            int n = 0;
            for (int f = 0; f < fl.Count; f++)
                if (fl.IsBlooming(f) && fl.Nectar[f] > 2f && (fl.Pos[f] - c).sqrMagnitude < r * r) n++;
            return n;
        }

        // ------------------------------------------------------------------ улей
        static bool Saving(GameSim sim)
        {
            var sw = sim.Swarm;
            var cfg = sim.Cfg;
            if (cfg.Endless) return false;
            return sw.SwarmCount >= cfg.GoalBees
                   || (sim.TimeLeft < 120f && sw.SwarmCount >= cfg.Winter2Bees)
                   || (sim.Time > 420f && sw.SwarmCount >= cfg.GoalBees - 15 && sim.Colony.Honey < cfg.GoalHoney);
        }

        static int WantBrood(GameSim sim) => Saving(sim) ? 0 : 4;

        static int WantDefenders(GameSim sim) => sim.Time > 250f ? 12 : (sim.Time > 90f ? 6 : 0);

        /// <summary>Следующая кнопка в меню улья; 39 — «В ПОЛЁТ!», когда делать больше нечего.</summary>
        static int NextHiveButton(GameSim sim)
        {
            var sw = sim.Swarm;
            var col = sim.Colony;
            bool saving = Saving(sim);
            if (sim.BroodIndex != WantBrood(sim)) return 31;
            if (sw.SwarmCount + 3 >= col.MaxBees && sim.CanUpgrade(UpgradeType.Comb)) return 33;
            if (!saving && col.Level[(int)UpgradeType.Carry] < 2 && sim.CanUpgrade(UpgradeType.Carry)) return 34;
            if (!saving && col.Level[(int)UpgradeType.Sting] < 2 && sim.Time > 150f && sim.CanUpgrade(UpgradeType.Sting)) return 35;
            if (!saving && col.Level[(int)UpgradeType.Speed] < 1 && sim.CanUpgrade(UpgradeType.Speed)) return 36;
            if (sw.GuardCount < WantDefenders(sim) && sim.CanHireDefenders && col.Honey > sim.DefenderCost + 40) return 32;
            if (!saving && sim.Time > 40f && sim.CanHireWorkers && col.Honey > sim.WorkerCost + 30) return 37;
            return 39;
        }

        static bool HiveWanted(GameSim sim) => NextHiveButton(sim) != 39;

        static readonly PerkId[] Pref =
        {
            PerkId.SweetNectar, PerkId.ThickStings, PerkId.BigCrop, PerkId.FertileQueen, PerkId.Evasion,
            PerkId.HeatProof, PerkId.SwiftWings, PerkId.IronGuards, PerkId.Scouting
        };

        static int PickPerk(GameSim sim)
        {
            int pick = 0, best = 99;
            for (int k = 0; k < 3; k++)
                for (int r = 0; r < Pref.Length; r++)
                    if (Pref[r] == sim.PerkOffer[k] && r < best) { best = r; pick = k; }
            return pick;
        }

        // ------------------------------------------------------------------ курсор
        public void DrawCursor(PixelUI ui, float time)
        {
            var c = new Color32(255, 240, 160, 235);
            bool pressing = _tap == Tap.Press;
            Spr ring = pressing ? Spr.KRing0 : (WantHold && HoldStill > 0.1f ? Spr.KRing2 : Spr.KRing1);
            ui.Icon(ring, Mathf.Round(Cursor.x), Mathf.Round(Cursor.y), c);
            ui.Icon(Spr.Pixel2, Mathf.Round(Cursor.x), Mathf.Round(Cursor.y), pressing ? PixelUI.Honey : PixelUI.White);
        }
    }
}
