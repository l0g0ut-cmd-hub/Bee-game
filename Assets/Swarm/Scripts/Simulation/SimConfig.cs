using System;

namespace Swarm.Simulation
{
    /// <summary>Все игровые константы в одном месте. Единица длины — пиксель мира.</summary>
    [Serializable]
    public class SimConfig
    {
        // --- мир и сессия
        public float WorldWidth = 640f;
        public float WorldHeight = 900f;
        public float SessionTime = 600f;     // «лето»: 10 минут
        public int GoalBees = 120;
        public int GoalHoney = 350;

        // --- рой
        public int MaxBeesHard = 256;        // ёмкость массивов
        public int StartBees = 20;
        public float LeaderSpeed = 64f;      // базовая скорость центра роя
        public float LeaderAccel = 240f;
        public float LeaderBrake = 170f;
        public float BeeSpeedFactor = 1.55f; // пчела быстрее центра, чтобы догонять
        public float BeeAccel = 420f;
        public float SwarmRadiusBase = 7f;
        public float SwarmRadiusPerSqrtBee = 3.0f;
        public float SeparationDist = 7f;
        public float SeparationForce = 40f;   // скорость расталкивания при полном перекрытии
        public float WanderAmp = 26f;
        public float LeashDistance = 95f;    // дальше этого пчела бросает дела и летит к рою

        // --- нектар и улей
        public float GatherReach = 18f;      // насколько цветок может быть дальше края роя
        public float CollectRate = 1.3f;     // нектара/сек на пчелу
        public float CarryBase = 1.0f;       // ёмкость пчелы на уровне 0
        public float HiveReach = 34f;
        public float FlowerRegrowTime = 28f;

        // --- бой
        public float BeeDps = 1.0f;
        public float StingRange = 3f;
        public float AggroRadius = 36f;
        public int MaxStingersPerEnemy = 14;  // для осы; шершень вмещает вдвое больше

        // --- экономика
        public int HatchBatch = 5;
        public int HatchCostBase = 20;
        public float HatchCostPerBee = 0.08f; // стоимость партии растёт с размером колонии

        // Улучшения: уровень 0 — базовый. Стоимость перехода на следующий уровень.
        public float[] SpeedMul = { 1f, 1.15f, 1.3f, 1.45f, 1.6f };
        public int[] SpeedCost = { 35, 70, 120, 180 };
        public float[] CarryMul = { 1f, 1.5f, 2f, 2.6f, 3.3f };
        public int[] CarryCost = { 30, 65, 110, 170 };
        public float[] StingMul = { 1f, 1.4f, 1.85f, 2.4f, 3f };
        public int[] StingCost = { 40, 80, 130, 200 };
        public int[] CombCap = { 40, 65, 95, 130, 170 };
        public int[] CombCost = { 30, 70, 120, 180 };

        public static SimConfig Default() => new SimConfig();
    }
}
