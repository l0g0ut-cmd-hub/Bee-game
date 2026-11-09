using System.Collections.Generic;
using Swarm.Simulation;
using UnityEngine;

namespace Swarm.Presentation
{
    /// <summary>
    /// Отрисовка мира набором QuadBatch-слоёв (по одному draw call на слой):
    /// земля и плоский декор — статичные меши; трава/цветы/улей/камни — y-сортированный слой
    /// с ветром; пчёлы и враги; кроны деревьев с затуханием над роем; живое окружение.
    /// </summary>
    public sealed class WorldRenderer
    {
        enum StandKind : byte { Tuft, TallGrass, Mushroom, Rock, Bush, Stump, Trunk, Flower, Hive }

        struct StandItem
        {
            public StandKind Kind;
            public byte Variant;
            public short Index;
            public Vector2 Pos;
        }

        struct Butterfly { public Vector2 Pos, Vel, Target; public Color32 Tint; public float Phase; }
        struct Mote { public Vector2 Pos, Vel; public float Life, Phase; }

        // порядок сортировки слоёв
        const int OrderGround = 0, OrderFlat = 1, OrderShadow = 2, OrderStand = 3, OrderFly = 4,
            OrderCanopy = 5, OrderCloud = 6, OrderOverlay = 9;

        readonly QuadBatch _ground, _flat, _shadow, _stand, _fly, _canopy, _cloud, _overlay, _glow;
        readonly List<StandItem> _standing = new List<StandItem>(1024);
        readonly List<int> _trees = new List<int>(32);
        float[] _canopyAlpha = new float[0];

        readonly Butterfly[] _butterflies = new Butterfly[7];
        readonly Mote[] _motes = new Mote[56];
        readonly System.Random _rnd = new System.Random(12345);
        float _hiveBounce, _hiveGlow;
        GameSim _builtFor;
        int _flowerSeen;
        int _fogVersion = -1;
        Color32 _tint = new Color32(255, 255, 255, 255);
        readonly QuadBatch _fog, _night, _nightGlow, _weather;
        struct Drop { public Vector2 Pos; public float Life; public Vector2 SplashPos; public float Splash; }
        readonly Drop[] _rain = new Drop[90];
        readonly Mote[] _flies = new Mote[24];

        static readonly Color32 White = new Color32(255, 255, 255, 255);

        public WorldRenderer(Transform root)
        {
            var a = SpriteAtlas.Alpha;
            int L = PixelScreen.LayerWorld;
            _ground = new QuadBatch("L0_Ground", 3200, a, OrderGround, L, root);
            _flat = new QuadBatch("L1_Flat", 900, a, OrderFlat, L, root);
            _shadow = new QuadBatch("L2_Shadows", 700, a, OrderShadow, L, root);
            _stand = new QuadBatch("L3_Standing", 1400, a, OrderStand, L, root);
            _fly = new QuadBatch("L4_Flyers", 700, a, OrderFly, L, root);
            _canopy = new QuadBatch("L5_Canopy", 64, a, OrderCanopy, L, root);
            _cloud = new QuadBatch("L6_Clouds", 8, a, OrderCloud, L, root);
            _glow = new QuadBatch("L8_Glow", 160, SpriteAtlas.Additive, 8, L, root);
            _overlay = new QuadBatch("L9_Overlay", 500, a, OrderOverlay, L, root);
            _fog = new QuadBatch("L11_Fog", 2600, a, 11, L, root);
            _weather = new QuadBatch("L12_Weather", 200, a, 12, L, root);
            _night = new QuadBatch("L13_Night", 4, a, 13, L, root);
            _nightGlow = new QuadBatch("L14_NightGlow", 120, SpriteAtlas.Additive, 14, L, root);
        }

        static Color32 A(byte a) => new Color32(255, 255, 255, a);

