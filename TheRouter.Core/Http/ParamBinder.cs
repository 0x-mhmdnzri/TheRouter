using System.Buffers.Text;
using System.Runtime.CompilerServices;

namespace TheRouter.Core.Http;

/// <summary>
/// Zero / low-allocation helpers that turn absolute (start, length) ranges
/// into typed values without necessarily allocating a string.
/// </summary>
public static class ParamBinder
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> AsSpan(ReadOnlySpan<char> path, (int Start, int Length) range)
        => path.Slice(range.Start, range.Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetInt32(ReadOnlySpan<char> path, (int Start, int Length) range, out int value)
        => int.TryParse(AsSpan(path, range), out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetInt64(ReadOnlySpan<char> path, (int Start, int Length) range, out long value)
        => long.TryParse(AsSpan(path, range), out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetGuid(ReadOnlySpan<char> path, (int Start, int Length) range, out Guid value)
        => Guid.TryParse(AsSpan(path, range), out value);

    /// <summary>
    /// Only allocates when you explicitly need a string (e.g. for logging or downstream APIs that require string).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToString(ReadOnlySpan<char> path, (int Start, int Length) range)
        => AsSpan(path, range).ToString();
}
