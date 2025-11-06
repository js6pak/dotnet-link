// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetLink;

internal static class DotnetBuildCommand
{
    public static async Task<GetResultOutput?> ExecuteAsync(
        string project,
        bool restore = false,
        IReadOnlyCollection<string>? targets = null,
        IDictionary<string, string>? properties = null,
        IReadOnlyCollection<string>? getProperty = null,
        IReadOnlyCollection<string>? getItem = null,
        IReadOnlyCollection<string>? getTargetResult = null,
        IEnumerable<string>? additionalArguments = null
    )
    {
        string? getResultOutputFile = null;

        if (getProperty?.Count > 0 || getItem?.Count > 0 || getTargetResult?.Count > 0)
        {
            getResultOutputFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        }

        try
        {
            var arguments = new List<string>
            {
                "build",
                "-tl:off",
            };

            if (!restore) arguments.Add("--no-restore");

            if (targets != null) AddListArgument("target", targets);
            if (properties != null) AddListArgument("property", properties.Select(p => $"{p.Key}=\"{p.Value}\""));

            if (getResultOutputFile != null) AddArgument("getResultOutputFile", getResultOutputFile);
            if (getProperty != null) AddListArgument("getProperty", getProperty);
            if (getItem != null) AddListArgument("getItem", getItem);
            if (getTargetResult != null) AddListArgument("getTargetResult", getTargetResult);

            arguments.Add(project);

            if (additionalArguments != null)
            {
                arguments.AddRange(additionalArguments);
            }

            void AddArgument(string name, string value)
            {
                arguments.Add($"/{name}:{value}");
            }

            void AddListArgument(string name, IEnumerable<string> list)
            {
                AddArgument(name, string.Join(';', list));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start process");
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Process exited with code {process.ExitCode}");
            }

            GetResultOutput? getResultOutput = null;

            if (getResultOutputFile != null)
            {
                if (getProperty?.Count == 1 && (getItem == null || getItem.Count == 0))
                {
                    var propertyName = getProperty.Single();

                    getResultOutput = new GetResultOutput(
                        new Dictionary<string, string>
                        {
                            [propertyName] = (await File.ReadAllTextAsync(getResultOutputFile)).TrimEnd(),
                        },
                        null,
                        null
                    );
                }
                else
                {
                    await using var stream = File.OpenRead(getResultOutputFile);
                    getResultOutput = await JsonSerializer.DeserializeAsync<GetResultOutput>(stream);
                }
            }

            return getResultOutput;
        }
        finally
        {
            if (getResultOutputFile != null)
            {
                File.Delete(getResultOutputFile);
            }
        }
    }

    public static async Task<string?> GetPropertyAsync(
        string project,
        string propertyName,
        IReadOnlyCollection<string>? targets = null,
        IDictionary<string, string>? properties = null,
        IEnumerable<string>? additionalArguments = null
    )
    {
        var result = await ExecuteAsync(
            project: project,
            targets: targets,
            properties: properties,
            getProperty: [propertyName],
            additionalArguments: additionalArguments
        );

        return result?.Properties?[propertyName];
    }

    internal sealed record GetResultOutput(
        [property: JsonPropertyName("Properties")]
        Dictionary<string, string>? Properties,
        [property: JsonPropertyName("Items")]
        Dictionary<string, Dictionary<string, string>>? Items,
        [property: JsonPropertyName("TargetResults")]
        Dictionary<string, GetResultOutput.TargetResult>? TargetResults
    )
    {
        internal sealed record TargetResult(
            [property: JsonPropertyName("Result")]
            string Result,
            [property: JsonPropertyName("Items")]
            Dictionary<string, string>[] Items
        );
    }
}
