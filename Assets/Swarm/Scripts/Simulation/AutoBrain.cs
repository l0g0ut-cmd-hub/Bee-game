using UnityEngine;

namespace Swarm.Simulation
{
    /// <summary>
    /// «Мозг» автопилота: куда вести рой, что купить в улье, какой дар взять, когда оставить отряд.
    /// Чистая логика без UI — её же гоняет стенд tools/simtest (BOT=brain).
    /// </summary>
    public sealed class AutoBrain
    {
        public Vector2 WorldTarget;
        public bool WantHold;
        public int TargetFlower => _flower;
        public bool Delivering => _delivering;
        int _flower = -1;
        bool _delivering;
        float _progT, _progCarry;
        readonly float[] _skip = new float[512];

        public void Reset()
        {
            _flower = -1;
            _delivering = false;
            _progT = _progCarry = 0f;
            WantHold = false;
            System.Array.Clear(_skip, 0, _skip.Length);
        }

        public bool ShouldPost(GameSim sim)
        {
            var sw = sim.Swarm;
            return sim.CanPost && sw.SwarmCount >= 50 && sim.Time > 120f && !_delivering && _flower >= 0
                   && sim.Flowers.CollectMul > 0.05f
                   && (sim.Flowers.Pos[_flower] - sw.LeaderPos).sqrMagnitude < 30f * 30f
                   && BloomingNear(sim, sw.LeaderPos, sim.Cfg.PostRadius) >= 3;
        }

        public static bool ShouldRecall(GameSim sim, float postAge)
        {
            var sw = sim.Swarm;
            return sw.SquadActive && postAge > 8f && (BloomingNear(sim, sw.SquadTarget, sim.Cfg.PostRadius) == 0 || sw.SquadCount < 5);
        }

        public void Decide(GameSim sim)
        {
            var sw = sim.Swarm;
            var col = sim.Colony;
            var cfg = sim.Cfg;
            Vector2 hive = sim.World.HivePos + BeeSwarm.HiveEntranceOffset;
            float cap = cfg.CarryBase * col.CarryMul * sim.Mods.Carry * Mathf.Max(1, sw.SwarmCount - sw.SquadCount);
            var en = sim.Enemies;
            int nest = -1;
            int threat = -1, raider = -1;
            float threatD = 140f * 140f;
            for (int e = 0; e < en.Count; e++)
            {
                if (en.Type[e] == EnemyType.Nest) { nest = e; continue; }
                if (en.Type[e] == EnemyType.Bear && sw.SwarmCount < 60) continue;
                if (en.Mode[e] == EnemyMode.Flee) continue;   // спит/убегает — не гоняемся
                float d = (en.Pos[e] - sw.LeaderPos).sqrMagnitude;
                if (d < threatD) { threatD = d; threat = e; }
                if (en.Mode[e] == EnemyMode.Raid && (en.Pos[e] - hive).sqrMagnitude < 90f * 90f) raider = e;
            }
            WantHold = false;
            if (threat >= 0 && en.Type[threat] == EnemyType.Hornet && sw.SwarmCount >= 40)
            {
                WorldTarget = en.Pos[threat];
                WantHold = true;
            }
            else if (threat >= 0 && en.Type[threat] == EnemyType.Hornet && sw.SwarmCount < 70)
            {
                Vector2 away = sw.LeaderPos - en.Pos[threat];
                WorldTarget = sw.LeaderPos + away.normalized * 80f;
            }
            else if (threat >= 0 && threatD < 90f * 90f) WorldTarget = en.Pos[threat];
            else if (raider >= 0) WorldTarget = en.Pos[raider];
            else if (sim.Sky.Thirst > 0.6f && sim.World.Ponds.Count > 0)
            {
                Vector2 best = sim.World.Ponds[0];
                for (int i = 1; i < sim.World.Ponds.Count; i++)
                    if ((sim.World.Ponds[i] - sw.LeaderPos).sqrMagnitude < (best - sw.LeaderPos).sqrMagnitude) best = sim.World.Ponds[i];
                WorldTarget = best;
            }
            else if (sw.FieldCarry >= cap * 0.85f || (_delivering && sw.FieldCarry > 0.5f))
            {
                _delivering = true;
                WorldTarget = hive;
            }
            else if (sim.Flowers.CollectMul < 0.05f)
            {
                // ночь: цветы закрыты, осы спят — большим роем идём жечь гнездо, иначе отдыхаем у улья
                _delivering = false;
                _flower = -1;
                WorldTarget = nest >= 0 && sw.SwarmCount >= 60 ? en.Pos[nest] : hive + new Vector2(0f, -24f);
            }
            else
            {
                _delivering = false;
                var fl = sim.Flowers;
                // сторож прогресса: рой у цели, а ноша не растёт — поляна выжата, летим дальше / сдаём
                if (_flower >= 0 && _flower < fl.Count && (fl.Pos[_flower] - sw.LeaderPos).sqrMagnitude < 35f * 35f)
                {
                    _progT += 0.25f;
                    if (_progT >= 2.5f)
                    {
                        if (sw.FieldCarry - _progCarry < cap * 0.04f)
                        {
                            if (_flower < _skip.Length) _skip[_flower] = sim.Time + 25f;
                            if (sw.FieldCarry >= cap * 0.35f) { _delivering = true; WorldTarget = hive; _flower = -1; return; }
                            _flower = -1;
                        }
                        _progT = 0f;
                        _progCarry = sw.FieldCarry;
                    }
                }
                else { _progT = 0f; _progCarry = sw.FieldCarry; }

                if (_flower < 0 || _flower >= fl.Count || !fl.IsBlooming(_flower) || fl.Nectar[_flower] < 1f)
                {
                    _flower = -1;
                    float best = float.MaxValue;
                    for (int f = 0; f < fl.Count; f++)
                    {
                        if (!fl.IsBlooming(f) || fl.Nectar[f] < 2f) continue;
                        if (f < _skip.Length && _skip[f] > sim.Time) continue;
                        if (sw.SquadActive && (fl.Pos[f] - sw.SquadTarget).sqrMagnitude < cfg.PostRadius * cfg.PostRadius) continue; // там работает отряд
                        // ценность поляны: нектар цветов вокруг кандидата
                        float patch = 0f;
                        for (int g = 0; g < fl.Count; g++)
                            if (fl.IsBlooming(g) && (fl.Pos[g] - fl.Pos[f]).sqrMagnitude < 32f * 32f) patch += fl.Nectar[g];
                        float d = (fl.Pos[f] - sw.LeaderPos).magnitude + (fl.Pos[f] - hive).magnitude * 0.5f;
                        float score = (d + 40f) / (2f + patch);
                        if (score < best) { best = score; _flower = f; }
                    }
                    _progT = 0f;
                    _progCarry = sw.FieldCarry;
                }
                if (_flower >= 0) WorldTarget = fl.Pos[_flower];
                else if (sw.FieldCarry > 0.5f) { _delivering = true; WorldTarget = hive; }
                else WorldTarget = hive + new Vector2(0f, -24f);
            }
        }

