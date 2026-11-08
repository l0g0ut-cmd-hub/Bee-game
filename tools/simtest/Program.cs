// Headless-стенд: бот играет полную сессию на нескольких сидах, печатает метрики.
// dotnet run -c Release -- [seeds] [verbose]
using System;
using System.Diagnostics;
using Swarm.Simulation;
using UnityEngine;

static class Program
{
    enum BotState { Gather, Deliver, Defend }

    static int Main(string[] args)
    {
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

    static (SimResult result, float time) RunOne(uint seed, float skill, bool verbose, ref long steps)
    {
        var cfg = SimConfig.Default();
        var sim = new GameSim(cfg, seed);
        var rnd = new Random((int)seed);
        const float dt = 1f / 60f;
        var state = BotState.Gather;
        int targetFlower = -1;
        float nextLog = 30f;
        float spreadAcc = 0f; int spreadN = 0; float maxSpread = 0f; int stuck = 0;
        float reactionTimer = 0f;
        Vector2 heldTarget = sim.Swarm.LeaderPos;

        while (sim.Result == SimResult.Running)
        {
            var sw = sim.Swarm;
            var col = sim.Colony;
            Vector2 hive = sim.World.HivePos + BeeSwarm.HiveEntranceOffset;
            float cap = cfg.CarryBase * col.CarryMul * sw.Count;

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
                // умный бот: не гоняется за всеми подряд; малым роем уходит от шершней
                if (threat >= 0 && skill >= 1f && sim.Enemies.Type[threat] == EnemyType.Hornet && sw.Count < 70)
                {
                    Vector2 away = sw.LeaderPos - sim.Enemies.Pos[threat];
                    state = BotState.Defend; heldTarget = sw.LeaderPos + away.normalized * 80f;
                }
                else if (threat >= 0 && (skill >= 1f ? threatD < 90f * 90f : rnd.NextDouble() < 0.5)) { state = BotState.Defend; heldTarget = sim.Enemies.Pos[threat]; }
                else if (raider >= 0 && skill >= 1f) { state = BotState.Defend; heldTarget = sim.Enemies.Pos[raider]; }
                else if (sw.TotalCarry >= cap * 0.85f || (state == BotState.Deliver && sw.TotalCarry > 0.5f))
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
            if (sim.SwarmAtHive && sw.TotalCarry < 0.5f)
            {
                for (int guard = 0; guard < 20; guard++)
                {
                    bool bought = false;
                    bool goalBees = sw.Count >= cfg.GoalBees;
                    if (goalBees) break; // копим мёд
                    // к концу лета умный бот не перевыкупает потери, если до цели рукой подать
                    if (skill >= 1f && sim.Time > 420f && sw.Count >= cfg.GoalBees - 15 && col.Honey < cfg.GoalHoney) break;
                    if (sw.Count + cfg.HatchBatch > col.MaxBees && sim.CanUpgrade(UpgradeType.Comb)) bought = sim.TryUpgrade(UpgradeType.Comb);
                    else if (sim.CanHatch) bought = sim.TryHatch();
                    else if (col.Level[(int)UpgradeType.Carry] < 2 && sim.CanUpgrade(UpgradeType.Carry)) bought = sim.TryUpgrade(UpgradeType.Carry);
                    else if (col.Level[(int)UpgradeType.Sting] < 2 && sim.Time > 150 && sim.CanUpgrade(UpgradeType.Sting)) bought = sim.TryUpgrade(UpgradeType.Sting);
                    else if (col.Level[(int)UpgradeType.Speed] < 1 && sim.CanUpgrade(UpgradeType.Speed)) bought = sim.TryUpgrade(UpgradeType.Speed);
                    if (!bought) break;
                }
            }

            var input = new SimInput { Active = true, Target = heldTarget };
            sim.Step(dt, input);
            sim.Events.Clear();
            steps++;

            // метрики сплочённости роя
            if (sw.Count > 0 && ((int)(sim.Time * 60) % 30) == 0)
            {
                float acc = 0f, mx = 0f;
                for (int i = 0; i < sw.Count; i++)
                {
                    float d = (sw.Pos[i] - sw.LeaderPos).magnitude;
                    acc += d; if (d > mx) mx = d;
                    if (sim.World.InsideObstacle(sw.Pos[i], 0f)) stuck++;
                }
                spreadAcc += acc / sw.Count; spreadN++;
                if (mx > maxSpread) maxSpread = mx;
            }

            if (verbose && sim.Time >= nextLog)
            {
                nextLog += 30f;
                Console.WriteLine($"  t={sim.Time,5:F0} bees={sw.Count,3}/{col.MaxBees,3} honey={col.Honey,6:F0} carry={sw.TotalCarry,5:F1} " +
                                  $"lv[spd {col.Level[0]} car {col.Level[1]} sti {col.Level[2]} comb {col.Level[3]}] enemies={sim.Enemies.Count,2} " +
                                  $"lost={sim.Stats.BeesLost,3} stolen={sim.Stats.HoneyStolen,4:F0} R={sw.Radius:F1}");
            }
        }
        var st = sim.Stats;
        Console.WriteLine($" seed {seed,6}: {sim.Result,-10} t={sim.Time,5:F0}s bees={sim.Swarm.Count,3} honey={sim.Colony.Honey,4:F0} " +
                          $"delivered={st.HoneyDelivered,5:F0} hatched={st.BeesHatched,3} lost={st.BeesLost,3} kills w{st.WaspsKilled}/h{st.HornetsKilled} " +
                          $"stolen={st.HoneyStolen,4:F0} spread avg={spreadAcc / Math.Max(1, spreadN):F1} max={maxSpread:F0} inObstacle={stuck}");
        return (sim.Result, sim.Time);
    }
}
