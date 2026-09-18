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

using NuGet.Versioning;

namespace PreReleaseDelistCli.Helpers;

public static class ResultHelper
{
    public static async Task PrintDelistedVersions((NuGetVersion version, string responseMessage)[] delistedVersions, string packageId)
    {
        await Console.Out.WriteLineAsync($"Versions Delisted for Package: {packageId}");
        
        foreach ((NuGetVersion version, string responseMessage) in delistedVersions)
        {
            string line = version.ToFullString();
            if (!string.IsNullOrEmpty(responseMessage))
            {
                line += $" - {responseMessage}";
            }
            await Console.Out.WriteLineAsync(line);
        }
    }

    public static async Task<int> PrintNonDelistedVersions((NuGetVersion version, bool isDelisted, string responseMessage)[] nonDelistedVersions, string packageId)
    {
        await Console.Out.WriteLineAsync($"The following versions of {packageId} could not be delisted:");

        foreach ((NuGetVersion version, bool isDelisted, string responseMessage) result in nonDelistedVersions)
        {
            await Console.Out.WriteLineAsync($"{result.version.ToFullString()} - With Reason: {result.responseMessage}");
        }

        return 1;
    }
}