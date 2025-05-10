// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

using System.CommandLine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
[assembly: DisableRuntimeMarshalling]

namespace DotNetLink;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "true");
        Environment.SetEnvironmentVariable("DOTNET_NOLOGO", "true");

        RgbAnsiColorExtensions.EnableAnsi();

        var cliConfiguration = new CliConfiguration(LinkCommandParser.Command)
        {
            EnableDefaultExceptionHandler = false,
            EnablePosixBundling = false,
        };

        try
        {
            return await cliConfiguration.Parse(args).InvokeAsync();
        }
        catch (Exception e)
        {
            var isGraceful = e is GracefulException;
            Console.WriteLine((isGraceful ? e.Message : e.ToString()).Red());
            return 1;
        }
    }
}