        // ------------------------------------------------------------ построение статики
        public void Build(GameSim sim)
        {
            _builtFor = sim;
            var world = sim.World;
            _tint = world.Biome == Biome.Garden ? new Color32(255, 246, 222, 255)
                  : world.Biome == Biome.Swamp ? new Color32(186, 212, 196, 255) : White;
            _fogVersion = -1;
            var rng = new Rng(world.Seed * 2654435761u + 17u);

            // земля: тайлы 16x16 с вариациями
            _ground.Begin();
            int cols = Mathf.CeilToInt(world.Width / 16f) + 2, rows = Mathf.CeilToInt(world.Height / 16f) + 2;
            for (int y = -1; y < rows; y++)
                for (int x = -1; x < cols; x++)
                {
                    int v = rng.Range(0, 100);
                    int tile = v < 40 ? 0 : v < 62 ? 1 : v < 78 ? 2 : v < 88 ? 3 : v < 95 ? 4 : 5;
                    _ground.Add((int)Spr.Grass0 + tile, x * 16, y * 16, _tint);
                }
            _ground.End();

            // плоский декор: пятна, поляна у улья, тени деревьев, мелкие цветы
            _flat.Begin();
            for (int i = 0; i < 26; i++)
            {
                var p = RandPos(rng, world, 0f);
                Spr s = i % 3 == 0 ? Spr.PatchLight : (i % 3 == 1 ? Spr.PatchDark : Spr.PatchMoss);
                _flat.Add(s, p.x, p.y, White, rng.Value < 0.5f);
            }
            for (int i = 0; i < 7; i++)
            {
                var p = RandPos(rng, world, 0f);
                if ((p - world.HivePos).magnitude < 80f) continue;
                _flat.Add(Spr.PatchDirt, p.x, p.y, White, rng.Value < 0.5f);
            }
            _flat.Add(Spr.Clearing, world.HivePos.x, world.HivePos.y + 4f, White);
            foreach (var pd in world.Ponds) _flat.Add(Spr.Pond, pd.x, pd.y, White);
            foreach (var o in world.Obstacles)
            {
                if (o.Kind == ObstacleKind.Tree)
                    _flat.AddStretched((int)Spr.ShadowXL, o.Pos.x - 26f, o.Pos.y - 11f, 58f, 16f, new Color32(255, 255, 255, 80));
                else if (o.Kind == ObstacleKind.Bush)
                    _flat.Add(Spr.ShadowL, o.Pos.x + 1f, o.Pos.y - 2f, new Color32(255, 255, 255, 90));
                else
                    _flat.Add(Spr.ShadowM, o.Pos.x + 1f, o.Pos.y - 1f, new Color32(255, 255, 255, 90));
            }
            _flat.Add(Spr.ShadowXL, world.HivePos.x + 2f, world.HivePos.y + 1f, new Color32(255, 255, 255, 100));
            for (int i = 0; i < 170; i++)
            {
                var p = RandPos(rng, world, 6f);
                if (world.InsideObstacle(p, 4f)) continue;
                int r = rng.Range(0, 10);
                Spr s = r < 2 ? Spr.TinyWhite : r < 4 ? Spr.TinyYellow : r < 5 ? Spr.TinyPink : r < 6 ? Spr.TinyBlue : r < 8 ? Spr.Clover : Spr.Pebbles;
                _flat.Add(s, p.x, p.y, White, rng.Value < 0.5f);
            }
            _flat.End();

            // стоячие объекты, отсортированные по Y (дальние рисуются раньше)
            _standing.Clear();
            _trees.Clear();
            for (int i = 0; i < world.Obstacles.Count; i++)
            {
                var o = world.Obstacles[i];
                switch (o.Kind)
                {
                    case ObstacleKind.Tree:
                        _trees.Add(i);
                        _standing.Add(new StandItem { Kind = StandKind.Trunk, Pos = o.Pos + new Vector2(0f, -6f), Index = (short)i });
                        break;
                    case ObstacleKind.Rock:
                        _standing.Add(new StandItem { Kind = StandKind.Rock, Variant = o.Variant, Pos = o.Pos + new Vector2(0f, -4f) });
                        break;
                    case ObstacleKind.Bush:
                        _standing.Add(new StandItem { Kind = StandKind.Bush, Variant = o.Variant, Pos = o.Pos + new Vector2(0f, -6f) });
                        break;
                    case ObstacleKind.Stump:
                        _standing.Add(new StandItem { Kind = StandKind.Stump, Pos = o.Pos + new Vector2(0f, -4f) });
                        break;
                }
            }
            for (int f = 0; f < sim.Flowers.Count; f++)
                _standing.Add(new StandItem { Kind = StandKind.Flower, Index = (short)f, Pos = sim.Flowers.Pos[f] + new Vector2(0f, -13f) });
            _standing.Add(new StandItem { Kind = StandKind.Hive, Pos = world.HivePos });
            for (int i = 0; i < 260; i++)
            {
                var p = RandPos(rng, world, 4f);
                if (world.InsideObstacle(p, 3f)) continue;
                if ((p - world.HivePos).magnitude < 46f) continue;
                StandKind k = i % 5 == 0 ? StandKind.TallGrass : StandKind.Tuft;
                _standing.Add(new StandItem { Kind = k, Pos = p });
            }
            for (int i = 0; i < 14; i++)
            {
                var p = RandPos(rng, world, 8f);
                if (world.InsideObstacle(p, 5f)) continue;
                _standing.Add(new StandItem { Kind = StandKind.Mushroom, Variant = (byte)rng.Range(0, 2), Pos = p });
            }
            _standing.Sort((a, b) => b.Pos.y.CompareTo(a.Pos.y));
            _flowerSeen = sim.Flowers.Count;
            _trees.Sort((a, b) => world.Obstacles[b].Pos.y.CompareTo(world.Obstacles[a].Pos.y));
            _canopyAlpha = new float[_trees.Count];
            for (int i = 0; i < _canopyAlpha.Length; i++) _canopyAlpha[i] = 1f;

            // окружение
            for (int i = 0; i < _butterflies.Length; i++)
            {
                _butterflies[i].Pos = RandPos(rng, world, 20f);
                _butterflies[i].Target = RandPos(rng, world, 20f);
                _butterflies[i].Phase = rng.Value * 10f;
                Color32[] tints = { new Color32(255, 255, 255, 255), new Color32(255, 226, 120, 255), new Color32(255, 170, 210, 255), new Color32(150, 205, 255, 255), new Color32(255, 160, 90, 255) };
                _butterflies[i].Tint = tints[i % tints.Length];
            }
            for (int i = 0; i < _motes.Length; i++) _motes[i].Life = 0f;
        }

        static Vector2 RandPos(Rng rng, WorldMap w, float margin) =>
            new Vector2(rng.Range(margin, w.Width - margin), rng.Range(margin, w.Height - margin));

        public void NotifyDeposit() { _hiveBounce = 0.15f; }

