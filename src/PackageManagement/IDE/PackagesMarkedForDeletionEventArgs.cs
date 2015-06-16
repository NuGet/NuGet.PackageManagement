// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace NuGet.PackageManagement
{
    public class PackagesMarkedForDeletionEventArgs : EventArgs
    {
        public IReadOnlyList<string> MarkedForDeletion { get; }

        public PackagesMarkedForDeletionEventArgs(IReadOnlyList<string> markedForDeletion)
        {
            MarkedForDeletion = markedForDeletion;
        }
    }
}