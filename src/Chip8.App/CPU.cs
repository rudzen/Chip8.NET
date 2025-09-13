using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Chip8.App;

public sealed class Chip8
{
    public readonly byte[] Memory = new byte[4096];
    public readonly byte[] V = new byte[16];
    public readonly uint[] Gfx = new uint[64 * 32];
    public readonly ushort[] Stack = new ushort[16];
    public byte Sp;
    public ushort Pc;
    public ushort I;
    public byte DelayTimer;
    public byte SoundTimer; // sound
    public ushort Keyboard;

    public bool WaitingForKeyPress;

    public readonly Stopwatch Watch = new();
}

public static class Cpu
{
    private static ReadOnlySpan<byte> FontCharacters =>
    [
        0xF0, 0x90, 0x90, 0x90, 0xF0, 0x20, 0x60, 0x20, 0x20, 0x70, 0xF0, 0x10, 0xF0, 0x80, 0xF0, 0xF0, 0x10, 0xF0, 0x10, 0xF0, 0x90, 0x90, 0xF0, 0x10, 0x10, 0xF0, 0x80, 0xF0,
        0x10, 0xF0, 0xF0, 0x80, 0xF0, 0x90, 0xF0, 0xF0, 0x10, 0x20, 0x40, 0x40, 0xF0, 0x90, 0xF0, 0x90, 0xF0, 0xF0, 0x90, 0xF0, 0x10, 0xF0, 0xF0, 0x90, 0xF0, 0x90, 0x90, 0xE0,
        0x90, 0xE0, 0x90, 0xE0, 0xF0, 0x80, 0x80, 0x80, 0xF0, 0xE0, 0x90, 0x90, 0x90, 0xE0, 0xF0, 0x80, 0xF0, 0x80, 0xF0, 0xF0, 0x80, 0xF0, 0x80, 0x80
    ];

    private static readonly Random Generator = new(Environment.TickCount);

    public static void LoadProgram(Chip8 chip8, ReadOnlySpan<byte> program)
    {
        const int programStart = 512;
        var ram = chip8.Memory.AsSpan();
        FontCharacters.CopyTo(ram);
        ram.Slice(FontCharacters.Length, programStart).Clear();
        program.CopyTo(ram.Slice(programStart, program.Length));
        var usedRam = programStart + program.Length + FontCharacters.Length;
        ram[usedRam..].Clear();
        chip8.Pc = programStart;
        chip8.Sp = 0;
    }

    public static void KeyPressed(Chip8 chip8, byte key)
    {
        chip8.WaitingForKeyPress = false;

        var opcode = (ushort)((chip8.Memory[chip8.Pc] << 8) | chip8.Memory[chip8.Pc + 1]);
        chip8.V[(opcode & 0x0F00) >> 8] = key;
        chip8.Pc += 2;
    }

