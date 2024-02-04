using System.CommandLine;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Tools;
using PackLocalizableStrings = Microsoft.DotNet.Tools.Pack.LocalizableStrings;

namespace DotNetLink;

internal static class LinkCommandParser
{
    public static readonly CliArgument<IEnumerable<string>?> SlnOrProjectArgument = new(CommonLocalizableStrings.SolutionOrProjectArgumentName)
    {
        Description = CommonLocalizableStrings.SolutionOrProjectArgumentDescription,
        Arity = ArgumentArity.ZeroOrMore,
    };

    public static readonly CliOption<bool> NoPackOption = new("--no-pack") { Description = "Do not build and pack the project before linking" };

    public static readonly CliOption<string> ConfigurationOption = CommonOptions.ConfigurationOption(PackLocalizableStrings.ConfigurationOptionDescription);

    private static readonly CliCommand Command = ConstructCommand();

    public static CliCommand GetCommand()
    {
        return Command;
    }

    private static CliCommand ConstructCommand()
    {
        NoPackOption.Aliases.Add("--no-build");

        var command = new CliRootCommand("Symlinks a nuget package for easier development")
        {
            SlnOrProjectArgument,
            NoPackOption,
            CommonOptions.InteractiveMsBuildForwardOption,
            CommonOptions.VerbosityOption,
            CommonOptions.VersionSuffixOption,
            ConfigurationOption,
        };

        RestoreCommandParser.AddImplicitRestoreOptions(command, includeRuntimeOption: true, includeNoDependenciesOption: true);

        command.SetAction(LinkCommand.Run);

        return command;
    }
}