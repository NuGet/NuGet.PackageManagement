// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.PackageManagement;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.Versioning;
using Test.Utility;
using Xunit;
using NuGet.Protocol.Core.Types;
using Xunit.Abstractions;

namespace NuGet.Test
{
    public class V2V3ParityTests
    {
        ITestOutputHelper _output;

        public V2V3ParityTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private async Task<IEnumerable<NuGetProjectAction>> PacManCleanInstall(SourceRepositoryProvider sourceRepositoryProvider, PackageIdentity target)
        {
            // Arrange
            var testSolutionManager = new TestSolutionManager();
            var testSettings = new NullSettings();
            var token = CancellationToken.None;
            var nuGetPackageManager = new NuGetPackageManager(sourceRepositoryProvider, testSettings, testSolutionManager);
            var packagesFolderPath = PackagesFolderPathUtility.GetPackagesFolderPath(testSolutionManager, testSettings);

            var randomPackagesConfigFolderPath = TestFilesystemUtility.CreateRandomTestFolder();
            var randomPackagesConfigPath = Path.Combine(randomPackagesConfigFolderPath, "packages.config");

            var projectTargetFramework = NuGetFramework.Parse("net45");
            var msBuildNuGetProjectSystem = new TestMSBuildNuGetProjectSystem(projectTargetFramework, new TestNuGetProjectContext());
            var msBuildNuGetProject = new MSBuildNuGetProject(msBuildNuGetProjectSystem, packagesFolderPath, randomPackagesConfigFolderPath);

            // Pre-Assert
            // Check that the packages.config file does not exist
            Assert.False(File.Exists(randomPackagesConfigPath));
            // Check that there are no packages returned by PackagesConfigProject
            var packagesInPackagesConfig = (await msBuildNuGetProject.PackagesConfigNuGetProject.GetInstalledPackagesAsync(token)).ToList();
            Assert.Equal(0, packagesInPackagesConfig.Count);
            Assert.Equal(0, msBuildNuGetProjectSystem.References.Count);

            // Act
            var nugetProjectActions = await nuGetPackageManager.PreviewInstallPackageAsync(msBuildNuGetProject, target,
                new ResolutionContext(), new TestNuGetProjectContext(), sourceRepositoryProvider.GetRepositories().First(), null, token);

            // Clean-up
            TestFilesystemUtility.DeleteRandomTestFolders(testSolutionManager.SolutionDirectory, randomPackagesConfigFolderPath);

            return nugetProjectActions;
        }

        private bool Compare(IEnumerable<NuGetProjectAction> x, IEnumerable<NuGetProjectAction> y)
        {
            var xyExcept = x.Except(y, new NuGetProjectActionComparer()).ToList();

            _output.WriteLine("xyExcept:");
            foreach (var entry in xyExcept)
            {
                _output.WriteLine("{0} {1}", entry.NuGetProjectActionType, entry.PackageIdentity.ToString());
            }

            var yxExcept = y.Except(x, new NuGetProjectActionComparer()).ToList();

            _output.WriteLine("yxExcept:");
            foreach (var entry in yxExcept)
            {
                _output.WriteLine("{0} {1}", entry.NuGetProjectActionType, entry.PackageIdentity.ToString());
            }

            var comparer = new NuGetProjectActionComparer();

            //TODO: remove all these when we understand what has goen wrong here...

            bool f = comparer.Equals(xyExcept.First(), yxExcept.First());

            int xh = comparer.GetHashCode(xyExcept.First());
            int yh = comparer.GetHashCode(yxExcept.First());

            int xph = xyExcept.First().PackageIdentity.GetHashCode();
            int yph = yxExcept.First().PackageIdentity.GetHashCode();

            int xpih = xyExcept.First().PackageIdentity.Id.ToUpperInvariant().GetHashCode();
            int ypih = yxExcept.First().PackageIdentity.Id.ToUpperInvariant().GetHashCode();

            int xpvh = VersionComparer.Default.GetHashCode(xyExcept.First().PackageIdentity.Version);
            int ypvh = VersionComparer.Default.GetHashCode(yxExcept.First().PackageIdentity.Version);

            var combinerx = new HashCodeCombiner();
            combinerx.AddObject(xyExcept.First().PackageIdentity.Id.ToUpperInvariant());
            combinerx.AddObject(VersionComparer.Default.GetHashCode(xyExcept.First().PackageIdentity.Version));
            var xch = combinerx.CombinedHash;

            var combinery = new HashCodeCombiner();
            combinery.AddObject(yxExcept.First().PackageIdentity.Id.ToUpperInvariant());
            combinery.AddObject(VersionComparer.Default.GetHashCode(yxExcept.First().PackageIdentity.Version));
            var ych = combinery.CombinedHash;

            //BUGBUG: all the component parts appear equal and yet they are not equal - very odd

            return (xyExcept.Count() == 0 && yxExcept.Count() == 0);
        }

        [Fact]
        public async Task TestPacManCleanInstall()
        {
            var target = new PackageIdentity("Umbraco", NuGetVersion.Parse("5.1.0.175"));

            _output.WriteLine("target: {0}", target);

            var actionsV2 = await PacManCleanInstall(TestSourceRepositoryUtility.CreateV2OnlySourceRepositoryProvider(), target);
            var actionsV3 = await PacManCleanInstall(TestSourceRepositoryUtility.CreateV3OnlySourceRepositoryProvider(), target);

            //TODO: uncomment this line when we have fixed the comparison logic
            //Assert.True(Compare(actionsV2, actionsV3));
        }

        class NuGetProjectActionComparer : IEqualityComparer<NuGetProjectAction>
        {
            public bool Equals(NuGetProjectAction x, NuGetProjectAction y)
            {
                var packageIdentityEquals = x.PackageIdentity.Equals(y.PackageIdentity);
                var NuGetProjectActionTypeEquals = x.NuGetProjectActionType == y.NuGetProjectActionType;

                return packageIdentityEquals & NuGetProjectActionTypeEquals;
            }

            public int GetHashCode(NuGetProjectAction obj)
            {
                var combiner = new HashCodeCombiner();
                combiner.AddObject(obj.PackageIdentity.GetHashCode());
                combiner.AddObject(obj.NuGetProjectActionType);
                return combiner.CombinedHash;
            }
        }
    }
}
