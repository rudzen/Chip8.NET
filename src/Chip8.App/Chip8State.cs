using System.Diagnostics;
using System.Runtime.CompilerServices;
using Silk.NET.SDL;

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

[Flags]
public enum StateError : byte
{
    None = 0,
    SdlInit = 1,
    WindowCreate = 2,
    RendererCreate = 4,
    FileLoad = 8,
    AudioInit = 16
}

public static class StateErrorExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasFlagFast(this StateError value, StateError flag)
    {
        return (value & flag) != 0;
    }
}

public sealed class Chip8State
{
    public Sdl Sdl = null!;
    public unsafe Window* Window;
    public unsafe Renderer* Renderer;
    public uint AudioDevice;
    public int Sample;
    public int BeepSamples;
    public string RomName = null!;
    public StateError Error;

    public readonly Chip8 Chip8 = new();

    public static Chip8State State => new();
}

public static class StateExtensions
{
    public static unsafe Chip8State InitState(string[] args)
    {
        var state = Chip8State.State;
        state.Sdl = Sdl.GetApi();
        state.Error = StateError.None;
        state.RomName = string.Empty;

        if (state.Sdl.Init(Sdl.InitEverything) < 0)
        {
            state.Error |= StateError.SdlInit;
            return state;
        }

        const int windowX = 128;
        const int windowY = 128;
        const int windowWidth = 64 * 8;
        const int windowHeight = 32 * 8;
        const uint windowFlags = 0;

        state.Window = state.Sdl.CreateWindow(
            title: "Chip-8 Interpreter",
            x: windowX,
            y: windowY,
            w: windowWidth,
            h: windowHeight,
            flags: windowFlags
        );

        if (state.Window is null)
        {
            state.Error |= StateError.WindowCreate;
            return state;
        }

        const int rendererIndex = -1;
        const uint rendererFlags = (uint)RendererFlags.Accelerated;

        state.Renderer = state.Sdl.CreateRenderer(state.Window, rendererIndex, rendererFlags);

        if (state.Renderer is null)
        {
            state.Error |= StateError.RendererCreate;
            return state;
        }

        LoadFile(state, args);

        state.Sdl.SetWindowTitle(state.Window, Path.GetFileName(state.RomName));

        InitAudio(state);

        return state;
    }

    private static void LoadFile(Chip8State state, ReadOnlySpan<string> args)
    {
        var file = args.IsEmpty
            ? Path.Combine("roms", "sample.ch8")
            : args[0];

        if (!File.Exists(file))
        {
            state.Error |= StateError.FileLoad;
            return;
        }

        using var fs = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);

        Span<byte> data = stackalloc byte[(int)fs.Length];
        var read = fs.Read(data);

        if (read != fs.Length)
        {
            state.Error |= StateError.FileLoad;
            return;
        }

        Cpu.LoadProgram(state.Chip8, data);
        state.RomName = file;
    }

    private static unsafe void InitAudio(Chip8State state)
    {
        const byte channels = 1;
        const int freq = 44100;
        const ushort samples = 256;
        const ushort format = Sdl.AudioS8;

        var audioSpec = new AudioSpec
        {
            Channels = channels,
            Freq = freq,
            Samples = samples,
            Format = format,
            Callback = new PfnAudioCallback(Audio.AudioCallback)
        };

        const int isCapture = 0;
        const int allowedChanges = 0;

        state.AudioDevice = state.Sdl.OpenAudioDevice(
            device: (byte*)null, // default
            iscapture: isCapture,
            desired: &audioSpec,
            obtained: null,
            allowed_changes: allowedChanges
        );

        if (state.AudioDevice == 0)
        {
            Console.WriteLine("SDL could not open an audio device.");
            state.Error |= StateError.AudioInit;
            return;
        }

        state.Sdl.PauseAudioDevice(state.AudioDevice, 0);
    }
}