using UnityEngine;

namespace Swarm.Simulation
{
    public enum EnemyType : byte { Wasp, Hornet, Bear, Nest, Ant, Queen, WildHive, Rival }
    public enum EnemyMode : byte { Hunt, Raid, Flee }
    public enum EnemyAct : byte { Move, Windup, Dash }

    /// <summary>
    /// Осы и шершни. Осы охотятся на рой, если он рядом, иначе грабят улей и уносят мёд.
    /// Шершни всегда идут на рой. Атака телеграфируется: враг замирает и дрожит (windup),
    /// затем делает рывок и кусает всех на пути — рой можно увести из-под удара.
    /// Тепловой шар: если рой облепил врага в режиме шара, урон растёт, но пчёлы гибнут от перегрева.
    /// </summary>
    public sealed class EnemySystem
    {
        public const int Capacity = 64;
        public readonly EnemyType[] Type = new EnemyType[Capacity];
        public readonly EnemyMode[] Mode = new EnemyMode[Capacity];
        public readonly EnemyAct[] Act = new EnemyAct[Capacity];
        public readonly Vector2[] Pos = new Vector2[Capacity];
        public readonly Vector2[] Vel = new Vector2[Capacity];
        public readonly Vector2[] DashDir = new Vector2[Capacity];
        public readonly float[] Hp = new float[Capacity];
        public readonly float[] MaxHp = new float[Capacity];
        public readonly float[] BiteTimer = new float[Capacity];
        public readonly float[] ActTimer = new float[Capacity];
        public readonly float[] DashCd = new float[Capacity];
        public readonly float[] Flash = new float[Capacity];
        public readonly float[] Stolen = new float[Capacity];
        public readonly float[] Phase = new float[Capacity];
        public readonly float[] Heat = new float[Capacity];
        public readonly int[] Stingers = new int[Capacity];
        public readonly byte[] Stage = new byte[Capacity];
        public readonly int[] Uid = new int[Capacity];
        public readonly int[] Aux = new int[Capacity];      // дикая пчела: цветок, с которого носит

        // --- дикий рой (конкурент): свой улей, свои сборщицы
        public bool WildAlive;
        public float WildHoney;
        public int RivalCount;
        bool _wildAttacked;
        float _wildTimer;      // постоянный id (индексы меняются при удалении)
        int _nextUid = 1;

        public int IndexOfUid(int uid)
        {
            if (uid <= 0) return -1;
            for (int i = 0; i < Count; i++) if (Uid[i] == uid) return i;
            return -1;
        }   // фаза босса (0 → 1 при 60% → 2 при 30%)
        public int Count;

        public static float RadiusOf(EnemyType t)
        {
            switch (t) { case EnemyType.Wasp: return 5f; case EnemyType.Hornet: return 8f; case EnemyType.Bear: return 15f; case EnemyType.Nest: return 11f; case EnemyType.Queen: return 12f; case EnemyType.WildHive: return 12f; default: return 3f; }
        }
        public static float SpeedOf(EnemyType t)
        {
            switch (t) { case EnemyType.Wasp: return 50f; case EnemyType.Hornet: return 40f; case EnemyType.Bear: return 20f; case EnemyType.Nest: return 0f; case EnemyType.Queen: return 34f; case EnemyType.WildHive: return 0f; case EnemyType.Rival: return 46f; default: return 13f; }
        }
        public static float HpOf(EnemyType t)
        {
            switch (t) { case EnemyType.Wasp: return 10f; case EnemyType.Hornet: return 45f; case EnemyType.Bear: return 300f; case EnemyType.Nest: return 220f; case EnemyType.Queen: return 1000f; case EnemyType.WildHive: return 260f; case EnemyType.Rival: return 4f; default: return 3f; }
        }
        public static float BiteCooldown(EnemyType t)
        {
            switch (t) { case EnemyType.Wasp: return 1.1f; case EnemyType.Hornet: return 0.85f; case EnemyType.Bear: return 1.4f; case EnemyType.Nest: return 9999f; case EnemyType.Queen: return 0.75f; case EnemyType.WildHive: return 9999f; case EnemyType.Rival: return 1.8f; default: return 2.4f; }
        }
        static int BiteKills(EnemyType t) => t == EnemyType.Bear || t == EnemyType.Queen ? 3 : (t == EnemyType.Nest || t == EnemyType.WildHive ? 0 : 1);   // медведь бьёт лапой по площади
        public static bool Flies(EnemyType t) => t == EnemyType.Wasp || t == EnemyType.Hornet || t == EnemyType.Queen || t == EnemyType.Rival;
        public static bool IsStatic(EnemyType t) => t == EnemyType.Nest || t == EnemyType.WildHive;
        public static bool IsBoss(EnemyType t) => t == EnemyType.Queen;
        static float WindupTime(EnemyType t) => t == EnemyType.Wasp ? 0.45f : t == EnemyType.Queen ? 0.8f : 0.65f;
        static float DashTime(EnemyType t) => t == EnemyType.Wasp ? 0.3f : t == EnemyType.Queen ? 0.5f : 0.45f;
        static float DashRange(EnemyType t) => t == EnemyType.Wasp ? 42f : t == EnemyType.Queen ? 75f : 58f;
        static float DashCooldown(EnemyType t) => t == EnemyType.Wasp ? 7f : t == EnemyType.Queen ? 4f : 5f;
        static int DashMaxKills(EnemyType t) => t == EnemyType.Wasp ? 1 : t == EnemyType.Queen ? 4 : 2;

