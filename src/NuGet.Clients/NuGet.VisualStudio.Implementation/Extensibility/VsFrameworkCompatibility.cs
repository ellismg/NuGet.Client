// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.Versioning;
using NuGet.Frameworks;
using NuGet.VisualStudio.Implementation.Resources;
using NuGet.VisualStudio.Implementation.Utility;

namespace NuGet.VisualStudio
{
    [Export(typeof(IVsFrameworkCompatibility))]
    public class VsFrameworkCompatibility : IVsFrameworkCompatibility
    {
        public IEnumerable<FrameworkName> GetNetStandardFrameworks()
        {
            return DefaultFrameworkNameProvider
                .Instance
                .GetNetStandardVersions()
                .Select(FrameworkNameUtility.GetFrameworkName);
        }

        public IEnumerable<FrameworkName> GetFrameworksSupportingNetStandard(FrameworkName frameworkName)
        {
            if (frameworkName == null)
            {
                throw new ArgumentNullException(nameof(frameworkName));
            }

            var nuGetFramework = FrameworkNameUtility.GetNuGetFramework(frameworkName);

            if (!StringComparer.OrdinalIgnoreCase.Equals(
                nuGetFramework.Framework,
                FrameworkConstants.FrameworkIdentifiers.NetStandard))
            {
                throw new ArgumentException(string.Format(
                    VsResources.InvalidNetStandardFramework,
                    frameworkName));
            }

            return CompatibilityListProvider
                .Default
                .GetFrameworksSupporting(nuGetFramework)
                .Select(FrameworkNameUtility.GetFrameworkName);
        }

        public FrameworkName GetNearest(FrameworkName targetFramework, IEnumerable<FrameworkName> frameworks)
        {
            if (targetFramework == null)
            {
                throw new ArgumentNullException(nameof(targetFramework));
            }

            if (frameworks == null)
            {
                throw new ArgumentNullException(nameof(frameworks));
            }

            var nuGetTargetFramework = FrameworkNameUtility.GetNuGetFramework(targetFramework);
            var nuGetFrameworks = frameworks.Select(FrameworkNameUtility.GetNuGetFramework);
            
            var reducer = new FrameworkReducer();
            var nearest = reducer.GetNearest(nuGetTargetFramework, nuGetFrameworks);

            if (nearest == null)
            {
                return null;
            }

            return FrameworkNameUtility.GetFrameworkName(nearest);
        }
    }
}