        public static int BloomingNear(GameSim sim, Vector2 c, float r)
        {
            var fl = sim.Flowers;
            int n = 0;
            for (int f = 0; f < fl.Count; f++)
                if (fl.IsBlooming(f) && fl.Nectar[f] > 2f && (fl.Pos[f] - c).sqrMagnitude < r * r) n++;
            return n;
        }

        // ------------------------------------------------------------------ улей
        public static bool Saving(GameSim sim)
        {
            var sw = sim.Swarm;
            var cfg = sim.Cfg;
            if (cfg.Endless) return false;
            return sw.SwarmCount >= cfg.GoalBees
                   || (sim.TimeLeft < 120f && sw.SwarmCount >= cfg.Winter2Bees)
                   || (sim.Time > 420f && sw.SwarmCount >= cfg.GoalBees - 15 && sim.Colony.Honey < cfg.GoalHoney);
        }

        public static int WantBrood(GameSim sim) => Saving(sim) ? 0 : 4;

        public static int WantDefenders(GameSim sim) => sim.Time > 250f ? 12 : (sim.Time > 90f ? 6 : 0);

        /// <summary>Следующая кнопка в меню улья; 39 — «В ПОЛЁТ!», когда делать больше нечего.</summary>
        public static int NextHiveButton(GameSim sim)
        {
            var sw = sim.Swarm;
            var col = sim.Colony;
            bool saving = Saving(sim);
            if (sim.BroodIndex != WantBrood(sim)) return 31;
            if (sw.SwarmCount + 3 >= col.MaxBees && sim.CanUpgrade(UpgradeType.Comb)) return 33;
            if (!saving && col.Level[(int)UpgradeType.Carry] < 2 && sim.CanUpgrade(UpgradeType.Carry)) return 34;
            if (!saving && col.Level[(int)UpgradeType.Sting] < 2 && sim.Time > 150f && sim.CanUpgrade(UpgradeType.Sting)) return 35;
            if (!saving && col.Level[(int)UpgradeType.Speed] < 1 && sim.CanUpgrade(UpgradeType.Speed)) return 36;
            if (sw.GuardCount < WantDefenders(sim) && sim.CanHireDefenders && col.Honey > sim.DefenderCost + 40) return 32;
            if (!saving && sim.Time > 40f && sim.CanHireWorkers && col.Honey > sim.WorkerCost + 30) return 37;
            return 39;
        }

        public static bool HiveWanted(GameSim sim) => NextHiveButton(sim) != 39;

        static readonly PerkId[] Pref =
        {
            PerkId.SweetNectar, PerkId.ThickStings, PerkId.BigCrop, PerkId.FertileQueen, PerkId.Evasion,
            PerkId.HeatProof, PerkId.SwiftWings, PerkId.IronGuards, PerkId.Scouting
        };

        public static int PickPerk(GameSim sim)
        {
            int pick = 0, best = 99;
            for (int k = 0; k < 3; k++)
                for (int r = 0; r < Pref.Length; r++)
                    if (Pref[r] == sim.PerkOffer[k] && r < best) { best = r; pick = k; }
            return pick;
        }

    }
}
