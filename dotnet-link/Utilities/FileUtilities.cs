// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

namespace DotNetLink.Utilities;

internal static partial class FileUtilities
{
    public static string GetProjectOrSolution(string path)
    {
        if (Directory.Exists(path))
        {
            var possibleSolutionPath = Directory.GetFiles(path).Where(IsSolutionFilename).ToArray();

            if (possibleSolutionPath.Length > 1)
            {
                throw new GracefulException($"Found more than one solution file in {path}. Specify which one to use.");
            }

            if (possibleSolutionPath.Length == 1)
            {
                return possibleSolutionPath[0];
            }

            var possibleProjectPath = Directory.GetFiles(path, "*.*proj", SearchOption.TopDirectoryOnly)
                .Where(static path => !path.EndsWith(".xproj", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (possibleProjectPath.Length == 0)
            {
                throw new GracefulException($"A project or solution file could not be found in {path}. Specify a project or solution file to use.");
            }

            if (possibleProjectPath.Length == 1)
            {
                return possibleProjectPath[0];
            }

            throw new GracefulException($"Found more than one project in `{path}`. Specify which one to use.");
        }

        if (!File.Exists(path))
        {
            throw new GracefulException($"File `{path}` not found.");
        }

        return path;
    }

    public static bool IsSolutionFilename(string filename)
    {
        return filename.EndsWith(".sln") || filename.EndsWith(".slnf") || filename.EndsWith(".slnx");
    }
}
