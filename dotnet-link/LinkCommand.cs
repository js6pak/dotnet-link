// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

using System.CommandLine;
using System.Drawing;
using System.Text;
using DotNetLink.Utilities;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace DotNetLink;

internal sealed class LinkCommand
{
    private readonly IEnumerable<string> _slnOrProjectArgument;
    private readonly IReadOnlyList<string> _additionalArguments;
    private readonly bool _noBuild;
    private readonly bool _copy;

    private LinkCommand(ParseResult parseResult)
    {
        _slnOrProjectArgument = parseResult.GetValue(LinkCommandParser.SlnOrProjectArgument) ?? [];
        _additionalArguments = parseResult.UnmatchedTokens;
        _noBuild = parseResult.GetValue(LinkCommandParser.NoBuildOption);
        _copy = parseResult.GetValue(LinkCommandParser.CopyOption);
    }

    private async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var slnOrProjects = _slnOrProjectArgument.ToList();

        if (slnOrProjects.Count == 0)
        {
            slnOrProjects.Add(Directory.GetCurrentDirectory());
        }

        var projects = new HashSet<string>();

        foreach (var slnOrProject in slnOrProjects.Select(FileUtilities.GetProjectOrSolution))
        {
            if (FileUtilities.IsSolutionFilename(slnOrProject))
            {
                projects.AddRange(await DotnetSlnCommand.ListAsync(slnOrProject));
            }
            else
            {
                projects.Add(slnOrProject);
            }
        }

        projects.RemoveWhere(p => Path.GetExtension(p) == ".vcxproj");

