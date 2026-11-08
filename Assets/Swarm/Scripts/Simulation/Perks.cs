namespace Swarm.Simulation
{
    public enum PerkId : byte
    {
        ThickStings, HeatProof, Scouting, BigCrop, SwiftWings, IronGuards, FertileQueen, SweetNectar, Evasion, Count
    }

    /// <summary>Множители, которыми перки меняют симуляцию. 1 = без изменений.</summary>
    public sealed class PerkMods
    {
        public float Dps = 1f, HeatDeath = 1f, ScoutReach = 1f, Carry = 1f, Speed = 1f;
        public float DefenderRegen = 1f, DefenderHpBonus = 0f, Brood = 1f, BroodCost = 1f, Collect = 1f, EnemyBiteSlow = 1f;

        public void Reset()
        {
            Dps = HeatDeath = ScoutReach = Carry = Speed = DefenderRegen = Brood = BroodCost = Collect = EnemyBiteSlow = 1f;
            DefenderHpBonus = 0f;
        }
    }

    /// <summary>
    /// Перки «1 из 3» на вехах роста роя. Каждый — заметное изменение стиля игры,
    /// чтобы партии отличались сборками.
    /// </summary>
    public static class Perks
    {
        public static readonly int[] Milestones = { 40, 80, 120 };

        public static string Name(PerkId p)
        {
            switch (p)
            {
                case PerkId.ThickStings: return "ТОЛСТЫЕ ЖАЛА";
                case PerkId.HeatProof: return "ЖАРОСТОЙКОСТЬ";
                case PerkId.Scouting: return "РАЗВЕДКА";
                case PerkId.BigCrop: return "БОЛЬШОЙ ЗОБ";
                case PerkId.SwiftWings: return "БЫСТРЫЕ КРЫЛЬЯ";
                case PerkId.IronGuards: return "ЖЕЛЕЗНЫЕ СТРАЖИ";
                case PerkId.FertileQueen: return "ПЛОДОВИТАЯ МАТКА";
                case PerkId.SweetNectar: return "СЛАДКИЙ НЕКТАР";
                default: return "УВЁРТЛИВОСТЬ";
            }
        }

        public static string Desc(PerkId p)
        {
            switch (p)
            {
                case PerkId.ThickStings: return "УРОН ЖАЛ +30%";
                case PerkId.HeatProof: return "В ШАРЕ ГИБНЕТ ВДВОЕ МЕНЬШЕ";
                case PerkId.Scouting: return "РАЗВЕДЧИЦ ВДВОЕ БОЛЬШЕ";
                case PerkId.BigCrop: return "НОША +25%";
                case PerkId.SwiftWings: return "СКОРОСТЬ +12%";
                case PerkId.IronGuards: return "ЗАЩИТНИКИ +1 ЖИЗНЬ";
                case PerkId.FertileQueen: return "РАСПЛОД БЫСТРЕЕ И ДЕШЕВЛЕ";
                case PerkId.SweetNectar: return "СБОР НЕКТАРА +30%";
                default: return "ВРАГИ КУСАЮТ РЕЖЕ";
            }
        }

        public static void Apply(PerkId p, GameSim sim)
        {
            var m = sim.Mods;
            switch (p)
            {
                case PerkId.ThickStings: m.Dps *= 1.3f; break;
                case PerkId.HeatProof: m.HeatDeath *= 0.5f; break;
                case PerkId.Scouting:
                    m.ScoutReach *= 1.3f;
                    sim.Swarm.ScoutChance = 0.15f;
                    for (int i = 0; i < sim.Swarm.Count; i++)
                        if (sim.Swarm.Role[i] == BeeRole.Swarm && sim.Rng.Value < 0.09f) sim.Swarm.Scout[i] = true;
                    break;
                case PerkId.BigCrop: m.Carry *= 1.25f; break;
                case PerkId.SwiftWings: m.Speed *= 1.12f; break;
                case PerkId.IronGuards:
                    m.DefenderHpBonus += 1f;
                    m.DefenderRegen *= 1.5f;
                    for (int i = 0; i < sim.Swarm.Count; i++)
                        if (sim.Swarm.Role[i] == BeeRole.Guard) { sim.Swarm.MaxHp[i] += 1f; sim.Swarm.Hp[i] += 1f; }
                    break;
                case PerkId.FertileQueen: m.Brood *= 1.4f; m.BroodCost *= 0.85f; break;
                case PerkId.SweetNectar: m.Collect *= 1.3f; break;
                default: m.EnemyBiteSlow *= 1.25f; break;
            }
        }
    }
}
