using System.Runtime.InteropServices;
using Silk.NET.SDL;

namespace Chip8.App.Extensions;

public static class SdlExtensions
{
    public static unsafe string? AsString(this DropEvent dropEvent, Sdl sdl)
    {
        if (dropEvent.File is null)
            return string.Empty;

        // Convert byte* to IntPtr for Marshal methods
        var filePtr = new IntPtr(dropEvent.File);

        // Try UTF-8 first (most common for SDL2)
        var filePath = Marshal.PtrToStringUTF8(filePtr);

        // Fallback: try ANSI if UTF-8 fails or returns empty
        if (string.IsNullOrEmpty(filePath))
            filePath = Marshal.PtrToStringAnsi(filePtr);

        // Free the SDL allocated memory
        if (dropEvent.File is not null)
            sdl.Free(dropEvent.File);

        return filePath;
    }
}