﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using NuGet.Common;

namespace NuGet.CommandLine
{
    public static class MsBuildUtility
    {
        private const string GetProjectReferenceBuildTarget = "GetProjectsReferencingProjectJson";
        private const string ProjectReferencesKey = "ProjectReferences";
        private static readonly HashSet<string> _msbuildExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".csproj",
            ".vbproj",
            ".fsproj",
        };

        public static bool IsMsBuildBasedProject(string projectFullPath)
        {
            return _msbuildExtensions.Contains(Path.GetExtension(projectFullPath));
        }

        public static IEnumerable<string> GetProjectReferences(string msbuildPath, string projectFullPath)
        {
            if (string.IsNullOrEmpty(msbuildPath))
            {
                msbuildPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "msbuild",
                    "14.0",
                    "bin",
                    "msbuild.exe");
            }

            if (!File.Exists(msbuildPath))
            {
                throw new CommandLineException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        NuGetResources.MsBuildDoesNotExistAtPath,
                        msbuildPath));
            }

            var targetPath = Path.GetTempFileName();
            using (var input = typeof(MsBuildUtility).Assembly.GetManifestResourceStream("NuGet.CommandLine.GetProjectsReferencingProjectJsonFiles.target"))
            {
                using (var output = File.OpenWrite(targetPath))
                {
                    input.CopyTo(output);
                }
            }

            var resultsPath = Path.GetTempFileName();
            var arguments = $"/p:BuildProjectReferences=false /v:M /p:CustomAfterMicrosoftCommonTargets={targetPath} " +
                "/t:NuGet_GetProjectsReferencingProjectJson " +
                $"/nologo /nr:false /p:ResultsFile={resultsPath} {projectFullPath}";

            var processStartInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                FileName = msbuildPath,
                Arguments = arguments,
                RedirectStandardError = true
            };
            try
            {
                using (var process = Process.Start(processStartInfo))
                {
                    process.WaitForExit(60 * 1000);

                    if (process.ExitCode != 0)
                    {
                        throw new CommandLineException(process.StandardError.ReadToEnd());
                    }
                }

                if (File.Exists(resultsPath))
                {
                    return File.ReadAllLines(resultsPath)
                        .Where(file => !string.Equals(projectFullPath, file, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                }
            }
            finally
            {
                File.Delete(targetPath);
                File.Delete(resultsPath);
            }

            return Enumerable.Empty<string>();
        }

        public static IEnumerable<string> GetAllProjectFileNames(string solutionFile)
        {
            var solution = new Solution(solutionFile);
            var solutionDirectory = Path.GetDirectoryName(solutionFile);
            return solution.Projects.Where(project => !project.IsSolutionFolder)
                .Select(project => Path.Combine(solutionDirectory, project.RelativePath));
        }
    }
}
