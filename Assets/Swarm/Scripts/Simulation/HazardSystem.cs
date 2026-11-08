using UnityEngine;

namespace Swarm.Simulation
{
    public enum BirdState : byte { None, Shadow, Gone }

    /// <summary>
    /// Угрозы, которые не дерутся, а наказывают за невнимательность:
    /// - птица-щурка: тень на земле за 1.6 c до пике в точку, куда летит рой; уводи рой — шар не поможет;
    /// - паутина между деревьями: влетевшие пчёлы застревают, паук ест их по одной;
    ///   масса роя внутри сети рвёт её, освобождая пленниц. Сеть отстраивается через минуту.
    /// </summary>
    public sealed class HazardSystem
    {
        // --- птицы
        public const int MaxBirds = 3;
        public readonly BirdState[] BState = new BirdState[MaxBirds];
        public readonly Vector2[] BTarget = new Vector2[MaxBirds];
        public readonly Vector2[] BDir = new Vector2[MaxBirds];
        public readonly float[] BTimer = new float[MaxBirds];
        public const float ShadowTime = 1.6f;
        public const float StrikeRadius = 17f;
        const int StrikeMaxKills = 7;
        float _nextBird;

        // --- паутина
        public const int MaxWebs = 6;
        public int WebCount;
        public readonly Vector2[] WPos = new Vector2[MaxWebs];
        public readonly float[] WStrength = new float[MaxWebs];   // 0 — порвана
        public readonly float[] WRebuild = new float[MaxWebs];
        public readonly float[] WEat = new float[MaxWebs];
        public readonly int[] WInside = new int[MaxWebs];
        public readonly Vector2[] SpiderPos = new Vector2[MaxWebs];
        public const float WebRadius = 15f;
        const float WebStrength = 100f;

        public void Reset(GameSim sim)
        {
            for (int i = 0; i < MaxBirds; i++) BState[i] = BirdState.None;
            _nextBird = 110f + sim.Rng.Range(0f, 20f);
            for (int w = 0; w < WebCount; w++) { WStrength[w] = WebStrength; WRebuild[w] = 0f; WEat[w] = 2.5f; SpiderPos[w] = WPos[w]; }
        }

        /// <summary>Паутина натягивается между парами близких деревьев.</summary>
        public void Generate(WorldMap world, Rng rng)
        {
            WebCount = 0;
            var obs = world.Obstacles;
            for (int a = 0; a < obs.Count && WebCount < MaxWebs; a++)
            {
                if (obs[a].Kind != ObstacleKind.Tree) continue;
                for (int b = a + 1; b < obs.Count && WebCount < MaxWebs; b++)
                {
                    if (obs[b].Kind != ObstacleKind.Tree) continue;
                    float d = (obs[a].Pos - obs[b].Pos).magnitude;
                    if (d < 45f || d > 95f || rng.Value < 0.35f) continue;
                    Vector2 mid = (obs[a].Pos + obs[b].Pos) * 0.5f;
                    if ((mid - world.HivePos).magnitude < 120f) continue;
                    if (world.InsideObstacle(mid, WebRadius)) continue;
                    WPos[WebCount] = mid;
                    WStrength[WebCount] = WebStrength;
                    SpiderPos[WebCount] = mid;
                    WebCount++;
                }
            }
        }

        public bool CatchInWeb(Vector2 p)
        {
            for (int w = 0; w < WebCount; w++)
                if (WStrength[w] > 0f && (p - WPos[w]).sqrMagnitude < WebRadius * WebRadius) return true;
            return false;
        }

        public bool StillStuck(Vector2 p) => CatchInWeb(p);

        public void Step(float dt, GameSim sim)
        {
            var sw = sim.Swarm;
            var ev = sim.Events;

            // ---------------- птицы
            if (sim.Time >= _nextBird && sw.SwarmCount > 15)
            {
                _nextBird = sim.Time + sim.Rng.Range(sim.Cfg.BirdIntervalMin, sim.Cfg.BirdIntervalMax);
                for (int i = 0; i < MaxBirds; i++)
                {
                    if (BState[i] != BirdState.None) continue;
                    BState[i] = BirdState.Shadow;
                    BTimer[i] = ShadowTime;
                    // целится туда, где рой будет через ~полсекунды
                    BTarget[i] = sim.World.ClampToWorld(sw.LeaderPos + sw.LeaderVel * 0.5f, 20f);
                    float a = sim.Rng.Value * 6.2832f;
                    BDir[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.6f).normalized;
                    ev.Push(SimEventType.BirdIncoming, BTarget[i], 0f, i);
                    break;
                }
            }
            for (int i = 0; i < MaxBirds; i++)
            {
                if (BState[i] == BirdState.None) continue;
                BTimer[i] -= dt;
                if (BState[i] == BirdState.Shadow && BTimer[i] <= 0f)
                {
                    // пике: съедает до StrikeMaxKills пчёл под тенью
                    int kills = 0;
                    var buf = sw.QueryBuffer;
                    int nq = sw.Grid.Query(BTarget[i], StrikeRadius, buf);
                    for (int q = 0; q < nq && kills < StrikeMaxKills; q++)
                    {
                        int b = buf[q];
                        if (b >= sw.Count || sw.Dead[b]) continue;
                        if ((sw.Pos[b] - BTarget[i]).sqrMagnitude > StrikeRadius * StrikeRadius) continue;
                        sim.HurtBee(b, 97, true);
                        kills++;
                    }
                    ev.Push(SimEventType.BirdStrike, BTarget[i], kills, i);
                    BState[i] = BirdState.Gone;
                    BTimer[i] = 0.9f;
                }
                else if (BState[i] == BirdState.Gone && BTimer[i] <= 0f) BState[i] = BirdState.None;
            }

            // ---------------- паутина
            for (int w = 0; w < WebCount; w++)
            {
                if (WStrength[w] <= 0f)
                {
                    WRebuild[w] -= dt;
                    if (WRebuild[w] <= 0f) { WStrength[w] = WebStrength; SpiderPos[w] = WPos[w]; }
                    continue;
                }
                int inside = 0, stuck = -1;
                float bestStuck = 0f;
                var buf = sw.QueryBuffer;
                int nq = sw.Grid.Query(WPos[w], WebRadius + 2f, buf);
                for (int q = 0; q < nq; q++)
                {
                    int b = buf[q];
                    if (b >= sw.Count || sw.Dead[b]) continue;
                    if ((sw.Pos[b] - WPos[w]).sqrMagnitude > WebRadius * WebRadius) continue;
                    inside++;
                    if (sw.Stuck[b] > bestStuck) { bestStuck = sw.Stuck[b]; stuck = b; }
                }
                WInside[w] = inside;
                // рой рвёт сеть массой: чем больше пчёл внутри, тем быстрее
                if (inside >= 6) WStrength[w] -= inside * 2.2f * dt;
                // паук ползёт к пленнице и ест её
                Vector2 st = stuck >= 0 ? sw.Pos[stuck] : WPos[w];
                SpiderPos[w] = Vector2.MoveTowards(SpiderPos[w], st, 14f * dt);
                if (stuck >= 0)
                {
                    WEat[w] -= dt;
                    if (WEat[w] <= 0f && (SpiderPos[w] - sw.Pos[stuck]).sqrMagnitude < 9f)
                    {
                        WEat[w] = 2.5f;
                        sim.HurtBee(stuck, 98, true);
                    }
                }
                if (WStrength[w] <= 0f)
                {
                    WStrength[w] = 0f;
                    WRebuild[w] = 60f;
                    ev.Push(SimEventType.WebTorn, WPos[w], inside, w);
                }
            }
        }
    }
}
