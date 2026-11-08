using Swarm.Simulation;
using UnityEngine;

namespace Swarm.Presentation
{
    /// <summary>
    /// Частицы и всплывающие числа на пулах-массивах. Реагирует на события симуляции:
    /// гибель пчёл, сбор и сдачу нектара, удары и смерть врагов, вывод пчёл, кражу мёда.
    /// </summary>
    public sealed class FxSystem
    {
        struct Particle
        {
            public Vector2 Pos, Vel;
            public float Life, MaxLife, Z, Vz, Drag;
            public short Spr;
            public Color32 Col;
            public bool Additive, Flip, Fade;
        }

        struct FloatText
        {
            public Vector2 Pos;
            public float Life;
            public int Value;
            public Color32 Col;
            public byte Icon; // 0 нет, 1 мёд, 2 пчела
        }

        const int MaxParticles = 900;
        const int MaxTexts = 40;
        readonly Particle[] _p = new Particle[MaxParticles];
        int _pCount;
        readonly FloatText[] _t = new FloatText[MaxTexts];
        int _tCount;

        readonly QuadBatch _alpha, _add, _text;
        readonly System.Random _rnd = new System.Random(777);
        readonly TextBuf _buf = new TextBuf();

        public float Shake;
        bool _sound;
        float _depositAcc, _depositTimer, _stolenAcc, _stolenTimer;
        Vector2 _stolenPos;

        public FxSystem(Transform root)
        {
            _alpha = new QuadBatch("L7_Fx", MaxParticles, SpriteAtlas.Alpha, 7, PixelScreen.LayerWorld, root);
            _add = new QuadBatch("L8_FxAdd", MaxParticles, SpriteAtlas.Additive, 8, PixelScreen.LayerWorld, root);
            _text = new QuadBatch("L10_FxText", 600, SpriteAtlas.Alpha, 10, PixelScreen.LayerWorld, root);
        }

        public void Clear() { _pCount = 0; _tCount = 0; Shake = 0f; _depositAcc = 0f; _stolenAcc = 0f; }

        float R => (float)_rnd.NextDouble();
        float S => (float)_rnd.NextDouble() * 2f - 1f;

        void Emit(Spr spr, Vector2 pos, Vector2 vel, float life, Color32 col, bool additive = false,
                  float z = 0f, float vz = 0f, float drag = 2f, bool fade = true, bool flip = false)
        {
            if (_pCount >= MaxParticles) return;
            _p[_pCount++] = new Particle
            {
                Pos = pos, Vel = vel, Life = life, MaxLife = life, Z = z, Vz = vz, Drag = drag,
                Spr = (short)spr, Col = col, Additive = additive, Fade = fade, Flip = flip,
            };
        }

        public void Text(Vector2 pos, int value, Color32 col, byte icon)
        {
            if (_tCount >= MaxTexts) return;
            _t[_tCount++] = new FloatText { Pos = pos, Life = 1.1f, Value = value, Col = col, Icon = icon };
        }

        static readonly Color32 Honey = new Color32(247, 207, 85, 255);
        static readonly Color32 HoneyDeep = new Color32(235, 166, 52, 255);
        static readonly Color32 White = new Color32(255, 255, 255, 255);
        static readonly Color32 Red = new Color32(238, 106, 82, 255);
        static readonly Color32 Ink = new Color32(44, 34, 50, 255);

