using UnityEngine;

namespace Swarm.Simulation
{
    public enum BeeTask : byte { Follow, Gather, Attack, Deliver }

    /// <summary>
    /// Рой как структура массивов. Центр роя («лидер») ведёт игрок, каждая пчела — boid:
    /// стремление к своему слоту формации (сплочённость), разделение с соседями, выравнивание по
    /// скорости роя, блуждающий шум, облёт препятствий. Поверх — задачи: сбор, атака, доставка.
    /// </summary>
    public sealed class BeeSwarm
    {
        public readonly int Capacity;
        public readonly Vector2[] Pos, Vel;
        public readonly float[] Carry, Phase;
        public readonly BeeTask[] Task;
        public readonly bool[] Dead;
        public int Count;

        public Vector2 LeaderPos, LeaderVel;
        public float Radius;
        public float TotalCarry;
        public int GatheringCount, AttackingCount;

        public readonly SpatialGrid Grid;
        public readonly int[] QueryBuffer = new int[256];

        readonly int[] _nearFlowers = new int[6];
        readonly float[] _nearFlowerDist = new float[6];
        int _nearFlowerCount;
        readonly int[] _enemyCand = new int[EnemySystem.Capacity];
        readonly int[] _attackers = new int[EnemySystem.Capacity];
        int _deadCount;

        public static readonly Vector2 HiveEntranceOffset = new Vector2(0f, 12f);

        public BeeSwarm(SimConfig cfg)
        {
            Capacity = cfg.MaxBeesHard;
            Pos = new Vector2[Capacity];
            Vel = new Vector2[Capacity];
            Carry = new float[Capacity];
            Phase = new float[Capacity];
            Task = new BeeTask[Capacity];
            Dead = new bool[Capacity];
            Grid = new SpatialGrid(cfg.WorldWidth, cfg.WorldHeight, 6f, Capacity);
        }

        public bool Spawn(Vector2 p, Rng rng)
        {
            if (Count >= Capacity) return false;
            int i = Count++;
            Pos[i] = p + new Vector2(rng.Signed, rng.Signed) * 3f;
            Vel[i] = new Vector2(rng.Signed, rng.Signed) * 40f;
            Carry[i] = 0f;
            Phase[i] = rng.Value * 100f;
            Task[i] = BeeTask.Follow;
            Dead[i] = false;
            return true;
        }

        /// <summary>Отложенное удаление: индексы в сетке остаются валидными до Compact().</summary>
        public void MarkDead(int i)
        {
            if (i < 0 || i >= Count || Dead[i]) return;
            Dead[i] = true;
            _deadCount++;
        }

        public void Compact()
        {
            if (_deadCount == 0) return;
            for (int i = Count - 1; i >= 0; i--)
            {
                if (!Dead[i]) continue;
                int last = Count - 1;
                if (i != last)
                {
                    Pos[i] = Pos[last]; Vel[i] = Vel[last]; Carry[i] = Carry[last];
                    Phase[i] = Phase[last]; Task[i] = Task[last]; Dead[i] = Dead[last];
                }
                Dead[last] = false;
                Count--;
            }
            _deadCount = 0;
        }

        public void ResetLeader(Vector2 p)
        {
            LeaderPos = p;
            LeaderVel = Vector2.zero;
        }

