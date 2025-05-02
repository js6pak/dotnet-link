// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

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
    private static partial bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    private const int ErrorPrivilegeNotHeld = unchecked((int) 0x80070522);

    public static void CreateLink(string path, string pathToTarget, bool symbolic = true)
    {
        if (File.Exists(path)) File.Delete(path);

        if (symbolic)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    File.CreateSymbolicLink(path, pathToTarget);
                }
                catch (IOException e) when (e.HResult == ErrorPrivilegeNotHeld)
                {
                    Reporter.Error.WriteLine(
                        """
                        You don't have privileges to create a symlink
                        Make sure to enable Developer Mode in Windows "For developers" settings
                        """.Yellow()
                    );
                    throw;
                }
            }
            else
            {
                File.CreateSymbolicLink(path, pathToTarget);
            }
        }
        else
        {
            if (OperatingSystem.IsWindows())
            {
                if (!CreateHardLinkW(path, pathToTarget, IntPtr.Zero)) throw new Win32Exception();
            }
            else
            {
                if (Link(pathToTarget, path) != 0) throw new Win32Exception();
            }
        }

        Reporter.Output.WriteLine(
            $"Linked {path.TrimCurrentDirectory().Cyan()} " +
            $"to {pathToTarget.TrimCurrentDirectory().Cyan()} " +
            $"({(symbolic ? "symbolic" : "hard")})"
        );
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