        /// <summary>Сколько пчёл одновременно помещается вокруг врага.</summary>
        public static int MaxStingers(EnemyType t, SimConfig cfg)
        {
            switch (t)
            {
                case EnemyType.Wasp: return cfg.MaxStingersPerEnemy;
                case EnemyType.Hornet: return cfg.MaxStingersPerEnemy * 2;
                case EnemyType.Ant: return 5;
                case EnemyType.Rival: return 3;
                case EnemyType.Queen: return cfg.MaxStingersPerEnemy * 3;
                default: return cfg.MaxStingersPerEnemy * 4;
            }
        }

        const float WaspAggro = 120f;
        static float StealRate(EnemyType t) => t == EnemyType.Bear ? 16f : t == EnemyType.Ant ? 2f : 7f;
        static float StealMax(EnemyType t) => t == EnemyType.Bear ? 140f : t == EnemyType.Ant ? 6f : 25f;
        /// <summary>Сила врагов (выживание: растёт с каждым кругом).</summary>
        public float HpMul = 1f, BiteMul = 1f;
        public int NestIndex = -1;    // индекс гнезда (обновляется при перестановках)
        public bool NestAlive;
        public int NestBrood;         // сколько ос сейчас вылетело из гнезда на защиту
        float _nestSpawnTimer;

        readonly int[] _dashKills = new int[Capacity];

        public void Clear() { Count = 0; NestAlive = false; WildAlive = false; WildHoney = 0f; RivalCount = 0; HpMul = 1f; BiteMul = 1f; }

        public bool Spawn(EnemyType t, Vector2 p, Rng rng)
        {
            if (Count >= Capacity) return false;
            int i = Count++;
            Type[i] = t;
            Mode[i] = t == EnemyType.Hornet || t == EnemyType.Queen ? EnemyMode.Hunt : EnemyMode.Raid;
            Stage[i] = 0;
            Uid[i] = _nextUid++;
            if (!Flies(t)) DashCd[i] = 9999f;
            if (t == EnemyType.Nest) { NestAlive = true; NestBrood = 0; _nestSpawnTimer = 0f; }
            if (t == EnemyType.WildHive) { WildAlive = true; WildHoney = 0f; _wildTimer = 2f; }
            Act[i] = EnemyAct.Move;
            Pos[i] = p;
            Vel[i] = Vector2.zero;
            DashDir[i] = new Vector2(1f, 0f);
            Hp[i] = MaxHp[i] = HpOf(t) * (t == EnemyType.Nest ? 1f : HpMul);
            BiteTimer[i] = 1f;
            ActTimer[i] = 0f;
            DashCd[i] = 1.5f + rng.Value * 1.5f;
            Flash[i] = 0f;
            Stolen[i] = 0f;
            Phase[i] = rng.Value * 100f;
            Heat[i] = 0f;
            Stingers[i] = 0;
            _dashKills[i] = 0;
            Aux[i] = -1;
            if (t == EnemyType.Rival) DashCd[i] = 9999f;   // дикие пчёлы не делают рывков
            return true;
        }

