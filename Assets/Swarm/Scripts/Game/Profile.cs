using System;
using System.Collections.Generic;
using Swarm.Simulation;
using UnityEngine;

namespace Swarm.Game
{
    /// <summary>
    /// Прогресс игрока между партиями: звёзды кампании, рекорды выживания и задания дня, достижения,
    /// накопительная статистика. Хранится в PlayerPrefs как JSON.
    /// </summary>
    [Serializable]
    public sealed class Profile
    {
        public int[] LevelStars = new int[3];
        public float[] LevelBestTime = new float[3];
        public float EndlessBest;
        public int DailyDate;          // yyyymmdd последнего задания дня
        public int DailyStars;
        public int Achievements;       // битовая маска
        public int SeenIntro;          // какие сезоны уже показали карточку «что нового»
        public int TotalCooked, TotalNests, TotalBears, TotalRare, TotalBosses;
        public int BreedIndex;         // выбранная порода (Swarm.Simulation.Breed)

        public bool BreedUnlocked(Simulation.Breed b) => CampaignStars >= Breeds.UnlockStars[(int)b];

        /// <summary>Следующая открытая порода по кругу.</summary>
        public void CycleBreed()
        {
            for (int k = 1; k <= (int)Simulation.Breed.Count; k++)
            {
                int n = (BreedIndex + k) % (int)Simulation.Breed.Count;
                if (BreedUnlocked((Simulation.Breed)n)) { BreedIndex = n; Save(); return; }
            }
        }

        public Simulation.Breed SelectedBreed => BreedUnlocked((Simulation.Breed)BreedIndex) ? (Simulation.Breed)BreedIndex : Simulation.Breed.Central;

        const string Key = "swarm_profile_v1";