        return await LinkAsync(projects, cancellationToken) ? 1 : 0;
    }

    private async Task<bool> LinkAsync(HashSet<string> projects, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Packing {projects.Count.ToString().Cyan()} project(s): {string.Join(", ", projects.Select(p => p.TrimCurrentDirectory().Cyan()))}");
        if (_additionalArguments.Count > 0)
        {
            Console.WriteLine($"Additional arguments: {string.Join(' ', _additionalArguments).Cyan()}");
        }

        var properties = new Dictionary<string, string>
        {
            ["ProjectsToLink"] = string.Join(';', projects.Select(Path.GetFullPath)),
        };

        if (_noBuild)
        {
            properties["NoBuild"] = "true";
            properties["BuildProjectReferences"] = "false";
        }

        var result = await DotnetBuildCommand.ExecuteAsync(
            Path.Combine(AppContext.BaseDirectory, "DotNetLink.proj"),
            restore: !_noBuild,
            targets: ["CollectLinkMetadata"],
            getTargetResult: ["CollectLinkMetadata"],
            properties: properties,
            additionalArguments: _additionalArguments
        );

        var targetResult = result!.TargetResults!["CollectLinkMetadata"];
        if (targetResult.Result != "Success")
        {
            throw new InvalidOperationException($"Target result was {targetResult.Result}");
        }

        var anyFailed = false;

        foreach (var projectItem in targetResult.Items)
        {
            try
            {
                if (!await LinkAsync(projectItem, cancellationToken))
                {
                    anyFailed = true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                anyFailed = false;
            }
        }

        return anyFailed;
    }

    private async Task<bool> LinkAsync(Dictionary<string, string> projectItem, CancellationToken cancellationToken)
    {
        var projectPath = projectItem["Identity"];

        Console.WriteLine($"Linking {projectPath.TrimCurrentDirectory().Cyan()}");

        var nuspecPath = projectItem["NuspecFile"];

        if (string.IsNullOrEmpty(nuspecPath))
        {
            Console.WriteLine("Couldn't find a nuspec file");
            return false;
        }

        if (!File.Exists(nuspecPath))
        {
            Console.WriteLine($"{nuspecPath.TrimCurrentDirectory().Cyan()} doesn't exist");
            return false;
        }

        Console.WriteLine($"Linking {nuspecPath.TrimCurrentDirectory().Cyan()}");

        await using var fileStream = File.OpenRead(nuspecPath);
        var manifest = Manifest.ReadFrom(fileStream, true);

        var isTool = false;
        var isMSBuildSdk = false;

        foreach (var packageType in manifest.Metadata.PackageTypes)
        {
            if (packageType == PackageType.DotnetTool)
            {
                isTool = true;
            }
            else if (PackageType.PackageTypeNameComparer.Equals(packageType.Name, "MSBuildSdk"))
            {
                isMSBuildSdk = true;
            }
            else
            {
                throw new NotImplementedException($"Package type {packageType.Name} is not supported yet");
            }
        }

        var id = manifest.Metadata.Id.ToLower();

        if (isTool)
        {
            var dotnetHomePath = GetDotnetHomePath() ?? throw new InvalidOperationException("The user's home directory could not be determined.");
            var toolsShimPath = Path.Combine(dotnetHomePath, ".dotnet", "tools");

            var packagePath = Path.Combine(toolsShimPath, ".store", id);
            if (Directory.Exists(packagePath))
            {
                Directory.Delete(packagePath);
            }

            var toolCommandName = projectItem["ToolCommandName"];
            var targetExecutablePath = projectItem["RunCommand"]; // TODO figure out if there is a better way to get apphost path

            var shimPath = Path.Combine(toolsShimPath, toolCommandName);
            CreateLink(shimPath, targetExecutablePath);
        }
        else
        {
            var packagesPath = Path.Combine(NuGetEnvironment.GetFolderPath(NuGetFolderPath.NuGetHome), "packages");

            var packagePath = Path.Combine(packagesPath, id, manifest.Metadata.Version.ToNormalizedString().ToLower());
            if (Directory.Exists(packagePath)) Directory.Delete(packagePath, true);
            Directory.CreateDirectory(packagePath);

            await File.WriteAllTextAsync(
                Path.Combine(packagePath, PackagingCoreConstants.NupkgMetadataFileExtension),
                /* lang=json */
                """{ "version": 2, "contentHash": null, "source": null }""",
                cancellationToken
            );
            CreateLink(Path.Combine(packagePath, id + PackagingCoreConstants.NuspecExtension), nuspecPath);

            foreach (var manifestFile in manifest.Files)
            {
                var source = Path.Combine(packagePath, manifestFile.Target);
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                CreateLink(source, manifestFile.Source, !manifestFile.Target.StartsWith("lib"));
            }

            var tagName = Color.FromArgb(0xE8, 0xBF, 0x6A);
            var attributeName = Color.FromArgb(0xBA, 0xBA, 0xBA);
            var attributeValue = Color.FromArgb(0x6A, 0x87, 0x59);

            var packageReferenceBuilder = new StringBuilder();

            if (isMSBuildSdk)
            {
                packageReferenceBuilder.Append("<Project".Color(tagName));
                packageReferenceBuilder.Append(' ');
                packageReferenceBuilder.Append("Sdk".Color(attributeName));
                packageReferenceBuilder.Append($"=\"{manifest.Metadata.Id}/{manifest.Metadata.Version}\"".Color(attributeValue));
                packageReferenceBuilder.Append(">".Color(tagName));

                packageReferenceBuilder.AppendLine();
                packageReferenceBuilder.AppendLine("or");

                packageReferenceBuilder.Append("<Sdk".Color(tagName));
                packageReferenceBuilder.Append(' ');
                packageReferenceBuilder.Append("Name".Color(attributeName) + $"=\"{manifest.Metadata.Id}\"".Color(attributeValue));
                packageReferenceBuilder.Append(' ');
                packageReferenceBuilder.Append("Version".Color(attributeName) + $"=\"{manifest.Metadata.Version}\"".Color(attributeValue));
                packageReferenceBuilder.Append(" />".Color(tagName));
            }
            else
            {
                packageReferenceBuilder.Append("<PackageReference".Color(tagName));
                packageReferenceBuilder.Append(' ');
                packageReferenceBuilder.Append("Include".Color(attributeName) + $"=\"{manifest.Metadata.Id}\"".Color(attributeValue));
                packageReferenceBuilder.Append(' ');
                packageReferenceBuilder.Append("Version".Color(attributeName) + $"=\"{manifest.Metadata.Version}\"".Color(attributeValue));
                packageReferenceBuilder.Append(' ');

                if (string.Equals(projectItem["DevelopmentDependency"], "true", StringComparison.OrdinalIgnoreCase))
                {
                    packageReferenceBuilder.Append("PrivateAssets".Color(attributeName) + "=\"all\"".Color(attributeValue));
                    packageReferenceBuilder.Append(' ');
                }

                packageReferenceBuilder.Append("/>".Color(tagName));
            }

            Console.WriteLine(packageReferenceBuilder.ToString());
        }

        return true;
    }

    private static string? GetDotnetHomePath()
    {
        var home = Environment.GetEnvironmentVariable("DOTNET_CLI_HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME");
            if (string.IsNullOrEmpty(home))
            {
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(home))
                {
                    return null;
                }
            }
        }

        return home;
    }

    private void CreateLink(string path, string pathToTarget, bool symbolic = true)
    {
        if (_copy)
        {
            if (File.Exists(path)) File.Delete(path);
            File.Copy(pathToTarget, path);
            Console.WriteLine($"Copied {path.TrimCurrentDirectory().Cyan()} to {pathToTarget.TrimCurrentDirectory().Cyan()})");
        }
        else
        {
            FileUtilities.CreateLink(path, pathToTarget, symbolic);
        }
    }

    public static async Task<int> RunAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        return await new LinkCommand(parseResult).ExecuteAsync(cancellationToken);
    }
}
