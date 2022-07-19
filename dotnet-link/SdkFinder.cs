using System.Reflection;

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

        SdkDirectory = Directory.GetDirectories(Path.Combine(dotnetExeDirectory, "sdk")).LastOrDefault(p => Path.GetFileName(p).StartsWith("6.0."));
        if (SdkDirectory == null)
        {
            return false;
        }

        Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", Path.Combine(SdkDirectory, "MSBuild.dll"));

        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var assemblyName = new AssemblyName(e.Name);
            var path = Path.Combine(SdkDirectory, $"{assemblyName.Name}.dll");

            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };

        return true;
    }
}
