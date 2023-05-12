using System.CommandLine;
using DotNetLink;
using Microsoft.DotNet.Cli;
using ParserExtensions = System.CommandLine.Parsing.ParserExtensions;

if (!SdkFinder.Initialize())
{
    Console.Error.WriteLine("Failed to find a compatible .NET SDK");
    Console.Error.WriteLine("dotnet-link requires 7.0.x sdk to be installed");
    return 1;
}

return await InvokeAsync(args);

static async Task<int> InvokeAsync(string[] args)
{
    if (RgbAnsiColorExtensions.EnableAnsi())
    {
        Environment.SetEnvironmentVariable("DOTNET_CLI_CONTEXT_ANSI_PASS_THRU", "true");
    }

    // Based on https://github.com/dotnet/sdk/blob/v7.0.203/src/Cli/dotnet/Parser.cs#L146
    var parser = new CommandLineBuilder(LinkCommandParser.GetCommand())
        .UseExceptionHandler(Parser.ExceptionHandler)
        .UseHelp()
        .UseHelpBuilder(_ => Parser.DotnetHelpBuilder.Instance.Value)
        .UseLocalizationResources(new CommandLineValidationMessages())
        .UseParseDirective()
        .UseSuggestDirective()
        .DisablePosixBinding()
        .UseTokenReplacer(Parser.TokenPerLine)
        .Build();

    return await ParserExtensions.InvokeAsync(parser, args);
}
