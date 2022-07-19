using Spectre.Console;

namespace DotNetLink;

internal static class Extensions
{
    public static void CreateSymbolicLink(string path, string pathToTarget)
    {
        if (File.Exists(path)) File.Delete(path);
        File.CreateSymbolicLink(path, pathToTarget);

        AnsiConsole.MarkupLine($"Linked [cyan]{path.TrimCurrentDirectory()}[/] to [cyan]{pathToTarget.TrimCurrentDirectory()}[/]");
    }

    public static string TrimStart(this string text, string value)
    {
        return text.StartsWith(value) ? text[(value.Length + 1)..] : text;
    }

    public static string TrimCurrentDirectory(this string text)
    {
        return text.TrimStart(Directory.GetCurrentDirectory());
    }
}
