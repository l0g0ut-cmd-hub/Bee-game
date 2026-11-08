namespace Swarm.Simulation
{
    /// <summary>Детерминированный xorshift: симуляция не зависит от UnityEngine.Random.</summary>
    public sealed class Rng
    {
        uint _s;

        public Rng(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; }

        public uint NextUInt()
        {
            uint x = _s;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _s = x;
            return x;
        }

        /// <summary>[0,1)</summary>
        public float Value => (NextUInt() >> 8) * (1f / 16777216f);

        public float Range(float a, float b) => a + (b - a) * Value;
        public int Range(int a, int bExclusive) => a + (int)(NextUInt() % (uint)(bExclusive - a));
        public float Signed => Value * 2f - 1f;
    }
}
