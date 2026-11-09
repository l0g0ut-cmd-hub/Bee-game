// Headless-стенд: бот играет полную сессию на нескольких сидах, печатает метрики.
// dotnet run -c Release -- [seeds] [verbose]
using System;
using System.Diagnostics;
using Swarm.Simulation;
using UnityEngine;

static class Program
{
    enum BotState { Gather, Deliver, Defend }
    static readonly bool NoBall = Environment.GetEnvironmentVariable("BOT_NOBALL") == "1";
    static readonly bool NoWorkers = Environment.GetEnvironmentVariable("BOT_NOWORK") == "1";
    static readonly bool NoGuard = Environment.GetEnvironmentVariable("BOT_NOGUARD") == "1";

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "ballcheck") { BallCheck(); return 0; }
        int seeds = args.Length > 0 ? int.Parse(args[0]) : 6;
        bool verbose = args.Length > 1;
        int wins = 0;
        double totalTime = 0;
        var sw = Stopwatch.StartNew();
        long steps = 0;
        foreach (var skill in new[] { 1.0f, 0.6f })
        {
            Console.WriteLine($"=== skill {skill:F1} ===");
            for (int s = 1; s <= seeds; s++)
            {
                var r = RunOne((uint)(s * 7919), skill, verbose && s == 1, ref steps);
                if (skill == 1.0f) { if (r.result == SimResult.Won) wins++; totalTime += r.time; }
            }
        }
        double el = sw.Elapsed.TotalSeconds;
        Console.WriteLine($"\nsim steps: {steps}, {steps / el:F0} steps/s ({el:F1}s wall)");
        Console.WriteLine($"skill 1.0 wins: {wins}/{seeds}");
        return 0;
    }

    static void BallCheck()
    {
        var sim = new GameSim(SimConfig.Default(), 777u);
        while (sim.Swarm.Count < 80 && sim.Swarm.Spawn(sim.Swarm.LeaderPos, sim.Rng)) { }
        for (int i = 0; i < 120; i++) { sim.Step(1f / 60f, new SimInput { Active = true, Target = sim.Swarm.LeaderPos }); sim.Events.Clear(); }
        Vector2 lp = sim.Swarm.LeaderPos;
        sim.Enemies.Spawn(EnemyType.Hornet, lp + new Vector2(40f, 50f), sim.Rng);
        sim.Enemies.DashCd[0] = 99f;
        for (int i = 0; i < 200; i++)
        {
            var tgt = sim.Enemies.Count > 0 ? sim.Enemies.Pos[0] : sim.Swarm.LeaderPos;
            sim.Step(1f / 60f, new SimInput { Active = true, Target = tgt, Hold = true });
            sim.Events.Clear();
            if (i % 20 == 0)
                Console.WriteLine($"i={i} balling={sim.Swarm.Balling} inBall={sim.Swarm.BallCount} swarmCount={sim.Swarm.SwarmCount} R={sim.Swarm.Radius:F1} " +
                    $"dist={(sim.Enemies.Count > 0 ? (sim.Enemies.Pos[0] - sim.Swarm.LeaderPos).magnitude : -1):F1} hp={(sim.Enemies.Count > 0 ? sim.Enemies.Hp[0] : 0):F1} stingers={(sim.Enemies.Count > 0 ? sim.Enemies.Stingers[0] : 0)}");
        }
    }

    static (SimResult result, float time) RunOne(uint seed, float skill, bool verbose, ref long steps)
    {
        int lvl = int.TryParse(Environment.GetEnvironmentVariable("LEVEL"), out var lv) ? lv : 1;
        var mode = Environment.GetEnvironmentVariable("ENDLESS") == "1" ? GameMode.Endless : GameMode.Campaign;
        var cfg = Levels.Make(mode, lvl);
        if (mode == GameMode.Endless) cfg.SessionTime = 1200f; // для стенда ограничиваем
        var sim = new GameSim(cfg, seed);
        var rnd = new Random((int)seed);
        const float dt = 1f / 60f;
        var state = BotState.Gather;
        int targetFlower = -1;
        float nextLog = 30f;
        float spreadAcc = 0f; int spreadN = 0; float maxSpread = 0f; int stuck = 0;
        float reactionTimer = 0f;
        Vector2 heldTarget = sim.Swarm.LeaderPos;
        bool hold = false;

        while (sim.Result == SimResult.Running)
        {
            var sw = sim.Swarm;
            var col = sim.Colony;
            Vector2 hive = sim.World.HivePos + BeeSwarm.HiveEntranceOffset;
            float cap = cfg.CarryBase * col.CarryMul * Math.Max(1, sw.SwarmCount);

            // --- решение бота (слабый бот реагирует медленнее и хуже выбирает цели)
            reactionTimer -= dt;
            if (reactionTimer <= 0f)
            {
                reactionTimer = skill >= 1f ? 0.1f : 0.6f;
                int threat = -1; float threatD = 140f * 140f;
                int raider = -1;
                for (int e = 0; e < sim.Enemies.Count; e++)
                {
                    float d = (sim.Enemies.Pos[e] - sw.LeaderPos).sqrMagnitude;
                    if (d < threatD) { threatD = d; threat = e; }
                    if (sim.Enemies.Mode[e] == EnemyMode.Raid && (sim.Enemies.Pos[e] - hive).sqrMagnitude < 90f * 90f) raider = e;
                }
                hold = false;
                // тепловой шар: крупным роем умный бот держит палец на шершне
                if (!NoBall && threat >= 0 && sim.Enemies.Type[threat] == EnemyType.Hornet && sw.SwarmCount >= 40 && (skill >= 1f || rnd.NextDouble() < 0.4))
                {
                    state = BotState.Defend; heldTarget = sim.Enemies.Pos[threat]; hold = true;
                }
                // умный бот: не гоняется за всеми подряд; малым роем уходит от шершней
                else if (threat >= 0 && skill >= 1f && sim.Enemies.Type[threat] == EnemyType.Hornet && sw.Count < 70)
                {
                    Vector2 away = sw.LeaderPos - sim.Enemies.Pos[threat];
                    state = BotState.Defend; heldTarget = sw.LeaderPos + away.normalized * 80f;
                }
                else if (threat >= 0 && (skill >= 1f ? threatD < 90f * 90f : rnd.NextDouble() < 0.5)) { state = BotState.Defend; heldTarget = sim.Enemies.Pos[threat]; }
                else if (raider >= 0 && skill >= 1f) { state = BotState.Defend; heldTarget = sim.Enemies.Pos[raider]; }
                else if (sw.FieldCarry >= cap * 0.85f || (state == BotState.Deliver && sw.FieldCarry > 0.5f))
                { state = BotState.Deliver; heldTarget = hive; }
                else
                {
                    state = BotState.Gather;
                    if (targetFlower < 0 || !sim.Flowers.IsBlooming(targetFlower))
                    {
                        float best = float.MaxValue;
                        for (int f = 0; f < sim.Flowers.Count; f++)
                        {
                            if (!sim.Flowers.IsBlooming(f)) continue;
                            float d = (sim.Flowers.Pos[f] - sw.LeaderPos).magnitude + (sim.Flowers.Pos[f] - hive).magnitude * 0.5f;
                            float score = d / (2f + sim.Flowers.Nectar[f]) * (skill >= 1f ? 1f : (float)(0.5 + rnd.NextDouble()));
                            if (score < best) { best = score; targetFlower = f; }
                        }
                    }
                    if (targetFlower >= 0) heldTarget = sim.Flowers.Pos[targetFlower];
                }
            }

            // --- покупки в улье
            // --- матка: расплод на полную, пока не пора копить мёд
            bool saving = sw.SwarmCount >= cfg.GoalBees
                          || (sim.TimeLeft < 120f && sw.SwarmCount >= cfg.Winter2Bees)
                          || (skill >= 1f && sim.Time > 420f && sw.SwarmCount >= cfg.GoalBees - 15 && col.Honey < cfg.GoalHoney);
            sim.BroodIndex = saving ? 0 : (skill >= 1f ? 4 : 3);

            // --- перки: умный бот по приоритету, слабый — первый попавшийся
            if (sim.PerkPending)
            {
                int pick = 0;
                if (skill >= 1f)
                {
                    PerkId[] pref = { PerkId.SweetNectar, PerkId.ThickStings, PerkId.BigCrop, PerkId.FertileQueen, PerkId.Evasion, PerkId.HeatProof, PerkId.SwiftWings, PerkId.IronGuards, PerkId.Scouting };
                    int best = 99;
                    for (int k = 0; k < 3; k++)
                        for (int r = 0; r < pref.Length; r++)
                            if (pref[r] == sim.PerkOffer[k] && r < best) { best = r; pick = k; }
                }
                sim.ChoosePerk(pick);
            }

            // --- покупки в улье
            if (sim.SwarmAtHive && sw.FieldCarry < 0.5f)
            {
                for (int guard = 0; guard < 20; guard++)
                {
                    bool bought = false;
                    int wantDef = NoGuard ? 0 : (sim.Time > 250f ? 12 : (sim.Time > 90f ? 6 : 0));
                    if (sw.SwarmCount + 3 >= col.MaxBees && sim.CanUpgrade(UpgradeType.Comb)) bought = sim.TryUpgrade(UpgradeType.Comb);
                    else if (!saving && col.Level[(int)UpgradeType.Carry] < 2 && sim.CanUpgrade(UpgradeType.Carry)) bought = sim.TryUpgrade(UpgradeType.Carry);
                    else if (!saving && col.Level[(int)UpgradeType.Sting] < 2 && sim.Time > 150 && sim.CanUpgrade(UpgradeType.Sting)) bought = sim.TryUpgrade(UpgradeType.Sting);
                    else if (!saving && col.Level[(int)UpgradeType.Speed] < 1 && sim.CanUpgrade(UpgradeType.Speed)) bought = sim.TryUpgrade(UpgradeType.Speed);
                    else if (sw.GuardCount < wantDef && sim.CanHireDefenders && col.Honey > sim.DefenderCost + 40) bought = sim.HireDefenders();
                    else if (!NoWorkers && !saving && sim.Time > 40f && sim.CanHireWorkers && col.Honey > sim.WorkerCost + 30) bought = sim.HireWorkers();
                    if (!bought) break;
                }
            }

            var input = new SimInput { Active = true, Target = heldTarget, Hold = hold };
            sim.Step(dt, input);
            sim.Events.Clear();
            steps++;

            // метрики сплочённости роя
            if (sw.Count > 0 && ((int)(sim.Time * 60) % 30) == 0)
            {
                float acc = 0f, mx = 0f;
                for (int i = 0; i < sw.Count; i++)
                {
                    if (sw.Role[i] == BeeRole.Guard) continue;
                    float d = (sw.Pos[i] - sw.LeaderPos).magnitude;
                    acc += d; if (d > mx) mx = d;
                    if (sim.World.InsideObstacle(sw.Pos[i], 0f)) stuck++;
                }
                spreadAcc += acc / Math.Max(1, sw.SwarmCount); spreadN++;
                if (mx > maxSpread) maxSpread = mx;
            }

            if (verbose && sim.Time >= nextLog)
            {
                nextLog += 30f;
                Console.WriteLine($"  t={sim.Time,5:F0} bees={sw.Count,3}/{col.MaxBees,3} honey={col.Honey,6:F0} carry={sw.TotalCarry,5:F1} " +
                                  $"lv[spd {col.Level[0]} car {col.Level[1]} sti {col.Level[2]} comb {col.Level[3]}] enemies={sim.Enemies.Count,2} " +
                                  $"lost={sim.Stats.BeesLost,3} stolen={sim.Stats.HoneyStolen,4:F0} R={sw.Radius:F1} def={sw.GuardCount} brood={sim.BroodShare:F2} perks={sim.PerkStage} sprouts={sim.Stats.FlowersSprouted} " +
                                  $"G{sw.GatheringCount} A{sw.AttackingCount} S{sw.ScoutingCount} B{sw.BallCount} st={state} hold={hold} lead={sw.LeaderPos} tgt={heldTarget} atHive={sim.SwarmAtHive}");
            }
        }
        var st = sim.Stats;
        Console.WriteLine($" seed {seed,6}: {sim.Result,-10} ★{sim.Stars} post={sim.Waves.Postponements} t={sim.Time,5:F0}s bees={sim.Swarm.SwarmCount,3} honey={sim.Colony.Honey,4:F0} " +
                          $"delivered={st.HoneyDelivered,5:F0} hatched={st.BeesHatched,3} lost={st.BeesLost,3} kills w{st.WaspsKilled}/h{st.HornetsKilled} " +
                          $"stolen={st.HoneyStolen,4:F0} night={sim.Sky.Day(sim.Time)}d nest={(sim.Enemies.NestAlive ? 1 : 0)} ants={st.AntsKilled} cooked={st.HornetsCooked} heat/dash/bite/bird/spider={st.LostToHeat}/{st.LostToDash}/{st.LostToBite}/{st.LostToBird}/{st.LostToSpider} guards={st.LostGuards} workers={sim.Swarm.WorkerCount}/{st.WorkersHired} bears={st.BearsRepelled} sprouts={st.FlowersSprouted} spread avg={spreadAcc / Math.Max(1, spreadN):F1} max={maxSpread:F0} inObstacle={stuck}");
        return (sim.Result, sim.Time);
    }
}
