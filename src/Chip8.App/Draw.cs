using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Chip8.App;

public static class Draw
{
    public static void DrawSpriteNaive(Chip8 chip8, int x, int y, int n)
    {
        chip8.V[^1] = 0;

        for (var byteIndex = 0; byteIndex < n; byteIndex++)
        {
            var mem = chip8.Memory[chip8.I + byteIndex];

            for (var bitIndex = 0; bitIndex < 8; bitIndex++)
            {
                var index = x + bitIndex + (y + byteIndex) * 64;
                if (index > 2047)
                    break;

                var pixel = (byte)((mem >> (7 - bitIndex)) & 0x01);

                if (pixel == 1 && chip8.Gfx[index] != 0)
                    chip8.V[^1] = 1;

                chip8.Gfx[index] = (chip8.Gfx[index] != 0 && pixel == 0) || (chip8.Gfx[index] == 0 && pixel == 1) ? 0xffffffff : 0;
            }
        }
    }

    public static void DrawSprites(Chip8 chip8, int x, int y, int n)
    {
        // clear last buba
        chip8.V[^1] = 0;

        if (Avx2.IsSupported && x <= 56)
            DrawSpritesAvx2(chip8, x, y, n);
        else if (Sse2.IsSupported && x <= 60)
            DrawSpritesSse2(chip8, x, y, n);
        else
            DrawSpritesFast(chip8, x, y, n);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DrawSpritesFast(Chip8 chip8, int x, int y, int n)
    {
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

                if (gfx[index] != 0) chip8.V[15] = 1;
                gfx[index] ^= 0xffffffff;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DrawSpritesAvx2(Chip8 chip8, int x, int y, int n)
    {
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

                if (!Avx.TestZ(collision, collision))
                    chip8.V[15] = 1;

                var result = Avx2.Xor(existing, pixels);
                Avx.Store(rowPtr + x, result);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DrawSpritesSse2(Chip8 chip8, int x, int y, int n)
    {
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

                    if (!Sse41.TestZ(collision.AsInt32(), collision.AsInt32()))
                        chip8.V[15] = 1;

                    var result = Sse2.Xor(existing.AsInt32(), pixels.AsInt32()).AsUInt32();
                    Sse2.Store(rowPtr + currentX, result);
                }

                // remaining
                for (var bit = 3; bit >= 0 && currentX < 64; bit--, currentX++)
                {
                    var pixel = (mem >> bit) & 1;

                    if (pixel == 0)
                        continue;

                    if (rowPtr[currentX] != 0)
                        chip8.V[^1] = 1;

                    rowPtr[currentX] ^= 0xffffffff;
                }
            }
        }
    }
}