using System.CommandLine;
using System.CommandLine.Completions;
using DotNetLink;
using Microsoft.DotNet.Cli;

if (!SdkFinder.Initialize())
{
    Console.Error.WriteLine("Failed to find a compatible .NET SDK");
    Console.Error.WriteLine($"dotnet-link requires {SdkFinder.TargetVersion}.x sdk to be installed");
    return 1;
}

return await InvokeAsync(args);

static async Task<int> InvokeAsync(string[] args)
{
    if (RgbAnsiColorExtensions.EnableAnsi())
    {
        Environment.SetEnvironmentVariable("DOTNET_CLI_CONTEXT_ANSI_PASS_THRU", "true");
    }

    // Based on https://github.com/dotnet/sdk/blob/v8.0.101/src/Cli/dotnet/Parser.cs#L158-L165
    var cliConfiguration = new CliConfiguration(LinkCommandParser.GetCommand())
    {
        EnableDefaultExceptionHandler = false,
        EnableParseErrorReporting = true,
        EnablePosixBundling = false,
        Directives = { new DiagramDirective(), new SuggestDirective() },
        ResponseFileTokenReplacer = Parser.TokenPerLine
    };

    return await cliConfiguration.Parse(args).InvokeAsync();
}