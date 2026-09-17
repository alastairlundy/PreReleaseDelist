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
using PreReleaseDelistCli;
using TUnit.Core;
using TUnit.FsCheck;

namespace PreReleaseDelistLib.FuzzTests;

public class VersionFilterProperties
{
    [Test, FsCheckProperty]
    public bool ParseVersions_NonStrict_NeverThrows(string[] versions)
    {
        try
        {
            DelistCommand.ParseVersions(versions, throwOnError: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Test, FsCheckProperty]
    public bool ParseVersions_NonStrict_NoDuplicates(string[] versions)
    {
        IList<NuGetVersion> result = DelistCommand.ParseVersions(versions, throwOnError: false);

        return result.Count == result.Distinct().Count();
    }

    [Test, FsCheckProperty]
    public bool ParseVersions_Strict_ThrowsOnInvalidInput(string[] versions)
    {
        bool hasUnparseableVersion = versions.Any(s =>
            !NuGetVersion.TryParse(s, out _));

        try
        {
            DelistCommand.ParseVersions(versions, throwOnError: true);

            return !hasUnparseableVersion;
        }
        catch (ArgumentException)
        {
            return hasUnparseableVersion;
        }
    }

    [Test, FsCheckProperty]
    public bool ParseVersions_WhitespaceFiltered(string input)
    {
        string[] versions = [input];

        try
        {
            IList<NuGetVersion> result = DelistCommand.ParseVersions(versions, throwOnError: false);

            return result.All(v => v.ToNormalizedString().Length > 0);
        }
        catch (IndexOutOfRangeException)
        {
            return string.IsNullOrWhiteSpace(input);
        }
    }
}
