// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

using System.Drawing;
using System.Runtime.InteropServices;

namespace DotNetLink;

internal static partial class RgbAnsiColorExtensions
{
    [LibraryImport("kernel32")]
    [SuppressGCTransition]
    private static partial IntPtr GetStdHandle(int nStdHandle);

    private const int STD_OUTPUT_HANDLE = -11;

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(IntPtr handle, out int mode);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(IntPtr handle, int mode);

    private const int ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    public static bool EnableAnsi()
    {
        if (Console.IsOutputRedirected)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var stdOut = GetStdHandle(STD_OUTPUT_HANDLE);
                if (GetConsoleMode(stdOut, out var consoleMode))
                {
                    if ((consoleMode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) == ENABLE_VIRTUAL_TERMINAL_PROCESSING)
                    {
                        return true;
                    }

                    consoleMode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                    if (SetConsoleMode(stdOut, consoleMode) && GetConsoleMode(stdOut, out consoleMode))
                    {
                        return (consoleMode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) == ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
        else
        {
            return Environment.GetEnvironmentVariable("TERM") != "dumb";
        }
    }

    public static string Color(this string text, Color color) => $"\u001B[38;2;{color.R};{color.G};{color.B}m" + text + "\u001B[39m";
}
