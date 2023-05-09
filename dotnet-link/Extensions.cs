using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.DotNet.Cli.Utils;

namespace DotNetLink;

internal static partial class Extensions
{
    [LibraryImport("libSystem.Native", EntryPoint = "SystemNative_Link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Link(string source, string link);

    [LibraryImport("kernel32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    public static void CreateLink(string path, string pathToTarget, bool symbolic = true)
    {
        if (File.Exists(path)) File.Delete(path);

        if (symbolic)
        {
            File.CreateSymbolicLink(path, pathToTarget);
        }
        else
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!CreateHardLink(path, pathToTarget, IntPtr.Zero)) throw new Win32Exception();
            }
            else
            {
                if (Link(pathToTarget, path) != 0) throw new Win32Exception();
            }
        }

        Reporter.Output.WriteLine($"Linked {path.TrimCurrentDirectory().Cyan()} to {pathToTarget.TrimCurrentDirectory().Cyan()}");
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
