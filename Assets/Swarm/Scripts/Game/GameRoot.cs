using System.Collections.Generic;
using Swarm.Presentation;
using Swarm.Simulation;
using Swarm.UI;
using UnityEngine;

namespace Swarm.Game
{
    /// <summary>
    /// Точка сборки: состояния игры, фиксированный шаг симуляции, ввод, камера, связка
    /// симуляции с рендером/FX/звуком/UI. Вся сцена собирается из кода — в сцене ничего нет.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        enum State { Menu, Levels, Achievements, Playing, Hive, Perk, Paused, Result }

        public static GameRoot Instance { get; private set; }

        const float Step = 1f / 60f;
        const int MaxStepsPerFrame = 4;

        PixelScreen _screen;
        WorldRenderer _world;
        FxSystem _fx;
        PixelUI _ui;
        GameUI _gameUi;
        TouchInput _input;
        AudioDirector _audio;

        SimConfig _cfg;
        GameSim _sim;       // текущая партия
        GameSim _attract;   // «витрина» в меню
        State _state;
        float _acc;
        float _time;
        float _animTime;   // время анимаций мира: почти стоит на паузе
        Vector2 _cam;
        float _fade = 1f;
        Profile _profile;
        GameMode _mode;
        int _level;
        float _best;
        bool _newRecord;
        List<Achievement> _fresh;
        float _threat;
        bool _resultSoundPlayed;

        // удержание пальца: почти неподвижен дольше HoldTime → тепловой шар у врага
        const float HoldTime = 0.3f, HoldTolerance = 7f;   // секунды; RT-пиксели
        Vector2 _holdAnchor;
        float _holdTimer;

        // автопилот витрины
        float _autoTimer;
        Vector2 _autoTarget;
        readonly System.Random _rnd = new System.Random();

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.Portrait;
            Input.multiTouchEnabled = true;

            SpriteAtlas.Init();
            _screen = new PixelScreen(transform);
            _world = new WorldRenderer(transform);
            _fx = new FxSystem(transform);
            _ui = new PixelUI(transform);
            _gameUi = new GameUI(_ui);
            _input = new TouchInput();
            _audio = gameObject.AddComponent<AudioDirector>();

