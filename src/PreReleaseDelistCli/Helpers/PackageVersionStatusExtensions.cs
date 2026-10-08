/*
    prerelease-delist - Delist pre-release package versions from a Nuget Server
    Copyright (C) 2026 Alastair Lundy

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
     any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using PreReleaseDelistLib.Models;

namespace PreReleaseDelistCli.Helpers;

/// <summary>
/// The single CLI-side renderer for the closed <see cref="PackageVersionStatus"/> vocabulary (T018).
/// </summary>
/// <remarks>
/// Every member of the closed vocabulary is spelled out explicitly, here and at the exit-code mapping
/// site, so no output mode can silently render an unknown status (T004). C# requires a catch-all arm on
/// an enum switch expression (CS8524), so the unnamed arm throws instead of rendering: a member added to
/// the enum reaches it loudly and instructs the maintainer to add its kebab-case spelling (T004, T018).
/// </remarks>
internal static class PackageVersionStatusExtensions
{
    internal static string ToKebabCase(this PackageVersionStatus status) => status switch
    {
        PackageVersionStatus.Delisted => "delisted",
        PackageVersionStatus.AlreadyDelisted => "already-delisted",
        PackageVersionStatus.NotOnServer => "not-on-server",
        PackageVersionStatus.RateLimited => "rate-limited",
        PackageVersionStatus.Failed => "failed",
        PackageVersionStatus.NotAttempted => "not-attempted",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status,
            $"Unhandled {nameof(PackageVersionStatus)} member: add its kebab-case spelling in {nameof(ToKebabCase)}.")
    };
}
