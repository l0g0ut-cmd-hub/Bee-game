using UnityEngine;

namespace Swarm.Simulation
{
    public enum EnemyType : byte { Wasp, Hornet }
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
        public int Count;

        public static float RadiusOf(EnemyType t) => t == EnemyType.Wasp ? 5f : 8f;
        public static float SpeedOf(EnemyType t) => t == EnemyType.Wasp ? 50f : 40f;
        public static float HpOf(EnemyType t) => t == EnemyType.Wasp ? 10f : 45f;
        public static float BiteCooldown(EnemyType t) => t == EnemyType.Wasp ? 1.1f : 0.85f;
        static float WindupTime(EnemyType t) => t == EnemyType.Wasp ? 0.45f : 0.65f;
        static float DashTime(EnemyType t) => t == EnemyType.Wasp ? 0.3f : 0.45f;
        static float DashRange(EnemyType t) => t == EnemyType.Wasp ? 42f : 58f;
        static float DashCooldown(EnemyType t) => t == EnemyType.Wasp ? 7f : 5f;
        static int DashMaxKills(EnemyType t) => t == EnemyType.Wasp ? 1 : 2;

        /// <summary>Сколько пчёл одновременно помещается вокруг врага.</summary>
        public static int MaxStingers(EnemyType t, SimConfig cfg) =>
            t == EnemyType.Wasp ? cfg.MaxStingersPerEnemy : cfg.MaxStingersPerEnemy * 2;

        const float WaspAggro = 120f;
        const float StealRate = 7f;
        const float StealMax = 25f;

        readonly int[] _dashKills = new int[Capacity];

        public void Clear() => Count = 0;

        public bool Spawn(EnemyType t, Vector2 p, Rng rng)
        {
            if (Count >= Capacity) return false;
            int i = Count++;
            Type[i] = t;
            Mode[i] = t == EnemyType.Hornet ? EnemyMode.Hunt : EnemyMode.Raid;
            Act[i] = EnemyAct.Move;
            Pos[i] = p;
            Vel[i] = Vector2.zero;
            DashDir[i] = new Vector2(1f, 0f);
            Hp[i] = MaxHp[i] = HpOf(t);
            BiteTimer[i] = 1f;
            ActTimer[i] = 0f;
            DashCd[i] = 1.5f + rng.Value * 1.5f;
            Flash[i] = 0f;
            Stolen[i] = 0f;
            Phase[i] = rng.Value * 100f;
            Heat[i] = 0f;
            Stingers[i] = 0;
            _dashKills[i] = 0;
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
                Phase[i] = Phase[last]; Heat[i] = Heat[last]; Stingers[i] = Stingers[last]; _dashKills[i] = _dashKills[last];
            }
            Count--;
        }

        void KillBee(GameSim sim, int b, EnemyType t, bool heat, bool dash = false)
        {
            if (sim.Swarm.Role[b] == BeeRole.Guard) sim.Stats.LostGuards++;
            if (heat) sim.Stats.LostToHeat++; else if (dash) sim.Stats.LostToDash++; else sim.Stats.LostToBite++;
            var swarm = sim.Swarm;
            Vector2 vp = swarm.Pos[b];
            swarm.MarkDead(b);
            sim.Stats.BeesLost++;
            sim.RecentLosses += 1f;
            sim.Events.Push(SimEventType.BeeDied, vp, swarm.Carry[b], heat ? 99 : (int)t);
        }

        public void Step(float dt, float time, GameSim sim)
        {
            var swarm = sim.Swarm;
            var world = sim.World;
            var colony = sim.Colony;
            var ev = sim.Events;
            var cfg = sim.Cfg;
            var buf = swarm.QueryBuffer;
            Vector2 entrance = world.HivePos + BeeSwarm.HiveEntranceOffset;
            float stingMul = colony.StingMul;
            int ballTarget = swarm.Balling ? swarm.BallTarget : -1;

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
                    Mode[i] = (t == EnemyType.Hornet && swarm.Count > 0) || swarmClose ? EnemyMode.Hunt : EnemyMode.Raid;
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
                else
                {
                    target = entrance + new Vector2(0f, 4f);
                    if ((p - target).sqrMagnitude < 14f * 14f)
                    {
                        float take = Mathf.Min(StealRate * dt, Mathf.Min(colony.Honey, StealMax - Stolen[i]));
                        if (take > 0f)
                        {
                            colony.Honey -= take;
                            Stolen[i] += take;
                            sim.Stats.HoneyStolen += take;
                            if (sim.Rng.Value < dt * 3f) ev.Push(SimEventType.HoneyStolen, p, take, i);
                        }
                        if (Stolen[i] >= StealMax - 0.01f || colony.Honey <= 0.01f) Mode[i] = EnemyMode.Flee;
                    }
                }

                // --- телеграф и рывок (только на охоте)
                Vector2 v;
                if (Act[i] == EnemyAct.Move && Mode[i] == EnemyMode.Hunt && DashCd[i] <= 0f &&
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
                else
                {
                    Vector2 to = target - p;
                    float d = to.magnitude;
                    Vector2 desired = d > 0.5f ? to / d * Mathf.Min(speed, d * 4f) : Vector2.zero;
                    float ph = Phase[i];
                    desired += new Vector2(Mathf.Sin(time * 3.1f + ph), Mathf.Cos(time * 2.7f + ph * 1.7f)) * 18f;
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
                bool balled = i == ballTarget;
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
                if (balled) maxSt = Mathf.RoundToInt(maxSt * cfg.BallStingerMul);
                if (stingers > maxSt) { guardStingers = guardStingers * maxSt / stingers; stingers = maxSt; }
                Stingers[i] = stingers;
                Heat[i] = Mathf.MoveTowards(Heat[i], balled && stingers > 0 ? 1f : 0f, dt * 1.5f);
                if (stingers > 0)
                {
                    float power = stingers + guardStingers * (cfg.GuardStingMul - 1f);
                    float dmg = power * cfg.BeeDps * stingMul * dt * (balled ? cfg.HeatDamageMul : 1f);
                    Hp[i] -= dmg;
                    if (Flash[i] <= 0f)
                    {
                        Flash[i] = 0.12f;
                        ev.Push(SimEventType.EnemyHit, p, dmg, (int)t);
                    }
                    if (Act[i] != EnemyAct.Dash)
                    {
                        v += new Vector2(sim.Rng.Signed, sim.Rng.Signed) * (stingers * 6f);
                        v *= 1f - Mathf.Min(balled ? 0.8f : 0.5f, stingers * 0.03f) * dt * 10f;
                    }
                    // перегрев: пчёлы внутри шара гибнут
                    if (balled && sim.Rng.Value < stingers * cfg.HeatDeathRate * dt)
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
                    else if (BiteTimer[i] <= 0f && Act[i] == EnemyAct.Move)
                    {
                        NearestBee(p, r + 4f, swarm, out int victim);
                        if (victim >= 0)
                        {
                            KillBee(sim, victim, t, false);
                            BiteTimer[i] = BiteCooldown(t) * (Mode[i] == EnemyMode.Raid ? cfg.RaidBiteSlow : 1f);
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
                    if (t == EnemyType.Wasp) sim.Stats.WaspsKilled++; else sim.Stats.HornetsKilled++;
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
