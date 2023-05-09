using System.CommandLine;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Tools;
using PackLocalizableStrings = Microsoft.DotNet.Tools.Pack.LocalizableStrings;

namespace DotNetLink;

internal static class LinkCommandParser
{
    public static readonly Argument<IEnumerable<string>?> SlnOrProjectArgument = new(CommonLocalizableStrings.SolutionOrProjectArgumentName)
    {
        Description = CommonLocalizableStrings.SolutionOrProjectArgumentDescription,
        Arity = ArgumentArity.ZeroOrMore,
    };

    public static readonly Option<bool> NoPackOption = new("--no-pack", "Do not build and pack the project before linking");

    public static readonly Option<string> ConfigurationOption = CommonOptions.ConfigurationOption(PackLocalizableStrings.ConfigurationOptionDescription);

    private static readonly Command Command = ConstructCommand();

    public static Command GetCommand()
    {
        return Command;
    }

    private static Command ConstructCommand()
    {
        NoPackOption.AddAlias("--no-build");
        
        var command = new Command("link", "Symlinks a nuget package for easier development");

        command.AddArgument(SlnOrProjectArgument);
        command.AddOption(NoPackOption);
        command.AddOption(CommonOptions.InteractiveMsBuildForwardOption);
        command.AddOption(CommonOptions.VerbosityOption);
        command.AddOption(CommonOptions.VersionSuffixOption);
        command.AddOption(ConfigurationOption);
        RestoreCommandParser.AddImplicitRestoreOptions(command, includeRuntimeOption: true, includeNoDependenciesOption: true);

        command.SetHandler(LinkCommand.Run);

        return command;
    }
}
