using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Chip8.App;

public static class Draw
{
    /// <summary>
    /// Modifies the chip8.Gfx array to draw n bytes starting at memory location chip8.I at position (x, y).
    /// Each byte is 8 pixels wide, and n can be at most 15.
    /// If any pixels are erased (changed from set to unset), the VF register (chip8.V[0xF]) is set to 1, otherwise it is set to 0.
    /// The drawing wraps around the screen if it exceeds the boundaries.
    /// Uses SIMD instructions (AVX2 or SSE2) if available for performance optimization.
    /// </summary>
    /// <param name="chip8">The current Chip8 state</param>
    /// <param name="x">X</param>
    /// <param name="y">Y</param>
    /// <param name="n">N</param>
    /// <returns>true if collision was detected, otherwise false</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool DrawSprites(Chip8 chip8, int x, int y, int n)
    {
        if (Avx2.IsSupported && x <= 56)
            return DrawSpritesAvx2(chip8, x, y, n);

        if (Sse2.IsSupported && x <= 60)
            return DrawSpritesSse2(chip8, x, y, n);

        return DrawSpritesInternal(chip8, x, y, n);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool DrawSpritesInternal(Chip8 chip8, int x, int y, int n)
    {
        var applyCollisionFlag = false;
        var gfx = chip8.Gfx.AsSpan();
        var memory = chip8.Memory.AsSpan();

        for (var byteIndex = 0; byteIndex < n; byteIndex++)
        {
            var currentY = y + byteIndex;
            if (currentY >= 32)
                break;

            var mem = memory[chip8.I + byteIndex];
            var rowOffset = currentY * 64;

            var currentX = x;
            for (var bit = 7; bit >= 0 && currentX < 64; bit--, currentX++)
            {
                var index = rowOffset + currentX;
                var pixel = (mem >> bit) & 1;

                if (pixel == 0)
                    continue;

                applyCollisionFlag |= gfx[index] > 0U;
                gfx[index] ^= 0xffffffff;
            }
        }

        return applyCollisionFlag;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool DrawSpritesAvx2(Chip8 chip8, int x, int y, int n)
    {
        var applyCollisionFlag = false;

        fixed (uint* gfxPtr = chip8.Gfx)
        fixed (byte* memPtr = chip8.Memory)
        {
            for (var byteIndex = 0; byteIndex < n; byteIndex++)
            {
                var currentY = y + byteIndex;

                if (currentY >= 32)
                    break;

                var mem = memPtr[chip8.I + byteIndex];
                var rowPtr = gfxPtr + currentY * 64;

                var bits = Vector256.Create(
                    (uint)((mem >> 7) & 1),
                    (uint)((mem >> 6) & 1),
                    (uint)((mem >> 5) & 1),
                    (uint)((mem >> 4) & 1),
                    (uint)((mem >> 3) & 1),
                    (uint)((mem >> 2) & 1),
                    (uint)((mem >> 1) & 1),
                    (uint)(mem & 1)
                );

                var mask = Avx2.CompareEqual(bits, Vector256<uint>.Zero);
                var pixels = Avx2.AndNot(mask, Vector256.Create(0xffffffffu));

                var existing = Avx.LoadVector256(rowPtr + x);

                var left = Avx2.CompareEqual(existing, Vector256<uint>.Zero);
                var right = Avx2.CompareEqual(pixels, Vector256<uint>.Zero);
                var collision = Avx2.AndNot(left, right);

                applyCollisionFlag |= !Avx.TestZ(collision, collision);

                var result = Avx2.Xor(existing, pixels);
                Avx.Store(rowPtr + x, result);
            }
        }

        return applyCollisionFlag;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool DrawSpritesSse2(Chip8 chip8, int x, int y, int n)
    {
        var applyCollisionFlag = false;

        fixed (uint* gfxPtr = chip8.Gfx)
        fixed (byte* memPtr = chip8.Memory)
        {
            for (var byteIndex = 0; byteIndex < n; byteIndex++)
            {
                var currentY = y + byteIndex;
                if (currentY >= 32)
                    break;

                var mem = memPtr[chip8.I + byteIndex];
                var rowPtr = gfxPtr + (currentY * 64);

                var currentX = x;
                for (var bit = 7; bit >= 4; bit -= 4, currentX += 4)
                {
                    if (currentX + 3 >= 64)
                        break;

                    var bits = Vector128.Create(
                        (uint)((mem >> bit) & 1),
                        (uint)((mem >> (bit - 1)) & 1),
                        (uint)((mem >> (bit - 2)) & 1),
                        (uint)((mem >> (bit - 3)) & 1)
                    );

                    var mask = Sse2.CompareEqual(bits.AsInt32(), Vector128<int>.Zero);
                    var pixels = Sse2.AndNot(mask, Vector128.Create(0xffffffffu).AsInt32()).AsUInt32();

                    var existing = Sse2.LoadVector128(rowPtr + currentX);

                    var left = Sse2.CompareEqual(existing.AsInt32(), Vector128<int>.Zero);
                    var right = Sse2.CompareEqual(pixels.AsInt32(), Vector128<int>.Zero);
                    var collision = Sse2.AndNot(left, right);

                    applyCollisionFlag |= !Sse41.TestZ(collision.AsInt32(), collision.AsInt32());

                    var result = Sse2.Xor(existing.AsInt32(), pixels.AsInt32()).AsUInt32();
                    Sse2.Store(rowPtr + currentX, result);
                }

                // remaining
                for (var bit = 3; bit >= 0 && currentX < 64; bit--, currentX++)
                {
                    var pixel = (mem >> bit) & 1;

                    if (pixel == 0)
                        continue;

                    applyCollisionFlag |= rowPtr[currentX] > 0U;
                    rowPtr[currentX] ^= 0xffffffff;
                }
            }
        }

        return applyCollisionFlag;
    }
}