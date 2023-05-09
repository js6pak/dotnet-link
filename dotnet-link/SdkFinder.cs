using System.Reflection;
using System.Runtime.InteropServices;

namespace DotNetLink;

internal static class SdkFinder
{
    public static string? SdkDirectory { get; private set; }

    public static bool Initialize()
    {
        var dotnetExeDirectory = Microsoft.DotNet.NativeWrapper.EnvironmentProvider.GetDotnetExeDirectory();
        if (dotnetExeDirectory == null)
        {
            return false;
        }

        var exeSuffix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : string.Empty;
        Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", Path.Combine(dotnetExeDirectory, "dotnet" + exeSuffix));

        SdkDirectory = Directory.GetDirectories(Path.Combine(dotnetExeDirectory, "sdk")).LastOrDefault(p => Path.GetFileName(p).StartsWith("7.0."));
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
}
