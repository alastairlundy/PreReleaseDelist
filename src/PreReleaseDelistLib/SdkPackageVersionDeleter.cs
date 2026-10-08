/*
    PreReleaseDelistLib
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

using CliInvoke.Core;

namespace PreReleaseDelistLib;

/// <summary>
/// SDK delete backend behind the per-version delete seam, driven by <c>dotnet nuget delete</c>.
/// </summary>
/// <remarks>
/// <para>
/// This backend passes the API key on the process command line, which is visible in process listings.
/// This is inherent to the <c>dotnet nuget delete</c> command and cannot be avoided.
/// Consider using the HTTP-based <see cref="HttpPackageVersionDeleter"/> where possible for improved security.
/// </para>
/// <para>
/// One version goes in, one process run happens, one <see cref="PackageVersionOutcome"/> comes out.
/// No retry logic of any kind lives here.
/// </para>
/// <para>
/// Detection confidence: <see cref="PackageVersionStatus.RateLimited"/> is best-effort here,
/// detected by non-zero exit plus a stderr rate-limit pattern, and
/// <see cref="PackageVersionStatus.AlreadyDelisted"/> is best-effort here,
/// detected by process output indicating the version is not listed.
/// Server and SDK wording varies, so confidence is limited; runs that cannot be
/// confidently distinguished are classified as <see cref="PackageVersionStatus.Failed"/>.
/// </para>
/// </remarks>
public sealed class SdkPackageVersionDeleter : IPackageVersionDeleter
{
    private readonly IProcessInvoker _processInvoker;

    public SdkPackageVersionDeleter(IProcessInvoker processInvoker)
    {
        ArgumentNullException.ThrowIfNull(processInvoker);
        _processInvoker = processInvoker;
    }

    /// <summary>
    /// Deletes (delists) a single package version via <c>dotnet nuget delete</c>.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint serving the package.</param>
    /// <param name="packageId">The identifier of the package the version belongs to.</param>
    /// <param name="version">The specific version of the package to delete.</param>
    /// <param name="apiKey">The API key required for authentication with the NuGet service.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The closed outcome of the delete attempt for this version.</returns>
    /// <remarks>
    /// Exit code <c>0</c> yields <see cref="PackageVersionStatus.Delisted"/>.
    /// A non-zero exit whose output matches the rate-limit pattern yields
    /// <see cref="PackageVersionStatus.RateLimited"/> (best-effort).
    /// A non-zero exit whose output indicates the version is not listed yields
    /// <see cref="PackageVersionStatus.AlreadyDelisted"/> (best-effort).
    /// All other failures yield <see cref="PackageVersionStatus.Failed"/>.
    /// </remarks>
    public async ValueTask<PackageVersionOutcome> DeleteAsync(
        string nugetApiUrl, string packageId, NuGetVersion version,
        string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);

        ProcessConfiguration configuration = new()
        {
            TargetFilePath = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet",
            OutputRedirection = true,
            ArgumentList =
            [
                "nuget", "delete",
                packageId.ToLowerInvariant(),
                version.ToNormalizedString(),
                "--api-key", apiKey,
                "--source", nugetApiUrl,
                "--non-interactive"
            ]
        };

        try
        {
            BufferedProcessResult result = await _processInvoker.ExecuteBufferedAsync(configuration,
                new ProcessExitConfiguration(),
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Delisted);
            }

            string output = $"{result.StandardOutput} {result.StandardError}";

            if (LooksRateLimited(output))
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.RateLimited);
            }

            if (LooksAlreadyDelisted(output))
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.AlreadyDelisted);
            }

            return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Failed);
        }
        catch
        {
            return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Failed);
        }
    }

    private static bool LooksRateLimited(string output)
    {
        return output.Contains("429", StringComparison.OrdinalIgnoreCase)
            || output.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
            || output.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || output.Contains("rate-limit", StringComparison.OrdinalIgnoreCase)
            || output.Contains("ratelimit", StringComparison.OrdinalIgnoreCase)
            || output.Contains("retry later", StringComparison.OrdinalIgnoreCase)
            || output.Contains("throttl", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksAlreadyDelisted(string output)
    {
        return output.Contains("not listed", StringComparison.OrdinalIgnoreCase)
            || output.Contains("already delisted", StringComparison.OrdinalIgnoreCase)
            || output.Contains("already unlisted", StringComparison.OrdinalIgnoreCase)
            || output.Contains("is not listed", StringComparison.OrdinalIgnoreCase);
    }
}
