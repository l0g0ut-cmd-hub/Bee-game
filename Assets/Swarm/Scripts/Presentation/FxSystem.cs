using Swarm.Simulation;
using UnityEngine;

namespace Swarm.Presentation
{
    /// <summary>
    /// Частицы, всплывающие числа и «сок» (game feel) на пулах-массивах.
    ///
    /// Правила (по скиллу game-feel): отклик многослойный и короткий; сила — по важности события
    /// (Small/Medium/Large); тряска — через накопительную «травму» (амплитуда = травма², плавный синус,
    /// затухание), только смещение камеры; hit-stop — лишь на крупных убийствах, по реальному времени,
    /// ввод не блокируется; числа «выпрыгивают» с перелётом масштаба.
    /// Частицы ударов/дыма/искр — пикселизированный Kenney Particle Pack (CC0).
    /// </summary>
    public sealed class FxSystem
    {
        public enum Tier : byte { Small, Medium, Large }

        struct Particle
        {
            public Vector2 Pos, Vel;
            public float Life, MaxLife, Z, Vz, Drag;
            public short Spr, SprEnd;    // SprEnd >= 0 — покадровая анимация Spr..SprEnd за жизнь
            public Color32 Col;
            public bool Additive, Flip, Fade;
        }

        struct FloatText
        {
            public Vector2 Pos;
            public float Life;
            public int Value;
            public Color32 Col;
            public byte Icon; // 0 нет, 1 мёд
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

        // --- сок
        public float Trauma;            // 0..1, накапливается от ударов
        public float HitStop;           // сек реального времени, пока симуляция заморожена
        public static bool ShakeEnabled = true;
        float _shakeT;
        bool _sound;
        float _depositAcc, _depositTimer, _stolenAcc, _stolenTimer, _trailTimer, _heatTimer;
        Vector2 _stolenPos;

        public FxSystem(Transform root)
        {
            _alpha = new QuadBatch("L7_Fx", MaxParticles, SpriteAtlas.Alpha, 7, PixelScreen.LayerWorld, root);
            _add = new QuadBatch("L8_FxAdd", MaxParticles, SpriteAtlas.Additive, 8, PixelScreen.LayerWorld, root);
            _text = new QuadBatch("L10_FxText", 600, SpriteAtlas.Alpha, 10, PixelScreen.LayerWorld, root);
        }

        public void Clear() { _pCount = 0; _tCount = 0; Trauma = 0f; HitStop = 0f; _depositAcc = 0f; _stolenAcc = 0f; }

        float R => (float)_rnd.NextDouble();
        float S => (float)_rnd.NextDouble() * 2f - 1f;

        void Emit(Spr spr, Vector2 pos, Vector2 vel, float life, Color32 col, bool additive = false,
                  float z = 0f, float vz = 0f, float drag = 2f, bool fade = true, bool flip = false, Spr sprEnd = Spr.Count)
        {
            if (_pCount >= MaxParticles) return;
            _p[_pCount++] = new Particle
            {
                Pos = pos, Vel = vel, Life = life, MaxLife = life, Z = z, Vz = vz, Drag = drag,
                Spr = (short)spr, SprEnd = (short)(sprEnd == Spr.Count ? -1 : (int)sprEnd),
                Col = col, Additive = additive, Fade = fade, Flip = flip,
            };
        }

        public void Text(Vector2 pos, int value, Color32 col, byte icon)
        {
            if (_tCount >= MaxTexts) return;
            _t[_tCount++] = new FloatText { Pos = pos, Life = 1.1f, Value = value, Col = col, Icon = icon };
        }

        /// <summary>Единый «пакет отклика» по важности события.</summary>
        public void Feedback(Tier tier)
        {
            switch (tier)
            {
                case Tier.Small: AddTrauma(0.12f); Haptics.Pulse(12, 70); break;
                case Tier.Medium: AddTrauma(0.32f); Haptics.Pulse(22, 140); break;
                default: AddTrauma(0.65f); HitStop = Mathf.Max(HitStop, 0.09f); Haptics.Pulse(45, 255, 0f); break;
            }
        }

