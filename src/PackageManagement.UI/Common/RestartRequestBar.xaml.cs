﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell.Interop;
using NuGet.Packaging;
using NuGet.ProjectManagement;
using VsBrushes = Microsoft.VisualStudio.Shell.VsBrushes;

namespace NuGet.PackageManagement.UI
{
    /// <summary>
    /// Interaction logic for RestartRequestBar.xaml
    /// </summary>
    public partial class RestartRequestBar : UserControl, INuGetProjectContext
    {

        private readonly IDeleteOnRestartManager _deleteOnRestartManager;

        private readonly IVsShell4 _vsRestarter;

        public PackageExtractionContext PackageExtractionContext { get; set; }

        public ISourceControlManagerProvider SourceControlManagerProvider { get; }

        public ProjectManagement.ExecutionContext ExecutionContext { get; }

        public RestartRequestBar(IDeleteOnRestartManager deleteOnRestartManager, IVsShell4 vsRestarter)
        {
            InitializeComponent();
            _deleteOnRestartManager = deleteOnRestartManager;
            _vsRestarter = vsRestarter;

            // Set DynamicResource binding in code
            // The reason we can't set it in XAML is that the VsBrushes class come from either
            // Microsoft.VisualStudio.Shell.10 or Microsoft.VisualStudio.Shell.11 assembly,
            // depending on whether NuGet runs inside VS10 or VS11.
            StatusMessage.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.InfoTextKey);
            RestartBar.SetResourceReference(Border.BackgroundProperty, VsBrushes.InfoBackgroundKey);
            RestartBar.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ActiveBorderKey);
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            CheckForUnsuccessfulUninstall();
        }

        private void CheckForUnsuccessfulUninstall()
        {
            var packageDirectoriesMarkedForDeletion = _deleteOnRestartManager.GetPackageDirectoriesMarkedForDeletion();
            if (packageDirectoriesMarkedForDeletion != null && packageDirectoriesMarkedForDeletion.Count != 0)
            {
                var message = String.Format(
                    CultureInfo.CurrentCulture,
                    UI.Resources.RequestRestartToCompleteUninstall,
                    string.Join(", ", packageDirectoriesMarkedForDeletion));
                RequestRestartMessage.Text = message;
                RestartBar.Visibility = Visibility.Visible;
            }
        }

        private void ExecuteRestart(object sender, EventArgs e)
        {
            _vsRestarter.Restart((uint)__VSRESTARTTYPE.RESTART_Normal);
        }

        public void Log(MessageLevel level, string message, params object[] args)
        {
            ShowMessage(String.Format(CultureInfo.CurrentCulture, message, args));
        }

        public void ReportError(string message)
        {
            ShowMessage(message);
        }

        public FileConflictAction ResolveFileConflict(string message)
        {
            return FileConflictAction.IgnoreAll;
        }

        private void ShowMessage(string message)
        {
            NuGetUIThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                StatusMessage.Text = message;
            });
        }
    }
}