        // ------------------------------------------------------------ кадр
        public void Draw(GameSim sim, PixelScreen screen, float time, float dt)
        {
            if (sim != _builtFor) Build(sim);
            if (sim.Flowers.Count < _flowerSeen) Build(sim);   // новая партия на той же карте
            while (_flowerSeen < sim.Flowers.Count)
            {
                var item = new StandItem { Kind = StandKind.Flower, Index = (short)_flowerSeen, Pos = sim.Flowers.Pos[_flowerSeen] + new Vector2(0f, -13f) };
                int lo = 0, hi = _standing.Count;
                while (lo < hi) { int mid = (lo + hi) >> 1; if (_standing[mid].Pos.y > item.Pos.y) lo = mid + 1; else hi = mid; }
                _standing.Insert(lo, item);
                _flowerSeen++;
            }
            var view = screen.WorldViewRect(48f);
            var world = sim.World;
            var flowers = sim.Flowers;
            var swarm = sim.Swarm;
            float cap = sim.Cfg.CarryBase * sim.Colony.CarryMul;

            _shadow.Begin(); _stand.Begin(); _fly.Begin(); _canopy.Begin(); _cloud.Begin(); _overlay.Begin(); _glow.Begin();

            if (_hiveBounce > 0f) _hiveBounce -= dt;
            bool atHive = sim.SwarmAtHive;
            _hiveGlow = Mathf.MoveTowards(_hiveGlow, atHive ? 1f : 0f, dt * 3f);

            // --- стоячий слой
            for (int k = 0; k < _standing.Count; k++)
            {
                var it = _standing[k];
                if (!view.Contains(it.Pos)) continue;
                float wind = Wind(it.Pos, time) + sim.Sky.Wind.x * 0.06f;
                switch (it.Kind)
                {
                    case StandKind.Tuft:
                        _stand.Add((int)Spr.Tuft0 + (wind > 0.55f ? 2 : wind < -0.55f ? 0 : 1), it.Pos.x, it.Pos.y, White);
                        break;
                    case StandKind.TallGrass:
                        _stand.Add((int)Spr.TallGrass0 + (wind > 0.4f ? 2 : wind < -0.4f ? 0 : 1), it.Pos.x, it.Pos.y, White);
                        break;
                    case StandKind.Mushroom:
                        _stand.Add(it.Variant == 0 ? Spr.MushroomRed : Spr.MushroomBrown, it.Pos.x, it.Pos.y, White);
                        break;
                    case StandKind.Rock:
                        _stand.Add(it.Variant == 0 ? Spr.RockL : Spr.RockS, it.Pos.x, it.Pos.y, White);
                        break;
                    case StandKind.Bush:
                        _stand.Add(it.Variant == 0 ? Spr.BushA : Spr.BushB, it.Pos.x, it.Pos.y, White);
                        break;
                    case StandKind.Stump:
                        _stand.Add(Spr.Stump, it.Pos.x, it.Pos.y, White);
                        break;
                    case StandKind.Trunk:
                        _stand.Add(Spr.Trunk, it.Pos.x, it.Pos.y, White);
                        break;
                    case StandKind.Flower:
                        DrawFlower(it.Index, it.Pos, flowers, wind, swarm.LeaderPos, time);
                        break;
                    case StandKind.Hive:
                        _stand.Add(Spr.Hive, it.Pos.x, it.Pos.y + (_hiveBounce > 0f ? 1f : 0f), White);
                        {
                            // мёд виден на самом улье: подтёки и лужица растут к цели
                            float goal = sim.Cfg.Endless ? 600f : sim.Cfg.GoalHoney;
                            int lv = Mathf.Clamp(Mathf.FloorToInt(sim.Colony.Honey / goal * 4f + 0.5f), 0, 4);
                            if (lv > 0) _stand.Add((int)Spr.HiveHoney1 + lv - 1, it.Pos.x, it.Pos.y + (_hiveBounce > 0f ? 1f : 0f), White);
                        }
                        if (_hiveGlow > 0.01f)
                        {
                            float pulse = 0.6f + 0.4f * Mathf.Sin(time * 5f);
                            var e = world.HivePos + BeeSwarm.HiveEntranceOffset;
                            _glow.AddStretched((int)Spr.Glow, e.x - 13f, e.y - 6f, 26f, 14f, new Color32(255, 200, 90, (byte)(150 * _hiveGlow * pulse)));
                        }
                        break;
                }
            }

            // --- сторожевые пчёлы у летка (декор, не часть роя)
            Vector2 ent = world.HivePos + BeeSwarm.HiveEntranceOffset;
            for (int i = 0; i < 6; i++)
            {
                float ph = i * 1.7f;
                float t = time * (0.9f + i * 0.13f) + ph;
                Vector2 p = ent + new Vector2(Mathf.Sin(t) * (14f + i * 2f), Mathf.Sin(t * 2f) * 6f + 6f + (i % 3) * 3f);
                bool flip = Mathf.Cos(t) < 0f;
                int fr = (int)Spr.BeeA + ((int)(time * 30f + i) & 3);
                _shadow.Add(Spr.ShadowS, p.x, p.y - 7f, A(55));
                _fly.Add(fr, p.x, p.y, White, flip);
            }

            // --- рой
            float fastV = sim.Cfg.LeaderSpeed * sim.Colony.SpeedMul * 1.25f;
            float fastSq = fastV * fastV;
            for (int i = 0; i < swarm.Count; i++)
            {
                Vector2 p = swarm.Pos[i];
                if (!view.Contains(p)) continue;
                float ph = swarm.Phase[i];
                bool loaded = swarm.Carry[i] > cap * 0.3f;
                int f4 = (int)(time * 32f + ph) & 3;      // 4 кадра крыльев
                var role = swarm.Role[i];
                // смазанный кадр на большой скорости — рой «тянется» в рывке
                bool fast = role != BeeRole.Guard && swarm.Stuck[i] <= 0f && swarm.Vel[i].sqrMagnitude > fastSq;
                int spr;
                if (role == BeeRole.Guard) spr = (int)Spr.SoldierA + (f4 >> 1);
                else if (fast) spr = (loaded ? (int)Spr.BeeLoadFastA : (int)Spr.BeeFastA) + (f4 & 1);
                else spr = (loaded ? (int)Spr.BeeLoadA : (int)Spr.BeeA) + f4;
                float bob = swarm.Stuck[i] > 0f ? 0f : Mathf.Sin(time * 7f + ph) * 1.2f;
                Color32 bc = role == BeeRole.Squad ? new Color32(200, 228, 255, 255) : role == BeeRole.Worker ? new Color32(205, 245, 170, 255) : White;
                if (role == BeeRole.Guard && swarm.Hp[i] < swarm.MaxHp[i] - 0.05f)
                {
                    bc = new Color32(255, 190, 180, 255);
                    // лечение у летка: зелёное кольцо-пульс
                    if ((p - (sim.World.HivePos + BeeSwarm.HiveEntranceOffset)).sqrMagnitude < 50f * 50f)
                        _glow.Add(Spr.KRing1, p.x, p.y - 2f, new Color32(150, 255, 140, (byte)(90 + 60 * Mathf.Sin(time * 8f + ph))));
                }
                _shadow.Add(Spr.ShadowS, p.x, p.y - 6f, A(60));
                _fly.Add(spr, p.x, p.y + bob, bc, swarm.Vel[i].x < 0f);
            }

            // --- враги
            var en = sim.Enemies;
            for (int i = 0; i < en.Count; i++)
            {
                Vector2 p = en.Pos[i];
                if (!view.Contains(p)) continue;
                var et = en.Type[i];
                if (et == EnemyType.Nest || et == EnemyType.Ant || et == EnemyType.Bear || et == EnemyType.WildHive) { DrawGroundEnemy(en, i, p, time); continue; }
                if (et == EnemyType.Rival)
                {
                    // дикая пчела: как наша, но тёмная; несёт пыльцу — видно комочек
                    int rf = (int)(time * 30f + en.Phase[i]) & 3;
                    Color32 rc = en.Flash[i] > 0.06f ? new Color32(255, 200, 200, 255) : White;
                    float rb = Mathf.Round(Mathf.Sin(time * 7f + en.Phase[i]));
                    _shadow.Add(Spr.ShadowS, p.x, p.y - 7f, A(60));
                    _fly.Add((int)Spr.RivalA + rf, p.x, p.y + rb, rc, en.Vel[i].x < 0f);
                    if (en.Stolen[i] > 0.5f) _fly.Add(Spr.Pollen, p.x, p.y + rb - 4f, White);
                    if (en.Mode[i] == EnemyMode.Hunt) _overlay.Add(Spr.Pixel2, p.x, p.y + rb + 5f, new Color32(238, 70, 60, 255));
                    continue;
                }
                bool queen = et == EnemyType.Queen;
                bool hornet = et == EnemyType.Hornet || queen;   // крупный — для теней/полос
                int fr = (int)(time * 30f + en.Phase[i]) & 3;
                int spr;
                bool windup = en.Act[i] == EnemyAct.Windup;
                bool dash = en.Act[i] == EnemyAct.Dash;
                int baseA = queen ? (int)Spr.QueenA : et == EnemyType.Hornet ? (int)Spr.HornetA : (int)Spr.WaspA;
                int flashS = queen ? (int)Spr.QueenFlash : et == EnemyType.Hornet ? (int)Spr.HornetFlash : (int)Spr.WaspFlash;
                int windS = queen ? (int)Spr.QueenWind : et == EnemyType.Hornet ? (int)Spr.HornetWind : (int)Spr.WaspWind;
                if (en.Flash[i] > 0.06f || (windup && ((int)(time * 16f) & 3) == 0)) spr = flashS;
                else if (windup) spr = windS;                // поза замаха: брюшко поджато, жало вперёд
                else spr = baseA + fr;
                float bob = windup ? 0f : Mathf.Sin(time * 6f + en.Phase[i]) * 1.5f;
                // замах: дрожь на пиксель, чтобы читалось «сейчас бросится»
                float jx = windup ? (((int)(time * 40f) & 1) == 0 ? 1f : -1f) : 0f;
                bool flipE = windup || dash ? en.DashDir[i].x < 0f : en.Vel[i].x < 0f;
                float footY = p.y - (queen ? 15f : hornet ? 11f : 8f);
                _shadow.Add(queen ? Spr.ShadowXL : hornet ? Spr.ShadowL : Spr.ShadowM, p.x, footY, A(70));
                if (en.Uid[i] == sim.FocusUid)
                {
                    // метка-приказ: пульсирующий прицел над врагом
                    float fp = Mathf.Repeat(time * 2f, 1f);
                    _overlay.Add(fp < 0.5f ? Spr.KCross : Spr.KStar, p.x, p.y + (queen ? 22f : hornet ? 16f : 12f), new Color32(255, 240, 160, 230));
                }
                // кольцо опасности под врагом — враги читаются даже в гуще роя
                byte ra = (byte)(windup ? 230 : 110);
                _shadow.Add(hornet ? Spr.KRing1 : Spr.KRing0, p.x, footY, new Color32(238, 70, 60, ra));
                if (en.Heat[i] > 0.05f)
                {
                    float hp = 0.6f + 0.4f * Mathf.Sin(time * 14f);
                    _glow.AddStretched((int)Spr.Glow, p.x - 18f, p.y - 12f, 36f, 26f, new Color32(255, 120, 40, (byte)(170 * en.Heat[i] * hp)));
                }
                if (dash) _fly.Add(spr, p.x - en.Vel[i].x * 0.05f, p.y - en.Vel[i].y * 0.05f + bob, new Color32(255, 200, 200, 90), flipE);
                _fly.Add(spr, p.x + jx, p.y + bob, White, flipE);
                if (en.Hp[i] < en.MaxHp[i])
                {
                    float w = queen ? 26f : hornet ? 16f : 10f;
                    float y = p.y + (queen ? 15f : hornet ? 10f : 8f);
                    _overlay.Rect(p.x - w * 0.5f - 1f, y - 1f, w + 2f, 4f, new Color32(23, 18, 28, 220));
                    _overlay.Rect(p.x - w * 0.5f, y, Mathf.Max(1f, w * en.Hp[i] / en.MaxHp[i]), 2f, new Color32(238, 106, 82, 255));
                }
                if (en.Stolen[i] > 0.5f) _fly.Add(Spr.HoneyDrop, p.x, p.y - 6f, White);
            }

            DrawHazards(sim, view, time);
            DrawAmbient(sim, view, time, dt);

            // --- кроны: прозрачнеют, если под ними рой
            for (int k = 0; k < _trees.Count; k++)
            {
                var o = world.Obstacles[_trees[k]];
                Vector2 c = o.Pos + new Vector2(0f, 6f);
                Vector2 d = swarm.LeaderPos - (c + new Vector2(0f, 18f));
                bool under = Mathf.Abs(d.x) < 30f && Mathf.Abs(d.y) < 28f;
                for (int e = 0; e < en.Count && !under; e++)
                {
                    Vector2 de = en.Pos[e] - (c + new Vector2(0f, 18f));
                    under = Mathf.Abs(de.x) < 26f && Mathf.Abs(de.y) < 24f;
                }
                _canopyAlpha[k] = Mathf.MoveTowards(_canopyAlpha[k], under ? 0.42f : 1f, dt * 2.5f);
                if (!view.Contains(c)) continue;
                Spr s = o.Variant == 0 ? Spr.CanopyA : (o.Variant == 1 ? Spr.CanopyB : Spr.CanopyC);
                float sway = Mathf.Round(Wind(o.Pos, time * 0.6f) * 0.8f);
                _canopy.Add(s, c.x + sway, c.y, new Color32(255, 255, 255, (byte)(255 * _canopyAlpha[k])));
            }

            // --- тени облаков ползут по поляне
            for (int i = 0; i < 2; i++)
            {
                float cx = Mathf.Repeat(time * (6f + i * 2.5f) + i * 330f, world.Width + 400f) - 200f;
                float cy = world.Height * (0.3f + 0.4f * i) + Mathf.Sin(time * 0.05f + i) * 60f;
                _cloud.AddStretched((int)Spr.ShadowXL, cx - 110f, cy - 45f, 220f, 90f, new Color32(255, 255, 255, 22));
                _cloud.AddStretched((int)Spr.ShadowXL, cx - 60f, cy - 20f, 150f, 70f, new Color32(255, 255, 255, 18));
            }

            DrawMarks(sim, view, time);
            DrawSky(sim, screen, view, time, dt);
            DrawFog(sim);

            _shadow.End(); _stand.End(); _fly.End(); _canopy.End(); _cloud.End(); _overlay.End(); _glow.End();
        }