        void AddTrauma(float a) => Trauma = Mathf.Min(1f, Trauma + a);

        /// <summary>Смещение камеры от тряски: амплитуда ~ травма², плавный синус, а не случайный шум.</summary>
        public Vector2 ShakeOffset()
        {
            if (!ShakeEnabled || Trauma <= 0.001f) return Vector2.zero;
            float k = Trauma * Trauma;
            return new Vector2(Mathf.Sin(_shakeT * 1.7f) * 5f * k, Mathf.Sin(_shakeT * 2.3f + 1f) * 4f * k);
        }

        static readonly Color32 Honey = new Color32(247, 207, 85, 255);
        static readonly Color32 White = new Color32(255, 255, 255, 255);
        static readonly Color32 Red = new Color32(238, 106, 82, 255);
        static readonly Color32 Ink = new Color32(44, 34, 50, 255);
        static readonly Color32 Heat = new Color32(255, 150, 60, 255);

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
                    {
                        bool heat = e.Data == 99;
                        // пчела падает, кувыркаясь, + облачко дыма (от перегрева — оранжевое)
                        // падает на спину на землю (к своей тени), полежит и растает
                        Emit(Spr.BeeDead, e.Pos - new Vector2(0f, 6f), new Vector2(S * 14f, 0f), 1.8f, heat ? new Color32(230, 140, 90, 255) : new Color32(215, 205, 185, 255), z: 6f, vz: 28f, drag: 1f, flip: R < 0.5f);
                        Emit(R < 0.5f ? Spr.KSmokeA : Spr.KSmokeB, e.Pos, new Vector2(S * 6f, 10f), 0.5f, heat ? new Color32(255, 170, 90, 170) : new Color32(255, 255, 255, 140), drag: 3f);
                        if (_sound) Feedback(Tier.Small);
                        audio?.Play(Sfx.BeeDie);
                        break;
                    }
                    case SimEventType.BeeHatched:
                        Emit(Spr.KSpark, e.Pos + new Vector2(S * 6f, S * 4f), new Vector2(S * 10f, 15f + R * 10f), 0.6f, Honey, true);
                        if (e.Data == 0)
                        {
                            Emit(Spr.KRing0, e.Pos, Vector2.zero, 0.35f, Honey, true, drag: 0f, sprEnd: Spr.KRing2);
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
                            Emit(Spr.KSpark, e.Pos + new Vector2(S * 5f, S * 5f), new Vector2(0f, 8f), 0.5f + R * 0.4f, White, true);
                        break;
                    case SimEventType.FlowerDepleted:
                        for (int k = 0; k < 5; k++)
                            Emit(Spr.Petal, e.Pos, new Vector2(S * 16f, S * 10f), 0.8f + R * 0.5f, new Color32(255, 240, 220, 230), z: 3f, vz: 12f, drag: 2.5f);
                        break;
                    case SimEventType.EnemyWindup:
                        // телеграф: красный крест-искра над врагом и короткий сигнал
                        Emit(Spr.KCross, e.Pos + new Vector2(0f, 12f), new Vector2(0f, 6f), 0.5f, Red, true, drag: 1f);
                        audio?.Play(Sfx.Windup);
                        break;
                    case SimEventType.EnemyHit:
                        Emit(Spr.KCross, e.Pos + new Vector2(S * 4f, S * 4f), Vector2.zero, 0.12f, White, true, drag: 0f);
                        audio?.Play(Sfx.Hit);
                        break;
                    case SimEventType.EnemyKilled:
                    {
                        bool queenK = e.Data == (int)EnemyType.Queen;
                        bool hornet = e.Data == (int)EnemyType.Hornet || queenK;
                        bool cooked = e.Value > 0.5f;
                        Emit(Spr.KRing0, e.Pos, Vector2.zero, hornet ? 0.4f : 0.3f, White, true, drag: 0f, sprEnd: Spr.KRing2);
                        Emit(Spr.KBurst, e.Pos, Vector2.zero, 0.18f, cooked ? Heat : Honey, true, drag: 0f);
                        // «пуф»: облачка разлетаются кольцом и тают (приём из pixelfx)
                        int puffs = queenK ? 10 : hornet ? 6 : 4;
                        for (int k = 0; k < puffs; k++)
                        {
                            float an = k * (6.2832f / puffs) + R;
                            Emit(R < 0.5f ? Spr.KSmokeA : Spr.KSmokeB, e.Pos, new Vector2(Mathf.Cos(an), Mathf.Sin(an)) * (hornet ? 34f : 24f), 0.45f, new Color32(240, 232, 220, 170), drag: 5f);
                        }
                        Emit(Spr.KSlash, e.Pos + new Vector2(0f, 2f), new Vector2(0f, 4f), 0.22f, White, true, drag: 0f, flip: R < 0.5f);
                        int n = hornet ? 16 : 9;
                        for (int k = 0; k < n; k++)
                        {
                            Color32 c = k % 3 == 0 ? Ink : (k % 3 == 1 ? (cooked ? Heat : Honey) : new Color32(210, 230, 255, 200));
                            Emit(k % 2 == 0 ? Spr.Pixel2 : Spr.Pixel, e.Pos, new Vector2(S, S) * (hornet ? 55f : 40f), 0.5f + R * 0.4f, c, z: 2f, vz: 25f + R * 20f, drag: 2.5f);
                        }
                        for (int k = 0; k < (hornet ? 3 : 1); k++)
                            Emit(Spr.KSmokeB, e.Pos + new Vector2(S * 5f, S * 3f), new Vector2(S * 8f, 12f), 0.6f, new Color32(255, 255, 255, 120), drag: 2f);
                        Emit(queenK ? Spr.QueenA : hornet ? Spr.HornetA : Spr.WaspA, e.Pos, new Vector2(S * 10f, 0f), 1.0f, new Color32(150, 140, 130, 255), z: 3f, vz: 35f, drag: 1f, flip: R < 0.5f);
                        Feedback(hornet ? Tier.Large : Tier.Medium);
                        audio?.Play(hornet ? Sfx.KillBig : Sfx.Kill);
                        break;
                    }
                    case SimEventType.BossIncoming:
                        Feedback(Tier.Large);
                        audio?.Play(Sfx.Alarm);
                        break;
                    case SimEventType.BossPhase:
                        for (int k = 0; k < 3; k++)
                            Emit(Spr.KRing0, e.Pos, Vector2.zero, 0.5f + k * 0.15f, new Color32(238, 90, 70, 255), true, drag: 0f, sprEnd: Spr.KRing2);
                        Feedback(Tier.Large);
                        audio?.Play(Sfx.Alarm);
                        break;
                    case SimEventType.BossKilled:
                        for (int k = 0; k < 4; k++)
                            Emit(Spr.KRing0, e.Pos, Vector2.zero, 0.5f + k * 0.2f, Honey, true, drag: 0f, sprEnd: Spr.KRing2);
                        for (int k = 0; k < 24; k++)
                            Emit(Spr.HoneyDrop, e.Pos, new Vector2(S, S) * 60f, 0.9f + R * 0.4f, White, z: 4f, vz: 40f + R * 30f, drag: 2f);
                        Text(e.Pos + new Vector2(0f, 16f), Mathf.RoundToInt(e.Value), Honey, 1);
                        Feedback(Tier.Large);
                        audio?.Play(Sfx.Win);
                        break;
                    case SimEventType.FocusSet:
                        Emit(Spr.KRing0, e.Pos, Vector2.zero, 0.35f, e.Value > 0.5f ? new Color32(255, 240, 160, 255) : new Color32(200, 200, 200, 160), true, drag: 0f, sprEnd: Spr.KRing2);
                        audio?.Play(Sfx.Click);
                        break;
                    case SimEventType.BuffTaken:
                    {
                        bool poppy = e.Data == (int)FlowerKind.Poppy;
                        var bc = poppy ? new Color32(255, 110, 90, 255) : new Color32(190, 150, 255, 255);
                        for (int k = 0; k < 10; k++)
                            Emit(Spr.KSpark, e.Pos + new Vector2(S * 6f, S * 4f), new Vector2(S * 12f, 20f + R * 10f), 0.7f, bc, true);
                        Emit(Spr.KRing0, e.Pos, Vector2.zero, 0.4f, bc, true, drag: 0f, sprEnd: Spr.KRing2);
                        audio?.Play(Sfx.Upgrade, 0.7f);
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
                        if (_sound) Haptics.Pulse(60, 180, 0f);
                        break;
                    case SimEventType.BeeHurt:
                        Emit(Spr.KCross, e.Pos, new Vector2(0f, 10f), 0.25f, new Color32(255, 120, 100, 255), true, drag: 1f);
                        break;
                    case SimEventType.FlowerSprouted:
                        for (int k = 0; k < 5; k++)
                            Emit(Spr.Leaf, e.Pos + new Vector2(S * 4f, -10f), new Vector2(S * 10f, 14f + R * 8f), 0.6f, White, drag: 2f);
                        audio?.Play(Sfx.Sprout, e.Value > 0.5f ? 1.3f : 1f);
                        break;
                    case SimEventType.ScoutDance:
                        // разведчица «станцевала»: метка на карте + золотая вспышка
                        Emit(Spr.KRing0, e.Pos, Vector2.zero, 0.6f, Honey, true, drag: 0f, sprEnd: Spr.KRing2);
                        for (int k = 0; k < 8; k++)
                            Emit(Spr.KStar, e.Pos, new Vector2(S, S) * 30f, 0.7f, Honey, true, drag: 2f);
                        Text(e.Pos + new Vector2(0f, 14f), 0, Honey, 2);
                        audio?.Play(Sfx.Sprout, 1.5f);
                        if (_sound) Haptics.Pulse(15, 90);
                        break;
                    case SimEventType.BirdIncoming:
                        audio?.Play(Sfx.Bird);
                        break;
                    case SimEventType.BirdStrike:
                        for (int k = 0; k < 10; k++)
                            Emit(k % 2 == 0 ? Spr.Petal : Spr.KSmokeA, e.Pos + new Vector2(S * 8f, S * 6f), new Vector2(S, S) * 40f, 0.6f, k % 2 == 0 ? new Color32(80, 190, 180, 255) : new Color32(255, 255, 255, 140), z: 2f, vz: 20f, drag: 2.5f);
                        Feedback(e.Value >= 3 ? Tier.Large : Tier.Medium);
                        audio?.Play(Sfx.Swipe, 1.3f);
                        break;
                    case SimEventType.WebTorn:
                        for (int k = 0; k < 10; k++)
                            Emit(Spr.Pixel, e.Pos + new Vector2(S * 10f, S * 10f), new Vector2(S, S) * 30f, 0.7f, new Color32(235, 240, 245, 220), drag: 2f);
                        if (_sound) Feedback(Tier.Small);
                        break;
                    case SimEventType.BearSwipe:
                        Emit(Spr.KSlash, e.Pos + new Vector2(S * 6f, 4f), Vector2.zero, 0.25f, new Color32(255, 230, 200, 255), true, drag: 0f, flip: R < 0.5f);
                        Emit(Spr.KSmokeB, e.Pos + new Vector2(0f, -8f), new Vector2(S * 10f, 6f), 0.5f, new Color32(200, 170, 130, 150), drag: 2f);
                        Feedback(Tier.Medium);
                        audio?.Play(Sfx.Swipe);
                        break;
                    case SimEventType.BearRepelled:
                        Feedback(Tier.Large);
                        audio?.Play(Sfx.Upgrade, 0.8f);
                        break;
                    case SimEventType.NestDestroyed:
                        Emit(Spr.KBurst, e.Pos, Vector2.zero, 0.3f, Honey, true, drag: 0f);
                        Emit(Spr.KRing0, e.Pos, Vector2.zero, 0.5f, White, true, drag: 0f, sprEnd: Spr.KRing2);
                        for (int k = 0; k < 20; k++)
                            Emit(k % 2 == 0 ? Spr.Pixel2 : Spr.Petal, e.Pos, new Vector2(S, S) * 60f, 0.8f, new Color32(200, 195, 185, 255), z: 3f, vz: 30f, drag: 2f);
                        Feedback(Tier.Large);
                        audio?.Play(Sfx.KillBig, 0.8f);
                        break;
                    case SimEventType.LindenBloom:
                        for (int k = 0; k < 12; k++)
                            Emit(Spr.KSpark, e.Pos + new Vector2(S * 24f, S * 18f), new Vector2(0f, 10f), 0.9f, Honey, true, drag: 1f);
                        audio?.Play(Sfx.Sprout, 0.8f);
                        break;
                    case SimEventType.DefendersHired:
                        for (int k = 0; k < 10; k++)
                            Emit(Spr.KSpark, e.Pos + new Vector2(S * 10f, S * 6f), new Vector2(S * 10f, 16f), 0.6f, e.Data == 1 ? new Color32(200, 240, 160, 255) : new Color32(255, 150, 120, 255), true);
                        audio?.Play(Sfx.Hatch, 0.8f);
                        break;
                    case SimEventType.NightFall:
                        audio?.Play(Sfx.Night);
                        break;
                    case SimEventType.Dawn:
                        audio?.Play(Sfx.Sprout, 0.7f);
                        break;
                    case SimEventType.Upgrade:
                        for (int k = 0; k < 14; k++)
                            Emit(k % 3 == 0 ? Spr.KStar : Spr.KSpark, e.Pos + new Vector2(S * 16f, R * 30f), new Vector2(S * 12f, 18f + R * 16f), 0.8f + R * 0.4f, k % 2 == 0 ? Honey : White, true);
                        audio?.Play(Sfx.Upgrade);
                        if (_sound) Feedback(Tier.Small);
                        break;
                }
            }
            ev.Clear();
        }

