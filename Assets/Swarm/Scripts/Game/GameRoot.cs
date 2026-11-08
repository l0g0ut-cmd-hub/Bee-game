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
        enum State { Menu, Playing, Hive, Paused, Result }

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
        float _bestWin;
        bool _newRecord;
        bool _resultSoundPlayed;

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
            _bestWin = PlayerPrefs.GetFloat("swarm_best_win", 0f);
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

        GameSim Active => _state == State.Menu ? _attract : _sim;

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

        void StartGame()
        {
            uint seed = (uint)_rnd.Next(1, int.MaxValue);
            _sim = new GameSim(_cfg, seed);
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
            _animTime += _state == State.Paused ? dt * 0.1f : (_state == State.Hive ? dt * 0.35f : dt);
            _screen.Refresh();
            _input.Poll();
            _ui.Begin(_screen, _input);

            // --- UI текущего состояния (рисуется до шага симуляции, чтобы знать, «съеден» ли тап)
            UiAction act = UiAction.None;
            switch (_state)
            {
                case State.Menu: act = _gameUi.DrawMenu(_time, _bestWin, _audio.Muted); break;
                case State.Playing: act = _gameUi.DrawHud(_sim, _screen, _time, dt); break;
                case State.Hive: act = _gameUi.DrawHive(_sim, _time); break;
                case State.Paused: act = _gameUi.DrawPause(_audio.Muted); break;
                case State.Result: act = _gameUi.DrawResult(_sim, _time, _newRecord, _bestWin); break;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) act = BackAction();
            HandleAction(act);

            // --- симуляция
            var sim = Active;
            if (_state == State.Playing || _state == State.Menu)
            {
                SimInput inp = _state == State.Menu ? Autopilot(dt) : PlayerInput();
                _acc += dt;
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
            for (int i = 0; i < ev.Count; i++)
                if (ev.Items[i].Type == SimEventType.WaveIncoming && _state == State.Playing)
                    _gameUi.ShowWave((WaveMessage)ev.Items[i].Data);
            _fx.Consume(sim, _world, _state == State.Menu ? null : _audio);
            _fx.Update(sim, _state == State.Paused ? 0f : dt);

            if (_state == State.Playing && sim.Result != SimResult.Running) EnterResult();

            // --- камера
            UpdateCamera(sim, dt);

            // --- отрисовка мира
            _world.Draw(sim, _screen, _animTime, _state == State.Paused ? 0f : dt);
            _fx.Draw(_screen.WorldViewRect(16f));

            // --- звук роя
            float spd = sim.Swarm.LeaderVel.magnitude / (_cfg.LeaderSpeed * sim.Colony.SpeedMul);
            _audio.SetSwarm(sim.Swarm.Count, Mathf.Clamp01(spd), _state == State.Menu ? 0 : sim.Enemies.Count,
                _state == State.Playing || _state == State.Menu);

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
                default: Application.Quit(); return UiAction.None;
            }
        }

        void HandleAction(UiAction a)
        {
            switch (a)
            {
                case UiAction.Play: StartGame(); break;
                case UiAction.Pause: if (_state == State.Playing) _state = State.Paused; break;
                case UiAction.Resume: if (_state == State.Paused) { _state = State.Playing; _input.Reset(); } break;
                case UiAction.Restart: StartGame(); break;
                case UiAction.ToMenu: EnterMenu(); break;
                case UiAction.OpenHive: if (_state == State.Playing) _state = State.Hive; break;
                case UiAction.CloseHive: if (_state == State.Hive) { _state = State.Playing; _input.Reset(); } break;
                case UiAction.ToggleSound: _audio.ToggleMute(); break;
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
            if (_sim.Result == SimResult.Won && (_bestWin <= 0f || _sim.Time < _bestWin))
            {
                _bestWin = _sim.Time;
                _newRecord = true;
                PlayerPrefs.SetFloat("swarm_best_win", _bestWin);
                PlayerPrefs.Save();
            }
            if (!_resultSoundPlayed)
            {
                _audio.Play(_sim.Result == SimResult.Won ? Sfx.Win : Sfx.Lose);
                _resultSoundPlayed = true;
            }
        }

        SimInput PlayerInput()
        {
            var inp = new SimInput();
            if (_input.Held && !_ui.Captured)
            {
                inp.Active = true;
                inp.Target = _screen.PixelToWorld(_screen.ScreenToPixel(_input.ScreenPos));
            }
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
                float cap = _cfg.CarryBase * s.Colony.CarryMul * Mathf.Max(1, s.Swarm.Count);
                if (s.Swarm.TotalCarry > cap * 0.7f)
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

        void UpdateCamera(GameSim sim, float dt)
        {
            var sw = sim.Swarm;
            Vector2 target = sw.LeaderPos + sw.LeaderVel * 0.45f;
            // в меню рой чуть ниже центра, чтобы не перекрывался логотипом
            if (_state == State.Menu) target.y += _screen.Height * 0.18f;
            float k = 1f - Mathf.Exp(-dt * 4f);
            _cam += (target - _cam) * k;
            float hw = _screen.Width * 0.5f, hh = _screen.Height * 0.5f;
            var w = sim.World;
            _cam.x = w.Width > _screen.Width ? Mathf.Clamp(_cam.x, hw - 8f, w.Width - hw + 8f) : w.Width * 0.5f;
            _cam.y = w.Height > _screen.Height ? Mathf.Clamp(_cam.y, hh - 24f, w.Height - hh + 24f) : w.Height * 0.5f;
            Vector2 shake = Vector2.zero;
            if (_fx.Shake > 0.05f)
                shake = new Vector2(Mathf.Sin(_time * 71f), Mathf.Cos(_time * 53f)) * _fx.Shake;
            _screen.PlaceWorldCamera(_cam + shake);
        }
    }
}