        public void Consume(GameSim sim, WorldRenderer world, AudioDirector audio)
        {
            _sound = audio != null;
            var ev = sim.Events;
            Vector2 hive = sim.World.HivePos + BeeSwarm.HiveEntranceOffset;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev.Items[i];
                switch (e.Type)
                {
                    case SimEventType.BeeDied:
                        // пчела падает, кувыркаясь, + облачко
                        Emit(Spr.BeeA, e.Pos, new Vector2(S * 20f, 0f), 0.9f, new Color32(200, 190, 170, 255), z: 2f, vz: 30f, drag: 1f, flip: R < 0.5f);
                        for (int k = 0; k < 3; k++)
                            Emit(Spr.Puff, e.Pos, new Vector2(S, S) * 18f, 0.35f + R * 0.2f, new Color32(255, 255, 255, 170), drag: 4f);
                        Shake = Mathf.Max(Shake, 0.6f);
                        audio?.Play(Sfx.BeeDie);
                        break;
                    case SimEventType.BeeHatched:
                        Emit(Spr.Sparkle, e.Pos + new Vector2(S * 6f, S * 4f), new Vector2(S * 10f, 15f + R * 10f), 0.6f, Honey, true);
                        if (e.Data == 0)
                        {
                            Emit(Spr.Ring2, e.Pos, Vector2.zero, 0.35f, Honey, true, drag: 0f);
                            audio?.Play(Sfx.Hatch);
                        }
                        break;
                    case SimEventType.NectarCollected:
                        for (int k = 0; k < 2; k++)
                            Emit(Spr.Pollen, e.Pos + new Vector2(S * 3f, S * 3f), new Vector2(S * 8f, 10f + R * 12f), 0.6f + R * 0.4f, White, drag: 1.5f);
                        audio?.Play(Sfx.Collect);
                        break;
                    case SimEventType.NectarDeposited:
                        _depositAcc += e.Value;
                        if (R < 0.4f) Emit(Spr.HoneyDrop, e.Pos, new Vector2(S * 6f, -10f), 0.5f, White, drag: 1f);
                        world.NotifyDeposit();
                        break;
                    case SimEventType.FlowerBloomed:
                        for (int k = 0; k < 4; k++)
                            Emit(Spr.Sparkle, e.Pos + new Vector2(S * 5f, S * 5f), new Vector2(0f, 8f), 0.5f + R * 0.4f, White, true);
                        break;
                    case SimEventType.FlowerDepleted:
                        for (int k = 0; k < 5; k++)
                            Emit(Spr.Petal, e.Pos, new Vector2(S * 16f, S * 10f), 0.8f + R * 0.5f, new Color32(255, 240, 220, 230), z: 3f, vz: 12f, drag: 2.5f);
                        break;
                    case SimEventType.EnemySpawned:
                        break;
                    case SimEventType.EnemyHit:
                        Emit(Spr.Ring0, e.Pos + new Vector2(S * 4f, S * 4f), Vector2.zero, 0.15f, White, true, drag: 0f);
                        audio?.Play(Sfx.Hit);
                        break;
                    case SimEventType.EnemyKilled:
                    {
                        bool hornet = e.Data == (int)EnemyType.Hornet;
                        Emit(Spr.Ring2, e.Pos, Vector2.zero, 0.3f, White, true, drag: 0f);
                        Emit(Spr.Ring1, e.Pos, Vector2.zero, 0.2f, Honey, true, drag: 0f);
                        int n = hornet ? 16 : 9;
                        for (int k = 0; k < n; k++)
                        {
                            Color32 c = k % 3 == 0 ? Ink : (k % 3 == 1 ? Honey : new Color32(210, 230, 255, 200));
                            Emit(k % 2 == 0 ? Spr.Pixel2 : Spr.Pixel, e.Pos, new Vector2(S, S) * (hornet ? 55f : 40f), 0.5f + R * 0.4f, c, z: 2f, vz: 25f + R * 20f, drag: 2.5f);
                        }
                        Emit(hornet ? Spr.HornetA : Spr.WaspA, e.Pos, new Vector2(S * 10f, 0f), 1.0f, new Color32(150, 140, 130, 255), z: 3f, vz: 35f, drag: 1f, flip: R < 0.5f);
                        Shake = Mathf.Max(Shake, hornet ? 2.5f : 1.2f);
                        audio?.Play(hornet ? Sfx.KillBig : Sfx.Kill);
                        break;
                    }
                    case SimEventType.HoneyStolen:
                        _stolenAcc += e.Value;
                        _stolenPos = e.Pos;
                        Emit(Spr.HoneyDrop, hive, (e.Pos - hive).normalized * 30f, 0.5f, White, drag: 0.5f);
                        break;
                    case SimEventType.HoneyRecovered:
                        Text(e.Pos + new Vector2(0f, 10f), Mathf.RoundToInt(e.Value), Honey, 1);
                        for (int k = 0; k < 6; k++)
                            Emit(Spr.HoneyDrop, e.Pos, new Vector2(S, S) * 25f, 0.7f, White, z: 2f, vz: 20f, drag: 2f);
                        audio?.Play(Sfx.Deposit);
                        break;
                    case SimEventType.WaveIncoming:
                        audio?.Play(Sfx.Alarm);
                        break;
                    case SimEventType.Upgrade:
                        for (int k = 0; k < 14; k++)
                            Emit(Spr.Sparkle, e.Pos + new Vector2(S * 16f, R * 30f), new Vector2(S * 12f, 18f + R * 16f), 0.8f + R * 0.4f, k % 2 == 0 ? Honey : White, true);
                        audio?.Play(Sfx.Upgrade);
                        break;
                }
            }
            ev.Clear();
        }