        /// <summary>Непрерывные эффекты от состояния мира: шар, рывки, шлейф пыльцы.</summary>
        void Ambient(GameSim sim, float dt)
        {
            var sw = sim.Swarm;
            var en = sim.Enemies;
            // тепловой шар: языки пламени и завихрения жара над целью
            _heatTimer -= dt;
            if (sw.Balling && sw.BallTarget >= 0 && sw.BallTarget < en.Count && _heatTimer <= 0f)
            {
                _heatTimer = 0.05f;
                Vector2 c = en.Pos[sw.BallTarget];
                float heat = en.Heat[sw.BallTarget];
                Emit(Spr.KBurst, c + new Vector2(S * 8f, S * 4f), new Vector2(S * 4f, 22f + R * 10f), 0.35f, new Color32(255, (byte)(200 - 80 * heat), 70, (byte)(150 + 80 * heat)), true, drag: 1f);
                if (R < 0.4f) Emit(Spr.KTwirl, c + new Vector2(S * 6f, 6f), new Vector2(0f, 14f), 0.4f, new Color32(255, 190, 110, 150), true, drag: 1f, flip: R < 0.5f);
                if (_sound && R < 0.25f) AudioDirector.Instance?.Play(Sfx.Heat);
            }
            // рывок врага: штрихи скорости позади
            for (int i = 0; i < en.Count; i++)
                if (en.Act[i] == EnemyAct.Dash && R < dt * 30f)
                    Emit(Spr.KTrace, en.Pos[i] - en.Vel[i] * 0.04f + new Vector2(S * 3f, S * 3f), Vector2.zero, 0.15f, new Color32(255, 255, 255, 170), true, drag: 0f);
            // шлейф пыльцы за гружёным роем в полёте
            _trailTimer -= dt;
            if (sw.Count > 0 && sw.Speed01 > 0.4f && _trailTimer <= 0f)
            {
                _trailTimer = 0.06f;
                int b = _rnd.Next(sw.Count);
                if (sw.Carry[b] > 0.3f)
                    Emit(Spr.Pollen, sw.Pos[b], -sw.Vel[b] * 0.2f + new Vector2(0f, -4f), 0.6f, new Color32(255, 240, 170, 220), drag: 2f);
            }
        }