        void RemoveAt(int i)
        {
            int last = Count - 1;
            if (i != last)
            {
                Type[i] = Type[last]; Mode[i] = Mode[last]; Act[i] = Act[last]; Pos[i] = Pos[last]; Vel[i] = Vel[last];
                DashDir[i] = DashDir[last]; Hp[i] = Hp[last]; MaxHp[i] = MaxHp[last]; BiteTimer[i] = BiteTimer[last];
                ActTimer[i] = ActTimer[last]; DashCd[i] = DashCd[last]; Flash[i] = Flash[last]; Stolen[i] = Stolen[last];
                Phase[i] = Phase[last]; Heat[i] = Heat[last]; Stingers[i] = Stingers[last]; _dashKills[i] = _dashKills[last]; Stage[i] = Stage[last]; Uid[i] = Uid[last]; Aux[i] = Aux[last];
            }
            Count--;
        }

        static void KillBee(GameSim sim, int b, EnemyType t, bool heat, bool dash = false) =>
            sim.HurtBee(b, heat ? 99 : (int)t, heat, dash);

        public void Step(float dt, float time, GameSim sim)
        {
            var swarm = sim.Swarm;
            var world = sim.World;
            var colony = sim.Colony;
            var ev = sim.Events;
            var cfg = sim.Cfg;
            var buf = swarm.QueryBuffer;
            Vector2 entrance = world.HivePos + BeeSwarm.HiveEntranceOffset;
            float stingMul = colony.StingMul * sim.Mods.Dps * (sim.BuffSting > 0f ? sim.Cfg.PoppyStingMul : 1f);
            int ballTarget = swarm.Balling ? swarm.BallTarget : -1;
            bool grounded = sim.Sky.EnemiesGrounded(sim.Time);
            Vector2 wind = sim.Sky.Wind;
            _nestSpawnTimer -= dt;
            _wildTimer -= dt;
            int rivals = 0;
            bool wildAttacked = false;
            for (int k = 0; k < Count; k++)
            {
                if (Type[k] == EnemyType.Rival) rivals++;
                else if (Type[k] == EnemyType.WildHive && Stingers[k] > 0) wildAttacked = true;
            }
            RivalCount = rivals;
            _wildAttacked = wildAttacked;
            Vector2 wildHome = world.WildHivePos;
            bool wildDefend = WildAlive && (_wildAttacked || (swarm.Count > 0 && (swarm.LeaderPos - wildHome).sqrMagnitude < 70f * 70f));

            int i = 0;
            while (i < Count)
            {
                EnemyType t = Type[i];
                float r = RadiusOf(t);
                Vector2 p = Pos[i];
                if (Flash[i] > 0f) Flash[i] -= dt;
                BiteTimer[i] -= dt;
                DashCd[i] -= dt;

                // --- выбор цели
                Vector2 target;
                float speed = SpeedOf(t);
                if (Mode[i] != EnemyMode.Flee)
                {
                    bool swarmClose = swarm.Count > 0 && (swarm.LeaderPos - p).sqrMagnitude < WaspAggro * WaspAggro;
                    if (!Flies(t)) Mode[i] = IsStatic(t) ? EnemyMode.Hunt : EnemyMode.Raid;   // медведю и муравьям нужен только мёд
                    else Mode[i] = ((t == EnemyType.Hornet || t == EnemyType.Queen) && swarm.Count > 0) || swarmClose ? EnemyMode.Hunt : EnemyMode.Raid;
                }

                // ночью и в ливень осы и шершни улетают в укрытие (королева — нет)
                if (grounded && Flies(t) && !IsBoss(t) && t != EnemyType.Rival && Mode[i] != EnemyMode.Flee) Mode[i] = EnemyMode.Flee;

                // королева: на 60% и 30% здоровья зовёт свиту и злеет
                if (t == EnemyType.Queen)
                {
                    float hpf = Hp[i] / MaxHp[i];
                    int want = hpf < 0.3f ? 2 : hpf < 0.6f ? 1 : 0;
                    if (want > Stage[i])
                    {
                        Stage[i] = (byte)want;
                        int n = want == 1 ? 3 : 2;
                        for (int k = 0; k < n; k++)
                        {
                            var et = want == 2 && k == 0 ? EnemyType.Hornet : EnemyType.Wasp;
                            if (Spawn(et, p + new Vector2(sim.Rng.Signed, sim.Rng.Signed) * 14f, sim.Rng))
                            {
                                Mode[Count - 1] = EnemyMode.Hunt;
                                ev.Push(SimEventType.EnemySpawned, p, 0f, (int)et);
                            }
                        }
                        ev.Push(SimEventType.BossPhase, p, want, i);
                    }
                    if (Stage[i] > 0) speed *= 1f + 0.12f * Stage[i];
                }

                // гнездо: стоит на месте; когда его жалят — выпускает ос на защиту
                if (t == EnemyType.WildHive)
                {
                    // дикий улей выпускает своих сборщиц; когда его жалят — чаще и больше
                    int cap = _wildAttacked ? 14 : 9;
                    if (_wildTimer <= 0f && rivals < cap)
                    {
                        _wildTimer = _wildAttacked ? 2.5f : 6f;
                        if (Spawn(EnemyType.Rival, p + new Vector2(sim.Rng.Signed * 6f, -4f), sim.Rng)) rivals++;
                    }
                }
                else if (IsStatic(t))
                {
                    if (Stingers[i] > 0 && _nestSpawnTimer <= 0f && NestBrood < 6)
                    {
                        _nestSpawnTimer = 5f;
                        for (int k = 0; k < 2; k++)
                            if (Spawn(EnemyType.Wasp, p + new Vector2(sim.Rng.Signed, sim.Rng.Signed) * 8f, sim.Rng))
                            {
                                Mode[Count - 1] = EnemyMode.Hunt;
                                NestBrood++;
                                ev.Push(SimEventType.EnemySpawned, p, 0f, (int)EnemyType.Wasp);
                            }
                    }
                    else if (Stingers[i] == 0 && _nestSpawnTimer <= 0f)
                    {
                        _nestSpawnTimer = 6f;
                        if (NestBrood > 0) NestBrood--;   // гнездо постепенно «перезаряжается»
                    }
                }

                if (t == EnemyType.Rival)
                {
                    // дикая пчела: защищает свой улей, иначе мирно собирает; без улья — улетает
                    if (!WildAlive) Mode[i] = EnemyMode.Flee;
                    else Mode[i] = wildDefend && swarm.Count > 0 ? EnemyMode.Hunt : EnemyMode.Raid;
                }

                if (t == EnemyType.Bear && Mode[i] != EnemyMode.Flee && Hp[i] < MaxHp[i] * 0.35f)
                {
                    Mode[i] = EnemyMode.Flee;   // искусанный медведь отступает
                    sim.Stats.BearsRepelled++;
                    ev.Push(SimEventType.BearRepelled, p, Stolen[i], i);
                }
                if (Mode[i] == EnemyMode.Flee)
                {
                    target = EdgeExit(p, world);
                    speed *= 1.15f;
                    Act[i] = EnemyAct.Move;
                }
                else if (Mode[i] == EnemyMode.Hunt)
                {
                    target = NearestBee(p, 40f, swarm, out _);
                    if (target.x < 0f) target = swarm.LeaderPos;
                }
                else if (t == EnemyType.Rival)
                {
                    target = RivalForage(i, p, dt, sim, wildHome);
                }
                else
                {
                    target = entrance + new Vector2(0f, 4f);
                    if ((p - target).sqrMagnitude < (14f + r) * (14f + r))
                    {
                        float take = Mathf.Min(StealRate(t) * dt, Mathf.Min(colony.Honey, StealMax(t) - Stolen[i]));
                        if (take > 0f)
                        {
                            colony.Honey -= take;
                            Stolen[i] += take;
                            sim.Stats.HoneyStolen += take;
                            if (sim.Rng.Value < dt * 3f) ev.Push(SimEventType.HoneyStolen, p, take, i);
                        }
                        if (Stolen[i] >= StealMax(t) - 0.01f || colony.Honey <= 0.01f) Mode[i] = EnemyMode.Flee;
                    }
                }

                // --- телеграф и рывок (только на охоте)
                Vector2 v;
                if (Act[i] == EnemyAct.Move && Flies(t) && Mode[i] == EnemyMode.Hunt && DashCd[i] <= 0f &&
                    (target - p).sqrMagnitude < DashRange(t) * DashRange(t))
                {
                    Act[i] = EnemyAct.Windup;
                    ActTimer[i] = WindupTime(t);
                    Vector2 d = target - p;
                    float dm = d.magnitude;
                    DashDir[i] = dm > 0.01f ? d / dm : new Vector2(1f, 0f);
                    ev.Push(SimEventType.EnemyWindup, p, 0f, (int)t);
                }

                if (Act[i] == EnemyAct.Windup)
                {
                    // замирает, дрожит и доводит прицел — у игрока есть доля секунды увести рой
                    ActTimer[i] -= dt;
                    Vector2 d = target - p;
                    float dm = d.magnitude;
                    if (dm > 0.01f) DashDir[i] = (DashDir[i] * 0.85f + d / dm * 0.15f).normalized;
                    v = Vector2.MoveTowards(Vel[i], Vector2.zero, 400f * dt);
                    if (ActTimer[i] <= 0f)
                    {
                        Act[i] = EnemyAct.Dash;
                        ActTimer[i] = DashTime(t);
                        _dashKills[i] = 0;
                    }
                }
                else if (Act[i] == EnemyAct.Dash)
                {
                    ActTimer[i] -= dt;
                    v = DashDir[i] * (speed * 3f);
                    if (ActTimer[i] <= 0f)
                    {
                        Act[i] = EnemyAct.Move;
                        DashCd[i] = DashCooldown(t);
                    }
                }
                else if (IsStatic(t))
                {
                    v = Vector2.zero;
                }
                else
                {
                    Vector2 to = target - p;
                    float d = to.magnitude;
                    Vector2 desired = d > 0.5f ? to / d * Mathf.Min(speed, d * 4f) : Vector2.zero;
                    float ph = Phase[i];
                    if (Flies(t))
                    {
                        desired += new Vector2(Mathf.Sin(time * 3.1f + ph), Mathf.Cos(time * 2.7f + ph * 1.7f)) * 18f;
                        desired += wind;
                    }
                    for (int j = 0; j < Count; j++)
                    {
                        if (j == i) continue;
                        Vector2 dd = p - Pos[j];
                        float sq = dd.sqrMagnitude;
                        float rr = r + RadiusOf(Type[j]) + 2f;
                        if (sq < rr * rr && sq > 0.001f) desired += dd / Mathf.Sqrt(sq) * 30f;
                    }
                    v = Vector2.MoveTowards(Vel[i], desired, 160f * dt);
                }

                // --- урон от жал (в тепловом шаре — больше мест и прогрев)
                bool balled = i == ballTarget && Flies(t);   // медведя, гнездо и муравьёв шаром не сварить
                int stingers = 0, guardStingers = 0;
                float stingR = r + cfg.StingRange + 2f + (balled ? 2f : 0f);
                float stingRSq = stingR * stingR;
                int nq = swarm.Grid.Query(p, stingR, buf);
                for (int q = 0; q < nq; q++)
                {
                    int b = buf[q];
                    if (b >= swarm.Count || swarm.Dead[b]) continue;
                    if ((swarm.Pos[b] - p).sqrMagnitude < stingRSq)
                    {
                        stingers++;
                        if (swarm.Role[b] == BeeRole.Guard) guardStingers++;
                    }
                }
                if (guardStingers > stingers) guardStingers = stingers;
                int maxSt = MaxStingers(t, cfg);
                if (Uid[i] == sim.FocusUid) maxSt = maxSt * 3 / 2;   // по отмеченному врагу бьёт больше пчёл
                if (balled) maxSt = Mathf.RoundToInt(maxSt * (t == EnemyType.Queen ? 1.4f : cfg.BallStingerMul));
                if (stingers > maxSt) { guardStingers = guardStingers * maxSt / stingers; stingers = maxSt; }
                Stingers[i] = stingers;
                Heat[i] = Mathf.MoveTowards(Heat[i], balled && stingers > 0 ? 1f : 0f, dt * 1.5f);
                if (stingers > 0)
                {
                    float power = stingers + guardStingers * (cfg.GuardStingMul - 1f);
                    float dmg = power * cfg.BeeDps * stingMul * dt * (balled ? (t == EnemyType.Queen ? 1.4f : cfg.HeatDamageMul) : 1f);
                    Hp[i] -= dmg;
                    if (Flash[i] <= 0f)
                    {
                        Flash[i] = 0.12f;
                        ev.Push(SimEventType.EnemyHit, p, dmg, (int)t);
                    }
                    if (IsStatic(t)) { }
                    else if (balled && stingers >= (t == EnemyType.Queen ? 16 : 6))
                    {
                        // в шаре враг обездвижен: только трепыхается на месте, рывок срывается
                        v = Vector2.MoveTowards(v, Vector2.zero, 900f * dt) + new Vector2(sim.Rng.Signed, sim.Rng.Signed) * 12f;
                        if (Act[i] != EnemyAct.Move) { Act[i] = EnemyAct.Move; DashCd[i] = DashCooldown(t); }
                    }
                    else if (Act[i] != EnemyAct.Dash)
                    {
                        // облепившие пчёлы сбивают с курса (толчок ограничен — иначе врага «отшвыривает»)
                        v += new Vector2(sim.Rng.Signed, sim.Rng.Signed) * (Mathf.Min(stingers, 8) * 6f);
                        v *= 1f - Mathf.Min(0.5f, stingers * 0.03f) * dt * 10f;
                    }
                    // перегрев: пчёлы внутри шара гибнут
                    if (balled && sim.Rng.Value < stingers * cfg.HeatDeathRate * sim.Mods.HeatDeath * dt)
                    {
                        NearestBee(p, stingR, swarm, out int hot);
                        if (hot >= 0) KillBee(sim, hot, t, true);
                    }
                }

                // --- укусы: в рывке — по всем на пути, иначе по кулдауну
                if (Mode[i] != EnemyMode.Flee)
                {
                    if (Act[i] == EnemyAct.Dash)
                    {
                        if (_dashKills[i] < DashMaxKills(t) && BiteTimer[i] <= 0f)
                        {
                            NearestBee(p, r + 5f, swarm, out int victim);
                            if (victim >= 0)
                            {
                                KillBee(sim, victim, t, false, true);
                                _dashKills[i]++;
                                BiteTimer[i] = 0.08f;
                            }
                        }
                    }
                    else if (BiteTimer[i] <= 0f && Act[i] == EnemyAct.Move && (t != EnemyType.Rival || Mode[i] == EnemyMode.Hunt))
                    {
                        int hits = 0;
                        for (int k = 0; k < BiteKills(t); k++)
                        {
                            NearestBee(p, r + 4f, swarm, out int victim);
                            if (victim < 0) break;
                            KillBee(sim, victim, t, false);
                            hits++;
                        }
                        if (hits > 0)
                        {
                            float slow = t == EnemyType.Wasp && Mode[i] == EnemyMode.Raid ? cfg.RaidBiteSlow : 1f;
                            BiteTimer[i] = BiteCooldown(t) * slow * sim.Mods.EnemyBiteSlow / BiteMul;
                            if (t == EnemyType.Bear) ev.Push(SimEventType.BearSwipe, p, hits, i);
                        }
                        else BiteTimer[i] = 0.1f;
                    }
                }

                p += v * dt;
                Pos[i] = p;
                Vel[i] = v;

                // --- гибель / побег
                if (Hp[i] <= 0f)
                {
                    ev.Push(SimEventType.EnemyKilled, p, balled ? 1f : 0f, (int)t);
                    if (t == EnemyType.Nest) { NestAlive = false; sim.Stats.NestsDestroyed++; ev.Push(SimEventType.NestDestroyed, p, 0f, 0); }
                    if (balled && t == EnemyType.Hornet) sim.Stats.HornetsCooked++;
                    if (t == EnemyType.Ant) sim.Stats.AntsKilled++;
                    if (t == EnemyType.WildHive)
                    {
                        // разорили дикий улей: забираем его запасы
                        WildAlive = false;
                        float loot = sim.Cfg.WildHiveLoot + WildHoney * 0.6f;
                        colony.Honey += loot;
                        sim.Stats.HoneyDelivered += loot;
                        sim.Stats.WildHivesDestroyed++;
                        ev.Push(SimEventType.WildHiveDestroyed, p, loot, 0);
                    }
                    if (t == EnemyType.Queen)
                    {
                        sim.Stats.BossesKilled++;
                        float reward = sim.Cfg.BossReward;
                        colony.Honey += reward;
                        sim.Stats.HoneyDelivered += reward;
                        ev.Push(SimEventType.BossKilled, p, reward, 0);
                    }
                    if (t == EnemyType.Wasp) sim.Stats.WaspsKilled++;
                    else if (t == EnemyType.Hornet) sim.Stats.HornetsKilled++;
                    if (Stolen[i] > 0f)
                    {
                        colony.Honey += Stolen[i];
                        sim.Stats.HoneyStolen -= Stolen[i];
                        ev.Push(SimEventType.HoneyRecovered, p, Stolen[i]);
                    }
                    RemoveAt(i);
                    continue;
                }
                if (Mode[i] == EnemyMode.Flee &&
                    (p.x < -20f || p.y < -20f || p.x > world.Width + 20f || p.y > world.Height + 20f))
                {
                    RemoveAt(i);
                    continue;
                }
                i++;
            }
        }