        public void Update(GameSim sim, float dt)
        {
            // агрегируем «+мёд» над ульем, чтобы не спамить числами
            _depositTimer -= dt;
            if (_depositAcc >= 1f && _depositTimer <= 0f)
            {
                Vector2 hive = sim.World.HivePos + BeeSwarm.HiveEntranceOffset;
                Text(hive + new Vector2(0f, 26f), Mathf.RoundToInt(_depositAcc), Honey, 1);
                _depositAcc -= Mathf.Round(_depositAcc);
                _depositTimer = 0.45f;
                if (_sound) AudioDirector.Instance?.Play(Sfx.Deposit);
            }
            _stolenTimer -= dt;
            if (_stolenAcc >= 1f && _stolenTimer <= 0f)
            {
                Text(_stolenPos + new Vector2(0f, 12f), -Mathf.RoundToInt(_stolenAcc), Red, 1);
                _stolenAcc -= Mathf.Round(_stolenAcc);
                _stolenTimer = 0.6f;
            }

            Shake = Mathf.MoveTowards(Shake, 0f, dt * 6f);

            int w = 0;
            for (int i = 0; i < _pCount; i++)
            {
                var p = _p[i];
                p.Life -= dt;
                if (p.Life <= 0f) continue;
                p.Pos += p.Vel * dt;
                p.Vel *= Mathf.Max(0f, 1f - p.Drag * dt);
                if (p.Z > 0f || p.Vz != 0f)
                {
                    p.Vz -= 140f * dt;
                    p.Z += p.Vz * dt;
                    if (p.Z < 0f) { p.Z = 0f; p.Vz = 0f; p.Vel *= 0.3f; }
                }
                _p[w++] = p;
            }
            _pCount = w;

            w = 0;
            for (int i = 0; i < _tCount; i++)
            {
                var t = _t[i];
                t.Life -= dt;
                if (t.Life <= 0f) continue;
                t.Pos.y += dt * 14f * Mathf.Min(1f, t.Life * 2f);
                _t[w++] = t;
            }
            _tCount = w;
        }

        public void Draw(Rect view)
        {
            _alpha.Begin(); _add.Begin(); _text.Begin();
            for (int i = 0; i < _pCount; i++)
            {
                ref var p = ref _p[i];
                if (!view.Contains(p.Pos)) continue;
                float k = p.Fade ? Mathf.Clamp01(p.Life / p.MaxLife * 1.6f) : 1f;
                var c = p.Col;
                c.a = (byte)(c.a * k);
                var b = p.Additive ? _add : _alpha;
                // кольца «растут»: кадр Ring0 -> Ring1 -> Ring2 по ходу жизни
                int spr = p.Spr;
                if (spr >= (int)Spr.Ring0 && spr <= (int)Spr.Ring2)
                {
                    float age = 1f - p.Life / p.MaxLife;
                    spr = Mathf.Min((int)Spr.Ring2, spr + (int)(age * 2.5f));
                }
                b.Add(spr, p.Pos.x, p.Pos.y + p.Z, c, p.Flip);
            }
            for (int i = 0; i < _tCount; i++)
            {
                ref var t = ref _t[i];
                var c = t.Col;
                c.a = (byte)(255 * Mathf.Clamp01(t.Life * 2.5f));
                _buf.Clear();
                if (t.Value >= 0) _buf.Append('+');
                _buf.Append(t.Value);
                int w = PixelFont.Measure(_buf);
                float x = t.Pos.x - (w + (t.Icon != 0 ? 9 : 0)) * 0.5f;
                PixelFont.Draw(_text, _buf, x, t.Pos.y, c);
                if (t.Icon == 1) _text.Add(Spr.IconHoney, x + w + 5f, t.Pos.y + 3f, new Color32(255, 255, 255, c.a));
            }
            _alpha.End(); _add.End(); _text.End();
        }
    }
}
