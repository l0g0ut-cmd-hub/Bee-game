using UnityEngine;

namespace Swarm.Simulation
{
    /// <summary>Won — цель лета выполнена (3 звезды); Wintered — к концу лета хватает на зимовку (1–2 звезды).</summary>
    public enum SimResult : byte { Running, Won, Wintered, LostSwarm, LostSummer }

    public struct SimInput
    {
        public bool Active;
        public Vector2 Target;
        /// <summary>Второй палец: отряд отделяется от роя и летит сюда, пока палец на экране.</summary>
        /// <summary>Палец удерживается почти неподвижно — у врага это тепловой шар.</summary>
        public bool Hold;
    }

    public sealed class SimStats
    {
        public float NectarCollected, HoneyDelivered, HoneyStolen;
        public int BeesHatched, BeesLost, WaspsKilled, HornetsKilled, BearsRepelled, PeakBees;
        public int LostToHeat, LostToDash, LostToBite, LostGuards, LostToBird, LostToSpider;
        public int FlowersSprouted, RareFound, DefendersHired, WorkersHired, AntsKilled, NestsDestroyed, HornetsCooked;

        public void Reset()
        {
            NectarCollected = HoneyDelivered = HoneyStolen = 0f;
            BeesHatched = BeesLost = WaspsKilled = HornetsKilled = BearsRepelled = PeakBees = 0;
            LostToHeat = LostToDash = LostToBite = LostGuards = LostToBird = LostToSpider = 0;
            FlowersSprouted = RareFound = DefendersHired = WorkersHired = AntsKilled = NestsDestroyed = HornetsCooked = 0;
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
        public readonly HazardSystem Hazards = new HazardSystem();
        public readonly SkySystem Sky = new SkySystem();
        public readonly FogMap Fog;
        public readonly WaveDirector Waves = new WaveDirector();
        public readonly Colony Colony;
        public readonly PerkMods Mods = new PerkMods();
        public readonly SimEvents Events = new SimEvents();
        public readonly SimStats Stats = new SimStats();

        public float Time;
        public SimResult Result;
        public int Stars;
        /// <summary>Недавние потери пчёл (затухание ~30 c) — для директора волн.</summary>
        public float RecentLosses;
        /// <summary>Режим «витрины» для главного меню: без врагов, без таймера, без тумана.</summary>
        public bool AttractMode;

        // --- матка: доля потока мёда на расплод (индекс в BroodSteps)
        public static readonly float[] BroodSteps = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        public int BroodIndex;
        public float BroodProgress;

        // --- перки
        public bool PerkPending;
        public readonly PerkId[] PerkOffer = new PerkId[3];
        public int PerkStage;
        public readonly bool[] PerkTaken = new bool[(int)PerkId.Count];

        float _nextSprout;
        int _nextLinden;
        bool _wasNight;

        public GameSim(SimConfig cfg, uint seed)
        {
            Cfg = cfg;
            Rng = new Rng(seed);
            World = new WorldMap(cfg, seed);
            Swarm = new BeeSwarm(cfg);
            Colony = new Colony(cfg);
            Fog = new FogMap(cfg.WorldWidth, cfg.WorldHeight);
            World.Generate(Rng, Flowers);
            Flowers.BaseCount = Flowers.Count;
            Hazards.Generate(World, Rng);
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
            Mods.Reset();
            Colony.Honey = 0f;
            for (int i = 0; i < Colony.Level.Length; i++) Colony.Level[i] = 0;
            BroodIndex = 2;
            BroodProgress = 0f;
            PerkPending = false;
            PerkStage = 0;
            for (int i = 0; i < PerkTaken.Length; i++) PerkTaken[i] = false;
            Flowers.Count = Flowers.BaseCount;   // выросшие за прошлое лето цветы исчезают
            for (int f = 0; f < Flowers.Count; f++)
            {
                Flowers.Nectar[f] = Flowers.MaxNectar[f];
                Flowers.Regrow[f] = 0f;
                Flowers.Sprout[f] = 0f;
            }
            _nextSprout = Rng.Range(Cfg.SproutIntervalMin, Cfg.SproutIntervalMax);
            Swarm.Count = 0;
            Swarm.GuardCount = 0;
            Swarm.WorkerCount = 0;
            Swarm.ScoutChance = 0.07f;
            Vector2 start = World.HivePos + new Vector2(0f, -30f);
            Swarm.ResetLeader(start);
            for (int i = 0; i < Cfg.StartBees; i++) Swarm.Spawn(start, Rng);
            Stats.PeakBees = Swarm.Count;
            Hazards.Reset(this);
            Fog.Enabled = Cfg.FogEnabled && !AttractMode;
            Fog.Reset(World.HivePos);
            Sky.Enabled = Cfg.SkyEnabled && !AttractMode;
            Sky.Reset(Rng);
            _nextLinden = 0;
            _wasNight = false;
            if (Cfg.NestEnabled && !AttractMode) Enemies.Spawn(EnemyType.Nest, World.NestPos, Rng);
        }

        public float TimeLeft => Mathf.Max(0f, Cfg.SessionTime - Time);

        public bool SwarmAtHive =>
            (Swarm.LeaderPos - (World.HivePos + BeeSwarm.HiveEntranceOffset)).magnitude < Cfg.HiveReach + Swarm.Radius * 0.5f + 6f;

        public bool CanUpgrade(UpgradeType t) => SwarmAtHive && Colony.CanAfford(Colony.CostOf(t));

        public bool TryUpgrade(UpgradeType t)
        {
            if (!SwarmAtHive) return false;
            if (!Colony.TryUpgrade(t)) return false;
            Events.Push(SimEventType.Upgrade, World.HivePos, 0f, (int)t);
            return true;
        }

        // ------------------------------------------------------------ матка
        public float BroodShare => BroodSteps[BroodIndex];
        public bool CanPost => !Swarm.SquadActive && Swarm.SwarmCount >= Cfg.PostMinSwarm;
        public bool PostSquad() => Swarm.PostSquad(Cfg);
        public void RecallSquad() => Swarm.RecallSquad();
        public float BroodPerMinute => Cfg.BroodRate * BroodShare * Mods.Brood * 60f;
        public void CycleBrood() => BroodIndex = (BroodIndex + 1) % BroodSteps.Length;
        /// <summary>Мёд за одну новую пчелу (дорожает с ростом колонии).</summary>
        public float BroodCostPerBee => Colony.HatchCost(Swarm.SwarmCount) / (float)Cfg.HatchBatch * Mods.BroodCost;

        void StepBrood(float dt)
        {
            if (BroodShare <= 0f || Swarm.SwarmCount >= Colony.MaxBees) { BroodProgress = Mathf.Min(BroodProgress, 1f); return; }
            BroodProgress += Cfg.BroodRate * BroodShare * Mods.Brood * dt * (Sky.IsNight(Time) ? 2f : 1f);
            float cost = BroodCostPerBee;
            Vector2 at = World.HivePos + BeeSwarm.HiveEntranceOffset;
            int n = 0;
            while (BroodProgress >= 1f && Colony.Honey >= cost && Swarm.SwarmCount < Colony.MaxBees)
            {
                if (!Swarm.Spawn(at, Rng)) break;
                Colony.Honey -= cost;
                BroodProgress -= 1f;
                Stats.BeesHatched++;
                Events.Push(SimEventType.BeeHatched, at, 0f, n++);
            }
            if (BroodProgress > 1f) BroodProgress = 1f;
        }

        // ------------------------------------------------------------ сборщицы
        public int WorkerCost => Cfg.WorkerCostBase + Cfg.WorkerCostPer * Swarm.WorkerCount;
        public int WorkerRoom => Mathf.Clamp(Cfg.WorkerMax - Swarm.WorkerCount, 0, Cfg.WorkerBatch);
        public bool CanHireWorkers => SwarmAtHive && WorkerRoom > 0 && Colony.Honey >= WorkerCost;

        public bool HireWorkers()
        {
            if (!CanHireWorkers) return false;
            Colony.Honey -= WorkerCost;
            int n = WorkerRoom;
            Vector2 at = World.HivePos + BeeSwarm.HiveEntranceOffset;
            for (int i = 0; i < n; i++)
                if (Swarm.SpawnWorker(at, Rng)) Stats.WorkersHired++;
            Events.Push(SimEventType.DefendersHired, at, n, 1);
            return true;
        }

        // ------------------------------------------------------------ защитники
        public int DefenderCost => Cfg.DefenderCostBase + Cfg.DefenderCostPer * Swarm.GuardCount;
        public int DefenderRoom => Mathf.Clamp(Cfg.DefenderMax - Swarm.GuardCount, 0, Cfg.DefenderBatch);
        public bool CanHireDefenders => SwarmAtHive && DefenderRoom > 0 && Colony.Honey >= DefenderCost;

        public bool HireDefenders()
        {
            if (!CanHireDefenders) return false;
            Colony.Honey -= DefenderCost;
            int n = DefenderRoom;
            Vector2 at = World.HivePos + BeeSwarm.HiveEntranceOffset;
            for (int i = 0; i < n; i++)
                if (Swarm.SpawnDefender(at, Rng, Cfg.DefenderHp + Mods.DefenderHpBonus)) Stats.DefendersHired++;
            Events.Push(SimEventType.DefendersHired, at, n);
            return true;
        }

        // ------------------------------------------------------------ перки
        public void ChoosePerk(int index)
        {
            if (!PerkPending) return;
            var p = PerkOffer[Mathf.Clamp(index, 0, 2)];
            PerkTaken[(int)p] = true;
            Perks.Apply(p, this);
            PerkPending = false;
            PerkStage++;
            Events.Push(SimEventType.PerkTaken, Swarm.LeaderPos, 0f, (int)p);
        }

        void CheckPerkMilestone()
        {
            if (PerkPending || PerkStage >= Perks.Milestones.Length) return;
            if (Swarm.SwarmCount < Perks.Milestones[PerkStage]) return;
            int n = 0, guard = 0;
            while (n < 3 && guard++ < 100)
            {
                var p = (PerkId)Rng.Range(0, (int)PerkId.Count);
                if (PerkTaken[(int)p]) continue;
                bool dup = false;
                for (int k = 0; k < n; k++) dup |= PerkOffer[k] == p;
                if (dup) continue;
                PerkOffer[n++] = p;
            }
            PerkPending = true;
            Events.Push(SimEventType.PerkOffered, Swarm.LeaderPos, 0f, PerkStage);
        }

        // ------------------------------------------------------------ урон пчёлам (единая точка)
        /// <summary>cause: тип врага, 97 птица, 98 паук, 99 перегрев. lethal — убивает сразу, мимо «жизней».</summary>
        public void HurtBee(int b, int cause, bool lethal = false, bool dash = false)
        {
            if (b < 0 || b >= Swarm.Count || Swarm.Dead[b]) return;
            if (!lethal && Swarm.Hp[b] > 1f)
            {
                Swarm.Hp[b] -= 1f;
                Events.Push(SimEventType.BeeHurt, Swarm.Pos[b], 0f, cause);
                return;
            }
            Vector2 vp = Swarm.Pos[b];
            if (Swarm.Role[b] == BeeRole.Guard) Stats.LostGuards++;
            if (cause == 99) Stats.LostToHeat++;
            else if (cause == 97) Stats.LostToBird++;
            else if (cause == 98) Stats.LostToSpider++;
            else if (dash) Stats.LostToDash++;
            else Stats.LostToBite++;
            Swarm.MarkDead(b);
            Stats.BeesLost++;
            RecentLosses += 1f;
            Events.Push(SimEventType.BeeDied, vp, Swarm.Carry[b], cause);
        }

        // ------------------------------------------------------------ цветение липы
        /// <summary>Временное событие: у дальнего дерева зацветает липа — много нектара, но ненадолго.</summary>
        void StepLinden()
        {
            var times = Cfg.LindenTimes;
            if (_nextLinden >= times.Length || Time < times[_nextLinden]) return;
            _nextLinden++;
            int tree = -1;
            float best = 0f;
            for (int i = 0; i < World.Obstacles.Count; i++)
            {
                var o = World.Obstacles[i];
                if (o.Kind != ObstacleKind.Tree) continue;
                float d = (o.Pos - World.HivePos).magnitude + Rng.Value * 120f;
                if (d > best && (o.Pos - World.HivePos).magnitude > 150f) { best = d; tree = i; }
            }
            if (tree < 0) return;
            Vector2 c = World.Obstacles[tree].Pos;
            int made = 0;
            for (int k = 0; k < 30 && made < Cfg.LindenFlowers; k++)
            {
                float a = Rng.Value * 6.2832f, d = Rng.Range(17f, 30f);
                Vector2 p = World.ClampToWorld(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f) * d, 16f);
                if (World.InsideObstacle(p, 4f) || Flowers.Count >= FlowerField.Capacity) continue;
                Flowers.Add(p, FlowerKind.Linden, WorldMap.NectarOf(FlowerKind.Linden), Rng.Value);
                Flowers.Life[Flowers.Count - 1] = Cfg.LindenLife;
                made++;
            }
            if (made > 0) Events.Push(SimEventType.LindenBloom, c, made, tree);
        }

