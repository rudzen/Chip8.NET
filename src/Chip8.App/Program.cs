using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using Silk.NET.SDL;
using SystemThread = System.Threading.Thread;

namespace Chip8.App;

internal static class Program
{
    private static Sdl? _sdl;
    private static unsafe Window* _window;
    private static unsafe Renderer* _renderer;
    private static Chip8 _chip8 = null!;
    private static uint _audioDevice;
    private static int _sample;
    private static int _beepSamples;

    private static unsafe void Main(string[] args)
    {
        _sdl = Sdl.GetApi();

        if (_sdl.Init(Sdl.InitEverything) < 0)
        {
            Console.WriteLine("SDL failed to init.");
            return;
        }

        _window = _sdl.CreateWindow("Chip-8 Interpreter", 128, 128, 64 * 8, 32 * 8, 0);

        if (_window is null)
        {
            Console.WriteLine("SDL could not create a window.");
            return;
        }

        _renderer = _sdl.CreateRenderer(_window, -1, (uint)RendererFlags.Accelerated);

        if (_renderer is null)
        {
            Console.WriteLine("SDL could not create a valid renderer.");
            return;
        }

        _chip8 = new Chip8();

        var path = Environment.CurrentDirectory;

        path = Path.Combine("roms", "sample.ch8");

        var file = args.Length == 0 ? path : args[0];

        if (!File.Exists(file))
            throw new Chip8Exception("File does not exist");

        _sdl.SetWindowTitle(_window, Path.GetFileName(file));

        using (var fs = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Span<byte> data = stackalloc byte[(int)fs.Length];
            var read = fs.Read(data);
            if (read != fs.Length)
                throw new Chip8Exception("Could not read entire file");
            Cpu.LoadProgram(_chip8, data);
        }

        // Setup audio
        var audioSpec = new AudioSpec
        {
            Channels = 1,
            Freq = 44100,
            Samples = 256,
            Format = Sdl.AudioS8,
            Callback = new PfnAudioCallback(AudioCallback)
        };

        _audioDevice = _sdl.OpenAudioDevice((byte*)null, 0, &audioSpec, null, 0);
        _sdl.PauseAudioDevice(_audioDevice, 0);

        var running = true;

        Texture* sdlTexture = null;
        var frameTimer = Stopwatch.StartNew();
        var ticksPer60Hz = (int)(Stopwatch.Frequency * 0.016);

        while (running)
        {
            try
            {
                if (!_chip8.WaitingForKeyPress) Cpu.Step(_chip8, ticksPer60Hz);

                if (frameTimer.ElapsedTicks > ticksPer60Hz)
                {
                    Event sdlEvent;
                    while (_sdl.PollEvent(&sdlEvent) != 0)
                    {
                        switch (sdlEvent.Type)
                        {
                            case (uint)EventType.Quit:
                                running = false;
                                break;
                            case (uint)EventType.Keydown:
                            {
                                var key = KeyCodeToKey(sdlEvent.Key.Keysym.Sym);
                                _chip8.Keyboard |= (ushort)(1 << key);

                                if (_chip8.WaitingForKeyPress) Cpu.KeyPressed(_chip8, (byte)key);
                                break;
                            }
                            case (uint)EventType.Keyup:
                            {
                                var key = KeyCodeToKey(sdlEvent.Key.Keysym.Sym);
                                _chip8.Keyboard &= (ushort)~(1 << key);
                                break;
                            }
                        }
                    }

                    var displayHandle = GCHandle.Alloc(_chip8.Gfx, GCHandleType.Pinned);

                    if (sdlTexture != null) _sdl.DestroyTexture(sdlTexture);

                    var sdlSurface = _sdl.CreateRGBSurfaceFrom(
                        pixels: displayHandle.AddrOfPinnedObject().ToPointer(),
                        width: 64,
                        height: 32,
                        depth: 32,
                        pitch: 64 * 4,
                        Rmask: 0x000000ff,
                        Gmask: 0x0000ff00,
                        Bmask: 0x00ff0000,
                        Amask: 0xff000000
                    );

                    sdlTexture = _sdl.CreateTextureFromSurface(_renderer, sdlSurface);

                    displayHandle.Free();

                    _sdl.RenderClear(_renderer);
                    _sdl.RenderCopy(_renderer, sdlTexture, null, null);
                    _sdl.RenderPresent(_renderer);

                    frameTimer.Restart();
                }

                SystemThread.Sleep(1);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        _sdl.DestroyRenderer(_renderer);
        _sdl.DestroyWindow(_window);
        _sdl.Dispose();
    }

    private static unsafe void AudioCallback(void* userdata, byte* stream, int length)
    {
        Span<sbyte> waveData = stackalloc sbyte[length];

        for (var i = 0; i < waveData.Length && _chip8.SoundTimer > 0; i++, _beepSamples++)
        {
            if (_beepSamples == 730)
            {
                _beepSamples = 0;
                _chip8.SoundTimer--;
            }

            waveData[i] = (sbyte)(127 * Math.Sin(_sample * Math.PI * 2 * 604.1 / 44100));
            _sample++;
        }

        var byteSpan = MemoryMarshal.AsBytes(waveData);
        byteSpan.CopyTo(new Span<byte>(stream, length));
    }

    private static int KeyCodeToKey(int keycode)
    {
        var keyIndex = keycode < 58
            ? keycode - 48
            : keycode - 87;
        return keyIndex;
    }
}