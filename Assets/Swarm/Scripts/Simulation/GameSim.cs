using UnityEngine;

namespace Swarm.Simulation
{
    public enum SimResult : byte { Running, Won, LostSwarm, LostSummer }

    public struct SimInput
    {
        public bool Active;
        public Vector2 Target;
    }

    public sealed class SimStats
    {
        public float NectarCollected, HoneyDelivered, HoneyStolen;
        public int BeesHatched, BeesLost, WaspsKilled, HornetsKilled, PeakBees;

        public void Reset()
        {
            NectarCollected = HoneyDelivered = HoneyStolen = 0f;
            BeesHatched = BeesLost = WaspsKilled = HornetsKilled = PeakBees = 0;
        }
    }

    /// <summary>
    /// Корень игровой логики. Ничего не знает о рендере/вводе/UI: получает SimInput,
    /// продвигает мир на dt и складывает события в Events.
    /// </summary>
    public sealed class GameSim
    {
        public readonly SimConfig Cfg;
        public readonly Rng Rng;
        public readonly WorldMap World;
        public readonly FlowerField Flowers = new FlowerField();
        public readonly BeeSwarm Swarm;
        public readonly EnemySystem Enemies = new EnemySystem();
        public readonly WaveDirector Waves = new WaveDirector();
        public readonly Colony Colony;
        public readonly SimEvents Events = new SimEvents();
        public readonly SimStats Stats = new SimStats();

        public float Time;
        public SimResult Result;
        /// <summary>Режим «витрины» для главного меню: без врагов, без таймера.</summary>
        public bool AttractMode;

        public GameSim(SimConfig cfg, uint seed)
        {
            Cfg = cfg;
            Rng = new Rng(seed);
            World = new WorldMap(cfg, seed);
            Swarm = new BeeSwarm(cfg);
            Colony = new Colony(cfg);
            World.Generate(Rng, Flowers);
            Reset();
        }

        public void Reset()
        {
            Time = 0f;
            Result = SimResult.Running;
            Stats.Reset();
            Enemies.Clear();
            Waves.Reset();
            Events.Clear();
            Colony.Honey = 0f;
            for (int i = 0; i < Colony.Level.Length; i++) Colony.Level[i] = 0;
            for (int f = 0; f < Flowers.Count; f++)
            {
                Flowers.Nectar[f] = Flowers.MaxNectar[f];
                Flowers.Regrow[f] = 0f;
            }
            Swarm.Count = 0;
            Vector2 start = World.HivePos + new Vector2(0f, -30f);
            Swarm.ResetLeader(start);
            for (int i = 0; i < Cfg.StartBees; i++) Swarm.Spawn(start, Rng);
            Stats.PeakBees = Swarm.Count;
        }

        public float TimeLeft => Mathf.Max(0f, Cfg.SessionTime - Time);

        public bool SwarmAtHive =>
            (Swarm.LeaderPos - (World.HivePos + BeeSwarm.HiveEntranceOffset)).magnitude < Cfg.HiveReach + Swarm.Radius * 0.5f + 6f;

        public int HatchCost => Colony.HatchCost(Swarm.Count);
        public int HatchAmount => Mathf.Clamp(Colony.MaxBees - Swarm.Count, 0, Cfg.HatchBatch);

        public bool CanHatch => SwarmAtHive && HatchAmount > 0 && Colony.CanAfford(HatchCost);

        public bool TryHatch()
        {
            if (!CanHatch) return false;
            int n = HatchAmount;
            Colony.Honey -= HatchCost;
            Vector2 at = World.HivePos + BeeSwarm.HiveEntranceOffset;
            for (int i = 0; i < n; i++)
            {
                if (Swarm.Spawn(at, Rng))
                {
                    Stats.BeesHatched++;
                    Events.Push(SimEventType.BeeHatched, at, 0f, i);
                }
            }
            if (Swarm.Count > Stats.PeakBees) Stats.PeakBees = Swarm.Count;
            return true;
        }

        public bool CanUpgrade(UpgradeType t) => SwarmAtHive && Colony.CanAfford(Colony.CostOf(t));

        public bool TryUpgrade(UpgradeType t)
        {
            if (!SwarmAtHive) return false;
            if (!Colony.TryUpgrade(t)) return false;
            Events.Push(SimEventType.Upgrade, World.HivePos, 0f, (int)t);
            return true;
        }

        public bool GoalBeesMet => Swarm.Count >= Cfg.GoalBees;
        public bool GoalHoneyMet => Colony.Honey >= Cfg.GoalHoney;

        public void Step(float dt, in SimInput input)
        {
            if (Result != SimResult.Running) return;
            Time += dt;

            Flowers.Update(dt, Events);
            Swarm.Step(dt, Time, input, this);
            if (!AttractMode)
            {
                Waves.Step(Time, this);
                Enemies.Step(dt, Time, this);
            }
            Swarm.Compact();
            if (Swarm.Count > Stats.PeakBees) Stats.PeakBees = Swarm.Count;

            if (AttractMode) return;
            if (Swarm.Count == 0) Result = SimResult.LostSwarm;
            else if (GoalBeesMet && GoalHoneyMet) Result = SimResult.Won;
            else if (Time >= Cfg.SessionTime) Result = SimResult.LostSummer;
        }
    }
}
