using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace GameResource.Package.Internal;

/// <summary>
/// Standard XXH64 with seed zero. Detects accidental corruption; not a cryptographic hash.
/// An instance belongs to one writer/verification operation and is not thread-safe.
/// </summary>
internal sealed class XxHash64
{
    private const ulong Prime1 = 11400714785074694791UL;
    private const ulong Prime2 = 14029467366897019727UL;
    private const ulong Prime3 = 1609587929392839161UL;
    private const ulong Prime4 = 9650029242287828579UL;
    private const ulong Prime5 = 2870177450012600261UL;

    private State _state = new();
    private ulong _length;
    private TailBuffer _tail;
    private int _tailLength;

    [InlineArray(32)]
    private struct TailBuffer
    {
        private byte _element0;
    }

    public void Append(ReadOnlySpan<byte> source)
    {
        _length = unchecked(_length + (ulong)source.Length);
        Span<byte> tail = _tail;
        if (source.Length < 32 - _tailLength)
        {
            source.CopyTo(tail[_tailLength..]);
            _tailLength += source.Length;
            return;
        }

        if (_tailLength > 0)
        {
            int needed = 32 - _tailLength;
            source[..needed].CopyTo(tail[_tailLength..]);
            _state.ProcessStripe(tail);
            source = source[needed..];
        }

        while (source.Length >= 32)
        {
            _state.ProcessStripe(source);
            source = source[32..];
        }

        source.CopyTo(tail);
        _tailLength = source.Length;
    }

    // A snapshot does not consume the tail or change the running state.
    public ulong GetCurrentHashAsUInt64() => _state.Finish(_length, ((ReadOnlySpan<byte>)_tail)[.._tailLength]);

    // Keep the buffered read and small Meta paths free of heap allocations.
    public static ulong HashToUInt64(ReadOnlySpan<byte> source)
    {
        var state = new State();
        ulong length = (ulong)source.Length;
        while (source.Length >= 32)
        {
            state.ProcessStripe(source);
            source = source[32..];
        }
        return state.Finish(length, source);
    }

    private struct State
    {
        private ulong _v1 = unchecked(Prime1 + Prime2);
        private ulong _v2 = Prime2;
        private ulong _v3;
        private ulong _v4 = unchecked(0UL - Prime1);

        public State() { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ProcessStripe(ReadOnlySpan<byte> source)
        {
            _v1 = Round(_v1, BinaryPrimitives.ReadUInt64LittleEndian(source));
            _v2 = Round(_v2, BinaryPrimitives.ReadUInt64LittleEndian(source[8..]));
            _v3 = Round(_v3, BinaryPrimitives.ReadUInt64LittleEndian(source[16..]));
            _v4 = Round(_v4, BinaryPrimitives.ReadUInt64LittleEndian(source[24..]));
        }

        public readonly ulong Finish(ulong length, ReadOnlySpan<byte> tail)
        {
            unchecked
            {
                ulong hash;
                if (length >= 32)
                {
                    hash = BitOperations.RotateLeft(_v1, 1) + BitOperations.RotateLeft(_v2, 7)
                        + BitOperations.RotateLeft(_v3, 12) + BitOperations.RotateLeft(_v4, 18);
                    hash = Merge(hash, _v1);
                    hash = Merge(hash, _v2);
                    hash = Merge(hash, _v3);
                    hash = Merge(hash, _v4);
                }
                else
                {
                    hash = Prime5;
                }

                hash += length;
                while (tail.Length >= 8)
                {
                    hash ^= Round(0, BinaryPrimitives.ReadUInt64LittleEndian(tail));
                    hash = BitOperations.RotateLeft(hash, 27) * Prime1 + Prime4;
                    tail = tail[8..];
                }
                if (tail.Length >= 4)
                {
                    hash ^= BinaryPrimitives.ReadUInt32LittleEndian(tail) * Prime1;
                    hash = BitOperations.RotateLeft(hash, 23) * Prime2 + Prime3;
                    tail = tail[4..];
                }
                foreach (byte value in tail)
                {
                    hash ^= value * Prime5;
                    hash = BitOperations.RotateLeft(hash, 11) * Prime1;
                }

                hash ^= hash >> 33;
                hash *= Prime2;
                hash ^= hash >> 29;
                hash *= Prime3;
                return hash ^ (hash >> 32);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Round(ulong accumulator, ulong lane) =>
        unchecked(BitOperations.RotateLeft(accumulator + lane * Prime2, 31) * Prime1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Merge(ulong accumulator, ulong lane) =>
        unchecked((accumulator ^ Round(0, lane)) * Prime1 + Prime4);
}
