// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;

namespace DotNetLink;

internal static class LinkCommandParser
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Symbol")]
    private static extern Symbol GetSymbol(Token token);

    public static Argument<IEnumerable<string>> SlnOrProjectArgument { get; } = new("PROJECT | SOLUTION")
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

    public static Option<bool> NoBuildOption { get; } = new("--no-build")
    {
        Description = "Do not build the project before linking",
    };

    public static Option<bool> CopyOption { get; } = new("--copy")
    {
        Description = "Copy instead of linking",
    };

    public static Command Command { get; } = ConstructCommand();

    private static RootCommand ConstructCommand()
    {
        var command = new RootCommand("Symlinks a nuget package for easier development")
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
