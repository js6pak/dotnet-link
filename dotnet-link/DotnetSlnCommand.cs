// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

using System.Diagnostics;

namespace DotNetLink;

internal static class DotnetSlnCommand
{
    public static async Task<IEnumerable<string>> ListAsync(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "sln", path, "list" },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start process");
        await process.WaitForExitAsync();

        var output = await process.StandardOutput.ReadToEndAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Process exited with code {process.ExitCode}");
        }

        var projects = new List<string>();

        var directory = Path.GetDirectoryName(path)!;

        var foundHeader = false;
        foreach (var line in output.Split('\r', '\n'))
        {
            if (!foundHeader)
            {
                foundHeader = line.All(c => c == '-');
                continue;
            }

            projects.Add(Path.Combine(directory, line));
        }

        return projects;
    }
}
