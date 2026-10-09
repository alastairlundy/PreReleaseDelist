/*
    PreReleaseDelistLib.Tests
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

using PreReleaseDelistCli.Helpers;
using PreReleaseDelistLib.Models;
using TUnit.Core;

namespace PreReleaseDelistLib.Tests;

/// <summary>
/// Kebab-case rendering of the closed status vocabulary (T018): every member spells out exactly,
/// so no output mode can render an unknown or differently-shaped status.
/// </summary>
public class StatusKebabRenderingTests
{
    [Test]
    [Arguments(PackageVersionStatus.Delisted, "delisted")]
    [Arguments(PackageVersionStatus.AlreadyDelisted, "already-delisted")]
    [Arguments(PackageVersionStatus.NotOnServer, "not-on-server")]
    [Arguments(PackageVersionStatus.RateLimited, "rate-limited")]
    [Arguments(PackageVersionStatus.Failed, "failed")]
    [Arguments(PackageVersionStatus.NotAttempted, "not-attempted")]
    public async Task ToKebabCase_SpellsEveryStatusMemberExactly(PackageVersionStatus status, string expected)
    {
        await Assert.That(status.ToKebabCase()).IsEqualTo(expected);
    }

    [Test]
    public async Task ToKebabCase_RendersLowercaseLettersAndHyphensOnly()
    {
        foreach (PackageVersionStatus status in Enum.GetValues<PackageVersionStatus>())
        {
            string rendered = status.ToKebabCase();

            await Assert.That(rendered.Length).IsGreaterThan(0);
            await Assert.That(rendered.All(character => character is (>= 'a' and <= 'z') or '-')).IsTrue();
            await Assert.That(rendered.StartsWith('-')).IsFalse();
            await Assert.That(rendered.EndsWith('-')).IsFalse();
        }
    }
}
