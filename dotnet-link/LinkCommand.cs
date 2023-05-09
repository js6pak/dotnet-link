using System.CommandLine;
using System.CommandLine.Parsing;
using System.Drawing;
using System.Text;
using Microsoft.Build.CommandLine;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Shared;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Configurer;
using Microsoft.DotNet.ShellShim;
using Microsoft.DotNet.ToolPackage;
using Microsoft.Extensions.EnvironmentAbstractions;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace DotNetLink;

public class LinkCommand : CommandBase
{
    private readonly IEnumerable<string>? _slnOrProjectArgument;

    private LinkCommand(ParseResult parseResult) : base(parseResult)
    {
        _slnOrProjectArgument = parseResult.GetValueForArgument(LinkCommandParser.SlnOrProjectArgument);
    }

    private static void ParseMSBuildArgs(IEnumerable<string> msbuildArgs, out Dictionary<string, string> globalProperties, out string projectFile)
    {
        var commandLine = msbuildArgs.Prepend(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath).ToArray();
        MSBuildApp.GatherAllSwitches(commandLine, out var switchesFromAutoResponseFile, out var switchesNotFromAutoResponseFile, out var fullCommandLine);

        var commandLineSwitches = MSBuildApp.CombineSwitchesRespectingPriority(switchesFromAutoResponseFile, switchesNotFromAutoResponseFile, fullCommandLine);
        globalProperties = MSBuildApp.ProcessPropertySwitch(commandLineSwitches[CommandLineSwitches.ParameterizedSwitch.Property]);

        projectFile = MSBuildApp.ProcessProjectSwitch(commandLineSwitches[CommandLineSwitches.ParameterizedSwitch.Project], commandLineSwitches[CommandLineSwitches.ParameterizedSwitch.IgnoreProjectExtensions], Directory.GetFiles);
    }

    private static int RunMSBuild(IEnumerable<string> args)
    {
        // https://github.com/dotnet/sdk/blob/v7.0.203/src/Cli/Microsoft.DotNet.Cli.Utils/MSBuildForwardingAppWithoutLogging.cs#L47-L48
        var requiredParameters = new[] { "-maxcpucount", "-verbosity:m" };

        return MSBuildApp.Main(requiredParameters.Concat(args).ToArray());
    }

    public override int Execute()
    {
        var msbuildArgs = new List<string>();

        msbuildArgs.AddRange(_parseResult.OptionValuesToBeForwarded(LinkCommandParser.GetCommand()));
        if (_slnOrProjectArgument != null) msbuildArgs.AddRange(_slnOrProjectArgument);
        msbuildArgs.Add("-target:Restore;Build;Pack");

        Dictionary<string, string> globalProperties;
        string projectFile;

        try
        {
            ParseMSBuildArgs(msbuildArgs, out globalProperties, out projectFile);
        }
        catch (InitializationException e)
        {
            Reporter.Error.WriteLine(e.Message.Red());
            return 1;
        }

        var noPack = _parseResult.HasOption(LinkCommandParser.NoPackOption);
        if (!noPack)
        {
            Reporter.Output.WriteLine("Building and packing first...");

            var exitCode = RunMSBuild(msbuildArgs);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        var allProjects = new List<string>();

        if (FileUtilities.IsSolutionFilename(projectFile))
        {
            var solutionFile = SolutionFile.Parse(Path.GetFullPath(projectFile));
            var projects = solutionFile.ProjectsInOrder.Where(p => p.ProjectType != SolutionProjectType.SolutionFolder);
            allProjects.AddRange(projects.Select(p => p.AbsolutePath));
        }
        else
        {
            allProjects.Add(projectFile);
        }

        foreach (var projectPath in allProjects)
        {
            Project project;

            try
            {
                project = new Project(projectPath, globalProperties, null);
            }
            catch (Exception)
            {
                Reporter.Error.WriteLine($"Failed to load {projectPath.Cyan()}".Red());
                continue;
            }

            var isPackable = project.GetPropertyValue("IsPackable");
            if (isPackable is not "true")
            {
                Reporter.Output.WriteLine($"Skipping {projectPath.Cyan()} because it's not packable");
                continue;
            }

            Reporter.Output.WriteLine($"Linking {projectPath.Cyan()}");
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
            Reporter.Error.WriteLine($"{nuspecPath.TrimCurrentDirectory().Cyan()} doesn't exist".Red());
            return;
        }

        Reporter.Output.WriteLine($"Linking {nuspecPath.TrimCurrentDirectory().Cyan()}");

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

            Reporter.Output.WriteLine($"Created a shim {Path.Combine(CliFolderPathCalculator.ToolsShimPath, toolConfiguration.CommandName).TrimCurrentDirectory().Cyan()} to {targetExecutablePath.Cyan()}");
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

            var tagName = Color.FromArgb(0xE8, 0xBF, 0x6A);
            var attributeName = Color.FromArgb(0xBA, 0xBA, 0xBA);
            var attributeValue = Color.FromArgb(0x6A, 0x87, 0x59);

            var packageReferenceBuilder = new StringBuilder();

            packageReferenceBuilder.Append("<PackageReference".Color(tagName));
            packageReferenceBuilder.Append(' ');
            packageReferenceBuilder.Append("Include".Color(attributeName) + $"=\"{manifest.Metadata.Id}\"".Color(attributeValue));
            packageReferenceBuilder.Append(' ');
            packageReferenceBuilder.Append("Version".Color(attributeName) + $"=\"{manifest.Metadata.Version}\"".Color(attributeValue));
            packageReferenceBuilder.Append(' ');

            if (project.GetPropertyValue("DevelopmentDependency")?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false)
            {
                packageReferenceBuilder.Append("PrivateAssets".Color(attributeName) + "=\"all\"".Color(attributeValue));
                packageReferenceBuilder.Append(' ');
            }

            packageReferenceBuilder.Append("/>".Color(tagName));

            Console.WriteLine(packageReferenceBuilder.ToString());
        }
    }

    public static int Run(ParseResult parseResult)
    {
        parseResult.HandleDebugSwitch();
        parseResult.ShowHelpOrErrorIfAppropriate();

        return new LinkCommand(parseResult).Execute();
    }
}
