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

public class NuGetVersionParsingProperties
{
    [Test, FsCheckProperty]
    public bool TryParse_RoundTrip(string input)
    {
        if (!NuGetVersion.TryParse(input, out NuGetVersion? version))
            return true;

        string normalized = version.ToNormalizedString();

        bool parsed = NuGetVersion.TryParse(normalized, out NuGetVersion? reparsed);

        return parsed && reparsed is not null && reparsed.Equals(version);
    }

    [Test, FsCheckProperty]
    public bool TryParse_NeverThrows(string input)
    {
        try
        {
            NuGetVersion.TryParse(input, out _);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Test, FsCheckProperty]
    public bool TryParse_OrderingStability(string input1, string input2)
    {
        if (!NuGetVersion.TryParse(input1, out NuGetVersion? v1))
            return true;
        if (!NuGetVersion.TryParse(input2, out NuGetVersion? v2))
            return true;

        VersionComparer comparer = new(VersionComparison.Default);

        int originalComparison = comparer.Compare(v1, v2);

        NuGetVersion.TryParse(v1.ToNormalizedString(), out NuGetVersion? reparsed1);
        NuGetVersion.TryParse(v2.ToNormalizedString(), out NuGetVersion? reparsed2);

        int reparsedComparison = comparer.Compare(reparsed1!, reparsed2!);

        return originalComparison == reparsedComparison;
    }
}
