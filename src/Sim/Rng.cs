namespace Saltdal.Sim;

/// <summary>
/// Small deterministic random number generator (xorshift64*). Unlike
/// System.Random, its sequence never changes between .NET versions, so a
/// battle replayed from the same seed and orders plays out identically.
/// </summary>
public sealed class Rng
{
    ulong _state;

    public Rng(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    public ulong NextULong()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return _state * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>Uniform in [0, 1).</summary>
    public float NextFloat() => (NextULong() >> 40) / (float)(1UL << 24);

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    public bool Chance(float p) => NextFloat() < p;
}
