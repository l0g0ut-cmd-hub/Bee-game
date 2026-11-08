using UnityEngine;

namespace Swarm.Simulation
{
    public enum BeeTask : byte { Follow, Gather, Attack, Deliver, Guard, Scout, Ball }
    public enum BeeRole : byte { Swarm, Guard }

    /// <summary>
    /// Рой как структура массивов. Центр роя («лидер») ведёт игрок, каждая пчела — boid со своим характером.
    ///
    /// Организм:
    /// - форма зависит от движения: в полёте «комета» (плотная голова, шлейф), в покое — облако;
    /// - волна поворота: пчела целится в позицию лидера с задержкой по расстоянию от центра;
    /// - отставшие летят по феромонному следу лидера, а не напрямую сквозь препятствия;
    /// - характеры: разброс скорости/рысканья, ~7% разведчиц собирают с дальних цветов;
    /// - стражи держатся у улья и защищают его, пока рой в поле;
    /// - тепловой шар: палец удерживается на месте у врага — рой сжимается вокруг него.
    /// </summary>
    public sealed class BeeSwarm
    {
        public readonly int Capacity;
        public readonly Vector2[] Pos, Vel;
        public readonly float[] Carry, Phase, SpeedMul, WanderMul;
        public readonly BeeTask[] Task;
        public readonly BeeRole[] Role;
        public readonly bool[] Scout;
        public readonly bool[] Dead;
        public int Count;

        public Vector2 LeaderPos, LeaderVel;
        public Vector2 Heading = new Vector2(1f, 0f);
        public float Speed01;
        public float Radius;
        public float TotalCarry;
        /// <summary>Нектар в полевом рое (без стражей) — для шкалы «неси в улей».</summary>
        public float FieldCarry;
        public int GatheringCount, AttackingCount, ScoutingCount;

        // стражи
        public int GuardWanted;
        public int GuardCount;
        public int SwarmCount => Count - GuardCount;

        // тепловой шар
        public bool Balling;
        public int BallTarget = -1;
        public int BallCount;

        public readonly SpatialGrid Grid;
        public readonly int[] QueryBuffer = new int[256];

        readonly int[] _nearFlowers = new int[6];
        readonly float[] _nearFlowerDist = new float[6];
        int _nearFlowerCount;
        readonly int[] _farFlowers = new int[8];
        readonly float[] _farFlowerDist = new float[8];
        int _farFlowerCount;
        readonly int[] _enemyCand = new int[EnemySystem.Capacity];
        readonly int[] _guardCand = new int[EnemySystem.Capacity];
        readonly int[] _attackers = new int[EnemySystem.Capacity];
        int _deadCount;

        // история позиций лидера (волна поворота) — 30 Гц, ~2.4 c
        const float HistRate = 30f;
        readonly Vector2[] _hist = new Vector2[72];
        int _histHead;
        float _histAcc;

        // феромонный след для отставших — точка каждые ~10 px пути
        readonly Vector2[] _trail = new Vector2[40];
        int _trailHead, _trailCount;

        public static readonly Vector2 HiveEntranceOffset = new Vector2(0f, 12f);

        public BeeSwarm(SimConfig cfg)
        {
            Capacity = cfg.MaxBeesHard;
            Pos = new Vector2[Capacity];
            Vel = new Vector2[Capacity];
            Carry = new float[Capacity];
            Phase = new float[Capacity];
            SpeedMul = new float[Capacity];
            WanderMul = new float[Capacity];
            Task = new BeeTask[Capacity];
            Role = new BeeRole[Capacity];
            Scout = new bool[Capacity];
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
            SpeedMul[i] = 0.88f + rng.Value * 0.24f;
            WanderMul[i] = 0.6f + rng.Value * 0.8f;
            Scout[i] = rng.Value < 0.07f;
            Task[i] = BeeTask.Follow;
            Role[i] = BeeRole.Swarm;
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
                if (Role[i] == BeeRole.Guard) GuardCount--;
                if (i != last)
                {
                    Pos[i] = Pos[last]; Vel[i] = Vel[last]; Carry[i] = Carry[last];
                    Phase[i] = Phase[last]; SpeedMul[i] = SpeedMul[last]; WanderMul[i] = WanderMul[last];
                    Scout[i] = Scout[last]; Task[i] = Task[last]; Role[i] = Role[last]; Dead[i] = Dead[last];
                }
                Dead[last] = false;
                Count--;
            }
            if (GuardCount < 0) GuardCount = 0;
            _deadCount = 0;
        }

