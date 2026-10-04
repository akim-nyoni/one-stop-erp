using System.Security.Cryptography;
using System.Text;

namespace One4All.Application.Housing;

/// <summary>
/// The allocation draw (brief §4.10.4): pairs stands with candidates in order, runs a seeded ballot that anyone can
/// reproduce from the seed and the inputs, and hashes the inputs so the draw can be independently re-verified. The
/// ballot uses its own SplitMix64 generator and a Fisher–Yates shuffle, so the result never changes with the runtime.
/// Pure and unit-tested.
/// </summary>
public static class AllocationDraw
{
    /// <summary>The hash of a draw's inputs: SHA-256 over the candidates and stands in order, as lower-case hex.</summary>
    /// <param name="candidates">The candidates in canonical order.</param>
    /// <param name="stands">The stands in order.</param>
    /// <returns>The hash.</returns>
    public static string InputHash(IReadOnlyList<string> candidates, IReadOnlyList<string> stands)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(stands);
        string text = "C:" + string.Join('|', candidates) + "\nS:" + string.Join('|', stands);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>A seeded random order of the candidates, the same every time for the same seed and input.</summary>
    /// <param name="candidates">The candidates in canonical order.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The drawn order.</returns>
    public static IReadOnlyList<string> Ballot(IReadOnlyList<string> candidates, long seed)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        string[] order = [.. candidates];
        ulong state = unchecked((ulong)seed);
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = (int)(Next(ref state) % (ulong)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }

    /// <summary>Pairs stands with candidates in order until either runs out.</summary>
    /// <param name="stands">The stands in order.</param>
    /// <param name="candidates">The candidates in the drawn or ranked order.</param>
    /// <returns>The pairs.</returns>
    public static IReadOnlyList<(string Stand, string Candidate)> Pair(IReadOnlyList<string> stands, IReadOnlyList<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(stands);
        ArgumentNullException.ThrowIfNull(candidates);
        return [.. stands.Zip(candidates, (s, c) => (s, c))];
    }

    // SplitMix64: a small, well-known generator whose output is fixed by its definition.
    private static ulong Next(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
