using System.Runtime.CompilerServices;

namespace Chip8.App;

public static class Cpu
{
    private static ReadOnlySpan<byte> FontCharacters =>
    [
            0xF0, 0x90, 0x90, 0x90, 0xF0, 0x20, 0x60, 0x20, 0x20, 0x70, 0xF0, 0x10, 0xF0, 0x80, 0xF0, 0xF0, 0x10, 0xF0, 0x10, 0xF0,
            0x90, 0x90, 0xF0, 0x10, 0x10, 0xF0, 0x80, 0xF0, 0x10, 0xF0, 0xF0, 0x80, 0xF0, 0x90, 0xF0, 0xF0, 0x10, 0x20, 0x40, 0x40,
            0xF0, 0x90, 0xF0, 0x90, 0xF0, 0xF0, 0x90, 0xF0, 0x10, 0xF0, 0xF0, 0x90, 0xF0, 0x90, 0x90, 0xE0, 0x90, 0xE0, 0x90, 0xE0,
            0xF0, 0x80, 0x80, 0x80, 0xF0, 0xE0, 0x90, 0x90, 0x90, 0xE0, 0xF0, 0x80, 0xF0, 0x80, 0xF0, 0xF0, 0x80, 0xF0, 0x80, 0x80
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
                if (opcode == 0x00e0) // clear screen
                    Array.Clear(chip8.Gfx);
                else if (opcode == 0x00ee) // return from subroutine
                    chip8.Pc = chip8.Stack[--chip8.Sp];
                else
                    throw new Chip8Exception($"Unsupported opcode {opcode:X4}");

                break;
            case 0x1000: // jump to address NNN
                chip8.Pc = (ushort)(opcode & 0x0FFF);
                break;
            case 0x2000: // call subroutine at NNN
                chip8.Stack[chip8.Sp++] = chip8.Pc;
                chip8.Pc = (ushort)(opcode & 0x0FFF);
                break;
            case 0x3000: // skip next instruction if Vx == NN
                if (chip8.V[(opcode & 0x0F00) >> 8] == nn)
                    chip8.Pc += 2;
                break;
            case 0x4000: // skip next instruction if Vx != NN
                if (chip8.V[(opcode & 0x0F00) >> 8] != nn)
                    chip8.Pc += 2;
                break;
            case 0x5000: // skip next instruction if Vx == Vy
                if (chip8.V[(opcode & 0x0F00) >> 8] == chip8.V[(opcode & 0x00F0) >> 4])
                    chip8.Pc += 2;
                break;
            case 0x6000: // set Vx = NN
                chip8.V[(opcode & 0x0F00) >> 8] = (byte)nn;
                break;
            case 0x7000: // set Vx = Vx + NN
                chip8.V[(opcode & 0x0F00) >> 8] += (byte)nn;
                break;
            case 0x8000: // arithmetic operations
                var vx = (opcode & 0x0F00) >> 8;
                var vy = (opcode & 0x00F0) >> 4;
                switch (opcode & 0x000F)
                {
                    case 0: chip8.V[vx] = chip8.V[vy]; break;                       // LD Vx, Vy
                    case 1: chip8.V[vx] = (byte)(chip8.V[vx] | chip8.V[vy]); break; // OR Vx, Vy
                    case 2: chip8.V[vx] = (byte)(chip8.V[vx] & chip8.V[vy]); break; // AND Vx, Vy
                    case 3: chip8.V[vx] = (byte)(chip8.V[vx] ^ chip8.V[vy]); break; // XOR Vx, Vy
                    case 4:                                                         // ADD Vx, Vy
                        chip8.V[^1] = ToByte(chip8.V[vx] + chip8.V[vy] > 255);
                        chip8.V[vx] = (byte)((chip8.V[vx] + chip8.V[vy]) & 0x00FF);
                        break;
                    case 5: // SUB Vx, Vy
                        chip8.V[^1] = ToByte((chip8.V[vx] > chip8.V[vy]));
                        chip8.V[vx] = (byte)((chip8.V[vx] - chip8.V[vy]) & 0x00FF);
                        break;
                    case 6: // SHR Vx {, Vy}
                        chip8.V[^1] = (byte)(chip8.V[vx] & 0x0001);
                        chip8.V[vx] = (byte)(chip8.V[vx] >> 1);
                        break;
                    case 7: // SUBN Vx, Vy
                        chip8.V[^1] = ToByte(chip8.V[vy] > chip8.V[vx]);
                        chip8.V[vx] = (byte)((chip8.V[vy] - chip8.V[vx]) & 0x00FF);
                        break;
                    case 14: // SHL Vx {, Vy}
                        chip8.V[^1] = ToByte((chip8.V[vx] & 0x80) == 0x80);
                        chip8.V[vx] = (byte)(chip8.V[vx] << 1);
                        break;
                    default:
                        throw new Chip8Exception($"Unsupported opcode {opcode:X4}");
                }

                break;
            case 0x9000: // skip next instruction if Vx != Vy
                if (chip8.V[(opcode & 0x0F00) >> 8] != chip8.V[(opcode & 0x00F0) >> 4])
                    chip8.Pc += 2;
                break;
            case 0xA000: // set I = NNN
                chip8.I = (ushort)(opcode & 0x0FFF);
                break;
            case 0xB000: // jump to address NNN + V0
                chip8.Pc = (ushort)((opcode & 0x0FFF) + chip8.V[0]);
                break;
            case 0xC000: // set Vx = random byte AND NN
                Span<byte> rnd = stackalloc byte[1];
                Generator.NextBytes(rnd);
                chip8.V[(opcode & 0x0F00) >> 8] = (byte)(rnd[0] & nn);
                break;
            case 0xD000: // display n-byte sprite starting at memory location I at (Vx, Vy), set VF = collision
            {
                int x = chip8.V[(opcode & 0x0F00) >> 8];
                int y = chip8.V[(opcode & 0x00F0) >> 4];
                var n = opcode & 0x000F;
                chip8.V[^1] = ToByte(Draw.DrawSprites(chip8, x, y, n));
                break;
            }

            case 0xE000: // key operations
            {
                // skip next instruction if key with the value of Vx is pressed
                if (nn == 0x009E)
                {
                    if (((chip8.Keyboard >> chip8.V[(opcode & 0x0F00) >> 8]) & 0x01) == 0x01)
                        chip8.Pc += 2;
                    break;
                }

                // skip next instruction if key with the value of Vx is not pressed
                if (nn == 0x00A1)
                {
                    if (((chip8.Keyboard >> chip8.V[(opcode & 0x0F00) >> 8]) & 0x01) != 0x01)
                        chip8.Pc += 2;
                    break;
                }

                // unknown opcode
                throw new Chip8Exception($"Unsupported opcode {opcode:X4}");
            }
            case 0xF000: // miscellaneous operations
                var tx = (opcode & 0x0F00) >> 8;

                switch (opcode & 0x00FF)
                {
                    case 0x7: // set Vx = delay timer value
                        chip8.V[tx] = chip8.DelayTimer;
                        break;
                    case 0x0A: // wait for a key press, store the value of the key in Vx
                        chip8.WaitingForKeyPress = true;
                        chip8.Pc -= 2;
                        break;
                    case 0x15: // set delay timer = Vx
                        chip8.DelayTimer = chip8.V[tx];
                        break;
                    case 0x18: // set sound timer = Vx
                        chip8.SoundTimer = chip8.V[tx];
                        break;
                    case 0x1E: // set I = I + Vx
                        chip8.I = (ushort)(chip8.I + chip8.V[tx]);
                        break;
                    case 0x29: // set I = location of sprite for digit Vx
                        chip8.I = (ushort)(chip8.V[tx] * 5);
                        break;
                    case 0x33: // store BCD representation of Vx in memory locations I, I+1, and I+2
                        chip8.Memory[chip8.I] = (byte)(chip8.V[tx] / 100);
                        chip8.Memory[chip8.I + 1] = (byte)((chip8.V[tx] % 100) / 10);
                        chip8.Memory[chip8.I + 2] = (byte)(chip8.V[tx] % 10);
                        break;
                    case 0x55: // store registers V0 through Vx in memory starting at location I
                        chip8.V.AsSpan(0, tx + 1).CopyTo(chip8.Memory.AsSpan(chip8.I, tx + 1));
                        break;
                    case 0x65: // read registers V0 through Vx from memory starting at location I
                        chip8.Memory.AsSpan(chip8.I, tx + 1).CopyTo(chip8.V.AsSpan(0, tx + 1));
                        break;
                    default: // unknown opcode
                        throw new Chip8Exception($"Unsupported opcode {opcode:X4}");
                }

                break;
            default: // unknown opcode
                throw new Chip8Exception($"Unsupported opcode {opcode:X4}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe byte ToByte(bool v) => *(byte*)&v;
}