        // ------------------------------------------------------------ наземные враги
        void DrawGroundEnemy(EnemySystem en, int i, Vector2 p, float time)
        {
            var et = en.Type[i];
            if (et == EnemyType.Ant)
            {
                int fr = ((int)(time * 10f + en.Phase[i]) & 1);
                _shadow.Add(Spr.ShadowS, p.x, p.y - 1f, A(70));
                _shadow.Add(fr == 0 ? Spr.AntA : Spr.AntB, p.x, p.y, en.Flash[i] > 0f ? new Color32(255, 200, 190, 255) : White, en.Vel[i].x < 0f);
                if (en.Stolen[i] > 0.3f) _shadow.Add(Spr.Pollen, p.x, p.y + 2f, White);
                return;
            }
            if (et == EnemyType.WildHive)
            {
                float sw2 = Mathf.Round(Mathf.Sin(time * 1.1f) * 0.6f);
                _shadow.Add(Spr.ShadowL, p.x, p.y - 4f, A(80));
                _stand.Add(Spr.WildHive, p.x + sw2, p.y - 4f, en.Flash[i] > 0.06f ? new Color32(255, 210, 200, 255) : White);
                HpBar(p.x, p.y + 26f, 24f, en.Hp[i] / en.MaxHp[i]);
                return;
            }
            if (et == EnemyType.Nest)
            {
                float sway = Mathf.Round(Mathf.Sin(time * 1.3f) * 0.6f);
                _shadow.Add(Spr.ShadowL, p.x, p.y - 4f, A(80));
                _stand.Add(en.Flash[i] > 0.06f ? Spr.Nest : Spr.Nest, p.x + sway, p.y - 4f, en.Flash[i] > 0.06f ? new Color32(255, 200, 200, 255) : White);
                HpBar(p.x, p.y + 24f, 22f, en.Hp[i] / en.MaxHp[i]);
                return;
            }
            // медведь
            int bf = ((int)(time * 6f) & 1);
            int bs = en.Flash[i] > 0.06f ? (int)Spr.BearFlash : (bf == 0 ? (int)Spr.BearA : (int)Spr.BearB);
            bool flip = en.Vel[i].x < 0f;
            _shadow.Add(Spr.ShadowXL, p.x, p.y - 8f, A(110));
            _shadow.Add(Spr.KRing2, p.x, p.y - 8f, new Color32(238, 70, 60, 120));
            _fly.Add(bs, p.x, p.y, White, flip);
            HpBar(p.x, p.y + 16f, 26f, en.Hp[i] / en.MaxHp[i]);
            if (en.Stolen[i] > 1f) _fly.Add(Spr.HoneyDrop, p.x + (flip ? -10f : 10f), p.y + 2f, White);
        }

