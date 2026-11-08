using UnityEngine;

namespace Swarm.Simulation
{
    /// <summary>
    /// Туман войны: карта ячеек 16x16 px. Открывается вокруг роя и разведчиц.
    /// Разведчица, собравшая нектар с богатого или редкого цветка в ещё «немеченом» месте,
    /// «танцует» — на карте появляется метка нектара (как настоящий танец пчёл у улья).
    /// </summary>
    public sealed class FogMap
    {
        public const float Cell = 16f;
        public readonly int Cols, Rows;
        public readonly bool[] Revealed;
        public int Version;              // растёт при открытии новых ячеек — рендер перестраивает туман
        public bool Enabled = true;

        public const int MaxMarks = 6;
        public readonly Vector2[] MarkPos = new Vector2[MaxMarks];
        public readonly float[] MarkTime = new float[MaxMarks];
        public readonly int[] MarkFlower = new int[MaxMarks];
        readonly bool[] _marked = new bool[FlowerField.Capacity];

        public FogMap(float w, float h)
        {
            Cols = Mathf.CeilToInt(w / Cell);
            Rows = Mathf.CeilToInt(h / Cell);
            Revealed = new bool[Cols * Rows];
        }

        public void Reset(Vector2 start)
        {
            System.Array.Clear(Revealed, 0, Revealed.Length);
            System.Array.Clear(_marked, 0, _marked.Length);
            for (int i = 0; i < MaxMarks; i++) MarkTime[i] = 0f;
            Reveal(start, 115f);
            Version++;
        }

        public bool IsRevealed(Vector2 p)
        {
            if (!Enabled) return true;
            int cx = (int)(p.x / Cell), cy = (int)(p.y / Cell);
            if (cx < 0 || cy < 0 || cx >= Cols || cy >= Rows) return true;
            return Revealed[cy * Cols + cx];
        }

        public void Reveal(Vector2 c, float radius)
        {
            int x0 = Mathf.Max(0, (int)((c.x - radius) / Cell)), x1 = Mathf.Min(Cols - 1, (int)((c.x + radius) / Cell));
            int y0 = Mathf.Max(0, (int)((c.y - radius) / Cell)), y1 = Mathf.Min(Rows - 1, (int)((c.y + radius) / Cell));
            float r2 = radius * radius;
            bool changed = false;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * Cols + x;
                    if (Revealed[i]) continue;
                    float dx = (x + 0.5f) * Cell - c.x, dy = (y + 0.5f) * Cell - c.y;
                    if (dx * dx + dy * dy <= r2) { Revealed[i] = true; changed = true; }
                }
            if (changed) Version++;
        }

        public void Step(GameSim sim, float dt)
        {
            var sw = sim.Swarm;
            Reveal(sw.LeaderPos, sw.Radius + 75f);
            for (int i = 0; i < sw.Count; i++)
                if (sw.Task[i] == BeeTask.Scout) Reveal(sw.Pos[i], 34f);
            for (int m = 0; m < MaxMarks; m++)
                if (MarkTime[m] > 0f)
                {
                    MarkTime[m] -= dt;
                    int f = MarkFlower[m];
                    if (f >= sim.Flowers.Count || !sim.Flowers.IsBlooming(f)) MarkTime[m] = 0f;
                }
        }

        /// <summary>Разведчица собрала нектар: богатый или редкий цветок становится меткой на карте.</summary>
        public void ScoutVisit(GameSim sim, int flower)
        {
            if (flower < 0 || flower >= sim.Flowers.Count || _marked[flower]) return;
            var fl = sim.Flowers;
            if (!fl.Rare[flower] && fl.MaxNectar[flower] < 24f) return;
            _marked[flower] = true;
            int slot = 0;
            float oldest = float.MaxValue;
            for (int m = 0; m < MaxMarks; m++)
                if (MarkTime[m] < oldest) { oldest = MarkTime[m]; slot = m; }
            MarkPos[slot] = fl.Pos[flower];
            MarkTime[slot] = 30f;
            MarkFlower[slot] = flower;
            sim.Events.Push(SimEventType.ScoutDance, fl.Pos[flower], fl.Rare[flower] ? 1f : 0f, flower);
        }
    }
}
