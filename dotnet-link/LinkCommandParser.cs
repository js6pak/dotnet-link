// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

using System.CommandLine;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Tools;
using PackLocalizableStrings = Microsoft.DotNet.Tools.Pack.LocalizableStrings;

namespace DotNetLink;

internal static class LinkCommandParser
{
    public static CliArgument<IEnumerable<string>?> SlnOrProjectArgument { get; } = new(CommonLocalizableStrings.SolutionOrProjectArgumentName)
    {
        Description = CommonLocalizableStrings.SolutionOrProjectArgumentDescription,
        Arity = ArgumentArity.ZeroOrMore,
    };

    public static CliOption<bool> NoPackOption { get; } = new("--no-pack")
    {
        Description = "Do not build and pack the project before linking",
        Aliases = { "--no-build" },
    };

    public static CliOption<string> ConfigurationOption { get; } = CommonOptions.ConfigurationOption(PackLocalizableStrings.ConfigurationOptionDescription);

    public static CliCommand Command { get; } = ConstructCommand();

    private static CliRootCommand ConstructCommand()
    {
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
