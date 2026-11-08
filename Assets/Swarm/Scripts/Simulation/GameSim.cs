using UnityEngine;

namespace Swarm.Simulation
{
    /// <summary>Won — цель лета выполнена (3 звезды); Wintered — к концу лета хватает на зимовку (1–2 звезды).</summary>
    public enum SimResult : byte { Running, Won, Wintered, LostSwarm, LostSummer }

    public struct SimInput
    {
        public bool Active;
        public Vector2 Target;
        /// <summary>Палец удерживается почти неподвижно — у врага это тепловой шар.</summary>
        public bool Hold;
    }

    public sealed class SimStats
    {
        public float NectarCollected, HoneyDelivered, HoneyStolen;
        public int BeesHatched, BeesLost, WaspsKilled, HornetsKilled, PeakBees;
        public int LostToHeat, LostToDash, LostToBite, LostGuards;

        public void Reset()
        {
            NectarCollected = HoneyDelivered = HoneyStolen = 0f;
            BeesHatched = BeesLost = WaspsKilled = HornetsKilled = PeakBees = 0;
            LostToHeat = LostToDash = LostToBite = LostGuards = 0;
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
        public int Stars;
        /// <summary>Недавние потери пчёл (затухание ~30 c) — для директора волн.</summary>
        public float RecentLosses;
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
            Stars = 0;
            RecentLosses = 0f;
            Stats.Reset();
            Enemies.Clear();
            Waves.Reset();
            Events.Clear();
            Colony.Honey = 0f;
            for (int i = 0; i < Colony.Level.Length; i++) Colony.Level[i] = 0;
            Colony.GuardIndex = 0;
            Swarm.GuardWanted = 0;
            Swarm.GuardCount = 0;
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

        public int GuardWanted => Cfg.GuardSteps[Colony.GuardIndex];

        /// <summary>Переключает число стражей по кругу (0 → 5 → 10 → 20 → 0). Назначаются пчёлы у улья.</summary>
        public void CycleGuards()
        {
            Colony.GuardIndex = (Colony.GuardIndex + 1) % Cfg.GuardSteps.Length;
            Swarm.GuardWanted = GuardWanted;
        }

        /// <summary>Звёзды зимовки по текущему состоянию колонии.</summary>
        public int WinterStars()
        {
            int bees = Swarm.Count, honey = Mathf.FloorToInt(Colony.Honey);
            if (bees >= Cfg.GoalBees && honey >= Cfg.GoalHoney) return 3;
            if (bees >= Cfg.Winter2Bees && honey >= Cfg.Winter2Honey) return 2;
            if (bees >= Cfg.Winter1Bees && honey >= Cfg.Winter1Honey) return 1;
            return 0;
        }

        public bool GoalBeesMet => Swarm.Count >= Cfg.GoalBees;
        public bool GoalHoneyMet => Colony.Honey >= Cfg.GoalHoney;

        public void Step(float dt, in SimInput input)
        {
            if (Result != SimResult.Running) return;
            Time += dt;
            RecentLosses *= Mathf.Exp(-dt / 30f);

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
            if (Swarm.Count == 0) { Result = SimResult.LostSwarm; Stars = 0; }
            else if (GoalBeesMet && GoalHoneyMet) { Result = SimResult.Won; Stars = 3; }
            else if (Time >= Cfg.SessionTime)
            {
                Stars = WinterStars();
                Result = Stars > 0 ? SimResult.Wintered : SimResult.LostSummer;
            }
        }
    }
}
