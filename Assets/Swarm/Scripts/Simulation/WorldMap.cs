using System.Collections.Generic;
using UnityEngine;

namespace Swarm.Simulation
{
    public enum ObstacleKind : byte { Tree, Rock, Bush, Stump }

    public struct Obstacle
    {
        public Vector2 Pos;
        public float Radius;      // зона, которую пчёлы облетают
        public ObstacleKind Kind;
        public byte Variant;
    }

    public enum FlowerKind : byte { Daisy, Pink, Poppy, Corn, Violet, Sun }

    /// <summary>Процедурная карта: улей в центре, деревья/камни, луга с цветами.</summary>
    public sealed class WorldMap
    {
        public readonly float Width, Height;
        public readonly Vector2 HivePos;
        public readonly List<Obstacle> Obstacles = new List<Obstacle>(48);
        public readonly uint Seed;

        public WorldMap(SimConfig cfg, uint seed)
        {
            Seed = seed;
            Width = cfg.WorldWidth;
            Height = cfg.WorldHeight;
            HivePos = new Vector2(Width * 0.5f, Height * 0.5f);
        }

        public bool InsideObstacle(Vector2 p, float pad)
        {
            for (int i = 0; i < Obstacles.Count; i++)
            {
                var o = Obstacles[i];
                if ((p - o.Pos).sqrMagnitude < (o.Radius + pad) * (o.Radius + pad)) return true;
            }
            return false;
        }

        /// <summary>Выталкивает точку из препятствий. Возвращает true, если было пересечение.</summary>
        public bool PushOut(ref Vector2 p, ref Vector2 v, float pad)
        {
            bool hit = false;
            for (int i = 0; i < Obstacles.Count; i++)
            {
                var o = Obstacles[i];
                Vector2 d = p - o.Pos;
                float r = o.Radius + pad;
                float sq = d.sqrMagnitude;
                if (sq < r * r)
                {
                    float m = Mathf.Sqrt(sq);
                    Vector2 n = m > 0.001f ? d / m : new Vector2(1f, 0f);
                    p = o.Pos + n * r;
                    float vn = Vector2.Dot(v, n);
                    if (vn < 0f) v -= n * vn;
                    hit = true;
                }
            }
            return hit;
        }

        public Vector2 ClampToWorld(Vector2 p, float margin)
        {
            p.x = Mathf.Clamp(p.x, margin, Width - margin);
            p.y = Mathf.Clamp(p.y, margin, Height - margin);
            return p;
        }

        public void Generate(Rng rng, FlowerField flowers)
        {
            Obstacles.Clear();
            // --- деревья: держатся подальше от улья и друг от друга
            int attempts = 0;
            int trees = 0;
            while (trees < 15 && attempts++ < 600)
            {
                var p = new Vector2(rng.Range(30f, Width - 30f), rng.Range(40f, Height - 30f));
                if ((p - HivePos).magnitude < 110f) continue;
                if (InsideObstacle(p, 52f)) continue;
                Obstacles.Add(new Obstacle { Pos = p, Radius = 11f, Kind = ObstacleKind.Tree, Variant = (byte)rng.Range(0, 3) });
                trees++;
            }
            AddScatter(rng, ObstacleKind.Rock, 9, 7f, 70f, 26f);
            AddScatter(rng, ObstacleKind.Bush, 8, 9f, 75f, 28f);
            AddScatter(rng, ObstacleKind.Stump, 3, 7f, 80f, 30f);

            // --- луга: кластеры цветов, ценность растёт с удалением от улья
            flowers.Clear();
            int clusters = 0;
            attempts = 0;
            float maxDist = new Vector2(Width * 0.5f, Height * 0.5f).magnitude;
            while (clusters < 15 && attempts++ < 800)
            {
                var c = new Vector2(rng.Range(40f, Width - 40f), rng.Range(50f, Height - 40f));
                float dist = (c - HivePos).magnitude;
                if (dist < 70f) continue;
                if (InsideObstacle(c, 22f)) continue;
                bool nearOther = false;
                for (int i = 0; i < flowers.Count; i++)
                    if ((flowers.Pos[i] - c).sqrMagnitude < 40f * 40f) { nearOther = true; break; }
                if (nearOther) continue;

                float t = Mathf.Clamp01((dist - 70f) / (maxDist - 70f));
                int n = rng.Range(3, 7);
                for (int k = 0; k < n; k++)
                {
                    var p = c + new Vector2(rng.Signed, rng.Signed) * 18f;
                    p = ClampToWorld(p, 16f);
                    if (InsideObstacle(p, 8f)) continue;
                    FlowerKind kind = PickKind(rng, t);
                    float nectar = NectarOf(kind) * rng.Range(0.85f, 1.15f);
                    flowers.Add(p, kind, nectar, rng.Value);
                }
                clusters++;
            }
        }

        static FlowerKind PickKind(Rng rng, float t)
        {
            float r = rng.Value * 0.7f + t * 0.55f;
            if (r < 0.28f) return rng.Value < 0.5f ? FlowerKind.Daisy : FlowerKind.Pink;
            if (r < 0.62f) return rng.Value < 0.5f ? FlowerKind.Poppy : FlowerKind.Violet;
            if (r < 0.88f) return FlowerKind.Corn;
            return FlowerKind.Sun;
        }

        public static float NectarOf(FlowerKind k)
        {
            switch (k)
            {
                case FlowerKind.Daisy: return 10f;
                case FlowerKind.Pink: return 12f;
                case FlowerKind.Poppy: return 18f;
                case FlowerKind.Violet: return 20f;
                case FlowerKind.Corn: return 26f;
                default: return 45f;
            }
        }

        void AddScatter(Rng rng, ObstacleKind kind, int count, float radius, float hiveClear, float spacing)
        {
            int placed = 0, attempts = 0;
            while (placed < count && attempts++ < 400)
            {
                var p = new Vector2(rng.Range(24f, Width - 24f), rng.Range(30f, Height - 24f));
                if ((p - HivePos).magnitude < hiveClear) continue;
                if (InsideObstacle(p, spacing)) continue;
                Obstacles.Add(new Obstacle { Pos = p, Radius = radius, Kind = kind, Variant = (byte)rng.Range(0, 2) });
                placed++;
            }
        }
    }
}
