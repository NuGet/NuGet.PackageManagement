﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;

namespace NuGet.ProjectManagement
{
    /// <summary>
    /// Represents a NuGet project as represented by packages.config
    /// </summary>
    public class PackagesConfigNuGetProject : NuGetProject
    {
        public string FullPath
        {
            get
            {
                if (UsingPackagesProjectNameConfigPath)
                {
                    return PackagesProjectNameConfigPath;
                }
                return PackagesConfigPath;
            }
        }

        private bool UsingPackagesProjectNameConfigPath { get; set; }

        /// <summary>
        /// Represents the full path to "packages.config"
        /// </summary>
        private string PackagesConfigPath { get; }

        /// <summary>
        /// Represents the full path to "packages.'projectName'.config"
        /// </summary>
        private string PackagesProjectNameConfigPath { get; }

        private NuGetFramework TargetFramework { get; }

        public PackagesConfigNuGetProject(string folderPath, IDictionary<string, object> metadata)
            : base(metadata)
        {
            if (folderPath == null)
            {
                throw new ArgumentNullException(nameof(folderPath));
            }

            TargetFramework = GetMetadata<NuGetFramework>(NuGetProjectMetadataKeys.TargetFramework);

            PackagesConfigPath = Path.Combine(folderPath, "packages.config");

            var projectName = GetMetadata<string>(NuGetProjectMetadataKeys.Name);
            PackagesProjectNameConfigPath = Path.Combine(folderPath, "packages." + projectName + ".config");
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        public override Task<bool> InstallPackageAsync(
            PackageIdentity packageIdentity,
            DownloadResourceResult downloadResourceResult,
            INuGetProjectContext nuGetProjectContext,
            CancellationToken token)
        {
            if (packageIdentity == null)
            {
                throw new ArgumentNullException("packageIdentity");
            }

            if (nuGetProjectContext == null)
            {
                throw new ArgumentNullException("nuGetProjectContext");
            }

            var isDevelopmentDependency = CheckDevelopmentDependency(downloadResourceResult);
            var newPackageReference = new PackageReference(packageIdentity, TargetFramework, userInstalled: true, developmentDependency: isDevelopmentDependency, requireReinstallation: false);
            var installedPackagesList = GetInstalledPackagesList();

            // Packages.config exist at full path
            if (installedPackagesList.Any())
            {
                var packageReferenceWithSameId = installedPackagesList.FirstOrDefault(
                    p => p.PackageIdentity.Id.Equals(packageIdentity.Id, StringComparison.OrdinalIgnoreCase));

                if (packageReferenceWithSameId != null)
                {
                    if (packageReferenceWithSameId.PackageIdentity.Equals(packageIdentity))
                    {
                        nuGetProjectContext.Log(MessageLevel.Warning, Strings.PackageAlreadyExistsInPackagesConfig, packageIdentity, Path.GetFileName(FullPath));
                        return Task.FromResult(false);
                    }

                    // Higher version of an installed package is being installed. Remove old and add new
                    using (var stream = FileSystemUtility.GetFileStream(FullPath))
                    {
                        var writer = new PackagesConfigWriter(stream, createNew: false);
                        writer.UpdatePackageEntry(packageReferenceWithSameId, newPackageReference);
                        writer.Close();
                    }
                }
                else
                {
                    using (var stream = FileSystemUtility.GetFileStream(FullPath))
                    {
                        var writer = new PackagesConfigWriter(stream, createNew: false);
                        writer.AddPackageEntry(newPackageReference);
                        writer.Close();
                    }
                }
            }
            // Create new packages.config file and add the package entry
            else
            {
                using (var stream = FileSystemUtility.CreateFile(FullPath, nuGetProjectContext))
                {
                    var writer = new PackagesConfigWriter(stream, createNew: true);
                    writer.AddPackageEntry(newPackageReference);
                    writer.Close();
                }
            }

            nuGetProjectContext.Log(MessageLevel.Info, Strings.AddedPackageToPackagesConfig, packageIdentity, Path.GetFileName(FullPath));
            return Task.FromResult(true);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        public override Task<bool> UninstallPackageAsync(PackageIdentity packageIdentity, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            if (packageIdentity == null)
            {
                throw new ArgumentNullException("packageIdentity");
            }

            if (nuGetProjectContext == null)
            {
                throw new ArgumentNullException("nuGetProjectContext");
            }

            var installedPackagesList = GetInstalledPackagesList();
            var packageReference = installedPackagesList.Where(p => p.PackageIdentity.Equals(packageIdentity)).FirstOrDefault();
            if (packageReference == null)
            {
                nuGetProjectContext.Log(MessageLevel.Warning, Strings.PackageDoesNotExisttInPackagesConfig, packageIdentity, Path.GetFileName(FullPath));
                return Task.FromResult(false);
            }

            if (installedPackagesList.Any())
            {
                // Remove the package reference from packages.config file
                using (var stream = FileSystemUtility.GetFileStream(FullPath))
                {
                    var writer = new PackagesConfigWriter(stream, createNew: false);
                    writer.RemovePackageEntry(packageReference);
                    writer.Close();
                }
            }
            else
            {
                FileSystemUtility.DeleteFile(FullPath, nuGetProjectContext);
            }
            nuGetProjectContext.Log(MessageLevel.Info, Strings.RemovedPackageFromPackagesConfig, packageIdentity, Path.GetFileName(FullPath));
            return Task.FromResult(true);
        }

        public override Task<IEnumerable<PackageReference>> GetInstalledPackagesAsync(CancellationToken token)
        {
            return Task.FromResult<IEnumerable<PackageReference>>(GetInstalledPackagesList());
        }

        private void UpdateFullPath()
        {
            if (UsingPackagesProjectNameConfigPath
                && !File.Exists(PackagesProjectNameConfigPath)
                && File.Exists(PackagesConfigPath))
            {
                UsingPackagesProjectNameConfigPath = false;
            }
            else if (!File.Exists(PackagesConfigPath)
                     && File.Exists(PackagesProjectNameConfigPath))
            {
                UsingPackagesProjectNameConfigPath = true;
            }
        }

        private List<PackageReference> GetInstalledPackagesList()
        {
            UpdateFullPath();
            if (File.Exists(FullPath))
            {
                try
                {
                    var reader = new PackagesConfigReader(XDocument.Load(FullPath));
                    return reader.GetPackages().ToList();
                }
                catch (Exception ex)
                {
                    if (ex is System.Xml.XmlException ||
                        ex is PackagesConfigReaderException)
                    {
                        throw new InvalidOperationException(string.Format(
                            CultureInfo.CurrentCulture,
                            Strings.ErrorLoadingPackagesConfig,
                            FullPath,
                            ex.Message));
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            return new List<PackageReference>();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Disposing the PackageReader will dispose the backing stream that we want to leave open.")]
        private static bool CheckDevelopmentDependency(DownloadResourceResult downloadResourceResult)
        {
            bool isDevelopmentDependency = false;

            // Catch any exceptions while fetching DevelopmentDependency element from nuspec file. 
            // So it can continue to write the packages.config file.
            try
            {
                if (downloadResourceResult.PackageReader != null)
                {
                    isDevelopmentDependency = downloadResourceResult.PackageReader.GetDevelopmentDependency();
                }
                else
                {
                    var packageZipArchive = new ZipArchive(downloadResourceResult.PackageStream, ZipArchiveMode.Read, leaveOpen: true);
                    var packageReader = new PackageReader(packageZipArchive);
                    var nuspecReader = new NuspecReader(packageReader.GetNuspec());
                    isDevelopmentDependency = nuspecReader.GetDevelopmentDependency();
                }
            }
            catch { }

            return isDevelopmentDependency;
        }
    }
}
