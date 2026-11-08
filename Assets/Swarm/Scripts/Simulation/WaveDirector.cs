using UnityEngine;

namespace Swarm.Simulation
{
    public enum WaveMessage : byte { Scouts, Wasps, Hornet, Invasion }

    /// <summary>Расписание нападений на одну «летнюю» сессию.</summary>
    public sealed class WaveDirector
    {
        public struct Wave
        {
            public float Time;
            public int Wasps, Hornets;
            public WaveMessage Message;
            public Wave(float time, int wasps, int hornets, WaveMessage msg)
            { Time = time; Wasps = wasps; Hornets = hornets; Message = msg; }
        }

        public static readonly Wave[] Schedule =
        {
            new Wave(45f, 2, 0, WaveMessage.Scouts),
            new Wave(95f, 3, 0, WaveMessage.Wasps),
            new Wave(145f, 4, 0, WaveMessage.Wasps),
            new Wave(195f, 1, 1, WaveMessage.Hornet),
            new Wave(245f, 5, 0, WaveMessage.Wasps),
            new Wave(295f, 4, 1, WaveMessage.Hornet),
            new Wave(345f, 6, 0, WaveMessage.Wasps),
            new Wave(395f, 3, 2, WaveMessage.Hornet),
            new Wave(445f, 7, 1, WaveMessage.Wasps),
            new Wave(495f, 4, 2, WaveMessage.Invasion),
            new Wave(550f, 5, 2, WaveMessage.Invasion),
        };

        int _next;
        float _delay;       // накопленная отсрочка расписания
        int _postponed;     // сколько раз подряд отложена текущая волна
        public int NextIndex => _next;
        public float NextTime => _next < Schedule.Length ? Schedule[_next].Time + _delay : float.MaxValue;
        public int Postponements { get; private set; }

        public void Reset() { _next = 0; _delay = 0f; _postponed = 0; Postponements = 0; }

        /// <summary>
        /// Директор поверх расписания: если рой только что понёс тяжёлые потери, волна откладывается
        /// (до WaveMaxPostpones раз), а маленькой колонии приходит ослабленная волна. Лечит «спираль смерти».
        /// </summary>
        public void Step(float time, GameSim sim)
        {
            var cfg = sim.Cfg;
            while (_next < Schedule.Length && time >= Schedule[_next].Time + _delay)
            {
                if (_postponed < cfg.WaveMaxPostpones && sim.RecentLosses > sim.Swarm.Count * cfg.WaveLossThreshold)
                {
                    _delay += cfg.WavePostpone;
                    _postponed++;
                    Postponements++;
                    return;
                }
                var w = Schedule[_next];
                if (sim.Swarm.Count < 60)
                {
                    w.Hornets = Mathf.Max(0, w.Hornets - 1);
                    w.Wasps = Mathf.Max(1, Mathf.CeilToInt(w.Wasps * 0.75f));
                }
                Launch(w, sim);
                _next++;
                _postponed = 0;
            }
        }

        void Launch(in Wave w, GameSim sim)
        {
            var rng = sim.Rng;
            var world = sim.World;
            // точка входа на краю карты, подальше от роя
            Vector2 best = Vector2.zero;
            float bestD = -1f;
            for (int a = 0; a < 8; a++)
            {
                Vector2 p = RandomEdge(rng, world);
                float d = (p - sim.Swarm.LeaderPos).sqrMagnitude;
                if (d > bestD) { bestD = d; best = p; }
            }
            int total = w.Wasps + w.Hornets;
            for (int k = 0; k < total; k++)
            {
                var t = k < w.Hornets ? EnemyType.Hornet : EnemyType.Wasp;
                Vector2 p = best + new Vector2(rng.Signed, rng.Signed) * 14f;
                if (sim.Enemies.Spawn(t, p, rng))
                    sim.Events.Push(SimEventType.EnemySpawned, p, 0f, (int)t);
            }
            sim.Events.Push(SimEventType.WaveIncoming, best, total, (int)w.Message);
        }

        static Vector2 RandomEdge(Rng rng, WorldMap w)
        {
            switch (rng.Range(0, 4))
            {
                case 0: return new Vector2(-10f, rng.Range(40f, w.Height - 40f));
                case 1: return new Vector2(w.Width + 10f, rng.Range(40f, w.Height - 40f));
                case 2: return new Vector2(rng.Range(40f, w.Width - 40f), -10f);
                default: return new Vector2(rng.Range(40f, w.Width - 40f), w.Height + 10f);
            }
        }
    }
}
