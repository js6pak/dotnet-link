using System.Diagnostics.CodeAnalysis;
using DotNetLink;
using Microsoft.Build.Evaluation;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Configurer;
using Microsoft.DotNet.ShellShim;
using Microsoft.DotNet.ToolPackage;
using Microsoft.DotNet.Workloads.Workload.Restore;
using Microsoft.Extensions.EnvironmentAbstractions;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Spectre.Console.Cli;
using AnsiConsole = Spectre.Console.AnsiConsole;
using CommandContext = Spectre.Console.Cli.CommandContext;

if (!SdkFinder.Initialize())
{
    AnsiConsole.MarkupLine("[red]Failed to find a compatible .NET SDK\ndotnet-link requires 6.0.x sdk to be installed[/]");
    return 1;
}

var app = new CommandApp<LinkCommand>();
return app.Run(args);

internal sealed class LinkCommand : Command<LinkCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--project")]
        public string[] SlnOrProject { get; init; } = Array.Empty<string>();

        [CommandOption("-c|--configuration")]
        public string? Configuration { get; init; } = null;
    }

    private static Project LoadProject(string path, string? configuration)
    {
        var globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSBuildExtensionsPath"] = Path.GetDirectoryName(Environment.GetEnvironmentVariable(Constants.MSBUILD_EXE_PATH))!,
        };
        if (configuration != null) globalProperties["Configuration"] = configuration;

        return ProjectCollection.GlobalProjectCollection.LoadProject(path, globalProperties, null);
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        var allProjects = WorkloadRestoreCommand.DiscoverAllProjects(Directory.GetCurrentDirectory(), settings.SlnOrProject);

        if (allProjects == null || !allProjects.Any())
        {
            AnsiConsole.MarkupLine("[red]No projects found, you can specify one with --project[/]");
            return 1;
        }

        foreach (var projectPath in allProjects)
        {
            Project project;

            try
            {
                project = LoadProject(projectPath, settings.Configuration);
            }
            catch (Exception)
            {
                AnsiConsole.MarkupLineInterpolated($"Failed to load [cyan]{projectPath}[/]");
                continue;
            }

            AnsiConsole.MarkupLine($"Linking [cyan]{projectPath}[/]");
            Link(project);
        }

        return 0;
    }

    private static void Link(Project project)
    {
        var intermediateOutputPath = Path.Combine(project.DirectoryPath, project.GetPropertyValue("BaseIntermediateOutputPath")!).Replace('\\', '/');

        var nuspecOutputPath = Path.Combine(project.DirectoryPath, project.GetPropertyValue("NuspecOutputPath")!).Replace('\\', '/');

        var packageId = project.GetPropertyValue("PackageId")!;
        var packageVersion = NuGetVersion.Parse(project.GetPropertyValue("PackageVersion")!);

        var nuspecPath = Path.Combine(nuspecOutputPath, PackCommandRunner.GetOutputFileName(packageId, packageVersion, false, false, default));

        if (!File.Exists(nuspecPath))
        {
            AnsiConsole.MarkupLine($"[red]{nuspecPath} doesn't exist[/]");
            return;
        }

        AnsiConsole.MarkupLine($"Linking [cyan]{nuspecPath.TrimCurrentDirectory()}[/]");

        using var fileStream = File.OpenRead(nuspecPath);
        var manifest = Manifest.ReadFrom(fileStream, true);

        var isTool = false;

        foreach (var packageType in manifest.Metadata.PackageTypes)
        {
            if (packageType == PackageType.DotnetTool)
            {
                isTool = true;
            }
            else
            {
                throw new NotImplementedException($"Package type {packageType.Name} is not supported yet");
            }
        }

        var id = manifest.Metadata.Id.ToLower();

        if (isTool)
        {
            var packagePath = Path.Combine(CliFolderPathCalculator.ToolsPackagePath, id);
            if (Directory.Exists(packagePath))
            {
                Directory.Delete(packagePath);
            }

            var toolConfiguration = ToolConfigurationDeserializer.Deserialize(Path.Combine(intermediateOutputPath, ToolPackageInstance.ToolSettingsFileName));
            var toolCommandName = new ToolCommandName(toolConfiguration.CommandName);

            var targetExecutablePath = manifest.Files.Single(f => f.Target.EndsWith(toolConfiguration.ToolAssemblyEntryPoint)).Source!;

            var repo = ShellShimRepositoryFactory.CreateShellShimRepository(Path.Combine(SdkFinder.SdkDirectory!, "AppHostTemplate"));

            repo.RemoveShim(toolCommandName);
            repo.CreateShim(new FilePath(targetExecutablePath), toolCommandName);

            AnsiConsole.MarkupLine($"Created a shim [cyan]{Path.Combine(CliFolderPathCalculator.ToolsShimPath, toolConfiguration.CommandName).TrimCurrentDirectory()}[/] to [cyan]{targetExecutablePath}[/]");
        }
        else
        {
            var packagesPath = Path.Combine(NuGetEnvironment.GetFolderPath(NuGetFolderPath.NuGetHome), "packages");

            var packagePath = Path.Combine(packagesPath, id, manifest.Metadata.Version.ToNormalizedString().ToLower());
            Directory.CreateDirectory(packagePath);

            File.WriteAllText(Path.Combine(packagePath, PackagingCoreConstants.NupkgMetadataFileExtension), "{ \"version\": 2, \"contentHash\": null, \"source\": null }");
            Extensions.CreateLink(Path.Combine(packagePath, id + PackagingCoreConstants.NuspecExtension), nuspecPath);

            foreach (var manifestFile in manifest.Files)
            {
                var source = Path.Combine(packagePath, manifestFile.Target);
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                Extensions.CreateLink(source, manifestFile.Source, !manifestFile.Target.StartsWith("lib/"));
            }

            const string tagName = "#E8BF6A";
            const string attributeName = "#BABABA";
            const string attributeValue = "#6A8759";
            AnsiConsole.MarkupLine($"[{tagName}]<PackageReference[/] [{attributeName}]Include[/][{attributeValue}]=\"{manifest.Metadata.Id}\"[/] [{attributeName}]Version[/][{attributeValue}]=\"{manifest.Metadata.Version}\"[/] [{tagName}]/>[/]");
        }
    }
}