        public void ResetLeader(Vector2 p)
        {
            LeaderPos = p;
            LeaderVel = Vector2.zero;
            Speed01 = 0f;
            Balling = false;
            BallTarget = -1;
            for (int i = 0; i < _hist.Length; i++) _hist[i] = p;
            _histHead = 0;
            _trail[0] = p;
            _trailHead = 0;
            _trailCount = 1;
        }

        Vector2 HistAt(float delay)
        {
            int back = Mathf.Clamp(Mathf.RoundToInt(delay * HistRate), 0, _hist.Length - 1);
            int idx = _histHead - back;
            if (idx < 0) idx += _hist.Length;
            return back == 0 ? LeaderPos : _hist[idx];
        }

        /// <summary>
        /// Феромонный след: если прямую к рою перекрывает дерево/камень, отставшая летит к самой
        /// свежей точке пути роя, которую видно без препятствий. Иначе — напрямую (null-путь не нужен).
        /// </summary>
        bool TrailTarget(Vector2 p, Vector2 home, WorldMap world, out Vector2 target)
        {
            target = home;
            if (!world.SegmentBlocked(p, home, 2f)) return false;
            int look = Mathf.Min(_trailCount, 16);
            for (int c = 0; c < look; c++)
            {
                int idx = _trailHead - c;
                if (idx < 0) idx += _trail.Length;
                Vector2 tp = _trail[idx];
                if (!world.SegmentBlocked(p, tp, 2f)) { target = tp; return true; }
            }
            return false;
        }