            _cfg = SimConfig.Default();
            _attract = new GameSim(_cfg, 4242u) { AttractMode = true };
            _profile = Profile.Load();
            var info = Resources.Load<TextAsset>("BuildInfo");
            _gameUi.VersionText = info != null ? info.text.Trim() : Application.version;
            Haptics.Load();
            FxSystem.ShakeEnabled = PlayerPrefs.GetInt("swarm_shake", 1) == 1;
            EnterMenu();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && _state == State.Playing) _state = State.Paused;
        }

        bool InMenus => _state == State.Menu || _state == State.Levels || _state == State.Achievements;
        GameSim Active => InMenus ? _attract : _sim;

        // ------------------------------------------------------------ переходы
        void EnterMenu()
        {
            _state = State.Menu;
            _attract.Reset();
            _attract.Swarm.ResetLeader(_attract.World.HivePos + new Vector2(0f, -20f));
            _autoTimer = 0f;
            _cam = _attract.Swarm.LeaderPos;
            _fx.Clear();
            _gameUi.ClearBanner();
            _fade = 1f;
        }

        void StartGame(GameMode mode, int level)
        {
            _mode = mode;
            _level = level;
            uint seed = mode == GameMode.Daily ? Levels.DailySeed(System.DateTime.UtcNow) : (uint)_rnd.Next(1, int.MaxValue);
            _cfg = Levels.Make(mode, level);
            _sim = new GameSim(_cfg, seed);
            _fresh = null;
            _best = 0f;
            _state = State.Playing;
            _acc = 0f;
            _cam = _sim.Swarm.LeaderPos;
            _fx.Clear();
            _gameUi.ClearBanner();
            _gameUi.TutorialStep = PlayerPrefs.GetInt("swarm_tutorial_done", 0) == 1 ? 4 : 0;
            _newRecord = false;
            _resultSoundPlayed = false;
            _input.Reset();
            _fade = 1f;
        }

        // ------------------------------------------------------------ кадр
        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _time += dt;
            _animTime += _state == State.Paused ? dt * 0.1f : (_state == State.Hive || _state == State.Perk ? dt * 0.35f : dt);
            _screen.Refresh();
            _input.Poll();
            _ui.Begin(_screen, _input);

            // --- UI текущего состояния (рисуется до шага симуляции, чтобы знать, «съеден» ли тап)
            UiAction act = UiAction.None;
            switch (_state)
            {
                case State.Menu: act = _gameUi.DrawMenu(_time, _profile, _audio.Muted); break;
                case State.Levels: act = _gameUi.DrawLevels(_profile, _time); break;
                case State.Achievements: act = _gameUi.DrawAchievements(_profile); break;
                case State.Perk: act = _gameUi.DrawPerk(_sim, _time); break;
                case State.Playing: act = _gameUi.DrawHud(_sim, _screen, _time, dt); break;
                case State.Hive: act = _gameUi.DrawHive(_sim, _time); break;
                case State.Paused: act = _gameUi.DrawPause(_audio.Muted, Haptics.Enabled, FxSystem.ShakeEnabled); break;
                case State.Result: act = _gameUi.DrawResult(_sim, _time, _newRecord, _best, _fresh); break;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) act = BackAction();
            HandleAction(act);

            // --- симуляция
            var sim = Active;
            if (_state == State.Playing || InMenus)
            {
                SimInput inp = InMenus ? Autopilot(dt) : PlayerInput(dt);
                // hit-stop: симуляция на мгновение замирает (по реальному времени), ввод продолжает читаться
                _acc += _fx.HitStop > 0f ? 0f : dt;
                int steps = 0;
                while (_acc >= Step && steps < MaxStepsPerFrame)
                {
                    sim.Step(Step, inp);
                    _acc -= Step;
                    steps++;
                }
                if (steps == MaxStepsPerFrame) _acc = 0f; // не догоняем после фриза
            }

            // --- события: баннеры волн → UI, остальное → FX и звук
            var ev = sim.Events;
            if (!InMenus)
                for (int i = 0; i < ev.Count; i++) Banner(ev.Items[i]);
            _fx.Consume(sim, _world, InMenus ? null : _audio);
            _fx.Update(sim, _state == State.Paused ? 0f : dt);

            if (_state == State.Playing && sim.Result != SimResult.Running) EnterResult();
            else if (_state == State.Playing && sim.PerkPending) _state = State.Perk;

            // --- камера
            UpdateCamera(sim, dt);

            // --- отрисовка мира
            _world.Draw(sim, _screen, _animTime, _state == State.Paused ? 0f : dt);
            _fx.Draw(_screen.WorldViewRect(16f));

            // --- звук роя
            float spd = sim.Swarm.LeaderVel.magnitude / (_cfg.LeaderSpeed * sim.Colony.SpeedMul);
            _audio.SetSwarm(sim.Swarm.Count, Mathf.Clamp01(spd), InMenus ? 0 : sim.Enemies.Count,
                _state == State.Playing || InMenus);
            _threat = Mathf.MoveTowards(_threat, InMenus ? 0f : Threat(sim), dt * 0.5f);
            _audio.SetMusic(_threat, sim.Sky.IsNight(sim.Time), sim.Sky.Current == Weather.Rain ? sim.Sky.WeatherBlend : 0f, InMenus);

            _fade = Mathf.MoveTowards(_fade, 0f, dt * 2.5f);
            _gameUi.Fade(_fade);
            _ui.End();
        }

        UiAction BackAction()
        {
            switch (_state)
            {
                case State.Playing: return UiAction.Pause;
                case State.Paused: return UiAction.Resume;
                case State.Hive: return UiAction.CloseHive;
                case State.Result: return UiAction.ToMenu;
                case State.Levels: case State.Achievements: return UiAction.Back;
                case State.Perk: return UiAction.None;
                default: Application.Quit(); return UiAction.None;
            }
        }

        void HandleAction(UiAction a)
        {
            switch (a)
            {
                case UiAction.OpenCampaign: _state = State.Levels; break;
                case UiAction.OpenAchievements: _state = State.Achievements; break;
                case UiAction.Back: if (_state == State.Levels || _state == State.Achievements) _state = State.Menu; break;
                case UiAction.StartLevel: StartGame(GameMode.Campaign, _gameUi.SelectedLevel); break;
                case UiAction.PlayEndless: StartGame(GameMode.Endless, 0); break;
                case UiAction.PlayDaily: StartGame(GameMode.Daily, 1); break;
                case UiAction.ChoosePerk:
                    if (_state == State.Perk) { _sim.ChoosePerk(_gameUi.PerkChoice); _state = State.Playing; _input.Reset(); _audio.Play(Sfx.Upgrade); }
                    break;
                case UiAction.Pause: if (_state == State.Playing) _state = State.Paused; break;
                case UiAction.Resume: if (_state == State.Paused) { _state = State.Playing; _input.Reset(); } break;
                case UiAction.Restart: StartGame(_mode, _level); break;
                case UiAction.ToMenu: EnterMenu(); break;
                case UiAction.OpenHive: if (_state == State.Playing) _state = State.Hive; break;
                case UiAction.CloseHive: if (_state == State.Hive) { _state = State.Playing; _input.Reset(); } break;
                case UiAction.PostSquad:
                    if (_state == State.Playing) { if (_sim.Swarm.SquadActive) _sim.RecallSquad(); else _sim.PostSquad(); _audio.Play(Sfx.Click); }
                    break;
                case UiAction.ToggleSound: _audio.ToggleMute(); break;
                case UiAction.ToggleHaptics: Haptics.Toggle(); break;
                case UiAction.ToggleShake:
                    FxSystem.ShakeEnabled = !FxSystem.ShakeEnabled;
                    PlayerPrefs.SetInt("swarm_shake", FxSystem.ShakeEnabled ? 1 : 0);
                    PlayerPrefs.Save();
                    break;
            }
            if (_gameUi.TutorialStep >= 4 && PlayerPrefs.GetInt("swarm_tutorial_done", 0) == 0)
            {
                PlayerPrefs.SetInt("swarm_tutorial_done", 1);
                PlayerPrefs.Save();
            }
        }

        void EnterResult()
        {
            _state = State.Result;
            float prev = _mode == GameMode.Endless ? _profile.EndlessBest
                : _mode == GameMode.Campaign ? _profile.LevelBestTime[_level] : 0f;
            _fresh = _profile.Record(_sim, _mode, _level);
            if (_mode == GameMode.Endless) { _best = _profile.EndlessBest; _newRecord = _sim.Time > prev; }
            else if (_mode == GameMode.Campaign) { _best = _profile.LevelBestTime[_level]; _newRecord = _sim.Result == SimResult.Won && _best < prev - 0.01f || (prev <= 0f && _sim.Result == SimResult.Won); }
            else { _best = 0f; _newRecord = false; }
            if (!_resultSoundPlayed)
            {
                _audio.Play(_sim.Result == SimResult.Won || _sim.Result == SimResult.Wintered || (_mode == GameMode.Endless && _sim.Stars > 0) ? Sfx.Win : Sfx.Lose);
                _resultSoundPlayed = true;
            }
        }

        SimInput PlayerInput(float dt)
        {
            var inp = new SimInput();
            Vector2 px = _screen.ScreenToPixel(_input.ScreenPos);
            if (_input.Held && !_ui.Captured)
            {
                inp.Active = true;
                inp.Target = _screen.PixelToWorld(px);
                if (_input.Down || (px - _holdAnchor).sqrMagnitude > HoldTolerance * HoldTolerance)
                {
                    _holdAnchor = px;
                    _holdTimer = 0f;
                }
                else _holdTimer += dt;
                inp.Hold = _holdTimer >= HoldTime;
            }
            else _holdTimer = 0f;
            return inp;
        }

        /// <summary>Автопилот витрины: рой сам перелетает от цветов к улью.</summary>
        SimInput Autopilot(float dt)
        {
            var s = _attract;
            _autoTimer -= dt;
            if (_autoTimer <= 0f)
            {
                _autoTimer = 5f + (float)_rnd.NextDouble() * 4f;
                float cap = _cfg.CarryBase * s.Colony.CarryMul * Mathf.Max(1, s.Swarm.SwarmCount);
                if (s.Swarm.FieldCarry > cap * 0.7f)
                    _autoTarget = s.World.HivePos + BeeSwarm.HiveEntranceOffset;
                else
                {
                    int best = -1; float bd = float.MaxValue;
                    for (int f = 0; f < s.Flowers.Count; f++)
                    {
                        if (!s.Flowers.IsBlooming(f)) continue;
                        float d = (s.Flowers.Pos[f] - s.Swarm.LeaderPos).magnitude * (0.6f + (float)_rnd.NextDouble());
                        if (d < bd && d > 30f) { bd = d; best = f; }
                    }
                    _autoTarget = best >= 0 ? s.Flowers.Pos[best] : s.World.HivePos;
                }
            }
            return new SimInput { Active = true, Target = _autoTarget };
        }

        void Banner(SimEvent e)
        {
            switch (e.Type)
            {
                case SimEventType.WaveIncoming: _gameUi.ShowWave((WaveMessage)e.Data); break;
                case SimEventType.BearRepelled: _gameUi.ShowBanner(Strings.BearGone, null, PixelUI.Green); break;
                case SimEventType.NestDestroyed: _gameUi.ShowBanner(Strings.NestDown, Strings.NestDownSub, PixelUI.Green); break;
                case SimEventType.LindenBloom: _gameUi.ShowBanner(Strings.LindenTitle, Strings.LindenSub, PixelUI.Honey); break;
                case SimEventType.NightFall: _gameUi.ShowBanner(Strings.NightTitle, Strings.NightSub, new Color32(170, 190, 255, 255)); break;
                case SimEventType.ScoutDance: _gameUi.ShowBanner(Strings.ScoutTitle, Strings.ScoutSub, PixelUI.Honey); break;
                case SimEventType.FlowerSprouted:
                    if (e.Value > 0.5f) _gameUi.ShowBanner(Strings.RareSprout, null, new Color32(255, 230, 120, 255));
                    break;
                case SimEventType.WeatherChanged:
                    switch ((Weather)e.Data)
                    {
                        case Weather.Rain: _gameUi.ShowBanner(Strings.RainTitle, Strings.RainSub, new Color32(150, 190, 240, 255)); break;
                        case Weather.Wind: _gameUi.ShowBanner(Strings.WindTitle, Strings.WindSub, PixelUI.White); break;
                        case Weather.Heat: _gameUi.ShowBanner(Strings.HeatTitle, Strings.HeatSub, PixelUI.Honey); break;
                    }
                    break;
            }
        }

        /// <summary>Оценка угрозы для адаптивной музыки: враги рядом с роем или ульем.</summary>
        static float Threat(GameSim sim)
        {
            var en = sim.Enemies;
            float t = 0f;
            for (int i = 0; i < en.Count; i++)
            {
                if (en.Type[i] == EnemyType.Nest) continue;
                float d = Mathf.Min((en.Pos[i] - sim.Swarm.LeaderPos).magnitude, (en.Pos[i] - sim.World.HivePos).magnitude);
                float w = en.Type[i] == EnemyType.Wasp ? 0.15f : en.Type[i] == EnemyType.Ant ? 0.06f : 0.5f;
                t += w * Mathf.Clamp01(1.4f - d / 160f);
            }
            return Mathf.Clamp01(t);
        }

        void UpdateCamera(GameSim sim, float dt)
        {
            var sw = sim.Swarm;
            Vector2 target = sw.LeaderPos + sw.LeaderVel * 0.45f;
            // в меню рой чуть ниже центра, чтобы не перекрывался логотипом
            if (InMenus) target.y += _screen.Height * 0.18f;
            float k = 1f - Mathf.Exp(-dt * 4f);
            _cam += (target - _cam) * k;
            float hw = _screen.Width * 0.5f, hh = _screen.Height * 0.5f;
            var w = sim.World;
            _cam.x = w.Width > _screen.Width ? Mathf.Clamp(_cam.x, hw - 8f, w.Width - hw + 8f) : w.Width * 0.5f;
            _cam.y = w.Height > _screen.Height ? Mathf.Clamp(_cam.y, hh - 24f, w.Height - hh + 24f) : w.Height * 0.5f;
            _screen.PlaceWorldCamera(_cam + _fx.ShakeOffset());
        }
    }
}
