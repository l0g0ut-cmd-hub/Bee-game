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
        public float CometTail = 1.0f;       // длина шлейфа «кометы» в радиусах роя на полной скорости
        public float TurnWaveDelay = 0.45f;  // задержка следования края роя за лидером, сек
        public float ScoutReachMul = 2.3f;   // насколько дальше обычного разведчицы ищут цветы

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

        // --- тепловой шар: палец удерживается на месте рядом с врагом
        public int BallMinBees = 10;
        public float BallLockRange = 45f;     // насколько враг может быть дальше края роя
        public float BallJoinRange = 150f;    // пчёлы ближе этого к цели сбиваются в шар
        public float BallStingerMul = 2.5f;   // в шаре вокруг врага помещается больше пчёл
        public float HeatDamageMul = 2.2f;    // прогрев: урон пчёл в шаре
        public float HeatDeathRate = 0.03f;   // гибель от перегрева: на пчелу в шаре в секунду

        // --- стражи улья
        public int[] GuardSteps = { 0, 5, 10, 20 };
        public float GuardRecruitRadius = 80f;
        public float GuardDefendRadius = 85f;
        public float GuardStingMul = 2f;      // дома стражи жалят сильнее
        public float RaidBiteSlow = 1.7f;     // оса, занятая грабежом, кусается реже

        // --- зимовка (если цель лета не выполнена к концу)
        public int Winter2Bees = 85, Winter2Honey = 240;
        public int Winter1Bees = 50, Winter1Honey = 140;

        // --- директор волн
        public float WaveLossThreshold = 0.22f; // доля роя, потерянная «недавно», при которой волна откладывается
        public float WavePostpone = 18f;
        public int WaveMaxPostpones = 2;

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
