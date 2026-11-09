namespace Swarm.Simulation
{
    public enum Breed : byte { Central, Carpathian, Buckfast, Italian, Count }

    /// <summary>
    /// Породы пчёл — мета-прогресс между партиями: открываются звёздами кампании,
    /// выбираются перед сезоном и меняют стиль игры.
    /// </summary>
    public static class Breeds
    {
        public static readonly int[] UnlockStars = { 0, 3, 5, 8 };

        public static string Name(Breed b)
        {
            switch (b)
            {
                case Breed.Carpathian: return "КАРПАТКА";
                case Breed.Buckfast: return "БАКФАСТ";
                case Breed.Italian: return "ИТАЛЬЯНКА";
                default: return "СРЕДНЕРУССКАЯ";
            }
        }

        public static string Desc(Breed b)
        {
            switch (b)
            {
                case Breed.Carpathian: return "БЫСТРЫЕ: СКОРОСТЬ +12%";
                case Breed.Buckfast: return "ТРУЖЕНИЦЫ: НОША +20%, РАСПЛОД ДЕШЕВЛЕ";
                case Breed.Italian: return "ЗЛЫЕ: ЖАЛО +20%, +5 ПЧЁЛ НА СТАРТЕ";
                default: return "СТОЙКИЕ: МЕНЬШЕ ГИБНУТ В ШАРЕ";
            }
        }

        public static void Apply(Breed b, GameSim sim)
        {
            var m = sim.Mods;
            switch (b)
            {
                case Breed.Carpathian: m.Speed *= 1.12f; break;
                case Breed.Buckfast: m.Carry *= 1.2f; m.BroodCost *= 0.85f; break;
                case Breed.Italian:
                    m.Dps *= 1.2f;
                    for (int i = 0; i < 5; i++) sim.Swarm.Spawn(sim.World.HivePos + BeeSwarm.HiveEntranceOffset, sim.Rng);
                    break;
                default: m.HeatDeath *= 0.6f; break;
            }
        }
    }
}