        /// <summary>Цель дикой сборщицы: цветок рядом с её ульем → нектар → домой.</summary>
        Vector2 RivalForage(int i, Vector2 p, float dt, GameSim sim, Vector2 home)
        {
            var fl = sim.Flowers;
            bool closed = fl.CollectMul < 0.05f;
            if (Stolen[i] >= 3f || closed)
            {
                if ((p - home).sqrMagnitude < 9f * 9f) { WildHoney += Stolen[i]; Stolen[i] = 0f; }
                return home + new Vector2(Mathf.Sin(Phase[i]) * 6f, Mathf.Cos(Phase[i]) * 3f);
            }
            int f = Aux[i];
            if (f < 0 || f >= fl.Count || !fl.IsBlooming(f) || fl.Nectar[f] < 0.5f)
            {
                f = -1;
                float best = float.MaxValue;
                for (int k = 0; k < fl.Count; k++)
                {
                    if (!fl.IsBlooming(k) || fl.Nectar[k] < 1f) continue;
                    float d = (fl.Pos[k] - home).sqrMagnitude * (1f + 0.3f * Mathf.Abs(Mathf.Sin(k * 7.13f + Phase[i])));
                    if (d < best) { best = d; f = k; }
                }
                Aux[i] = f;
            }
            if (f < 0) return home;
            if ((p - fl.Pos[f]).sqrMagnitude < 6f * 6f)
                Stolen[i] += fl.Take(f, sim.Cfg.RivalCollectRate * fl.CollectMul * dt, sim.Cfg.FlowerRegrowTime, sim.Events);
            return fl.Pos[f];
        }

