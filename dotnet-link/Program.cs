// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

using System.CommandLine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.DotNet.Cli;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
[assembly: DisableRuntimeMarshalling]

namespace DotNetLink;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!SdkFinder.Initialize())
        {
            await Console.Error.WriteLineAsync("Failed to find a compatible .NET SDK");
            await Console.Error.WriteLineAsync($"dotnet-link requires {SdkFinder.TargetVersion}.x sdk to be installed");
            return 1;
        }

        return await InvokeAsync(args);
    }

    private static async Task<int> InvokeAsync(string[] args)
    {
        if (RgbAnsiColorExtensions.EnableAnsi())
            Environment.SetEnvironmentVariable("DOTNET_CLI_CONTEXT_ANSI_PASS_THRU", "true");

        // Based on https://github.com/dotnet/sdk/blob/v9.0.102/src/Cli/dotnet/Parser.cs#L166-L171
        var cliConfiguration = new CliConfiguration(LinkCommandParser.Command)
        {
            EnableDefaultExceptionHandler = false,
            EnablePosixBundling = false,
            ResponseFileTokenReplacer = Parser.TokenPerLine,
        };

        return await cliConfiguration.Parse(args).InvokeAsync();
    }
}
