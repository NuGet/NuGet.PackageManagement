﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement.Projects;
using NuGet.ProjectModel;

namespace NuGet.ProjectManagement
{
    /// <summary>
    /// Utilities for project.json
    /// </summary>
    public static class BuildIntegratedProjectUtility
    {
        /// <summary>
        /// project.json
        /// </summary>
        public const string ProjectConfigFileName = "project.json";

        /// <summary>
        /// Lock file name
        /// </summary>
        public const string ProjectLockFileName = "project.lock.json";

        /// <summary>
        /// Get the root path of a package from the global folder.
        /// </summary>
        public static string GetPackagePathFromGlobalSource(PackageIdentity identity)
        {
            var pathResolver = new VersionFolderPathResolver(GetGlobalPackagesFolder());
            return pathResolver.GetInstallPath(identity.Id, identity.Version);
        }

        /// <summary>
        /// nupkg path from the global cache folder
        /// </summary>
        public static string GetNupkgPathFromGlobalSource(PackageIdentity identity)
        {
            var pathResolver = new VersionFolderPathResolver(GetGlobalPackagesFolder());
            return pathResolver.GetPackageFileName(identity.Id, identity.Version);
        }

        /// <summary>
        /// Global package folder path
        /// </summary>
        public static string GetGlobalPackagesFolder()
        {
            var path = Environment.GetEnvironmentVariable("NUGET_PACKAGES");

            if (string.IsNullOrEmpty(path))
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                path = Path.Combine(userProfile, ".nuget\\packages\\");
            }

            return path;
        }

        /// <summary>
        /// Create the lock file path from the config file path.
        /// </summary>
        public static string GetLockFilePath(string configFilePath)
        {
            return Path.Combine(Path.GetDirectoryName(configFilePath), ProjectLockFileName);
        }

        /// <summary>
        /// BuildIntegratedProjectReference -> ExternalProjectReference
        /// </summary>
        public static ExternalProjectReference ConvertProjectReference(BuildIntegratedProjectReference reference)
        {
            return new ExternalProjectReference(reference.Name, reference.PackageSpecPath, reference.ExternalProjectReferences);
        }

        public static IReadOnlyList<PackageIdentity> GetOrderedProjectDependencies(BuildIntegratedNuGetProject buildIntegratedProject)
        {
            var results = new List<PackageIdentity>();

            var lockFilePath = BuildIntegratedProjectUtility.GetLockFilePath(buildIntegratedProject.JsonConfigPath);
            var lockFileFormat = new LockFileFormat();

            // Read the lock file to find the full closure of dependencies
            if (File.Exists(lockFilePath))
            {
                var lockFile = lockFileFormat.Read(lockFilePath);

                var dependencies = new HashSet<PackageDependencyInfo>(PackageIdentity.Comparer);

                foreach (var target in lockFile.Targets)
                {
                    foreach (var targetLibrary in target.Libraries)
                    {
                        var identity = new PackageIdentity(targetLibrary.Name, targetLibrary.Version);
                        var dependency = new PackageDependencyInfo(identity, targetLibrary.Dependencies);
                        dependencies.Add(dependency);
                    }
                }

                // Sort dependencies
                var sortedDependencies = SortPackagesByDependencyOrder(dependencies);
                results.AddRange(sortedDependencies);
            }

            return results;
        }

        /// <summary>
        /// Order dependencies by children first.
        /// </summary>
        private static IReadOnlyList<PackageDependencyInfo> SortPackagesByDependencyOrder(IEnumerable<PackageDependencyInfo> packages)
        {
            var sorted = new List<PackageDependencyInfo>();
            var toSort = packages.Distinct().ToList();

            while (toSort.Count > 0)
            {
                // Order packages by parent count, take the child with the lowest number of parents and remove it from the list
                var nextPackage = toSort.OrderBy(package => GetParentCount(toSort, package.Id))
                    .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase).First();

                sorted.Add(nextPackage);
                toSort.Remove(nextPackage);
            }

            // the list is ordered by parents first, reverse to run children first
            sorted.Reverse();

            return sorted;
        }

        private static int GetParentCount(List<PackageDependencyInfo> packages, string id)
        {
            int count = 0;

            foreach (var package in packages)
            {
                if (package.Dependencies != null
                    && package.Dependencies.Any(dependency => string.Equals(id, dependency.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