        void HpBar(float x, float y, float w, float frac)
        {
            _overlay.Rect(x - w * 0.5f - 1f, y - 1f, w + 2f, 4f, new Color32(23, 18, 28, 220));
            _overlay.Rect(x - w * 0.5f, y, Mathf.Max(1f, w * frac), 2f, new Color32(238, 106, 82, 255));
        }

        // ------------------------------------------------------------ паутина и птицы
        void DrawHazards(GameSim sim, Rect view, float time)
        {
            var hz = sim.Hazards;
            for (int w = 0; w < hz.WebCount; w++)
            {
                if (hz.WStrength[w] <= 0f || !view.Contains(hz.WPos[w])) continue;
                float k = hz.WStrength[w] / 100f;
                byte a = (byte)(110 + 120 * k);
                _fly.Add(Spr.Web, hz.WPos[w].x, hz.WPos[w].y, new Color32(255, 255, 255, a));
                _fly.Add(Spr.Spider, hz.SpiderPos[w].x, hz.SpiderPos[w].y + Mathf.Round(Mathf.Sin(time * 3f + w) * 0.6f), White);
            }
            for (int b = 0; b < HazardSystem.MaxBirds; b++)
            {
                var st = hz.BState[b];
                if (st == BirdState.None) continue;
                Vector2 tg = hz.BTarget[b];
                if (st == BirdState.Shadow)
                {
                    float k = 1f - hz.BTimer[b] / HazardSystem.ShadowTime;   // 0 → 1 к моменту пике
                    float r = HazardSystem.StrikeRadius * (0.6f + 0.6f * k);
                    _shadow.AddStretched((int)Spr.ShadowXL, tg.x - r * 1.3f, tg.y - r * 0.55f, r * 2.6f, r * 1.1f, new Color32(255, 255, 255, (byte)(60 + 110 * k)));
                    bool blink = ((int)(time * (6f + 10f * k)) & 1) == 0;
                    _overlay.Add(Spr.KRing2, tg.x, tg.y, new Color32(238, 70, 60, (byte)(blink ? 220 : 120)));
                    float dist = (1f - k) * 220f;
                    Vector2 bp = tg - hz.BDir[b] * dist + new Vector2(0f, 40f * (1f - k));
                    if (view.Contains(bp))
                        _canopy.Add(((int)(time * 12f) & 1) == 0 ? Spr.BirdA : Spr.BirdB, bp.x, bp.y, White, hz.BDir[b].x < 0f);
                }
                else
                {
                    float k = 1f - hz.BTimer[b] / 0.9f;
                    Vector2 bp = tg + hz.BDir[b] * (k * 240f) + new Vector2(0f, 50f * k);
                    if (view.Contains(bp))
                        _canopy.Add(((int)(time * 14f) & 1) == 0 ? Spr.BirdA : Spr.BirdB, bp.x, bp.y, White, hz.BDir[b].x < 0f);
                }
            }
        }