        /// <summary>Выживание: звёзды за продержанное время.</summary>
        public int EndlessStars() => Time >= 1800f ? 3 : Time >= 1200f ? 2 : Time >= 600f ? 1 : 0;

        // ------------------------------------------------------------ итоги
        /// <summary>Звёзды зимовки по текущему состоянию колонии (считается полевой рой, без защитников).</summary>
        public int WinterStars()
        {
            int bees = Swarm.SwarmCount, honey = Mathf.FloorToInt(Colony.Honey);
            if (bees >= Cfg.GoalBees && honey >= Cfg.GoalHoney) return 3;
            if (bees >= Cfg.Winter2Bees && honey >= Cfg.Winter2Honey) return 2;
            if (bees >= Cfg.Winter1Bees && honey >= Cfg.Winter1Honey) return 1;
            return 0;
        }

        public bool GoalBeesMet => Swarm.SwarmCount >= Cfg.GoalBees;
        public bool GoalHoneyMet => Colony.Honey >= Cfg.GoalHoney;

        public void Step(float dt, in SimInput input)
        {
            if (Result != SimResult.Running) return;
            Time += dt;
            RecentLosses *= Mathf.Exp(-dt / 30f);

            Flowers.CollectMul = Sky.CollectMul(Time);
            Flowers.Update(dt, Events);
            Swarm.Step(dt, Time, input, this);
            if (!AttractMode)
            {
                Sky.Step(dt, this);
                bool night = Sky.IsNight(Time);
                if (night != _wasNight)
                {
                    _wasNight = night;
                    Events.Push(night ? SimEventType.NightFall : SimEventType.Dawn, Swarm.LeaderPos, Sky.Day(Time));
                }
                StepLinden();
                Waves.Step(Time, this);
                Enemies.Step(dt, Time, this);
                Hazards.Step(dt, this);
                StepBrood(dt);
                if (Time >= _nextSprout)
                {
                    _nextSprout = Time + Rng.Range(Cfg.SproutIntervalMin, Cfg.SproutIntervalMax);
                    if (Stats.FlowersSprouted < Cfg.SproutMax && World.TrySprout(Rng, Flowers, Cfg))
                    {
                        Stats.FlowersSprouted++;
                        int f = Flowers.Count - 1;
                        Events.Push(SimEventType.FlowerSprouted, Flowers.Pos[f], Flowers.Rare[f] ? 1f : 0f, f);
                    }
                }
                Fog.Step(this, dt);
            }
            Swarm.Compact();
            if (Swarm.SwarmCount > Stats.PeakBees) Stats.PeakBees = Swarm.SwarmCount;

            if (AttractMode) return;
            CheckPerkMilestone();
            if (Swarm.SwarmCount == 0) { Result = SimResult.LostSwarm; Stars = Cfg.Endless ? EndlessStars() : 0; }
            else if (Cfg.Endless) { }
            else if (GoalBeesMet && GoalHoneyMet) { Result = SimResult.Won; Stars = 3; }
            else if (Time >= Cfg.SessionTime)
            {
                Stars = WinterStars();
                Result = Stars > 0 ? SimResult.Wintered : SimResult.LostSummer;
            }
        }
    }
}