        /// <summary>Ближайшая живая пчела в радиусе; если нет — (-1,-1) и index = -1.</summary>
        static Vector2 NearestBee(Vector2 p, float radius, BeeSwarm swarm, out int index)
        {
            index = -1;
            if (swarm.Count == 0) return new Vector2(-1f, -1f);
            var buf = swarm.QueryBuffer;
            int nq = swarm.Grid.Query(p, radius, buf);
            float best = radius * radius;
            for (int q = 0; q < nq; q++)
            {
                int b = buf[q];
                if (b >= swarm.Count || swarm.Dead[b]) continue;
                float sq = (swarm.Pos[b] - p).sqrMagnitude;
                if (sq < best) { best = sq; index = b; }
            }
            return index >= 0 ? swarm.Pos[index] : new Vector2(-1f, -1f);
        }

        static Vector2 EdgeExit(Vector2 p, WorldMap w)
        {
            float l = p.x, rr = w.Width - p.x, b = p.y, t = w.Height - p.y;
            float m = Mathf.Min(Mathf.Min(l, rr), Mathf.Min(b, t));
            if (m == l) return new Vector2(-40f, p.y);
            if (m == rr) return new Vector2(w.Width + 40f, p.y);
            if (m == b) return new Vector2(p.x, -40f);
            return new Vector2(p.x, w.Height + 40f);
        }
    }
}