    public static void Step(Chip8 chip8, int ticksPer60Hz)
    {
        if (!chip8.Watch.IsRunning) chip8.Watch.Start();

        if (chip8.DelayTimer > 0 && chip8.Watch.ElapsedTicks > ticksPer60Hz)
        {
            chip8.DelayTimer--;
            chip8.Watch.Restart();
        }

        var opcode = (ushort)((chip8.Memory[chip8.Pc] << 8) | chip8.Memory[chip8.Pc + 1]);

        if (chip8.WaitingForKeyPress)
            throw new Chip8Exception("Do not call Step when chip8.WaitingForKeyPress is set.");

        var nibble = (ushort)(opcode & 0xF000);

        var nn = opcode & 0x00FF;

        chip8.Pc += 2;

        switch (nibble)
        {
            case 0x0000:
                if (opcode == 0x00e0)
                    Array.Clear(chip8.Gfx);
                else if (opcode == 0x00ee)
                    chip8.Pc = chip8.Stack[--chip8.Sp];
                else
                    throw new Chip8Exception($"Unsupported opcode {opcode:X4}");

                break;
            case 0x1000:
                chip8.Pc = (ushort)(opcode & 0x0FFF);
                break;
            case 0x2000:
                chip8.Stack[chip8.Sp++] = chip8.Pc;
                chip8.Pc = (ushort)(opcode & 0x0FFF);
                break;
            case 0x3000:
                if (chip8.V[(opcode & 0x0F00) >> 8] == nn)
                    chip8.Pc += 2;
                break;
            case 0x4000:
                if (chip8.V[(opcode & 0x0F00) >> 8] != nn)
                    chip8.Pc += 2;
                break;
            case 0x5000:
                if (chip8.V[(opcode & 0x0F00) >> 8] == chip8.V[(opcode & 0x00F0) >> 4])
                    chip8.Pc += 2;
                break;
            case 0x6000:
                chip8.V[(opcode & 0x0F00) >> 8] = (byte)nn;
                break;
            case 0x7000:
                chip8.V[(opcode & 0x0F00) >> 8] += (byte)nn;
                break;
            case 0x8000:
                var vx = (opcode & 0x0F00) >> 8;
                var vy = (opcode & 0x00F0) >> 4;
                switch (opcode & 0x000F)
                {
                    case 0: chip8.V[vx] = chip8.V[vy]; break;
                    case 1: chip8.V[vx] = (byte)(chip8.V[vx] | chip8.V[vy]); break;
                    case 2: chip8.V[vx] = (byte)(chip8.V[vx] & chip8.V[vy]); break;
                    case 3: chip8.V[vx] = (byte)(chip8.V[vx] ^ chip8.V[vy]); break;
                    case 4:
                        chip8.V[^1] = ToByte(chip8.V[vx] + chip8.V[vy] > 255);
                        chip8.V[vx] = (byte)((chip8.V[vx] + chip8.V[vy]) & 0x00FF);
                        break;
                    case 5:
                        chip8.V[^1] = ToByte((chip8.V[vx] > chip8.V[vy]));
                        chip8.V[vx] = (byte)((chip8.V[vx] - chip8.V[vy]) & 0x00FF);
                        break;
                    case 6:
                        chip8.V[^1] = (byte)(chip8.V[vx] & 0x0001);
                        chip8.V[vx] = (byte)(chip8.V[vx] >> 1);
                        break;
                    case 7:
                        chip8.V[^1] = ToByte(chip8.V[vy] > chip8.V[vx]);
                        chip8.V[vx] = (byte)((chip8.V[vy] - chip8.V[vx]) & 0x00FF);
                        break;
                    case 14:
                        chip8.V[^1] = ToByte((chip8.V[vx] & 0x80) == 0x80);
                        chip8.V[vx] = (byte)(chip8.V[vx] << 1);
                        break;
                    default:
                        throw new Chip8Exception($"Unsupported opcode {opcode:X4}");
                }

                break;
            case 0x9000:
                if (chip8.V[(opcode & 0x0F00) >> 8] != chip8.V[(opcode & 0x00F0) >> 4])
                    chip8.Pc += 2;
                break;
            case 0xA000:
                chip8.I = (ushort)(opcode & 0x0FFF);
                break;
            case 0xB000:
                chip8.Pc = (ushort)((opcode & 0x0FFF) + chip8.V[0]);
                break;
            case 0xC000:
                Span<byte> rnd = stackalloc byte[1];
                Generator.NextBytes(rnd);
                chip8.V[(opcode & 0x0F00) >> 8] = (byte)(rnd[0] & nn);
                break;
            case 0xD000:
            {
                int x = chip8.V[(opcode & 0x0F00) >> 8];
                int y = chip8.V[(opcode & 0x00F0) >> 4];
                var n = opcode & 0x000F;
                Draw.DrawSprites(chip8, x, y, n);
                break;
            }

            case 0xE000:
            {
                if (nn == 0x009E)
                {
                    if (((chip8.Keyboard >> chip8.V[(opcode & 0x0F00) >> 8]) & 0x01) == 0x01)
                        chip8.Pc += 2;
                    break;
                }

                if (nn == 0x00A1)
                {
                    if (((chip8.Keyboard >> chip8.V[(opcode & 0x0F00) >> 8]) & 0x01) != 0x01)
                        chip8.Pc += 2;
                    break;
                }

                throw new Chip8Exception($"Unsupported opcode {opcode:X4}");
            }
            case 0xF000:
                var tx = (opcode & 0x0F00) >> 8;

                switch (opcode & 0x00FF)
                {
                    case 0x7:
                        chip8.V[tx] = chip8.DelayTimer;
                        break;
                    case 0x0A:
                        chip8.WaitingForKeyPress = true;
                        chip8.Pc -= 2;
                        break;
                    case 0x15:
                        chip8.DelayTimer = chip8.V[tx];
                        break;
                    case 0x18: // sound
                        chip8.SoundTimer = chip8.V[tx];
                        break;
                    case 0x1E:
                        chip8.I = (ushort)(chip8.I + chip8.V[tx]);
                        break;
                    case 0x29:
                        chip8.I = (ushort)(chip8.V[tx] * 5);
                        break;
                    case 0x33:
                        chip8.Memory[chip8.I] = (byte)(chip8.V[tx] / 100);
                        chip8.Memory[chip8.I + 1] = (byte)((chip8.V[tx] % 100) / 10);
                        chip8.Memory[chip8.I + 2] = (byte)(chip8.V[tx] % 10);
                        break;
                    case 0x55:
                        for (var i = 0; i <= tx; i++)
                            chip8.Memory[chip8.I + i] = chip8.V[i];

                        break;
                    case 0x65:
                        for (var i = 0; i <= tx; i++)
                            chip8.V[i] = chip8.Memory[chip8.I + i];

                        break;
                    default:
                        throw new Chip8Exception($"Unsupported opcode {opcode:X4}");
                }

                break;
            default:
                throw new Chip8Exception($"Unsupported opcode {opcode:X4}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe byte ToByte(bool v) => *(byte*)&v;
}