        public void Step(float dt, float time, in SimInput input, GameSim sim)
        {
            var cfg = sim.Cfg;
            var world = sim.World;
            var flowers = sim.Flowers;
            var enemies = sim.Enemies;
            var colony = sim.Colony;
            Vector2 entrance = world.HivePos + HiveEntranceOffset;

            // ---------------- тепловой шар: палец удерживается у врага
            float maxSpd = cfg.LeaderSpeed * colony.SpeedMul;
            int ballE = -1;
            if (input.Active && input.Hold && SwarmCount >= cfg.BallMinBees)
            {
                float best = (Radius + cfg.BallLockRange) * (Radius + cfg.BallLockRange);
                for (int e = 0; e < enemies.Count; e++)
                {
                    float sq = (enemies.Pos[e] - LeaderPos).sqrMagnitude;
                    if (sq < best) { best = sq; ballE = e; }
                }
            }
            Balling = ballE >= 0;
            BallTarget = ballE;

            // ---------------- центр роя: пружина к пальцу с инерцией (в шаре — к цели шара)
            if (input.Active)
            {
                Vector2 tgt = Balling ? enemies.Pos[ballE] : input.Target;
                Vector2 to = tgt - LeaderPos;
                float d = to.magnitude;
                float lim = Balling ? maxSpd * 0.9f : maxSpd;
                Vector2 desired = d > 1f ? to / d * Mathf.Min(lim, d * 2.6f) : Vector2.zero;
                LeaderVel = Vector2.MoveTowards(LeaderVel, desired, cfg.LeaderAccel * dt);
            }
            else
            {
                LeaderVel = Vector2.MoveTowards(LeaderVel, Vector2.zero, cfg.LeaderBrake * dt);
            }
            LeaderPos += LeaderVel * dt;
            world.PushOut(ref LeaderPos, ref LeaderVel, 4f);
            LeaderPos = world.ClampToWorld(LeaderPos, 12f);

            // история и след
            _histAcc += dt;
            while (_histAcc >= 1f / HistRate)
            {
                _histAcc -= 1f / HistRate;
                _histHead = (_histHead + 1) % _hist.Length;
                _hist[_histHead] = LeaderPos;
            }
            if ((LeaderPos - _trail[_trailHead]).sqrMagnitude > 100f)
            {
                _trailHead = (_trailHead + 1) % _trail.Length;
                _trail[_trailHead] = LeaderPos;
                if (_trailCount < _trail.Length) _trailCount++;
            }

            float lv = LeaderVel.magnitude;
            Speed01 = Mathf.MoveTowards(Speed01, Mathf.Clamp01(lv / Mathf.Max(1f, maxSpd)), dt * 2.5f);
            if (lv > 4f)
            {
                Vector2 h = Heading + LeaderVel / lv * (dt * 6f);
                float hm = h.magnitude;
                if (hm > 0.001f) Heading = h / hm;
            }
            Vector2 perp = new Vector2(-Heading.y, Heading.x);

            // ---------------- стражи: назначаются у улья, снимаются в любом месте
            int guards = 0;
            for (int i = 0; i < Count; i++) if (Role[i] == BeeRole.Guard) guards++;
            if (guards < GuardWanted)
            {
                float rr = cfg.GuardRecruitRadius * cfg.GuardRecruitRadius;
                for (int i = 0; i < Count && guards < GuardWanted; i++)
                    if (Role[i] == BeeRole.Swarm && (Pos[i] - entrance).sqrMagnitude < rr) { Role[i] = BeeRole.Guard; guards++; }
            }
            else if (guards > GuardWanted)
            {
                for (int i = 0; i < Count && guards > GuardWanted; i++)
                    if (Role[i] == BeeRole.Guard) { Role[i] = BeeRole.Swarm; guards--; }
            }
            GuardCount = guards;

            Radius = cfg.SwarmRadiusBase + cfg.SwarmRadiusPerSqrtBee * Mathf.Sqrt(Mathf.Max(1, SwarmCount));

            // ---------------- контекст: цветы, улей, враги
            CollectFlowers(flowers, Radius + cfg.GatherReach, _nearFlowers, _nearFlowerDist, ref _nearFlowerCount);
            CollectFlowers(flowers, (Radius + cfg.GatherReach) * cfg.ScoutReachMul, _farFlowers, _farFlowerDist, ref _farFlowerCount);
            bool atHive = (LeaderPos - entrance).magnitude < cfg.HiveReach + Radius * 0.5f;

            int candCount = 0, guardCandCount = 0;
            float candRange = cfg.LeashDistance + cfg.AggroRadius;
            float guardRange = cfg.GuardDefendRadius;
            bool enemyNear = false;
            for (int e = 0; e < enemies.Count; e++)
            {
                _attackers[e] = 0;
                float sq = (enemies.Pos[e] - LeaderPos).sqrMagnitude;
                if (sq < candRange * candRange) _enemyCand[candCount++] = e;
                if (sq < (Radius + 60f) * (Radius + 60f)) enemyNear = true;
                // стражи вступают в бой только с грабителями улья
                if (enemies.Mode[e] != EnemyMode.Hunt && (enemies.Pos[e] - entrance).sqrMagnitude < guardRange * guardRange)
                    _guardCand[guardCandCount++] = e;
            }

            Grid.Build(Pos, Count);

            float beeMax = maxSpd * cfg.BeeSpeedFactor;
            float cap = cfg.CarryBase * colony.CarryMul;
            float sepDist = cfg.SeparationDist;
            float sepSq = sepDist * sepDist;
            float aggroSq = cfg.AggroRadius * cfg.AggroRadius;
            float leashSq = cfg.LeashDistance * cfg.LeashDistance;
            float stragglerSq = (Radius * 1.6f + 24f) * (Radius * 1.6f + 24f);
            float ballRangeSq = cfg.BallJoinRange * cfg.BallJoinRange;
            float invCount = 1f / Mathf.Max(1, SwarmCount);
            float s = Speed01;
            TotalCarry = 0f;
            FieldCarry = 0f;
            GatheringCount = 0;
            AttackingCount = 0;
            ScoutingCount = 0;
            BallCount = 0;
            int swarmIdx = 0, guardIdx = 0;

            for (int i = 0; i < Count; i++)
            {
                Vector2 p = Pos[i];
                Vector2 v = Vel[i];
                float ph = Phase[i];
                BeeTask task;
                Vector2 target;
                int targetIdx = -1;

                if (Role[i] == BeeRole.Guard)
                {
                    // --- страж: кружит у летка, атакует врагов у улья, сдаёт ношу
                    float ga = time * (0.7f + 0.2f * Mathf.Sin(ph)) * ((guardIdx & 1) == 0 ? 1f : -1f) + ph;
                    float gr = 16f + (guardIdx % 4) * 5f;
                    guardIdx++;
                    task = BeeTask.Guard;
                    target = entrance + new Vector2(Mathf.Cos(ga) * gr, Mathf.Sin(ga) * gr * 0.6f + 6f);
                    float best = guardRange * guardRange;
                    for (int c = 0; c < guardCandCount; c++)
                    {
                        int e = _guardCand[c];
                        if (_attackers[e] >= EnemySystem.MaxStingers(enemies.Type[e], cfg)) continue;
                        float sq = (enemies.Pos[e] - p).sqrMagnitude;
                        if (sq < best) { best = sq; targetIdx = e; }
                    }
                    if (targetIdx >= 0)
                    {
                        task = BeeTask.Attack;
                        _attackers[targetIdx]++;
                        float oa = ph * 7f + time * 9f;
                        target = enemies.Pos[targetIdx] + new Vector2(Mathf.Cos(oa), Mathf.Sin(oa)) * (EnemySystem.RadiusOf(enemies.Type[targetIdx]) + 2f);
                    }
                    else if (Carry[i] > 0f)
                    {
                        task = BeeTask.Deliver;
                        target = entrance;
                    }
                }
                else
                {
                    // --- слот формации: спираль с золотым углом, медленно вращается — рой «дышит»
                    float k = Mathf.Sqrt((swarmIdx + 0.5f) * invCount);
                    float ang = swarmIdx * 2.39996f + time * (0.25f + 0.1f * Mathf.Sin(ph));
                    swarmIdx++;
                    float breathe = 1f + 0.12f * Mathf.Sin(time * 1.3f + ph * 0.1f);
                    Vector2 o = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.82f) * (Radius * k * breathe);
                    // форма: в полёте — комета (шлейф назад, сужение поперёк), в покое — облако
                    float a = Vector2.Dot(o, Heading), b = Vector2.Dot(o, perp);
                    a = a * (1f + 0.15f * s) - Radius * cfg.CometTail * s * k;
                    b *= 1f - 0.35f * s;
                    o = Heading * a + perp * b;
                    if (Balling) o *= 0.35f;
                    else if (s < 0.15f) o *= 1f + (0.15f - s) * 1.4f;
                    // волна поворота: дальние от центра пчёлы следуют за лидером с задержкой
                    Vector2 anchor = HistAt(cfg.TurnWaveDelay * k * s);
                    Vector2 home = anchor + o;

                    task = BeeTask.Follow;
                    target = home;
                    float distLeaderSq = (p - LeaderPos).sqrMagnitude;

                    if (Balling && (enemies.Pos[ballE] - p).sqrMagnitude < ballRangeSq)
                    {
                        // шар: плотно облепить цель, ограничений по числу нет
                        task = BeeTask.Ball;
                        targetIdx = ballE;
                        float oa = ph * 7f + time * 11f;
                        float r = EnemySystem.RadiusOf(enemies.Type[ballE]) + 1f + k * 3f;
                        target = enemies.Pos[ballE] + new Vector2(Mathf.Cos(oa), Mathf.Sin(oa)) * r;
                    }
                    else if (distLeaderSq < leashSq)
                    {
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
                        else if (Scout[i] && !atHive && !enemyNear && s < 0.35f && Carry[i] < cap - 0.01f && _farFlowerCount > 0)
                        {
                            // разведчица отлетает к дальнему цветку и возвращается с нектаром
                            task = BeeTask.Scout;
                            targetIdx = _farFlowers[(i + (int)(time * 0.2f)) % _farFlowerCount];
                            float oa = ph * 3f + time * 4f;
                            target = flowers.Pos[targetIdx] + new Vector2(Mathf.Cos(oa) * 3f, Mathf.Sin(oa) * 2f);
                        }
                    }
                    if (task == BeeTask.Follow && distLeaderSq > stragglerSq)
                        TrailTarget(p, home, world, out target); // отставшая — в обход по следу роя
                }

                // --- рулёжка
                Vector2 to = target - p;
                float dist = to.magnitude;
                float spd = beeMax * SpeedMul[i] * (task == BeeTask.Follow || task == BeeTask.Guard ? 1f : 1.2f);
                float gain = task == BeeTask.Follow ? 3.2f : 6f;
                Vector2 desired = dist > 0.01f ? to / dist * Mathf.Min(spd, dist * gain) : Vector2.zero;

                float wander = cfg.WanderAmp * WanderMul[i] * (task == BeeTask.Follow ? 1f : (task == BeeTask.Ball ? 0.25f : 0.45f));
                desired.x += Mathf.Sin(time * 2.3f + ph) * wander;
                desired.y += Mathf.Cos(time * 1.9f + ph * 1.37f) * wander;
                if (task == BeeTask.Follow) desired += LeaderVel * 0.3f; // выравнивание

                // разделение (в шаре пчёлы жмутся плотнее)
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
                desired += sep * (cfg.SeparationForce * (task == BeeTask.Ball ? 0.35f : 1f));

                v = Vector2.MoveTowards(v, desired, cfg.BeeAccel * dt);
                float vm = v.magnitude;
                float vmax = beeMax * 1.35f;
                if (vm > vmax) v *= vmax / vm;
                p += v * dt;
                world.PushOut(ref p, ref v, 1.5f);
                p = world.ClampToWorld(p, 3f);

                // --- действия
                if (task == BeeTask.Gather || task == BeeTask.Scout)
                {
                    if (task == BeeTask.Gather) GatheringCount++; else ScoutingCount++;
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
                else if (task == BeeTask.Attack) AttackingCount++;
                else if (task == BeeTask.Ball) { AttackingCount++; BallCount++; }

                Pos[i] = p;
                Vel[i] = v;
                Task[i] = task;
                TotalCarry += Carry[i];
                if (Role[i] == BeeRole.Swarm) FieldCarry += Carry[i];
            }
        }

        void CollectFlowers(FlowerField flowers, float reach, int[] list, float[] dist, ref int count)
        {
            count = 0;
            float reachSq = reach * reach;
            for (int f = 0; f < flowers.Count; f++)
            {
                if (!flowers.IsBlooming(f)) continue;
                float sq = (flowers.Pos[f] - LeaderPos).sqrMagnitude;
                if (sq > reachSq) continue;
                // вставка в отсортированный по дистанции список фиксированной длины
                int n = count;
                if (n == list.Length && sq >= dist[n - 1]) continue;
                int pos = n < list.Length ? n : n - 1;
                while (pos > 0 && dist[pos - 1] > sq)
                {
                    list[pos] = list[pos - 1];
                    dist[pos] = dist[pos - 1];
                    pos--;
                }
                list[pos] = f;
                dist[pos] = sq;
                if (count < list.Length) count++;
            }
        }
    }
}