        // ------------------------------------------------------------ метки разведчиц и редкие цветы
        void DrawMarks(GameSim sim, Rect view, float time)
        {
            var sw = sim.Swarm;
            if (sw.SquadActive && view.Contains(sw.SquadTarget))
            {
                Vector2 sp = sw.SquadTarget;
                _overlay.Add(Spr.IconSquad, sp.x, sp.y + 24f + Mathf.Round(Mathf.Sin(time * 3f)), new Color32(200, 228, 255, 230));
                // пунктир зоны поста
                float r = sim.Cfg.PostRadius;
                for (int k = 0; k < 20; k++)
                {
                    float an = k * 0.31416f + time * 0.15f;
                    _glow.Add(Spr.Pixel2, Mathf.Round(sp.x + Mathf.Cos(an) * r), Mathf.Round(sp.y + Mathf.Sin(an) * r * 0.82f), new Color32(200, 228, 255, 90));
                }
            }
            var fog = sim.Fog;
            for (int m = 0; m < FogMap.MaxMarks; m++)
            {
                if (fog.MarkTime[m] <= 0f) continue;
                Vector2 p = fog.MarkPos[m];
                if (!view.Contains(p)) continue;
                float pulse = Mathf.Repeat(time * 1.5f, 1f);
                _glow.Add(pulse < 0.33f ? Spr.KRing0 : pulse < 0.66f ? Spr.KRing1 : Spr.KRing2, p.x, p.y, new Color32(255, 210, 90, (byte)(200 * (1f - pulse))));
                _overlay.Add(Spr.IconNectar, p.x, p.y + 14f + Mathf.Round(Mathf.Sin(time * 4f) * 1.5f), White);
            }
        }

