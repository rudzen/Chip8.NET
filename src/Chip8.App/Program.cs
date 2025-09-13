using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Chip8.App;
using Silk.NET.SDL;
using SystemThread = System.Threading.Thread;

var state = State.InitState(args);

if (state.Error.HasFlagFast(StateError.SdlInit))
    Console.WriteLine("Failed to initialize SDL.");

if (state.Error.HasFlagFast(StateError.WindowCreate))
    Console.WriteLine("Failed to create window.");

if (state.Error.HasFlagFast(StateError.RendererCreate))
    Console.WriteLine("Failed to create renderer.");

if (state.Error.HasFlagFast(StateError.FileLoad))
    Console.WriteLine($"Failed to read ROM file '{state.RomName}'.");

if (state.Error.HasFlagFast(StateError.AudioInit))
    Console.WriteLine("Failed to initialize audio.");

if (state.Error != StateError.None)
    return;

MainLoop(state);

return;

static unsafe void MainLoop(Chip8State state)
{
    Texture* sdlTexture = null;
    var frameTimer = Stopwatch.StartNew();
    var ticksPer60Hz = (int)(Stopwatch.Frequency * 0.016);

    const uint quit = (uint)EventType.Quit;
    const uint keyDown = (uint)EventType.Keydown;
    const uint keyUp = (uint)EventType.Keyup;

    while (true)
    {
        try
        {
            if (!state.Chip8.WaitingForKeyPress)
                Cpu.Step(state.Chip8, ticksPer60Hz);

            if (frameTimer.ElapsedTicks > ticksPer60Hz)
            {
                Event sdlEvent;
                while (state.Sdl.PollEvent(&sdlEvent) != 0)
                {
                    switch (sdlEvent.Type)
                    {
                        case quit:
                            goto quit;
                        case keyDown:
                        {
                            var key = KeyCodeToKey(sdlEvent.Key.Keysym.Sym);
                            state.Chip8.Keyboard |= (ushort)(1 << key);

                            if (state.Chip8.WaitingForKeyPress)
                                Cpu.KeyPressed(state.Chip8, (byte)key);
                            break;
                        }
                        case keyUp:
                        {
                            var key = KeyCodeToKey(sdlEvent.Key.Keysym.Sym);
                            state.Chip8.Keyboard &= (ushort)~(1 << key);
                            break;
                        }
                    }
                }

                var displayHandle = GCHandle.Alloc(state.Chip8.Gfx, GCHandleType.Pinned);

                if (sdlTexture is not null)
                    state.Sdl.DestroyTexture(sdlTexture);

                var sdlSurface = state.Sdl.CreateRGBSurfaceFrom(
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

                sdlTexture = state.Sdl.CreateTextureFromSurface(state.Renderer, sdlSurface);

                displayHandle.Free();

                state.Sdl.RenderClear(state.Renderer);
                state.Sdl.RenderCopy(state.Renderer, sdlTexture, null, null);
                state.Sdl.RenderPresent(state.Renderer);

                frameTimer.Restart();
            }

            SystemThread.Sleep(1);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }

quit:
    state.Sdl.DestroyRenderer(state.Renderer);
    state.Sdl.DestroyWindow(state.Window);
    state.Sdl.Dispose();
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
static int KeyCodeToKey(int keycode)
{
    var keyIndex = keycode < 58
        ? keycode - 48
        : keycode - 87;
    return keyIndex;
}