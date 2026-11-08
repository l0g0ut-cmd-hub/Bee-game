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
            _overlay = new QuadBatch("L9_Overlay", 400, a, OrderOverlay, L, root);
        }

        static Color32 A(byte a) => new Color32(255, 255, 255, a);

        // ------------------------------------------------------------ построение статики
        public void Build(GameSim sim)
        {
            _builtFor = sim;
            var world = sim.World;
            var rng = new Rng(world.Seed * 2654435761u + 17u);

            // земля: тайлы 16x16 с вариациями
            _ground.Begin();
            int cols = Mathf.CeilToInt(world.Width / 16f) + 2, rows = Mathf.CeilToInt(world.Height / 16f) + 2;
            for (int y = -1; y < rows; y++)
                for (int x = -1; x < cols; x++)
                {
                    int v = rng.Range(0, 100);
                    int tile = v < 40 ? 0 : v < 62 ? 1 : v < 78 ? 2 : v < 88 ? 3 : v < 95 ? 4 : 5;
                    _ground.Add((int)Spr.Grass0 + tile, x * 16, y * 16, White);
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
                float wind = Wind(it.Pos, time);
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
                        DrawFlower(it.Index, it.Pos, flowers, wind, swarm.LeaderPos);
                        break;
                    case StandKind.Hive:
                        _stand.Add(Spr.Hive, it.Pos.x, it.Pos.y + (_hiveBounce > 0f ? 1f : 0f), White);
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
                int fr = ((int)(time * 24f + i) & 1) == 0 ? (int)Spr.BeeA : (int)Spr.BeeB;
                _shadow.Add(Spr.ShadowS, p.x, p.y - 7f, A(55));
                _fly.Add(fr, p.x, p.y, White, flip);
            }

            // --- рой
            for (int i = 0; i < swarm.Count; i++)
            {
                Vector2 p = swarm.Pos[i];
                if (!view.Contains(p)) continue;
                float ph = swarm.Phase[i];
                bool loaded = swarm.Carry[i] > cap * 0.3f;
                int fr = ((int)(time * 26f + ph) & 1) == 0 ? 0 : 1;
                int spr = loaded ? (int)Spr.BeeLoadA + fr : (int)Spr.BeeA + fr;
                float bob = Mathf.Sin(time * 7f + ph) * 1.2f;
                _shadow.Add(Spr.ShadowS, p.x, p.y - 6f, A(60));
                _fly.Add(spr, p.x, p.y + bob, White, swarm.Vel[i].x < 0f);
            }

            // --- враги
            var en = sim.Enemies;
            for (int i = 0; i < en.Count; i++)
            {
                Vector2 p = en.Pos[i];
                if (!view.Contains(p)) continue;
                bool hornet = en.Type[i] == EnemyType.Hornet;
                int fr = ((int)(time * 22f + en.Phase[i]) & 1);
                int spr;
                bool windup = en.Act[i] == EnemyAct.Windup;
                bool dash = en.Act[i] == EnemyAct.Dash;
                if (en.Flash[i] > 0.06f || (windup && ((int)(time * 16f) & 1) == 0)) spr = hornet ? (int)Spr.HornetFlash : (int)Spr.WaspFlash;
                else spr = hornet ? (int)Spr.HornetA + fr : (int)Spr.WaspA + fr;
                float bob = windup ? 0f : Mathf.Sin(time * 6f + en.Phase[i]) * 1.5f;
                // замах: дрожь на пиксель, чтобы читалось «сейчас бросится»
                float jx = windup ? (((int)(time * 40f) & 1) == 0 ? 1f : -1f) : 0f;
                bool flipE = windup || dash ? en.DashDir[i].x < 0f : en.Vel[i].x < 0f;
                float footY = p.y - (hornet ? 11f : 8f);
                _shadow.Add(hornet ? Spr.ShadowL : Spr.ShadowM, p.x, footY, A(70));
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
                    float w = hornet ? 16f : 10f;
                    float y = p.y + (hornet ? 10f : 8f);
                    _overlay.Rect(p.x - w * 0.5f - 1f, y - 1f, w + 2f, 4f, new Color32(23, 18, 28, 220));
                    _overlay.Rect(p.x - w * 0.5f, y, Mathf.Max(1f, w * en.Hp[i] / en.MaxHp[i]), 2f, new Color32(238, 106, 82, 255));
                }
                if (en.Stolen[i] > 0.5f) _fly.Add(Spr.HoneyDrop, p.x, p.y - 6f, White);
            }

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

            _shadow.End(); _stand.End(); _fly.End(); _canopy.End(); _cloud.End(); _overlay.End(); _glow.End();
        }

        void DrawFlower(int f, Vector2 basePos, FlowerField flowers, float wind, Vector2 leader)
        {
            int k = (int)flowers.Kind[f];
            // спрайты цветов в атласе идут тройками: A, B, Dry — в порядке Daisy, Poppy, Corn, Violet, Pink, Sun
            int baseSpr = (int)Spr.FlowerDaisyA + FlowerSpriteIndex(k) * 3;
            int spr = !flowers.IsBlooming(f) ? baseSpr + 2 : baseSpr + (wind > 0.2f ? 1 : 0);
            _stand.Add(spr, basePos.x, basePos.y, White);
            Vector2 head = flowers.Pos[f];
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
