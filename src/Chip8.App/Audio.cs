using System.Runtime.InteropServices;

namespace Chip8.App;

public static class Audio
{
    public static unsafe void AudioCallback(void* userdata, byte* stream, int length)
    {
        const double f = Math.PI * 2 * 604.1 / 44100;

        var state = Chip8State.State;
        Span<sbyte> waveData = stackalloc sbyte[length];

        for (var i = 0; i < waveData.Length && state.Chip8.SoundTimer > 0; i++, state.BeepSamples++)
        {
            if (state.BeepSamples == 730)
            {
                state.BeepSamples = 0;
                state.Chip8.SoundTimer--;
            }

            waveData[i] = (sbyte)(127 * Math.Sin(state.Sample * f));
            state.Sample++;
        }

        var byteSpan = MemoryMarshal.AsBytes(waveData);
        byteSpan.CopyTo(new Span<byte>(stream, length));
    }
}