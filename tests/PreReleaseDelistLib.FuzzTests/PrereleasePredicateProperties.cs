/*
    PreReleaseDelistLib.FuzzTests
    Copyright (C) 2026 Alastair Lundy

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
     any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using NuGet.Versioning;
using TUnit.Core;
using TUnit.FsCheck;

namespace PreReleaseDelistLib.FuzzTests;

public class PrereleasePredicateProperties
{
    [Test, FsCheckProperty]
    public bool IsTargetPrereleaseVersion_PrereleaseVersionsAlwaysIncluded(string input)
    {
        if (!NuGetVersion.TryParse(input, out NuGetVersion? version))
            return true;

        if (!version.IsPrerelease)
            return true;

        return PackageVersionService.IsTargetPrereleaseVersion(version, includeZeroMajorVersions: false);
    }

    [Test, FsCheckProperty]
    public bool IsTargetPrereleaseVersion_StableZeroMajor_OnlyIncludedWhenFlagSet(string input)
    {
        if (!NuGetVersion.TryParse(input, out NuGetVersion? version))
            return true;

        if (version.IsPrerelease || version.Major != 0)
            return true;

        bool withoutFlag = PackageVersionService.IsTargetPrereleaseVersion(version, includeZeroMajorVersions: false);
        bool withFlag = PackageVersionService.IsTargetPrereleaseVersion(version, includeZeroMajorVersions: true);

        return !withoutFlag && withFlag;
    }

    [Test, FsCheckProperty]
    public bool IsTargetPrereleaseVersion_StableNonZeroMajor_NeverIncluded(string input)
    {
        if (!NuGetVersion.TryParse(input, out NuGetVersion? version))
            return true;

        if (version.IsPrerelease || version.Major == 0)
            return true;

        return !PackageVersionService.IsTargetPrereleaseVersion(version, includeZeroMajorVersions: false)
            && !PackageVersionService.IsTargetPrereleaseVersion(version, includeZeroMajorVersions: true);
    }
}
