using UnityEngine;

namespace Swarm.Simulation
{
    /// <summary>Цветы как SoA-массивы. Pos — точка сбора (головка цветка).</summary>
    public sealed class FlowerField
    {
        public const int Capacity = 128;
        public readonly Vector2[] Pos = new Vector2[Capacity];
        public readonly FlowerKind[] Kind = new FlowerKind[Capacity];
        public readonly float[] Nectar = new float[Capacity];
        public readonly float[] MaxNectar = new float[Capacity];
        public readonly float[] Regrow = new float[Capacity];   // >0 — цветок увял и отрастает
        public readonly float[] Phase = new float[Capacity];
        public readonly float[] Touched = new float[Capacity];  // таймер «пчёлы недавно собирали» для FX
        public readonly float[] Sprout = new float[Capacity];   // >0 — росток, ещё не распустился
        public readonly bool[] Rare = new bool[Capacity];       // редкий золотой цветок
        public readonly float[] Life = new float[Capacity];     // >0 — временный цветок (липа), отцветает навсегда
        public readonly bool[] Gone = new bool[Capacity];       // отцвёл навсегда — не рисуется
        public readonly bool[] RareCredited = new bool[Capacity];
        public int BaseCount;                                   // цветы карты (без выросших за лето)
        public int Count;

        public void Clear() { Count = 0; BaseCount = 0; }

        public void Add(Vector2 p, FlowerKind k, float nectar, float phase)
        {
            if (Count >= Capacity) return;
            Pos[Count] = p;
            Kind[Count] = k;
            Nectar[Count] = nectar;
            MaxNectar[Count] = nectar;
            Regrow[Count] = 0f;
            Phase[Count] = phase;
            Touched[Count] = 0f;
            Sprout[Count] = 0f;
            Rare[Count] = false;
            Life[Count] = 0f;
            Gone[Count] = false;
            RareCredited[Count] = false;
            Count++;
        }

        public bool IsBlooming(int i) => !Gone[i] && Regrow[i] <= 0f && Sprout[i] <= 0f && Nectar[i] > 0.05f;
        public float CollectMul = 1f;   // ставит небо: ночь/дождь/жара

        public float Take(int i, float want, float regrowTime, SimEvents ev)
        {
            float got = Mathf.Min(want, Nectar[i]);
            Nectar[i] -= got;
            Touched[i] = 0.25f;
            if (Nectar[i] <= 0.05f && Regrow[i] <= 0f)
            {
                Nectar[i] = 0f;
                Regrow[i] = regrowTime;
                ev.Push(SimEventType.FlowerDepleted, Pos[i], 0f, i);
            }
            return got;
        }

        public void Update(float dt, SimEvents ev)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Touched[i] > 0f) Touched[i] -= dt;
                if (Gone[i]) continue;
                if (Life[i] > 0f)
                {
                    Life[i] -= dt;
                    if (Life[i] <= 0f) { Gone[i] = true; Nectar[i] = 0f; ev.Push(SimEventType.FlowerGone, Pos[i], 0f, i); continue; }
                }
                if (Sprout[i] > 0f)
                {
                    Sprout[i] -= dt;
                    if (Sprout[i] <= 0f)
                    {
                        Sprout[i] = 0f;
                        ev.Push(SimEventType.FlowerBloomed, Pos[i], Rare[i] ? 1f : 0f, i);
                    }
                }
                if (Regrow[i] > 0f)
                {
                    Regrow[i] -= dt;
                    if (Regrow[i] <= 0f)
                    {
                        Regrow[i] = 0f;
                        Nectar[i] = MaxNectar[i];
                        ev.Push(SimEventType.FlowerBloomed, Pos[i], 0f, i);
                    }
                }
            }
        }
    }
}
