using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Libjector.Core;

public static class Utilities
{
    [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process([In] nint processHandle, [Out] [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

    public static Architecture? GetProcessArchitecture(Process process)
    {
        if (!Environment.Is64BitOperatingSystem)
            return Architecture.X86;
        if (!IsWow64Process(process.Handle, out var result))
            return null;
        return result ? Architecture.X86 : Architecture.X64;
    }

    public static Architecture? GetDllArchitecture(string libraryPath)
    {
        using var stream = new FileStream(libraryPath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream);
        stream.Seek(0x3c, SeekOrigin.Begin);
        var offset = reader.ReadInt32();
        stream.Seek(offset, SeekOrigin.Begin);
        var head = reader.ReadUInt32();
        if (head != 0x00004550)
            return null;
        return (ushort)reader.ReadInt16() switch
        {
            0x8664 => Architecture.X64,
            0x200 => Architecture.X64,
            0x14c => Architecture.X86,
            _ => null
        };
    }

    public static bool IsRunningAsAdministrator()
    {
        return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void ShowFileInExplorer(string filePath)
    {
        Process.Start("explorer.exe", $"/select,\"{filePath}\"");
    }
}