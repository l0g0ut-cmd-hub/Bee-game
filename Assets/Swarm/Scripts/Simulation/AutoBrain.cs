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
        public static bool NoWaspBall, NoRetreat, NoIntercept;   // переключатели для A/B-замеров на стенде
        public bool WantHold;
        /// <summary>Кого отметить меткой-приказом (uid врага; 0 — никого).</summary>
        public int WantFocusUid;
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
            int nest = -1, queen = -1, wild = -1, danger = 0;
            int threat = -1, raider = -1;
            float threatD = 140f * 140f;
            for (int e = 0; e < en.Count; e++)
            {
                if (en.Type[e] == EnemyType.Nest) { nest = e; continue; }
                if (en.Type[e] == EnemyType.WildHive) { wild = e; continue; }
                if (en.Type[e] != EnemyType.Rival && en.Type[e] != EnemyType.Ant) danger++;
                if (en.Type[e] == EnemyType.Queen) queen = e;
                if (en.Type[e] == EnemyType.Bear && sw.SwarmCount < 60) continue;
                if (en.Mode[e] == EnemyMode.Flee) continue;   // спит/убегает — не гоняемся
                float d = (en.Pos[e] - sw.LeaderPos).sqrMagnitude;
                if (d < threatD) { threatD = d; threat = e; }
                if (en.Mode[e] == EnemyMode.Raid && (en.Pos[e] - hive).sqrMagnitude < 90f * 90f) raider = e;
            }
            WantHold = false;
            WantFocusUid = 0;
            // оценка сил: враги вокруг роя против мощи роя (число пчёл × жало × бафы)
            float enemyPow = 0f;
            Vector2 enemyC = Vector2.zero;
            int wasps = 0;
            Vector2 waspC = Vector2.zero;
            for (int e = 0; e < en.Count; e++)
            {
                if (en.Mode[e] == EnemyMode.Flee || EnemySystem.IsStatic(en.Type[e])) continue;
                float d2 = (en.Pos[e] - sw.LeaderPos).sqrMagnitude;
                if (d2 > 120f * 120f) continue;
                float w = PowerOf(en.Type[e], en.Mode[e]);
                enemyPow += w;
                enemyC += en.Pos[e] * w;
                if (en.Type[e] == EnemyType.Wasp && d2 < 45f * 45f) { wasps++; waspC += en.Pos[e]; }
            }
            if (enemyPow > 0f) enemyC /= enemyPow;
            float myPow = SwarmPower(sim);
            // перехват: враг идёт грабить улей (ещё далеко) или охотится на пчёл — встречаем заранее, а не ждём у летка
            int intercept = -1;
            float bestI = float.MaxValue;
            for (int e = 0; e < en.Count; e++)
            {
                var et = en.Type[e];
                if (EnemySystem.IsStatic(et) || et == EnemyType.Queen || en.Mode[e] == EnemyMode.Flee) continue;
                if (et == EnemyType.Rival) continue;
                if (et == EnemyType.Bear && sw.SwarmCount < 60) continue;
                float dHive = (en.Pos[e] - hive).magnitude, dMe = (en.Pos[e] - sw.LeaderPos).magnitude;
                // мелочь у улья добьют защитники; рой встречает тех, с кем солдаты не справятся
                bool heavy = et == EnemyType.Bear || et == EnemyType.Hornet;
                bool guarded = sw.GuardCount >= 6 && !heavy;
                bool raiding = en.Mode[e] == EnemyMode.Raid && dHive < (heavy ? 260f : 200f) && !guarded;
                bool hunting = en.Mode[e] == EnemyMode.Hunt && dMe < 140f && heavy;   // мелочь, что сама летит к рою, встретит обычный бой
                if (!raiding && !hunting) continue;
                // кто ближе к улью — тот опаснее; рядом с роем — дешевле перехватить
                float score = dMe + dHive * 0.6f - (et == EnemyType.Bear ? 80f : et == EnemyType.Hornet ? 40f : 0f);
                if (score < bestI) { bestI = score; intercept = e; }
            }
            if (NoIntercept) intercept = -1;
            bool outmatched = enemyPow > 0f && myPow < enemyPow * 1.4f;
            float queenD = queen >= 0 ? (en.Pos[queen] - sw.LeaderPos).sqrMagnitude : float.MaxValue;
            if (queen >= 0 && queenD < 160f * 160f && sw.SwarmCount >= 60)
            {
                // босс: большим роем — шар и метка на королеву (если рядом мак — сперва за ним)
                int poppy = sim.BuffSting <= 0f && queenD > 90f * 90f ? NearestKind(sim, FlowerKind.Poppy, 70f) : -1;
                if (poppy >= 0) WorldTarget = sim.Flowers.Pos[poppy];
                else { WorldTarget = en.Pos[queen]; WantHold = true; WantFocusUid = en.Uid[queen]; }
            }
            else if (queen >= 0 && queenD < 120f * 120f)
            {
                // рой мал для королевы — уводим к улью под защиту солдат
                WorldTarget = hive + (sw.LeaderPos - en.Pos[queen]).normalized * 30f;
            }
            else if (threat >= 0 && en.Type[threat] == EnemyType.Hornet && sw.SwarmCount >= 40)
            {
                WorldTarget = en.Pos[threat];
                WantHold = true;
                WantFocusUid = en.Uid[threat];
            }
            else if (threat >= 0 && en.Type[threat] == EnemyType.Hornet && sw.SwarmCount < 70)
            {
                Vector2 away = sw.LeaderPos - en.Pos[threat];
                WorldTarget = sw.LeaderPos + away.normalized * 80f;
            }
            else if (!NoRetreat && outmatched && threat >= 0 && en.Type[threat] != EnemyType.Bear)
            {
                // силы неравны — отходим к улью под защиту солдат, не размениваем пчёл
                Vector2 away = (sw.LeaderPos - enemyC).normalized;
                WorldTarget = Vector2.Lerp(hive, sw.LeaderPos + away * 60f, 0.35f);
            }
            else if (!NoWaspBall && wasps >= 3 && sw.SwarmCount >= 40 && sim.Sky.Thirst < 0.7f)
            {
                // кучка ос вплотную — тепловой шар по центру кучки: обездвижить и сварить разом
                WorldTarget = waspC / wasps;
                WantHold = true;
            }
            else if (intercept >= 0 && !outmatched && sw.SwarmCount >= 20 && !(en.Type[intercept] == EnemyType.Hornet && sw.SwarmCount < 40))
            {
                // летим навстречу угрозе; тяжёлым — метка всем роем
                WorldTarget = en.Pos[intercept] + en.Vel[intercept] * 0.4f;   // с упреждением
                var it = en.Type[intercept];
                if (it == EnemyType.Bear || it == EnemyType.Hornet) WantFocusUid = en.Uid[intercept];
                if (it == EnemyType.Hornet && sw.SwarmCount >= 40) WantHold = (en.Pos[intercept] - sw.LeaderPos).sqrMagnitude < 40f * 40f;
            }
            else if (threat >= 0 && threatD < 90f * 90f)
            {
                WorldTarget = en.Pos[threat];
                if (en.Type[threat] == EnemyType.Bear) WantFocusUid = en.Uid[threat];   // медведь — бить всем роем
            }
            else if (raider >= 0) WorldTarget = en.Pos[raider];
            else if (StuckWeb(sim, out Vector2 web)) WorldTarget = web;   // пчёлы в паутине — пролетаем роем, рвём сеть
            else if (wild >= 0 && sw.SwarmCount >= 90 && col.Level[(int)UpgradeType.Sting] >= 2 && danger == 0 && sw.FieldCarry < cap * 0.3f)
                WorldTarget = en.Pos[wild];                                  // разоряем дикий улей: конкуренты пропадут, запасы — нам
            else if (nest >= 0 && sw.SwarmCount >= 100 && col.Level[(int)UpgradeType.Sting] >= 2 && danger <= 2 && sw.FieldCarry < cap * 0.3f)
                WorldTarget = en.Pos[nest];                                  // сильным роем сносим гнездо и днём
            else if (sim.Sky.Thirst > 0.6f && sim.World.Ponds.Count > 0)
            {
                Vector2 best = sim.World.Ponds[0];
                for (int i = 1; i < sim.World.Ponds.Count; i++)
                    if ((sim.World.Ponds[i] - sw.LeaderPos).sqrMagnitude < (best - sw.LeaderPos).sqrMagnitude) best = sim.World.Ponds[i];
                WorldTarget = best;
            }
            else if (sw.FieldCarry >= cap * DeliverAt(sim, cap) || (_delivering && sw.FieldCarry > UnloadedCarry(sim)))
            {
                _delivering = true;
                WorldTarget = hive;
            }
            else if (sim.Sky.IsNight(sim.Time) && nest >= 0 && sw.SwarmCount >= 60)
            {
                // ночь: осы спят — большим роем идём жечь гнездо (иначе — собираем, ночью цветы дают меньше, но дают)
                _delivering = false;
                _flower = -1;
                WorldTarget = en.Pos[nest];
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
                        // ценна не вся поляна, а то, что рой сейчас унесёт: маленькому рою дальняя богатая поляна не нужна
                        float room = Mathf.Max(4f, cap - sw.FieldCarry);
                        float score = (d + 40f) / (2f + Mathf.Min(patch, room * 1.2f));
                        // цветы-усилители: мак перед боями, фиалка — когда далеко летать
                        if (fl.Kind[f] == FlowerKind.Poppy && sim.BuffSting <= 0f && en.Count > 1) score *= 0.6f;
                        if (fl.Kind[f] == FlowerKind.Violet && sim.BuffSpeed <= 0f && d > 150f) score *= 0.7f;
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

        static int NearestKind(GameSim sim, FlowerKind k, float maxDist)
        {
            var fl = sim.Flowers;
            int best = -1;
            float bd = maxDist * maxDist;
            for (int f = 0; f < fl.Count; f++)
            {
                if (fl.Kind[f] != k || !fl.IsBlooming(f)) continue;
                float d = (fl.Pos[f] - sim.Swarm.LeaderPos).sqrMagnitude;
                if (d < bd) { bd = d; best = f; }
            }
            return best;
        }

        /// <summary>
        /// Когда нести нектар: у улья — почти полным (лишний рейс дёшев), далеко — тоже полным,
        /// но если поляна под ногами выжата, сторож прогресса отправит раньше.
        /// </summary>
        float DeliverAt(GameSim sim, float cap)
        {
            float dHive = (sim.Swarm.LeaderPos - sim.World.HivePos).magnitude;
            return dHive < 110f ? 0.7f : 0.9f;
        }

        /// <summary>«Вес» врага в бою — сколько он стоит пчёл.</summary>
        static float PowerOf(EnemyType t, EnemyMode m)
        {
            switch (t)
            {
                case EnemyType.Wasp: return 1f;
                case EnemyType.Hornet: return 4f;
                case EnemyType.Bear: return 8f;
                case EnemyType.Queen: return 20f;
                case EnemyType.Ant: return 0.3f;
                case EnemyType.Rival: return m == EnemyMode.Hunt ? 0.5f : 0f;
                default: return 0f;
            }
        }

        /// <summary>Мощь роя в тех же единицах: ~10 пчёл с базовым жалом = 1 оса.</summary>
        public static float SwarmPower(GameSim sim)
        {
            float sting = sim.Colony.StingMul * (sim.BuffSting > 0f ? sim.Cfg.PoppyStingMul : 1f);
            return (sim.Swarm.SwarmCount - sim.Swarm.SquadCount) * sting / 10f;
        }

        /// <summary>Есть ли застрявшие в паутине (3+) — и где.</summary>
        static bool StuckWeb(GameSim sim, out Vector2 at)
        {
            var sw = sim.Swarm;
            at = default;
            int n = 0;
            Vector2 acc = Vector2.zero;
            for (int i = 0; i < sw.Count; i++)
                if (sw.Stuck[i] > 0.5f && sw.Role[i] == BeeRole.Swarm) { n++; acc += sw.Pos[i]; }
            if (n < 3) return false;
            at = acc / n;
            return true;
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
        /// <summary>Пора копить мёд (матку на СТОП)? Решает по тому, чего не хватает до цели.</summary>
        public static bool Saving(GameSim sim)
        {
            var sw = sim.Swarm;
            var cfg = sim.Cfg;
            if (cfg.Endless) return false;
            float bees = sw.SwarmCount / (float)cfg.GoalBees, honey = sim.Colony.Honey / cfg.GoalHoney;
            if (bees >= 1f) return true;                 // пчёл хватает — весь мёд в запас
            // мёда хватит и на недостающих пчёл, и на цель — растим рой
            float need = (cfg.GoalBees - sw.SwarmCount) * sim.BroodCostPerBee;
            if (sim.Colony.Honey - need >= cfg.GoalHoney) return false;
            if (sim.TimeLeft < 150f) return honey < bees; // под конец лета добираем то, чего меньше
            return sim.Time > 420f && bees >= 0.88f;
        }

        /// <summary>
        /// Скорость кладки. Матка не должна съедать весь мёд: когда соты почти полны и их можно расширить —
        /// копим на соты; когда улей пуст, а рой мал — нормальная кладка, чтобы хватало на защитников и улучшения.
        /// </summary>
        public static int WantBrood(GameSim sim)
        {
            if (Saving(sim)) return 0;
            var sw = sim.Swarm;
            var col = sim.Colony;
            if (sw.SwarmCount >= col.MaxBees - 4 && !col.IsMaxed(UpgradeType.Comb) && col.Honey < col.CostOf(UpgradeType.Comb)) return 0;
            if (sim.Cfg.DefendersEnabled && sw.GuardCount < WantDefenders(sim) && col.Honey < sim.DefenderCost) return 2;
            return 4;
        }

        public static int WantDefenders(GameSim sim)
        {
            int w = sim.Time > 400f ? 18 : sim.Time > 250f ? 12 : sim.Time > 90f ? 6 : 0;
            // враги крутятся у улья — защитников больше
            Vector2 hive = sim.World.HivePos;
            int near = 0;
            var en = sim.Enemies;
            for (int e = 0; e < en.Count; e++)
                if (!EnemySystem.IsStatic(en.Type[e]) && en.Type[e] != EnemyType.Rival && (en.Pos[e] - hive).sqrMagnitude < 140f * 140f) near++;
            if (near >= 2) w = Mathf.Max(w, 9);
            if (sim.Cfg.Endless) w = Mathf.Min(sim.Cfg.DefenderMax, w + 3 * sim.Waves.Cycle);
            return w;
        }

        /// <summary>Следующая кнопка в меню улья; 39 — «В ПОЛЁТ!», когда делать больше нечего.</summary>
        /// <summary>
        /// Следующая кнопка в меню улья; 39 — «В ПОЛЁТ!», когда делать больше нечего.
        /// Траты дисциплинированы: к концу лета покупаем только из излишка сверх цели по мёду.
        /// </summary>
        public static int NextHiveButton(GameSim sim)
        {
            var sw = sim.Swarm;
            var col = sim.Colony;
            var cfg = sim.Cfg;
            bool saving = Saving(sim);
            if (sim.BroodIndex != WantBrood(sim)) return 31;

            // «пол» мёда, ниже которого не тратим: рано — 0, со временем растёт к цели (в выживании — небольшой запас)
            float late = cfg.Endless ? 0f : Mathf.Clamp01((sim.Time - 240f) / 240f);
            float floor = cfg.Endless ? 30f : cfg.GoalHoney * late;
            bool endgame = !cfg.Endless && sim.TimeLeft < 75f;
            float free = col.Honey - floor;

            bool needBees = sw.SwarmCount < cfg.GoalBees || cfg.Endless;
            if (needBees && sw.SwarmCount + 3 >= col.MaxBees && sim.CanUpgrade(UpgradeType.Comb) && (free >= col.CostOf(UpgradeType.Comb) || !endgame && sim.Time < 300f)) return 33;
            if (endgame) return 39;
            // базовые улучшения — сразу, пока лето молодое
            if (!saving && sim.Time < 300f)
            {
                if (col.Level[(int)UpgradeType.Carry] < 2 && sim.CanUpgrade(UpgradeType.Carry)) return 34;
                if (col.Level[(int)UpgradeType.Sting] < 2 && sim.Time > 150f && sim.CanUpgrade(UpgradeType.Sting)) return 35;
                if (col.Level[(int)UpgradeType.Speed] < 1 && sim.CanUpgrade(UpgradeType.Speed)) return 36;
            }
            if (sw.GuardCount < WantDefenders(sim) && sim.CanHireDefenders && free >= sim.DefenderCost + 20) return 32;
            if (!saving && sim.Time > 20f && sim.CanHireWorkers && col.Honey >= sim.WorkerCost + 10 && (cfg.Endless || sim.TimeLeft > 150f)) return 37;
            // дальше — только из излишка
            if (sim.CanUpgrade(UpgradeType.Carry) && free >= col.CostOf(UpgradeType.Carry) + 40) return 34;
            if (sim.CanUpgrade(UpgradeType.Sting) && free >= col.CostOf(UpgradeType.Sting) + 40) return 35;
            if (sim.CanUpgrade(UpgradeType.Speed) && free >= col.CostOf(UpgradeType.Speed) + 40) return 36;
            return 39;
        }

        /// <summary>Ноша, которую считаем «разгрузились» (пара отставших/застрявших пчёл не держит рой у улья).</summary>
        public static float UnloadedCarry(GameSim sim) =>
            0.5f + 0.06f * sim.Cfg.CarryBase * sim.Colony.CarryMul * sim.Mods.Carry * Mathf.Max(1, sim.Swarm.SwarmCount - sim.Swarm.SquadCount);

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
