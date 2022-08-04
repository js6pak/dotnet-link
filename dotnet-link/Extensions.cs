using System.ComponentModel;
using System.Runtime.InteropServices;
using Spectre.Console;

namespace DotNetLink;

internal static class Extensions
{
    [DllImport("libc", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int link(string oldpath, string newpath);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    public static void CreateLink(string path, string pathToTarget, bool symbolic = true)
    {
        if (File.Exists(path)) File.Delete(path);

        if (symbolic)
        {
            File.CreateSymbolicLink(path, pathToTarget);
        }
        else
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (link(pathToTarget, path) != 0) throw new Win32Exception();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!CreateHardLink(path, pathToTarget, IntPtr.Zero)) throw new Win32Exception();
            }
            else
            {
                throw new PlatformNotSupportedException();
            }
        }

        AnsiConsole.MarkupLine($"Linked [cyan]{path.TrimCurrentDirectory()}[/] to [cyan]{pathToTarget.TrimCurrentDirectory()}[/]");
    }

    public static string TrimStart(this string text, string value)
    {
        return text.StartsWith(value) ? text[(value.Length + 1)..] : text;
    }

    public static string TrimCurrentDirectory(this string text)
    {
        return text.TrimStart(Directory.GetCurrentDirectory());
    }
}
