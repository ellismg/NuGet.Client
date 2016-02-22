// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.Versioning;
using NuGet.Frameworks;

namespace NuGet.VisualStudio.Implementation.Utility
{
    public static class FrameworkNameUtility
    {
        public static NuGetFramework GetNuGetFramework(FrameworkName frameworkName)
        {
            return NuGetFramework.ParseFrameworkName(frameworkName.ToString(), DefaultFrameworkNameProvider.Instance);
        }

        public static FrameworkName GetFrameworkName(NuGetFramework nuGetFramework)
        {
            return new FrameworkName(nuGetFramework.DotNetFrameworkName);
        }
    }
}
