// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

using System.CommandLine;
using System.Drawing;
using System.Text;
using DotNetLink.Utilities;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace DotNetLink;

internal sealed class LinkCommand
{
    private readonly IEnumerable<string> _slnOrProjectArgument;
    private readonly IEnumerable<string> _additionalArguments;
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

        foreach (var slnOrProject in projects)
        {
            var exitCode = await LinkAsync(slnOrProject, cancellationToken);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        return 0;
    }

    private async Task<int> LinkAsync(string projectPath, CancellationToken cancellationToken)
    {
        var isPackable = await DotnetBuildCommand.GetPropertyAsync(projectPath, "IsPackable", additionalArguments: _additionalArguments);
        if (!string.Equals(isPackable, "true", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Skipping {projectPath.TrimCurrentDirectory().Cyan()} because it's not packable");
            return 0;
        }

        var projectDirectory = Path.GetDirectoryName(projectPath)!;

        Console.WriteLine($"Linking {projectPath.TrimCurrentDirectory().Cyan()}");

        var result = await DotnetBuildCommand.ExecuteAsync(
            projectPath,
            restore: !_noBuild,
            targets: _noBuild ? ["GenerateNuspec"] : ["Build", "Pack"],
            properties: new Dictionary<string, string>
            {
                // Needed for nugetizer
                ["EmitNuspec"] = "true",
            },
            getProperty:
            [
                "IsNuGetized",
                "NuspecOutputPath",
                "NuspecPackageId",
                "PackageId",
                "PackageVersion",
                "NuspecFile",
                "DevelopmentDependency",
                "ToolCommandName",
                "RunCommand",
            ],
            additionalArguments: _additionalArguments
        );

        var properties = result!.Properties!;

        var isNuGetized = properties["IsNuGetized"].Equals("true", StringComparison.OrdinalIgnoreCase);

        if (isNuGetized)
        {
            Console.WriteLine("The project is using nugetizer".Yellow());
        }

        string nuspecPath;

        if (!isNuGetized)
        {
            var nuspecOutputPath = Path.Combine(projectDirectory, properties["NuspecOutputPath"]).Replace('\\', '/');

            var packageId = properties["NuspecPackageId"];
            if (string.IsNullOrEmpty(packageId))
            {
                packageId = properties["PackageId"];
            }

            var packageVersion = NuGetVersion.Parse(properties["PackageVersion"]);

            nuspecPath = Path.Combine(nuspecOutputPath, $"{packageId}.{packageVersion.ToNormalizedString()}.nuspec");
        }
        else
        {
            nuspecPath = properties["NuspecFile"];
        }

        if (!File.Exists(nuspecPath))
        {
            Console.WriteLine($"{nuspecPath.TrimCurrentDirectory().Cyan()} doesn't exist".Red());
            return 1;
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

            var toolCommandName = properties["ToolCommandName"];
            var targetExecutablePath = properties["RunCommand"]; // TODO figure out if there is a better way to get apphost path

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

                if (properties["DevelopmentDependency"]?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false)
                {
                    packageReferenceBuilder.Append("PrivateAssets".Color(attributeName) + "=\"all\"".Color(attributeValue));
                    packageReferenceBuilder.Append(' ');
                }

                packageReferenceBuilder.Append("/>".Color(tagName));
            }

            Console.WriteLine(packageReferenceBuilder.ToString());
        }

        return 0;
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