        // ------------------------------------------------------------ небо: дождь, жара, ночь
        void DrawSky(GameSim sim, PixelScreen screen, Rect view, float time, float dt)
        {
            var sky = sim.Sky;
            _weather.Begin(); _night.Begin(); _nightGlow.Begin();
            var r = screen.WorldViewRect(0f);
            float wb = sky.WeatherBlend;
            if (sky.Current == Weather.Rain && wb > 0.02f)
            {
                _weather.Rect(r.x, r.y, r.width, r.height, new Color32(40, 60, 90, (byte)(60 * wb)));
                int n = Mathf.RoundToInt(_rain.Length * wb);
                for (int i = 0; i < n; i++)
                {
                    ref var d = ref _rain[i];
                    d.Life -= dt;
                    if (d.Life <= 0f || !r.Contains(d.Pos))
                    {
                        if (d.Life <= 0f) { d.SplashPos = d.Pos; d.Splash = 0.18f; }   // капля упала — всплеск
                        d.Pos = new Vector2(r.x + (float)_rnd.NextDouble() * r.width, r.y + (float)_rnd.NextDouble() * r.height);
                        d.Life = 0.3f + (float)_rnd.NextDouble() * 0.4f;
                    }
                    d.Pos += new Vector2(-40f, -220f) * dt;
                    _weather.Add(Spr.RainDrop, d.Pos.x, d.Pos.y, new Color32(200, 225, 255, 170));
                    if (d.Splash > 0f)
                    {
                        d.Splash -= dt;
                        _weather.Add(d.Splash > 0.09f ? Spr.KRing0 : Spr.KRing1, d.SplashPos.x, d.SplashPos.y, new Color32(200, 225, 255, (byte)(700 * d.Splash)));
                    }
                }
            }
            else if (sky.Current == Weather.Heat && wb > 0.02f)
                _weather.Rect(r.x, r.y, r.width, r.height, new Color32(255, 170, 70, (byte)(28 * wb)));
            else if (sky.Current == Weather.Wind && wb > 0.02f)
            {
                for (int i = 0; i < 18; i++)
                {
                    ref var d = ref _rain[i];
                    d.Life -= dt;
                    if (d.Life <= 0f || !r.Contains(d.Pos))
                    {
                        d.Pos = new Vector2(r.x + (float)_rnd.NextDouble() * r.width, r.y + (float)_rnd.NextDouble() * r.height);
                        d.Life = 0.6f + (float)_rnd.NextDouble() * 0.6f;
                    }
                    d.Pos += sky.Wind * 6f * dt;
                    _weather.Add(Spr.KTrace, d.Pos.x, d.Pos.y, new Color32(255, 255, 255, (byte)(70 * wb)));
                }
            }
            float dark = sky.Darkness(sim.Time);
            if (dark > 0.01f)
            {
                _night.Rect(r.x, r.y, r.width, r.height, new Color32(30, 40, 96, (byte)(95 * dark)));
                // светлячки и тёплый свет улья
                for (int i = 0; i < _flies.Length; i++)
                {
                    ref var f = ref _flies[i];
                    if (f.Life <= 0f || !r.Contains(f.Pos))
                    {
                        f.Pos = new Vector2(r.x + (float)_rnd.NextDouble() * r.width, r.y + (float)_rnd.NextDouble() * r.height);
                        f.Life = 2f + (float)_rnd.NextDouble() * 4f;
                        f.Phase = (float)_rnd.NextDouble() * 10f;
                    }
                    f.Life -= dt;
                    f.Pos += new Vector2(Mathf.Sin(time + f.Phase), Mathf.Cos(time * 0.8f + f.Phase)) * 8f * dt;
                    float tw = Mathf.Max(0f, Mathf.Sin(time * 2.5f + f.Phase));
                    _nightGlow.Add(Spr.Pixel2, f.Pos.x, f.Pos.y, new Color32(200, 255, 120, (byte)(220 * tw * dark)));
                }

            }
            _weather.End(); _night.End(); _nightGlow.End();
        }

        // ------------------------------------------------------------ туман войны (перестраивается только при открытии ячеек)
        void DrawFog(GameSim sim)
        {
            var fog = sim.Fog;
            if (fog.Version == _fogVersion) return;
            _fogVersion = fog.Version;
            _fog.Begin();
            if (fog.Enabled)
            {
                float c = FogMap.Cell;
                for (int y = -1; y <= fog.Rows; y++)
                    for (int x = -1; x <= fog.Cols; x++)
                    {
                        bool inside = x >= 0 && y >= 0 && x < fog.Cols && y < fog.Rows;
                        if (inside && fog.Revealed[y * fog.Cols + x]) continue;
                        bool edge = false;
                        for (int dy = -1; dy <= 1 && !edge; dy++)
                            for (int dx = -1; dx <= 1 && !edge; dx++)
                            {
                                int nx = x + dx, ny = y + dy;
                                if (nx >= 0 && ny >= 0 && nx < fog.Cols && ny < fog.Rows && fog.Revealed[ny * fog.Cols + nx]) edge = true;
                            }
                        if (!inside && !edge) continue;   // за краем карты туман не нужен
                        _fog.Rect(x * c, y * c, c, c, edge ? new Color32(12, 24, 18, 165) : new Color32(10, 20, 15, 238));
                    }
            }
            _fog.End();
        }

