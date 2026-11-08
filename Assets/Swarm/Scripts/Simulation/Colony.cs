using UnityEngine;

namespace Swarm.Simulation
{
    public enum UpgradeType : byte { Speed, Carry, Sting, Comb, Count }

    /// <summary>Экономика колонии: мёд, улучшения, стоимость вывода новых пчёл.</summary>
    public sealed class Colony
    {
        readonly SimConfig _cfg;
        public float Honey;
        public readonly int[] Level = new int[(int)UpgradeType.Count];
        public int GuardIndex;

        public Colony(SimConfig cfg) { _cfg = cfg; }

        public float SpeedMul => _cfg.SpeedMul[Level[(int)UpgradeType.Speed]];
        public float CarryMul => _cfg.CarryMul[Level[(int)UpgradeType.Carry]];
        public float StingMul => _cfg.StingMul[Level[(int)UpgradeType.Sting]];
        public int MaxBees => Mathf.Min(_cfg.CombCap[Level[(int)UpgradeType.Comb]], _cfg.MaxBeesHard);

        int[] Costs(UpgradeType t)
        {
            switch (t)
            {
                case UpgradeType.Speed: return _cfg.SpeedCost;
                case UpgradeType.Carry: return _cfg.CarryCost;
                case UpgradeType.Sting: return _cfg.StingCost;
                default: return _cfg.CombCost;
            }
        }

        public int MaxLevel(UpgradeType t) => Costs(t).Length;
        public bool IsMaxed(UpgradeType t) => Level[(int)t] >= MaxLevel(t);
        public int CostOf(UpgradeType t) => IsMaxed(t) ? -1 : Costs(t)[Level[(int)t]];

        public bool CanAfford(int cost) => cost >= 0 && Honey >= cost;

        public bool TryUpgrade(UpgradeType t)
        {
            int c = CostOf(t);
            if (!CanAfford(c)) return false;
            Honey -= c;
            Level[(int)t]++;
            return true;
        }

        public int HatchCost(int bees) => _cfg.HatchCostBase + Mathf.RoundToInt(bees * _cfg.HatchCostPerBee * _cfg.HatchBatch);
    }
}
