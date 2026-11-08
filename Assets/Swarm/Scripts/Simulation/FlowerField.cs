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
        public int Count;

        public void Clear() => Count = 0;

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
            Count++;
        }

        public bool IsBlooming(int i) => Regrow[i] <= 0f && Nectar[i] > 0.05f;

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
