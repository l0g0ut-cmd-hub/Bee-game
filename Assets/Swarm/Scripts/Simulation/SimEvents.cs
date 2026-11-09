using UnityEngine;

namespace Swarm.Simulation
{
    public enum SimEventType : byte
    {
        BeeDied,
        BeeHatched,
        NectarCollected,
        NectarDeposited,
        FlowerDepleted,
        FlowerBloomed,
        FlowerSprouted,
        BeeHurt,
        DefendersHired,
        ScoutDance,
        BirdIncoming,
        BirdStrike,
        WebTorn,
        PerkOffered,
        PerkTaken,
        BearSwipe,
        BearRepelled,
        FlowerGone,
        WeatherChanged,
        LindenBloom,
        NestDestroyed,
        NightFall,
        Dawn,
        EnemySpawned,
        EnemyHit,
        EnemyWindup,
        EnemyKilled,
        HoneyStolen,
        HoneyRecovered,
        WaveIncoming,
        Upgrade,
        BossIncoming,
        BossPhase,
        BossKilled,
        FocusSet,
        BuffTaken,
    }

    public struct SimEvent
    {
        public SimEventType Type;
        public Vector2 Pos;
        public float Value;
        public int Data;
    }

    /// <summary>Кадровый буфер событий для FX/звука/UI. Симуляция пишет, презентация читает и чистит.</summary>
    public sealed class SimEvents
    {
        public readonly SimEvent[] Items = new SimEvent[512];
        public int Count;

        public void Push(SimEventType t, Vector2 p, float value = 0f, int data = 0)
        {
            if (Count >= Items.Length) return;
            Items[Count++] = new SimEvent { Type = t, Pos = p, Value = value, Data = data };
        }

        public void Clear() => Count = 0;
    }
}