        void DrawFlower(int f, Vector2 basePos, FlowerField flowers, float wind, Vector2 leader, float time)
        {
            if (flowers.Gone[f]) return;
            int k = (int)flowers.Kind[f];
            // спрайты цветов в атласе идут тройками: A, B, Dry — в порядке Daisy, Poppy, Corn, Violet, Pink, Sun; липа отдельно
            int baseSpr = flowers.Kind[f] == FlowerKind.Linden ? (int)Spr.FlowerLindenA : (int)Spr.FlowerDaisyA + FlowerSpriteIndex(k) * 3;
            Vector2 head = flowers.Pos[f];
            if (flowers.Sprout[f] > 0f)
            {
                // росток: бутон, «подрастающий» снизу
                float g = 1f - flowers.Sprout[f] / 6f;
                _stand.Add(baseSpr + 2, basePos.x, basePos.y - Mathf.Round((1f - g) * 6f), new Color32(255, 255, 255, (byte)(140 + 115 * g)));
                return;
            }
            int spr = !flowers.IsBlooming(f) ? baseSpr + 2 : baseSpr + (wind > 0.2f ? 1 : 0);
            if (flowers.Rare[f] && flowers.IsBlooming(f))
            {
                float pulse = 0.6f + 0.4f * Mathf.Sin(time * 4f + f);
                _glow.AddStretched((int)Spr.Glow, head.x - 14f, head.y - 9f, 28f, 18f, new Color32(255, 215, 80, (byte)(170 * pulse)));
            }
            _stand.Add(spr, basePos.x, basePos.y, flowers.Rare[f] ? new Color32(255, 238, 170, 255) : White);
            if (flowers.IsBlooming(f) && (head - leader).sqrMagnitude < 75f * 75f)
            {
                float frac = flowers.Nectar[f] / flowers.MaxNectar[f];
                _overlay.Rect(head.x - 5f, head.y + 8f, 11f, 3f, new Color32(23, 18, 28, 200));
                _overlay.Rect(head.x - 4f, head.y + 9f, Mathf.Max(1f, 9f * frac), 1f, new Color32(247, 207, 85, 255));
            }
        }

        static int FlowerSpriteIndex(int kind)
        {
            // FlowerKind: Daisy, Pink, Poppy, Corn, Violet, Sun  -> атлас: Daisy, Poppy, Corn, Violet, Pink, Sun
            switch ((FlowerKind)kind)
            {
                case FlowerKind.Daisy: return 0;
                case FlowerKind.Poppy: return 1;
                case FlowerKind.Corn: return 2;
                case FlowerKind.Violet: return 3;
                case FlowerKind.Pink: return 4;
                default: return 5;
            }
        }

        public static float Wind(Vector2 p, float t) =>
            Mathf.Sin(t * 1.7f + p.x * 0.045f + p.y * 0.018f) * 0.75f + Mathf.Sin(t * 0.6f + p.x * 0.011f) * 0.35f;

        void DrawAmbient(GameSim sim, Rect view, float time, float dt)
        {
            var world = sim.World;
            // бабочки
            for (int i = 0; i < _butterflies.Length; i++)
            {
                ref var b = ref _butterflies[i];
                Vector2 to = b.Target - b.Pos;
                if (to.sqrMagnitude < 64f)
                    b.Target = new Vector2((float)_rnd.NextDouble() * world.Width, (float)_rnd.NextDouble() * world.Height);
                // пугаются роя
                Vector2 away = b.Pos - sim.Swarm.LeaderPos;
                Vector2 desired = to.normalized * 18f + new Vector2(Mathf.Sin(time * 2.1f + b.Phase), Mathf.Cos(time * 1.7f + b.Phase)) * 14f;
                if (away.sqrMagnitude < 40f * 40f) desired += away.normalized * 40f;
                b.Vel = Vector2.MoveTowards(b.Vel, desired, 60f * dt);
                b.Pos += b.Vel * dt;
                if (!view.Contains(b.Pos)) continue;
                int fr = ((int)(time * 9f + b.Phase) & 1);
                float flutter = Mathf.Sin(time * 4f + b.Phase) * 2f;
                _shadow.Add(Spr.ShadowS, b.Pos.x, b.Pos.y - 9f, A(40));
                _fly.Add(fr == 0 ? Spr.ButterflyA : Spr.ButterflyB, b.Pos.x, b.Pos.y + flutter, b.Tint);
            }
            // пыльца в воздухе
            for (int i = 0; i < _motes.Length; i++)
            {
                ref var m = ref _motes[i];
                if (m.Life <= 0f || !view.Contains(m.Pos))
                {
                    m.Pos = new Vector2(view.xMin + (float)_rnd.NextDouble() * view.width, view.yMin + (float)_rnd.NextDouble() * view.height);
                    m.Vel = new Vector2(4f + (float)_rnd.NextDouble() * 6f, (float)_rnd.NextDouble() * 4f - 1f);
                    m.Life = 3f + (float)_rnd.NextDouble() * 5f;
                    m.Phase = (float)_rnd.NextDouble() * 10f;
                }
                m.Life -= dt;
                m.Pos += (m.Vel + new Vector2(0f, Mathf.Sin(time * 1.3f + m.Phase) * 5f)) * dt;
                float tw = 0.5f + 0.5f * Mathf.Sin(time * 3f + m.Phase);
                byte a = (byte)(Mathf.Min(1f, m.Life) * (90 + 120 * tw));
                _glow.Add(Spr.Pixel, m.Pos.x, m.Pos.y, new Color32(255, 240, 170, a));
            }
        }
    }
}