        public void Update(GameSim sim, float dt)
        {
            float rdt = Time.unscaledDeltaTime;
            if (HitStop > 0f) HitStop -= Mathf.Min(rdt, 0.1f);
            Trauma = Mathf.Max(0f, Trauma - 1.4f * dt);
            _shakeT += dt * 30f;
            if (dt <= 0f) return;

            Ambient(sim, dt);

            // агрегируем «+мёд» над ульем, чтобы не спамить числами
            _depositTimer -= dt;
            if (_depositAcc >= 1f && _depositTimer <= 0f)
            {
                Vector2 hive = sim.World.HivePos + BeeSwarm.HiveEntranceOffset;
                Text(hive + new Vector2(0f, 26f), Mathf.RoundToInt(_depositAcc), Honey, 1);
                _depositAcc -= Mathf.Round(_depositAcc);
                _depositTimer = 0.45f;
                if (_sound) { AudioDirector.Instance?.Play(Sfx.Deposit); Haptics.Pulse(8, 50); }
            }
            _stolenTimer -= dt;
            if (_stolenAcc >= 1f && _stolenTimer <= 0f)
            {
                Text(_stolenPos + new Vector2(0f, 12f), -Mathf.RoundToInt(_stolenAcc), Red, 1);
                _stolenAcc -= Mathf.Round(_stolenAcc);
                _stolenTimer = 0.6f;
            }

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
                float age = 1f - p.Life / p.MaxLife;
                float k = p.Fade ? Mathf.Clamp01(p.Life / p.MaxLife * 1.6f) : 1f;
                var c = p.Col;
                c.a = (byte)(c.a * k);
                var b = p.Additive ? _add : _alpha;
                int spr = p.Spr;
                if (p.SprEnd >= 0) spr = Mathf.Min(p.SprEnd, p.Spr + (int)(age * (p.SprEnd - p.Spr + 1)));
                b.Add(spr, p.Pos.x, p.Pos.y + p.Z, c, p.Flip);
            }
            for (int i = 0; i < _tCount; i++)
            {
                ref var t = ref _t[i];
                var c = t.Col;
                c.a = (byte)(255 * Mathf.Clamp01(t.Life * 2.5f));
                // «поп»: первые доли секунды число вдвое крупнее, потом оседает
                int scale = t.Life > 0.98f ? 2 : 1;
                _buf.Clear();
                if (t.Icon == 2) _buf.Append('!');
                else { if (t.Value >= 0) _buf.Append('+'); _buf.Append(t.Value); }
                int w = PixelFont.Measure(_buf, scale);
                float x = t.Pos.x - (w + (t.Icon == 1 ? 9 : 0)) * 0.5f;
                PixelFont.Draw(_text, _buf, x, t.Pos.y, c, scale);
                if (t.Icon == 1) _text.Add(Spr.IconHoney, x + w + 5f, t.Pos.y + 3f * scale, new Color32(255, 255, 255, c.a));
            }
            _alpha.End(); _add.End(); _text.End();
        }
    }
}