        public static Profile Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json))
                {
                    var p = JsonUtility.FromJson<Profile>(json);
                    if (p != null)
                    {
                        if (p.LevelStars == null || p.LevelStars.Length < 3) p.LevelStars = new int[3];
                        if (p.LevelBestTime == null || p.LevelBestTime.Length < 3) p.LevelBestTime = new float[3];
                        return p;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[SWARM] профиль повреждён: " + e.Message); }
            var fresh = new Profile();
            // перенос старого рекорда из первых версий
            float old = PlayerPrefs.GetFloat("swarm_best_win", 0f);
            if (old > 0f) { fresh.LevelBestTime[1] = old; }
            return fresh;
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public int CampaignStars
        {
            get { int s = 0; for (int i = 0; i < LevelStars.Length; i++) s += LevelStars[i]; return s; }
        }

        public bool Has(Achievement a) => (Achievements & (1 << (int)a)) != 0;

        public static int TodayKey()
        {
            var d = DateTime.UtcNow;
            return d.Year * 10000 + d.Month * 100 + d.Day;
        }

        /// <summary>Учитывает итог партии; возвращает новые достижения (для тостов на экране итогов).</summary>
        public List<Achievement> Record(GameSim sim, GameMode mode, int level)
        {
            var st = sim.Stats;
            TotalCooked += st.HornetsCooked;
            TotalNests += st.NestsDestroyed;
            TotalBears += st.BearsRepelled;
            TotalRare += st.RareFound;
            TotalBosses += st.BossesKilled;
            switch (mode)
            {
                case GameMode.Campaign:
                    if (sim.Stars > LevelStars[level]) LevelStars[level] = sim.Stars;
                    if (sim.Result == SimResult.Won && (LevelBestTime[level] <= 0f || sim.Time < LevelBestTime[level])) LevelBestTime[level] = sim.Time;
                    break;
                case GameMode.Endless:
                    if (sim.Time > EndlessBest) EndlessBest = sim.Time;
                    break;
                case GameMode.Daily:
                    int today = TodayKey();
                    if (DailyDate != today) { DailyDate = today; DailyStars = 0; }
                    if (sim.Stars > DailyStars) DailyStars = sim.Stars;
                    break;
            }
            var fresh = new List<Achievement>();
            for (int i = 0; i < (int)Achievement.Count; i++)
            {
                var a = (Achievement)i;
                if (Has(a) || !Achievements_.Check(a, sim, mode, this)) continue;
                Achievements |= 1 << i;
                fresh.Add(a);
            }
            Save();
            return fresh;
        }
    }

    public enum Achievement
    {
        FirstHoney, Cook10, NestBuster, BearRepeller, GoldFind, GreatSwarm, ThreeStars, Campaign,
        Survivor, FullGuard, Flawless, Daily, QueenSlayer, WildRaider, Count
    }

    public static class Achievements_
    {
        public static string Name(Achievement a)
        {
            switch (a)
            {
                case Achievement.FirstHoney: return "ПЕРВЫЙ МЁД";
                case Achievement.Cook10: return "ПОВАР";
                case Achievement.NestBuster: return "ГРОЗА ОС";
                case Achievement.BearRepeller: return "МЕДВЕДЬ, УХОДИ";
                case Achievement.GoldFind: return "ЗОЛОТАЯ НАХОДКА";
                case Achievement.GreatSwarm: return "ВЕЛИКИЙ РОЙ";
                case Achievement.ThreeStars: return "ТРИ ЗВЕЗДЫ";
                case Achievement.Campaign: return "ВСЕ СЕЗОНЫ";
                case Achievement.Survivor: return "ВЫЖИВШИЙ";
                case Achievement.FullGuard: return "ГВАРДИЯ";
                case Achievement.Flawless: return "БЕЗ ПОТЕРЬ";
                case Achievement.QueenSlayer: return "ЦАРЕУБИЙЦА";
                case Achievement.WildRaider: return "РАЗОРИТЕЛЬ";
                default: return "ЗАДАНИЕ ДНЯ";
            }
        }

        public static string Desc(Achievement a)
        {
            switch (a)
            {
                case Achievement.FirstHoney: return "ПРИНЕСИ МЁД В УЛЕЙ";
                case Achievement.Cook10: return "СВАРИ 10 ШЕРШНЕЙ ШАРОМ";
                case Achievement.NestBuster: return "УНИЧТОЖЬ ОСИНОЕ ГНЕЗДО";
                case Achievement.BearRepeller: return "ПРОГОНИ МЕДВЕДЯ";
                case Achievement.GoldFind: return "СОБЕРИ С РЕДКОГО ЦВЕТКА";
                case Achievement.GreatSwarm: return "ВЫРАСТИ РОЙ ДО 170";
                case Achievement.ThreeStars: return "3 ЗВЕЗДЫ НА УРОВНЕ";
                case Achievement.Campaign: return "ЗВЁЗДЫ ВО ВСЕХ СЕЗОНАХ";
                case Achievement.Survivor: return "20 МИНУТ В ВЫЖИВАНИИ";
                case Achievement.FullGuard: return "НАЙМИ 24 ЗАЩИТНИКА";
                case Achievement.Flawless: return "ПОБЕДА, ПОТЕРЯВ < 25 ПЧЁЛ";
                case Achievement.QueenSlayer: return "ПОБЕДИ КОРОЛЕВУ ШЕРШНЕЙ";
                case Achievement.WildRaider: return "РАЗОРИ ДИКИЙ УЛЕЙ";
                default: return "ЗВЕЗДА В ЗАДАНИИ ДНЯ";
            }
        }

        public static bool Check(Achievement a, GameSim sim, GameMode mode, Profile p)
        {
            var st = sim.Stats;
            switch (a)
            {
                case Achievement.FirstHoney: return st.HoneyDelivered > 0f;
                case Achievement.Cook10: return p.TotalCooked >= 10;
                case Achievement.NestBuster: return p.TotalNests > 0;
                case Achievement.BearRepeller: return p.TotalBears > 0;
                case Achievement.GoldFind: return p.TotalRare > 0;
                case Achievement.GreatSwarm: return st.PeakBees >= 170;
                case Achievement.ThreeStars: return mode != GameMode.Endless && sim.Stars >= 3;
                case Achievement.Campaign: return p.LevelStars[0] > 0 && p.LevelStars[1] > 0 && p.LevelStars[2] > 0;
                case Achievement.Survivor: return mode == GameMode.Endless && sim.Time >= 1200f;
                case Achievement.FullGuard: return st.DefendersHired >= 24;
                case Achievement.Flawless: return sim.Result == SimResult.Won && st.BeesLost < 25;
                case Achievement.QueenSlayer: return p.TotalBosses > 0;
                case Achievement.WildRaider: return st.WildHivesDestroyed > 0;
                default: return mode == GameMode.Daily && sim.Stars > 0;
            }
        }
    }
}
