﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using System.Diagnostics;
using NuGet.Frameworks;
using System.Xml.Linq;
using System.IO.Compression;

namespace NuGet.ProjectManagement.Projects
{
    /// <summary>
    /// A NuGet integrated MSBuild project.
    /// These projects contain a project.json
    /// </summary>
    public class BuildIntegratedNuGetProject : NuGetProject, INuGetIntegratedProject
    {
        private readonly FileInfo _jsonConfig;
        private readonly IMSBuildNuGetProjectSystem _msbuildProjectSystem;

        public BuildIntegratedNuGetProject(string jsonConfig, IMSBuildNuGetProjectSystem msbuildProjectSystem)
        {
            if (jsonConfig == null)
            {
                throw new ArgumentNullException(nameof(jsonConfig));
            }

            _jsonConfig = new FileInfo(jsonConfig);
            _msbuildProjectSystem = msbuildProjectSystem;

            var json = GetJson();

            var targetFrameworks = JsonConfigUtility.GetFrameworks(json);

            // Default to unsupported if anything unexpected is returned
            NuGetFramework targetFramework = NuGetFramework.UnsupportedFramework;

            Debug.Assert(targetFrameworks.Count() == 1, "Invalid target framework count");

            if (targetFrameworks.Count() == 1)
            {
                targetFramework = targetFrameworks.First();
            }

            InternalMetadata.Add(NuGetProjectMetadataKeys.TargetFramework, targetFramework);

            var supported = new List<FrameworkName>()
            {
                new FrameworkName(targetFramework.DotNetFrameworkName)
            };

            InternalMetadata.Add(NuGetProjectMetadataKeys.SupportedFrameworks, supported);
        }

        public override async Task<IEnumerable<PackageReference>> GetInstalledPackagesAsync(CancellationToken token)
        {
            List<PackageReference> packages = new List<PackageReference>();

            //  Find all dependencies and convert them into packages.config style references
            foreach (var dependency in JsonConfigUtility.GetDependencies(await GetJsonAsync()))
            {
                // Use the minimum version of the range for the identity
                var identity = new PackageIdentity(dependency.Id, dependency.VersionRange.MinVersion);

                // Pass the actual version range as the allowed range
                // TODO: PackageReference needs to support this fully
                packages.Add(new PackageReference(identity, null, true, false, false, dependency.VersionRange));
            }

            return packages;
        }

        public override async Task<bool> InstallPackageAsync(PackageIdentity packageIdentity, Stream packageStream, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            var dependency = new PackageDependency(packageIdentity.Id, new VersionRange(packageIdentity.Version));

            return await AddDependency(dependency, nuGetProjectContext, token);
        }

        /// <summary>
        /// Install a package using the global packages folder.
        /// </summary>
        public async Task<bool> AddDependency(PackageDependency dependency,
            INuGetProjectContext nuGetProjectContext,
            CancellationToken token)
        {
            var json = await GetJsonAsync();

            JsonConfigUtility.AddDependency(json, dependency);

            await SaveJsonAsync(json);

            return true;
        }

        /// <summary>
        /// Uninstall a package from the config file.
        /// </summary>
        public async Task<bool> RemoveDependency(string packageId,
            INuGetProjectContext nuGetProjectContext,
            CancellationToken token)
        {
            var json = await GetJsonAsync();

            JsonConfigUtility.RemoveDependency(json, packageId);

            await SaveJsonAsync(json);

            return true;
        }

        public override async Task<bool> UninstallPackageAsync(PackageIdentity packageIdentity, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            return await RemoveDependency(packageIdentity.Id, nuGetProjectContext, token);
        }

        /// <summary>
        /// Add non-build time items such as content, install.ps1, and targets.
        /// </summary>
        public async Task<bool> InstallPackageContentAsync(PackageIdentity identity, Stream packageStream, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            bool changesMade = false;

            var zipArchive = new ZipArchive(packageStream);

            var reader = new PackageReader(zipArchive);

            var projectFramework = GetMetadata<NuGetFramework>(NuGetProjectMetadataKeys.TargetFramework);

            FrameworkSpecificGroup compatibleContentFilesGroup =
                MSBuildNuGetProjectSystemUtility.GetMostCompatibleGroup(projectFramework, reader.GetContentItems());

            // Step-8.3: Add Content Files
            if (MSBuildNuGetProjectSystemUtility.IsValid(compatibleContentFilesGroup))
            {
                changesMade = true;

                MSBuildNuGetProjectSystemUtility.AddFiles(MSBuildNuGetProjectSystem,
                    zipArchive, compatibleContentFilesGroup, FileTransformers);
            }

            return await Task.FromResult<bool>(changesMade);
        }

        public async Task<bool> UninstallPackageContentAsync(PackageIdentity identity, Stream packageStream, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            bool changesMade = false;

            var zipArchive = new ZipArchive(packageStream);

            var reader = new PackageReader(zipArchive);

            var projectFramework = GetMetadata<NuGetFramework>(NuGetProjectMetadataKeys.TargetFramework);

            FrameworkSpecificGroup compatibleContentFilesGroup =
                MSBuildNuGetProjectSystemUtility.GetMostCompatibleGroup(projectFramework, reader.GetContentItems());

            if (MSBuildNuGetProjectSystemUtility.IsValid(compatibleContentFilesGroup))
            {
                changesMade = true;

                MSBuildNuGetProjectSystemUtility.DeleteFiles(MSBuildNuGetProjectSystem,
                        zipArchive,
                        Enumerable.Empty<string>(),
                        compatibleContentFilesGroup,
                        FileTransformers);
            }

            return await Task.FromResult<bool>(changesMade);
        }

        /// <summary>
        /// nuget.json path
        /// </summary>
        public string JsonConfigPath
        {
            get
            {
                return _jsonConfig.FullName;
            }
        }

        /// <summary>
        /// The underlying msbuild project system
        /// </summary>
        public IMSBuildNuGetProjectSystem MSBuildNuGetProjectSystem
        {
            get
            {
                return _msbuildProjectSystem;
            }
        }

        private async Task<JObject> GetJsonAsync()
        {
            using (var streamReader = new StreamReader(_jsonConfig.OpenRead()))
            {
                return JObject.Parse(streamReader.ReadToEnd());
            }
        }

        private JObject GetJson()
        {
            using (var streamReader = new StreamReader(_jsonConfig.OpenRead()))
            {
                return JObject.Parse(streamReader.ReadToEnd());
            }
        }

        private async Task SaveJsonAsync(JObject json)
        {
            using (var writer = new StreamWriter(_jsonConfig.FullName, false, Encoding.UTF8))
            {
                writer.Write(json.ToString());
            }
        }

        private readonly IDictionary<FileTransformExtensions, IPackageFileTransformer> FileTransformers =
            new Dictionary<FileTransformExtensions, IPackageFileTransformer>()
        {
                    { new FileTransformExtensions(".transform", ".transform"), new XmlTransformer(GetConfigMappings()) },
                    { new FileTransformExtensions(".pp", ".pp"), new Preprocessor() },
                    { new FileTransformExtensions(".install.xdt", ".uninstall.xdt"), new XdtTransformer() }
        };

        private static IDictionary<XName, Action<XElement, XElement>> GetConfigMappings()
        {
            // REVIEW: This might be an edge case, but we're setting this rule for all xml files.
            // If someone happens to do a transform where the xml file has a configSections node
            // we will add it first. This is probably fine, but this is a config specific scenario
            return new Dictionary<XName, Action<XElement, XElement>>() {
                { "configSections" , (parent, element) => parent.AddFirst(element) }
            };
        }
    }
}
