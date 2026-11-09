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
        enum Tap { None, Move, Press, Focus }
        Tap _tap;
        int _tapId;
        float _tapTimer;
        float _wait;

        // решения — в AutoBrain (та же логика гоняется в стенде)
        public readonly AutoBrain Brain = new AutoBrain();
        float _think;
        float _hiveCooldown, _postCooldown, _perkDelay = -1f;
        float _postAge;
        // двойной тап по врагу (метка-приказ)
        int _focusUid, _focusStep;
        float _focusCd;

        const float CursorSpeed = 260f, TapSpeed = 420f;
        public const float HoldTime = 0.3f;

        public void Reset(PixelScreen screen)
        {
            Cursor = new Vector2(screen.Width * 0.5f, screen.Height * 0.4f);
            _tap = Tap.None;
            _wait = 0.8f;
            Brain.Reset();
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

        public Vector2 WorldTarget => Brain.WorldTarget;

        public void Tick(GameSim sim, AutoContext ctx, PixelUI ui, PixelScreen screen, float dt)
        {
            Down = Up = false;
            Steering = false;
            _hiveCooldown -= dt;
            _postCooldown -= dt;
            _focusCd -= dt;
            if (sim.Swarm.SquadActive) _postAge += dt; else _postAge = 0f;

            if (_tap == Tap.Focus) { RunFocus(sim, screen, dt); return; }
            if (_tap != Tap.None) { RunTap(ui, dt); return; }
            if (_wait > 0f) { _wait -= dt; Held = false; WantHold = false; return; }

            switch (ctx)
            {
                case AutoContext.Perk:
                    if (_perkDelay < 0f) _perkDelay = 1.6f;           // даём прочитать карточки
                    _perkDelay -= dt;
                    if (_perkDelay <= 0f) { _perkDelay = -1f; StartTap(60 + AutoBrain.PickPerk(sim)); }
                    break;
                case AutoContext.Hive:
                    StartTap(AutoBrain.NextHiveButton(sim));
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

        /// <summary>Подвести палец к врагу и дважды коснуться: видно, кого автопилот отметил.</summary>
        void RunFocus(GameSim sim, PixelScreen screen, float dt)
        {
            int fi = sim.Enemies.IndexOfUid(_focusUid);
            if (fi < 0) { _tap = Tap.None; Held = false; return; }
            Vector2 ep = screen.WorldToPixel(sim.Enemies.Pos[fi]);
            Steering = false;
            if (_focusStep == 0)
            {
                Held = false;
                Cursor = Vector2.MoveTowards(Cursor, ep, TapSpeed * 1.4f * dt);
                if ((Cursor - ep).sqrMagnitude < 4f) { _focusStep = 1; _tapTimer = 0.07f; }
                return;
            }
            Cursor = ep;
            _tapTimer -= dt;
            Held = _focusStep == 1 || _focusStep == 3;     // касание — пауза — касание
            if (_tapTimer > 0f) return;
            _focusStep++;
            _tapTimer = 0.07f;
            if (_focusStep >= 4)
            {
                sim.SetFocus(sim.Enemies.Pos[fi]);
                _tap = Tap.None;
                Held = false;
                _wait = 0.15f;
            }
        }

        // ------------------------------------------------------------------ поле
        void PlayField(GameSim sim, PixelUI ui, PixelScreen screen, float dt)
        {
            var sw = sim.Swarm;
            // 1. Улей: зайти, если есть что купить или поменять кладку
            if (sim.SwarmAtHive && sw.FieldCarry < AutoBrain.UnloadedCarry(sim) && _hiveCooldown <= 0f && AutoBrain.HiveWanted(sim) && ui.TryGetButton(20, out _))
            {
                _hiveCooldown = 20f;
                StartTap(20);
                return;
            }
            // 2. Отряд: оставить на богатой поляне / вернуть, когда поляна пуста или отряд мал
            if (sw.SquadActive)
            {
                if (AutoBrain.ShouldRecall(sim, _postAge) && ui.TryGetButton(21, out _))
                {
                    _postCooldown = 25f;
                    StartTap(21);
                    return;
                }
            }
            else if (_postCooldown <= 0f && Brain.ShouldPost(sim) && ui.TryGetButton(21, out _))
            {
                _postCooldown = 45f;
                StartTap(21);
                return;
            }

            // 3. Полёт
            _think -= dt;
            if (_think <= 0f) { _think = 0.25f; Brain.Decide(sim); WantHold = Brain.WantHold; }

            // 4. Метка-приказ: двойной тап по цели, если мозг выбрал её, а метка стоит не там
            if (Brain.WantFocusUid != 0 && Brain.WantFocusUid != sim.FocusUid && _focusCd <= 0f)
            {
                int fi = sim.Enemies.IndexOfUid(Brain.WantFocusUid);
                if (fi >= 0)
                {
                    Vector2 ep = screen.WorldToPixel(sim.Enemies.Pos[fi]);
                    if (ep.x > 10f && ep.x < screen.Width - 10f && ep.y > screen.SafeBottom + 40f && ep.y < screen.Height - screen.SafeTop - 50f)
                    {
                        _tap = Tap.Focus;
                        _focusUid = Brain.WantFocusUid;
                        _focusStep = 0;
                        _tapTimer = 0f;
                        _focusCd = 4f;
                        Held = false;
                        return;
                    }
                }
            }
            Vector2 want = screen.WorldToPixel(Brain.WorldTarget);
            float top = screen.Height - screen.SafeTop - 46f, bottom = screen.SafeBottom + 34f;
            want.x = Mathf.Clamp(want.x, 8f, screen.Width - 8f);
            want.y = Mathf.Clamp(want.y, bottom, top);
            Vector2 before = Cursor;
            Cursor = Vector2.MoveTowards(Cursor, want, CursorSpeed * dt);
            Held = true;
            Steering = true;
            if (WantHold && (Cursor - before).sqrMagnitude < 0.25f) HoldStill += dt; else HoldStill = 0f;
        }

        // ------------------------------------------------------------------ курсор
        public void DrawCursor(PixelUI ui, float time)
        {
            var c = new Color32(255, 240, 160, 235);
            bool pressing = _tap == Tap.Press || (_tap == Tap.Focus && Held);
            Spr ring = pressing ? Spr.KRing0 : (WantHold && HoldStill > 0.1f ? Spr.KRing2 : Spr.KRing1);
            float x = Mathf.Round(Cursor.x), y = Mathf.Round(Cursor.y);
            ui.Icon(ring, x + 1, y - 1, new Color32(23, 18, 28, 160), 2);   // тень — видно на светлых кнопках
            ui.Icon(ring, x, y, c, 2);
            ui.Icon(Spr.Dot3, x, y, pressing ? PixelUI.Honey : PixelUI.White);
        }
    }
}
