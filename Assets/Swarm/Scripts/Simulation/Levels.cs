namespace Swarm.Simulation
{
    public enum Biome : byte { Meadow, Garden, Swamp }
    public enum GameMode : byte { Campaign, Endless, Daily }

    public sealed class LevelDef
    {
        public string Name, Sub;
        public Biome Biome;
        public int GoalBees, GoalHoney;
        public float WaveMul;
        public int UnlockStars;   // сколько звёзд кампании нужно, чтобы открыть
    }

    /// <summary>
    /// Кампания «сезоны»: весна → лето → осень на разных биомах с растущей сложностью.
    /// Выживание: бесконечное лето, волны крепчают с каждым кругом. Задание дня: одна карта у всех на сутки.
    /// </summary>
    public static class Levels
    {
        public static readonly LevelDef[] Campaign =
        {
            new LevelDef { Name = "ВЕСНА", Sub = "ЦВЕТУЩИЙ ЛУГ", Biome = Biome.Meadow, GoalBees = 100, GoalHoney = 450, WaveMul = 0.8f, UnlockStars = 0 },
            new LevelDef { Name = "ЛЕТО", Sub = "СТАРЫЙ САД", Biome = Biome.Garden, GoalBees = 120, GoalHoney = 650, WaveMul = 1.15f, UnlockStars = 2 },
            new LevelDef { Name = "ОСЕНЬ", Sub = "ТУМАННОЕ БОЛОТО", Biome = Biome.Swamp, GoalBees = 130, GoalHoney = 700, WaveMul = 1.15f, UnlockStars = 4 },
        };

        public static SimConfig Make(GameMode mode, int level)
        {
            var c = SimConfig.Default();
            var d = Campaign[UnityEngine.Mathf.Clamp(level, 0, Campaign.Length - 1)];
            if (mode == GameMode.Daily) d = Campaign[1];
            c.Biome = d.Biome;
            c.GoalBees = d.GoalBees;
            c.GoalHoney = d.GoalHoney;
            c.WaveMul = d.WaveMul;
            switch (d.Biome)
            {
                case Biome.Garden:
                    c.BirdIntervalMin = 36f; c.BirdIntervalMax = 56f;
                    break;
                case Biome.Swamp:
                    c.BirdIntervalMin = 34f; c.BirdIntervalMax = 50f;
                    c.SproutMax = 18;
                    c.WindStrength = 20f;
                    break;
            }
            // обучение по сезонам: весна — основы, лето — защита и опасности, осень — всё + дикий рой
            int season = mode == GameMode.Campaign ? UnityEngine.Mathf.Clamp(level, 0, 2) : (mode == GameMode.Daily ? 1 : 2);
            bool summer = season >= 1, autumn = season >= 2;
            c.AntsEnabled = c.BearEnabled = c.WebsEnabled = c.WeatherEnabled = summer;
            c.DefendersEnabled = c.WorkersEnabled = c.PostEnabled = summer;
            c.NestEnabled = autumn;
            c.RivalsEnabled = autumn;
            if (season == 0) c.BossHpMul = 0.55f;
            if (mode == GameMode.Endless)
            {
                c.Endless = true;
                c.SessionTime = 1e9f;
                c.WaveMul = 0.9f;
            }
            return c;
        }

        /// <summary>Сид задания дня: дата по UTC → у всех одинаковая карта.</summary>
        public static uint DailySeed(System.DateTime utc) => (uint)(utc.Year * 10000 + utc.Month * 100 + utc.Day) * 2654435761u;
    }
}
