// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

using System.Reflection;

namespace DotNetLink;

internal static class SdkFinder
{
    public static string? SdkDirectory { get; private set; }

    public const string TargetVersion =
#if NET9_0
            "9.0"
#elif NET8_0
            "8.0"
#elif NET7_0
            "7.0"
#endif
        ;

    public static bool Initialize()
    {
        var dotnetExe = GetDotnetExe();
        if (dotnetExe == null)
        {
            return false;
        }

        Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", dotnetExe);

        var dotnetExeDirectory = Path.GetDirectoryName(dotnetExe)!;

        SdkDirectory = Directory.GetDirectories(Path.Combine(dotnetExeDirectory, "sdk")).LastOrDefault(p => Path.GetFileName(p).StartsWith($"{TargetVersion}."));
        if (SdkDirectory == null)
        {
            return false;
        }

        Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", Path.Combine(SdkDirectory, "MSBuild.dll"));
        Environment.SetEnvironmentVariable("MSBuildExtensionsPath", SdkDirectory);
        Environment.SetEnvironmentVariable("MSBuildSDKsPath", Path.Combine(SdkDirectory, "Sdks"));

        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var assemblyName = new AssemblyName(e.Name);
            var path = Path.Combine(SdkDirectory, $"{assemblyName.Name}.dll");

            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };

        return true;
    }

    private static string? GetDotnetExe()
    {
        var environmentOverride = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(environmentOverride))
        {
            return environmentOverride;
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) && Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var dotnetExeFromPath = GetCommandPath("dotnet");
        if (!string.IsNullOrWhiteSpace(dotnetExeFromPath))
        {
            return File.ResolveLinkTarget(dotnetExeFromPath, true)?.FullName ?? dotnetExeFromPath;
        }

        return null;
    }

    private static readonly string s_exeSuffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;

    private static string? GetCommandPath(string commandName)
    {
        var commandNameWithExtension = commandName + s_exeSuffix;

        var searchPaths = Environment.GetEnvironmentVariable("PATH")!
            .Split(Path.PathSeparator, options: StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim('"'))
            .Where(p => p.IndexOfAny(Path.GetInvalidPathChars()) == -1)
            .ToList();

        var commandPath = searchPaths
            .Select(p => Path.Combine(p, commandNameWithExtension))
            .FirstOrDefault(File.Exists);

        return commandPath;
    }
}
