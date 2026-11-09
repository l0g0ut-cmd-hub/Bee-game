using UnityEngine;

namespace Swarm.Simulation
{
    public enum Weather : byte { Clear, Rain, Wind, Heat }

    /// <summary>
    /// Небо: смена дня и ночи и погода.
    /// - Лето делится на дни; последние NightLength секунд каждого дня — ночь: цветы закрыты, враги
    ///   уходят и не нападают, рой медленнее, зато матка откладывает вдвое быстрее. Ритм «вылазка → передышка».
    /// - Дождь: сбор ×0.45, рой медленнее, осы не вылетают (волны откладываются).
    /// - Ветер: сносит рой и врагов постоянным вектором.
    /// - Жара: растёт жажда, сбор падает; пруд утоляет её (второй ресурс — вода).
    /// </summary>
    public sealed class SkySystem
    {
        public float DayLength = 150f;
        public float NightLength = 22f;
        public const float NightCollect = 0.4f;
        public bool Enabled = true;

        public Weather Current = Weather.Clear;
        public float WeatherBlend;           // 0..1 — насколько погода «вошла в силу» (для плавного перехода)
        public float WeatherTimer;
        public Vector2 Wind;
        public float Thirst;                 // 0..1 при жаре

        public int Day(float time) => Mathf.FloorToInt(time / DayLength) + 1;
        public float DayPhase(float time) => Mathf.Repeat(time, DayLength);
        public bool IsNight(float time) => Enabled && DayPhase(time) > DayLength - NightLength;
        /// <summary>0 — день, 1 — глубокая ночь; плавные сумерки по 4 c.</summary>
        public float Darkness(float time)
        {
            if (!Enabled) return 0f;
            float ph = DayPhase(time), start = DayLength - NightLength;
            if (ph < start - 4f) return Mathf.Clamp01(1f - ph / 4f) * (time > 4f ? 1f : 0f);
            return Mathf.Clamp01((ph - (start - 4f)) / 4f);
        }

        public float CollectMul(float time)
        {
            if (IsNight(time)) return NightCollect;   // ночью цветы дают меньше, но рой не простаивает
            float m = 1f;
            if (Current == Weather.Rain) m *= Mathf.Lerp(1f, 0.45f, WeatherBlend);
            if (Current == Weather.Heat) m *= Mathf.Lerp(1f, 0.55f, WeatherBlend * Mathf.Clamp01(Thirst * 1.6f));
            return m;
        }

        public float SpeedMul(float time)
        {
            float m = IsNight(time) ? 0.7f : 1f;
            if (Current == Weather.Rain) m *= Mathf.Lerp(1f, 0.78f, WeatherBlend);
            return m;
        }

        /// <summary>Осы и шершни не вылетают ночью и в дождь.</summary>
        public bool EnemiesGrounded(float time) => IsNight(time) || (Current == Weather.Rain && WeatherBlend > 0.5f);

        public void Reset(Rng rng)
        {
            Current = Weather.Clear;
            WeatherBlend = 0f;
            WeatherTimer = rng.Range(55f, 80f);
            Wind = Vector2.zero;
            Thirst = 0f;
        }

        public void Step(float dt, GameSim sim)
        {
            if (!Enabled) return;
            var rng = sim.Rng;
            WeatherTimer -= dt;
            if (!sim.Cfg.WeatherEnabled) { Current = Weather.Clear; WeatherTimer = 30f; }
            else if (WeatherTimer <= 0f)
            {
                if (Current == Weather.Clear)
                {
                    float r = rng.Value;
                    Current = r < 0.4f ? Weather.Rain : r < 0.72f ? Weather.Wind : Weather.Heat;
                    WeatherTimer = rng.Range(28f, 42f);
                    if (Current == Weather.Wind)
                    {
                        float a = rng.Value * 6.2832f;
                        Wind = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sim.Cfg.WindStrength;
                    }
                    sim.Events.Push(SimEventType.WeatherChanged, sim.Swarm.LeaderPos, 0f, (int)Current);
                }
                else
                {
                    Current = Weather.Clear;
                    WeatherTimer = rng.Range(55f, 85f);
                    sim.Events.Push(SimEventType.WeatherChanged, sim.Swarm.LeaderPos, 0f, (int)Current);
                }
            }
            WeatherBlend = Mathf.MoveTowards(WeatherBlend, Current == Weather.Clear ? 0f : 1f, dt / 4f);
            if (Current != Weather.Wind) Wind = Vector2.MoveTowards(Wind, Vector2.zero, dt * 6f);

            // жажда: копится в жару, утоляется у пруда
            bool atWater = sim.World.NearPond(sim.Swarm.LeaderPos, sim.Swarm.Radius * 0.5f + 6f);
            if (atWater && Thirst > 0.05f) Thirst = Mathf.MoveTowards(Thirst, 0f, dt * 0.6f);
            else if (Current == Weather.Heat) Thirst = Mathf.Min(1f, Thirst + dt / 30f);
            else Thirst = Mathf.MoveTowards(Thirst, 0f, dt / 60f);
        }
    }
}
