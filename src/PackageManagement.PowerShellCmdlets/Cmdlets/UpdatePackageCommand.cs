﻿using NuGet.Client;
using NuGet.Client.VisualStudio;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.PackagingCore;
using NuGet.ProjectManagement;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace NuGet.PackageManagement.PowerShellCmdlets
{
    [Cmdlet(VerbsData.Update, "Package", DefaultParameterSetName = "All")]
    public class UpdatePackageCommand : PackageActionBaseCommand
    {
        private ResolutionContext _context;
        private string _id;
        private string _projectName;
        private bool _idSpecified;
        private bool _projectSpecified;

        public UpdatePackageCommand(
            Lazy<INuGetResourceProvider, INuGetResourceProviderMetadata>[] resourceProvider,
            ISolutionManager solutionManager)
            : base(resourceProvider, solutionManager)
        {
        }

        [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true, Position = 0, ParameterSetName = "Project")]
        [Parameter(ValueFromPipelineByPropertyName = true, Position = 0, ParameterSetName = "All")]
        [Parameter(ValueFromPipelineByPropertyName = true, Position = 0, ParameterSetName = "Reinstall")]
        public override string Id
        {
            get
            {
                return _id;
            }
            set
            {
                _id = value;
                _idSpecified = true;
            }
        }

        [Parameter(Position = 1, ValueFromPipelineByPropertyName = true, ParameterSetName = "All")]
        [Parameter(Position = 1, ValueFromPipelineByPropertyName = true, ParameterSetName = "Project")]
        [Parameter(Position = 1, ValueFromPipelineByPropertyName = true, ParameterSetName = "Reinstall")]
        public override string ProjectName
        {
            get
            {
                return _projectName;
            }
            set
            {
                _projectName = value;
                _projectSpecified = true;
            }
        }

        [Parameter(Position = 2, ParameterSetName = "Project")]
        [ValidateNotNullOrEmpty]
        public override string Version { get; set; }

        [Parameter]
        public SwitchParameter Safe { get; set; }

        [Parameter(Mandatory = true, ParameterSetName = "Reinstall")]
        [Parameter(ParameterSetName = "All")]
        public SwitchParameter Reinstall { get; set; }

        public List<NuGetProject> Projects;

        protected override void Preprocess()
        {
            base.Preprocess();
            if (_projectSpecified)
            {
                Projects = VSSolutionManager.GetProjects().ToList();
            }
            else
            {
                Projects = new List<NuGetProject>() { Project };
            }
        }

        protected override void ProcessRecordCore()
        {
            base.ProcessRecordCore();

            SubscribeToProgressEvents();
            if (!Reinstall.IsPresent)
            {
                PerformPackageUpdates();
            }
            else
            {
                PerformPackageReinstalls();
            }
            UnsubscribeFromProgressEvents();
        }

        private void PerformPackageUpdates()
        {
            // Update All
            if (!_idSpecified)
            {
                foreach (NuGetProject project in Projects)
                {
                    string framework = project.GetMetadata<NuGetFramework>(NuGetProjectMetadataKeys.TargetFramework).Framework;
                    IEnumerable<PackageReference> installedPackages = Project.GetInstalledPackages();
                    Dictionary<PSSearchMetadata, NuGetVersion> remoteUpdates = GetPackageUpdatesFromRemoteSource(installedPackages, new List<string> { framework }, IncludePrerelease.IsPresent);
                    ExecuteUpdates(remoteUpdates, project);
                }
            }
            else
            {
                foreach (NuGetProject project in Projects)
                {
                    string framework = project.GetMetadata<NuGetFramework>(NuGetProjectMetadataKeys.TargetFramework).Framework;
                    PackageReference installedPackage = Project.GetInstalledPackages().Where(p => string.Equals(p.PackageIdentity.Id, Id, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

                    // If package Id exists in Packages folder but is not actually installed to the current project, throw.
                    if (installedPackage == null)
                    {
                        WriteError(string.Format(Resources.PackageNotInstalledInAnyProject, Id));
                    }
                    else
                    {
                        List<PackageReference> installedPackages = new List<PackageReference>() { installedPackage };
                        if (!string.IsNullOrEmpty(Version))
                        {
                            NuGetVersion nVersion = PowerShellCmdletsUtility.GetNuGetVersionFromString(Version);
                            PackageIdentity update = new PackageIdentity(Id, nVersion);
                            InstallPackageByIdentity(project, update, ResolutionContext, this, WhatIf.IsPresent);
                        }
                        else
                        {
                            Dictionary<PSSearchMetadata, NuGetVersion> remoteUpdates = GetPackageUpdatesFromRemoteSource(installedPackages, new List<string> { framework }, IncludePrerelease.IsPresent);
                            ExecuteUpdates(remoteUpdates, project);
                        }
                    }
                }
            }
        }

        private void PerformPackageReinstalls()
        {
            // Reinstall All
            if (!_idSpecified)
            {
                foreach (NuGetProject project in Projects)
                {
                    IEnumerable<PackageReference> installedPackages = Project.GetInstalledPackages();
                    foreach (PackageReference package in installedPackages)
                    {
                        InstallPackageByIdentity(project, package.PackageIdentity, ResolutionContext, this, WhatIf.IsPresent, true);
                    }
                }
            }
            else
            {
                foreach (NuGetProject project in Projects)
                {
                    PackageReference installedPackage = Project.GetInstalledPackages().Where(p => string.Equals(p.PackageIdentity.Id, Id, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

                    // If package Id exists in Packages folder but is not actually installed to the current project, throw.
                    if (installedPackage == null)
                    {
                        WriteError(string.Format(Resources.PackageNotInstalledInAnyProject, Id));
                    }
                    else
                    {
                        InstallPackageByIdentity(project, installedPackage.PackageIdentity, ResolutionContext, this, WhatIf.IsPresent, true);
                    }
                }
            }
        }

        private void ExecuteUpdates(Dictionary<PSSearchMetadata, NuGetVersion> updates, NuGetProject nuGetProject)
        {
            foreach (KeyValuePair<PSSearchMetadata, NuGetVersion> entry in updates)
            {
                InstallPackageByIdentity(nuGetProject, entry.Key.Identity, ResolutionContext, this, WhatIf.IsPresent);
            }
        }

        /// <summary>
        /// Resolution Context for the command
        /// </summary>
        public ResolutionContext ResolutionContext
        {
            get
            {
                _context = new ResolutionContext(GetDependencyBehavior(), IncludePrerelease.IsPresent, false, Reinstall.IsPresent, false);
                return _context;
            }
        }
    }
}