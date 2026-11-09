using UnityEngine;

namespace Swarm.Simulation
{
    public enum BeeTask : byte { Follow, Gather, Attack, Deliver, Guard, Scout, Ball }
    public enum BeeRole : byte { Swarm, Guard, Squad, Worker }

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
        public readonly float[] Carry, Phase, SpeedMul, WanderMul, Hp, MaxHp, Stuck;
        public readonly BeeTask[] Task;
        public readonly BeeRole[] Role;
        public readonly int[] Forage;     // сборщица: цветок, к которому она летает
        public readonly bool[] Scout;
        public readonly bool[] Dead;
        public int Count;

        public Vector2 LeaderPos, LeaderVel;
        public Vector2 Heading = new Vector2(1f, 0f);
        public float Speed01;
        public float Radius;
        public float TotalCarry;
        /// <summary>Нектар в основном рое (без стражей, отряда и сборщиц) — для шкалы «неси в улей».</summary>
        public float FieldCarry;
        public int GatheringCount, AttackingCount, ScoutingCount;

        // защитники улья (Role.Guard) — отдельная каста, в рой и цель не входят
        public int GuardCount;
        // сборщицы (Role.Worker) — автономные, только носят нектар; в рой и цель не входят
        public int WorkerCount;
        public int SwarmCount => Count - GuardCount - WorkerCount;
        // отряд второго пальца (входит в рой и в цель)
        public int SquadCount;
        public bool SquadActive;
        public Vector2 SquadTarget;
        readonly int[] _squadFlowers = new int[4];
        readonly float[] _squadFlowerDist = new float[4];
        int _squadFlowerCount;

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
            Hp = new float[Capacity];
            MaxHp = new float[Capacity];
            Stuck = new float[Capacity];
            Task = new BeeTask[Capacity];
            Role = new BeeRole[Capacity];
            Forage = new int[Capacity];
            Scout = new bool[Capacity];
            Dead = new bool[Capacity];
            Grid = new SpatialGrid(cfg.WorldWidth, cfg.WorldHeight, 6f, Capacity);
        }

        public float ScoutChance = 0.07f;

        public bool SpawnDefender(Vector2 p, Rng rng, float hp)
        {
            if (!Spawn(p, rng)) return false;
            int i = Count - 1;
            Role[i] = BeeRole.Guard;
            Scout[i] = false;
            Hp[i] = MaxHp[i] = hp;
            SpeedMul[i] = 1.05f;
            GuardCount++;
            return true;
        }

        public bool SpawnWorker(Vector2 p, Rng rng)
        {
            if (!Spawn(p, rng)) return false;
            int i = Count - 1;
            Role[i] = BeeRole.Worker;
            Scout[i] = false;
            Forage[i] = -1;
            SpeedMul[i] = 0.8f + rng.Value * 0.1f;
            WorkerCount++;
            return true;
        }

        public bool Spawn(Vector2 p, Rng rng)
        {
            if (Count >= Capacity) return false;
            int i = Count++;
            Pos[i] = p + new Vector2(rng.Signed * 2f, rng.Value * 2f);
            Vel[i] = new Vector2(rng.Signed * 30f, 35f + rng.Value * 30f);   // вылет из летка вверх-вбок
            Carry[i] = 0f;
            Phase[i] = rng.Value * 100f;
            SpeedMul[i] = 0.88f + rng.Value * 0.24f;
            WanderMul[i] = 0.6f + rng.Value * 0.8f;
            Scout[i] = rng.Value < ScoutChance;
            Hp[i] = MaxHp[i] = 1f;
            Stuck[i] = 0f;
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
                else if (Role[i] == BeeRole.Worker) WorkerCount--;
                if (i != last)
                {
                    Pos[i] = Pos[last]; Vel[i] = Vel[last]; Carry[i] = Carry[last];
                    Phase[i] = Phase[last]; SpeedMul[i] = SpeedMul[last]; WanderMul[i] = WanderMul[last];
                    Hp[i] = Hp[last]; MaxHp[i] = MaxHp[last]; Stuck[i] = Stuck[last];
                    Scout[i] = Scout[last]; Task[i] = Task[last]; Role[i] = Role[last]; Dead[i] = Dead[last]; Forage[i] = Forage[last];
                }
                Dead[last] = false;
                Count--;
            }
            if (GuardCount < 0) GuardCount = 0;
            if (WorkerCount < 0) WorkerCount = 0;
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
            float maxSpd = cfg.LeaderSpeed * colony.SpeedMul * sim.Mods.Speed * sim.Sky.SpeedMul(time) * (sim.BuffSpeed > 0f ? cfg.VioletSpeedMul : 1f);
            Vector2 wind = sim.Sky.Wind;
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
            LeaderPos += (LeaderVel + wind * 0.8f) * dt;
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

            int guards = 0;
            int workers = 0;
            for (int i = 0; i < Count; i++) { if (Role[i] == BeeRole.Guard) guards++; else if (Role[i] == BeeRole.Worker) workers++; }
            GuardCount = guards;
            WorkerCount = workers;

            // ---------------- пост: отряд стоит там, где его оставили (команды PostSquad/RecallSquad)
            int squad = 0;
            for (int i = 0; i < Count; i++) if (Role[i] == BeeRole.Squad) squad++;
            SquadCount = squad;
            if (SquadActive && SquadCount == 0) SquadActive = false;   // отряд погиб целиком
            if (SquadActive) CollectFlowersAt(flowers, SquadTarget, cfg.PostRadius, _squadFlowers, _squadFlowerDist, ref _squadFlowerCount);

            Radius = cfg.SwarmRadiusBase + cfg.SwarmRadiusPerSqrtBee * Mathf.Sqrt(Mathf.Max(1, SwarmCount - SquadCount));
            float squadR = 6f + 2.6f * Mathf.Sqrt(Mathf.Max(1, SquadCount));

            // ---------------- контекст: цветы, улей, враги
            CollectFlowers(flowers, Radius + cfg.GatherReach, _nearFlowers, _nearFlowerDist, ref _nearFlowerCount);
            CollectFlowers(flowers, (Radius + cfg.GatherReach) * cfg.ScoutReachMul * sim.Mods.ScoutReach, _farFlowers, _farFlowerDist, ref _farFlowerCount);
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
                // защитники встречают любого врага у улья
                if ((enemies.Pos[e] - entrance).sqrMagnitude < guardRange * guardRange)
                    _guardCand[guardCandCount++] = e;
            }

            Grid.Build(Pos, Count);

            float beeMax = maxSpd * cfg.BeeSpeedFactor;
            float cap = cfg.CarryBase * colony.CarryMul * sim.Mods.Carry;
            float collectRate = cfg.CollectRate * sim.Mods.Collect * flowers.CollectMul;
            var hz = sim.Hazards;
            float sepDist = cfg.SeparationDist;
            float sepSq = sepDist * sepDist;
            float aggroSq = cfg.AggroRadius * cfg.AggroRadius;
            float leashSq = cfg.LeashDistance * cfg.LeashDistance;
            float stragglerSq = (Radius * 1.6f + 24f) * (Radius * 1.6f + 24f);
            float ballRangeSq = cfg.BallJoinRange * cfg.BallJoinRange;
            float invCount = 1f / Mathf.Max(1, SwarmCount - SquadCount);
            float invSquad = 1f / Mathf.Max(1, SquadCount);
            int squadIdx = 0;
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

                // застряла в паутине — бьётся на месте, пока рой не порвёт сеть
                if (Stuck[i] > 0f)
                {
                    if (!hz.StillStuck(p))
                        Stuck[i] = 0f;
                    else
                    {
                        Stuck[i] += dt;
                        Vel[i] = new Vector2(Mathf.Sin(time * 40f + ph), Mathf.Cos(time * 37f + ph)) * 6f;
                        Task[i] = BeeTask.Follow;
                        TotalCarry += Carry[i];
                        if (Role[i] == BeeRole.Swarm) { FieldCarry += Carry[i]; swarmIdx++; }
                        else if (Role[i] == BeeRole.Squad) squadIdx++;
                        else if (Role[i] == BeeRole.Guard) guardIdx++;
                        continue;
                    }
                }
                Vector2 target;
                int targetIdx = -1;

                if (Role[i] == BeeRole.Guard)
                {
                    // солдат у летка лечится
                    if (Hp[i] < MaxHp[i] && (p - entrance).sqrMagnitude < 50f * 50f)
                        Hp[i] = Mathf.Min(MaxHp[i], Hp[i] + cfg.DefenderRegen * sim.Mods.DefenderRegen * dt);
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
                else if (Role[i] == BeeRole.Worker)
                {
                    // --- сборщица: цветок → улей → цветок; не воюет
                    bool closed = flowers.CollectMul < 0.05f;   // ночь: цветы закрыты
                    if (Carry[i] >= cap * cfg.WorkerCarryMul - 0.01f || (Carry[i] > 0f && (closed || !ForageValid(i, flowers))))
                    {
                        task = BeeTask.Deliver;
                        target = entrance + new Vector2(Mathf.Sin(ph) * 3f, 0f);
                    }
                    else
                    {
                        if (!closed && !ForageValid(i, flowers)) Forage[i] = PickForage(i, flowers, entrance, sim);
                        if (!closed && Forage[i] >= 0)
                        {
                            task = BeeTask.Gather;
                            targetIdx = Forage[i];
                            float oa = ph * 3f + time * 4f;
                            target = flowers.Pos[targetIdx] + new Vector2(Mathf.Cos(oa) * 3f, Mathf.Sin(oa) * 2f);
                        }
                        else
                        {
                            // цветы закрыты (ночь) — ждут у улья
                            task = BeeTask.Guard;
                            float ga = time * 0.5f + ph;
                            target = entrance + new Vector2(Mathf.Cos(ga) * 26f, Mathf.Sin(ga) * 12f + 4f);
                        }
                    }
                }
                else if (Role[i] == BeeRole.Squad)
                {
                    // --- пост: стережёт место — жалит врагов в зоне поста, собирает рядом, сам носит нектар в улей
                    float k = Mathf.Sqrt((squadIdx + 0.5f) * invSquad);
                    float ang = squadIdx * 2.39996f + time * 0.4f;
                    squadIdx++;
                    task = BeeTask.Follow;
                    target = SquadTarget + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.82f) * (squadR * k);
                    float best = float.MaxValue;
                    float postSq = (cfg.PostRadius + 12f) * (cfg.PostRadius + 12f);
                    for (int e = 0; e < enemies.Count; e++)
                    {
                        if (_attackers[e] >= EnemySystem.MaxStingers(enemies.Type[e], cfg)) continue;
                        float sq = (enemies.Pos[e] - p).sqrMagnitude;
                        if (sq >= aggroSq && (enemies.Pos[e] - SquadTarget).sqrMagnitude >= postSq) continue;
                        if (sq < best) { best = sq; targetIdx = e; }
                    }
                    if (targetIdx >= 0)
                    {
                        task = BeeTask.Attack;
                        _attackers[targetIdx]++;
                        float oa = ph * 7f + time * 9f;
                        target = enemies.Pos[targetIdx] + new Vector2(Mathf.Cos(oa), Mathf.Sin(oa)) * (EnemySystem.RadiusOf(enemies.Type[targetIdx]) + 2f);
                    }
                    else if (Carry[i] < cap - 0.01f && _squadFlowerCount > 0)
                    {
                        task = BeeTask.Gather;
                        targetIdx = _squadFlowers[i % _squadFlowerCount];
                        float oa = ph * 3f + time * 4f;
                        target = flowers.Pos[targetIdx] + new Vector2(Mathf.Cos(oa) * 3f, Mathf.Sin(oa) * 2f);
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
                        // метка-приказ: отмеченный враг в приоритете и с удвоенным числом мест
                        int fe = sim.FocusEnemy;
                        if (fe >= 0 && _attackers[fe] < EnemySystem.MaxStingers(enemies.Type[fe], cfg) * 2 &&
                            (enemies.Pos[fe] - p).sqrMagnitude < (cfg.AggroRadius + 70f) * (cfg.AggroRadius + 70f))
                        { targetIdx = fe; best = -1f; }
                        for (int c = 0; c < candCount && best >= 0f; c++)
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

                desired += wind;
                v = Vector2.MoveTowards(v, desired, cfg.BeeAccel * dt);
                float vm = v.magnitude;
                float vmax = beeMax * 1.35f;
                if (vm > vmax) v *= vmax / vm;
                p += v * dt;
                world.PushOut(ref p, ref v, 1.5f);
                p = world.ClampToWorld(p, 3f);
                if (hz.CatchInWeb(p)) Stuck[i] = 0.001f;

                // --- действия
                if (task == BeeTask.Gather || task == BeeTask.Scout)
                {
                    if (task == BeeTask.Gather) GatheringCount++; else ScoutingCount++;
                    if ((target - p).sqrMagnitude < 25f)
                    {
                        bool worker = Role[i] == BeeRole.Worker;
                        float myCap = worker ? cap * cfg.WorkerCarryMul : cap;
                        float got = flowers.Take(targetIdx, Mathf.Max(0f, Mathf.Min(collectRate * (worker ? cfg.WorkerCollectMul : 1f) * dt, myCap - Carry[i])), cfg.FlowerRegrowTime, sim.Events);
                        if (task == BeeTask.Scout && got > 0f) sim.Fog.ScoutVisit(sim, targetIdx);
                        if (got > 0f && flowers.Rare[targetIdx] && !flowers.RareCredited[targetIdx])
                        {
                            flowers.RareCredited[targetIdx] = true;
                            sim.Stats.RareFound++;
                        }
                        Carry[i] += got;
                        sim.Stats.NectarCollected += got;
                        if (got > 0f && (Role[i] == BeeRole.Swarm || Role[i] == BeeRole.Squad))
                        {
                            var fk = flowers.Kind[targetIdx];
                            if (fk == FlowerKind.Poppy || fk == FlowerKind.Violet) sim.TakeBuff(fk, flowers.Pos[targetIdx]);
                        }
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
                if (Role[i] == BeeRole.Swarm) FieldCarry += Carry[i];   // отряд и сборщицы носят сами
            }
        }

        /// <summary>Оставить отряд стеречь текущее место роя. Берутся пчёлы без ноши, ближние к центру.</summary>
        public bool PostSquad(SimConfig cfg)
        {
            if (SquadActive || SwarmCount < cfg.PostMinSwarm) return false;
            int want = Mathf.Clamp(Mathf.RoundToInt(SwarmCount * cfg.PostShare), 6, cfg.PostMax);
            SquadActive = true;
            SquadTarget = LeaderPos;
            SquadCount = 0;
            for (int pass = 0; pass < 2 && SquadCount < want; pass++)
                for (int i = 0; i < Count && SquadCount < want; i++)
                    if (Role[i] == BeeRole.Swarm && !Dead[i] && (pass == 1 || Carry[i] <= 0f)) { Role[i] = BeeRole.Squad; SquadCount++; }
            return true;
        }

        /// <summary>Отряд возвращается в рой.</summary>
        public void RecallSquad()
        {
            for (int i = 0; i < Count; i++) if (Role[i] == BeeRole.Squad) Role[i] = BeeRole.Swarm;
            SquadActive = false;
            SquadCount = 0;
        }

        bool ForageValid(int i, FlowerField flowers)
        {
            int f = Forage[i];
            return f >= 0 && f < flowers.Count && flowers.IsBlooming(f) && flowers.Nectar[f] > 0.4f;
        }

        /// <summary>Цветок для сборщицы: близко к улью и с нектаром; разные сборщицы — разные цветы.</summary>
        int PickForage(int i, FlowerField flowers, Vector2 entrance, GameSim sim)
        {
            int best = -1, second = -1;
            float bs = float.MaxValue, ss = float.MaxValue;
            for (int f = 0; f < flowers.Count; f++)
            {
                if (!flowers.IsBlooming(f) || flowers.Nectar[f] < 1f) continue;
                float d = (flowers.Pos[f] - entrance).magnitude;
                float score = (d + 30f) / (1f + flowers.Nectar[f]) * (1f + 0.25f * Mathf.Sin(f * 12.9898f + i * 78.233f));
                if (score < bs) { ss = bs; second = best; bs = score; best = f; }
                else if (score < ss) { ss = score; second = f; }
            }
            return (i & 1) == 1 && second >= 0 ? second : best;
        }

        void CollectFlowersAt(FlowerField flowers, Vector2 c, float reach, int[] list, float[] dist, ref int count)
        {
            Vector2 keep = LeaderPos;
            LeaderPos = c;
            CollectFlowers(flowers, reach, list, dist, ref count);
            LeaderPos = keep;
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