        public void Step(float dt, float time, in SimInput input, GameSim sim)
        {
            var cfg = sim.Cfg;
            var world = sim.World;
            var flowers = sim.Flowers;
            var enemies = sim.Enemies;
            var colony = sim.Colony;

            // ---------------- центр роя: пружина к пальцу с инерцией
            float maxSpd = cfg.LeaderSpeed * colony.SpeedMul;
            if (input.Active)
            {
                Vector2 to = input.Target - LeaderPos;
                float d = to.magnitude;
                Vector2 desired = d > 1f ? to / d * Mathf.Min(maxSpd, d * 2.6f) : Vector2.zero;
                LeaderVel = Vector2.MoveTowards(LeaderVel, desired, cfg.LeaderAccel * dt);
            }
            else
            {
                LeaderVel = Vector2.MoveTowards(LeaderVel, Vector2.zero, cfg.LeaderBrake * dt);
            }
            LeaderPos += LeaderVel * dt;
            world.PushOut(ref LeaderPos, ref LeaderVel, 4f);
            LeaderPos = world.ClampToWorld(LeaderPos, 12f);

            Radius = cfg.SwarmRadiusBase + cfg.SwarmRadiusPerSqrtBee * Mathf.Sqrt(Mathf.Max(1, Count));

            // ---------------- контекст: цветы рядом с роем, улей, враги
            CollectNearFlowers(flowers, Radius + cfg.GatherReach);
            Vector2 entrance = world.HivePos + HiveEntranceOffset;
            bool atHive = (LeaderPos - entrance).magnitude < cfg.HiveReach + Radius * 0.5f;

            int candCount = 0;
            float candRange = cfg.LeashDistance + cfg.AggroRadius;
            for (int e = 0; e < enemies.Count; e++)
            {
                _attackers[e] = 0;
                if ((enemies.Pos[e] - LeaderPos).sqrMagnitude < candRange * candRange)
                    _enemyCand[candCount++] = e;
            }

            Grid.Build(Pos, Count);

            float beeMax = maxSpd * cfg.BeeSpeedFactor;
            float cap = cfg.CarryBase * colony.CarryMul;
            float sepDist = cfg.SeparationDist;
            float sepSq = sepDist * sepDist;
            float aggroSq = cfg.AggroRadius * cfg.AggroRadius;
            float leashSq = cfg.LeashDistance * cfg.LeashDistance;
            float invCount = 1f / Mathf.Max(1, Count);
            TotalCarry = 0f;
            GatheringCount = 0;
            AttackingCount = 0;

            for (int i = 0; i < Count; i++)
            {
                Vector2 p = Pos[i];
                Vector2 v = Vel[i];
                float ph = Phase[i];

                // слот формации: спираль с золотым углом, медленно вращается — рой «дышит»
                float k = Mathf.Sqrt((i + 0.5f) * invCount);
                float ang = i * 2.39996f + time * (0.25f + 0.1f * Mathf.Sin(ph));
                float breathe = 1f + 0.12f * Mathf.Sin(time * 1.3f + ph * 0.1f);
                Vector2 home = LeaderPos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.82f) * (Radius * k * breathe);

                BeeTask task = BeeTask.Follow;
                Vector2 target = home;
                int targetIdx = -1;
                float distLeaderSq = (p - LeaderPos).sqrMagnitude;

                if (distLeaderSq < leashSq)
                {
                    // атака ближайшего врага в радиусе агрессии
                    float best = aggroSq;
                    for (int c = 0; c < candCount; c++)
                    {
                        int e = _enemyCand[c];
                        if (_attackers[e] >= EnemySystem.MaxStingers(enemies.Type[e], cfg)) continue;
                        float sq = (enemies.Pos[e] - p).sqrMagnitude;
                        if (sq < best) { best = sq; targetIdx = e; }
                    }
                    if (targetIdx >= 0)
                    {
                        task = BeeTask.Attack;
                        _attackers[targetIdx]++;
                        float r = EnemySystem.RadiusOf(enemies.Type[targetIdx]) + 2f;
                        float oa = ph * 7f + time * 9f;
                        target = enemies.Pos[targetIdx] + new Vector2(Mathf.Cos(oa), Mathf.Sin(oa)) * r;
                    }
                    else if (Carry[i] < cap - 0.01f && _nearFlowerCount > 0)
                    {
                        task = BeeTask.Gather;
                        targetIdx = _nearFlowers[i % _nearFlowerCount];
                        float oa = ph * 3f + time * 4f;
                        target = flowers.Pos[targetIdx] + new Vector2(Mathf.Cos(oa) * 3f, Mathf.Sin(oa) * 2f);
                    }
                    else if (atHive && Carry[i] > 0f)
                    {
                        task = BeeTask.Deliver;
                        target = entrance + new Vector2(Mathf.Sin(ph) * 3f, 0f);
                    }
                }

                // --- рулёжка
                Vector2 to = target - p;
                float dist = to.magnitude;
                float spd = task == BeeTask.Follow ? beeMax : beeMax * 1.15f;
                float gain = task == BeeTask.Follow ? 3.2f : 6f;
                Vector2 desired = dist > 0.01f ? to / dist * Mathf.Min(spd, dist * gain) : Vector2.zero;

                float wander = task == BeeTask.Follow ? cfg.WanderAmp : cfg.WanderAmp * 0.45f;
                desired.x += Mathf.Sin(time * 2.3f + ph) * wander;
                desired.y += Mathf.Cos(time * 1.9f + ph * 1.37f) * wander;
                if (task == BeeTask.Follow) desired += LeaderVel * 0.3f; // выравнивание

                // разделение
                int nq = Grid.Query(p, sepDist, QueryBuffer);
                Vector2 sep = Vector2.zero;
                for (int q = 0; q < nq; q++)
                {
                    int j = QueryBuffer[q];
                    if (j == i) continue;
                    Vector2 dd = p - Pos[j];
                    float sq = dd.sqrMagnitude;
                    if (sq < sepSq && sq > 0.0001f)
                    {
                        float m = Mathf.Sqrt(sq);
                        sep += dd / m * (1f - m / sepDist);
                    }
                }
                desired += sep * cfg.SeparationForce;

                v = Vector2.MoveTowards(v, desired, cfg.BeeAccel * dt);
                float vm = v.magnitude;
                float vmax = beeMax * 1.3f;
                if (vm > vmax) v *= vmax / vm;
                p += v * dt;
                world.PushOut(ref p, ref v, 1.5f);
                p = world.ClampToWorld(p, 3f);

                // --- действия
                if (task == BeeTask.Gather)
                {
                    GatheringCount++;
                    if ((target - p).sqrMagnitude < 25f)
                    {
                        float got = flowers.Take(targetIdx, Mathf.Min(cfg.CollectRate * dt, cap - Carry[i]), cfg.FlowerRegrowTime, sim.Events);
                        Carry[i] += got;
                        sim.Stats.NectarCollected += got;
                        if (got > 0f && sim.Rng.Value < dt * 1.5f)
                            sim.Events.Push(SimEventType.NectarCollected, p, got, targetIdx);
                    }
                }
                else if (task == BeeTask.Deliver)
                {
                    if ((target - p).sqrMagnitude < 36f)
                    {
                        float amt = Carry[i];
                        colony.Honey += amt;
                        sim.Stats.HoneyDelivered += amt;
                        Carry[i] = 0f;
                        sim.Events.Push(SimEventType.NectarDeposited, p, amt);
                    }
                }
                else if (task == BeeTask.Attack)
                {
                    AttackingCount++;
                }

                Pos[i] = p;
                Vel[i] = v;
                Task[i] = task;
                TotalCarry += Carry[i];
            }
        }

        void CollectNearFlowers(FlowerField flowers, float reach)
        {
            _nearFlowerCount = 0;
            float reachSq = reach * reach;
            for (int f = 0; f < flowers.Count; f++)
            {
                if (!flowers.IsBlooming(f)) continue;
                float sq = (flowers.Pos[f] - LeaderPos).sqrMagnitude;
                if (sq > reachSq) continue;
                // вставка в отсортированный по дистанции список фиксированной длины
                int n = _nearFlowerCount;
                if (n == _nearFlowers.Length && sq >= _nearFlowerDist[n - 1]) continue;
                int pos = n < _nearFlowers.Length ? n : n - 1;
                while (pos > 0 && _nearFlowerDist[pos - 1] > sq)
                {
                    _nearFlowers[pos] = _nearFlowers[pos - 1];
                    _nearFlowerDist[pos] = _nearFlowerDist[pos - 1];
                    pos--;
                }
                _nearFlowers[pos] = f;
                _nearFlowerDist[pos] = sq;
                if (_nearFlowerCount < _nearFlowers.Length) _nearFlowerCount++;
            }
        }
    }
}
