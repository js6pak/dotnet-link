// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;

namespace DotNetLink;

internal static class LinkCommandParser
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Symbol")]
    private static extern CliSymbol GetSymbol(CliToken token);

    public static CliArgument<IEnumerable<string>> SlnOrProjectArgument { get; } = new("PROJECT | SOLUTION")
    {
        Description = "The project or solution file to operate on. If a file is not specified, the command will search the current directory for one.",
        Arity = ArgumentArity.ZeroOrMore,
        CustomParser = argumentResult =>
        {
            if (argumentResult.Tokens.Count > 1)
            {
                var lastIndexBeforeDoubleDash = argumentResult.Tokens.ToList().FindLastIndex(t => GetSymbol(t) == SlnOrProjectArgument) + 1;
                argumentResult.OnlyTake(lastIndexBeforeDoubleDash);
                return argumentResult.Tokens.Take(lastIndexBeforeDoubleDash).Select(t => t.Value);
            }

            argumentResult.OnlyTake(0);
            return [];
        },
    };

    public static CliOption<bool> NoBuildOption { get; } = new("--no-build")
    {
        Description = "Do not build the project before linking",
    };

    public static CliOption<bool> CopyOption { get; } = new("--copy")
    {
        Description = "Copy instead of linking",
    };

    public static CliCommand Command { get; } = ConstructCommand();

    private static CliRootCommand ConstructCommand()
    {
        var command = new CliRootCommand("Symlinks a nuget package for easier development")
        {
            SlnOrProjectArgument,
            NoBuildOption,
            CopyOption,
        };

        command.TreatUnmatchedTokensAsErrors = false;

        command.SetAction(LinkCommand.RunAsync);

        return command;
    }
}
