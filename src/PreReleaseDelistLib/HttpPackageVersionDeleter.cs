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

namespace PreReleaseDelistLib;

/// <summary>
/// HTTP delete backend behind the per-version delete seam.
/// </summary>
/// <remarks>
/// <para>
/// One version goes in, one <see cref="PackageVersionOutcome"/> comes out.
/// The publish URL is resolved per call inside this backend via the service index,
/// so a single instance may serve multiple sources.
/// No retry logic of any kind lives here; the fail-fast rule belongs to the composing service.
/// </para>
/// <para>
/// A 404 is only reported as <see cref="PackageVersionStatus.AlreadyDelisted"/> when the server's
/// service index advertised the <c>PackagePublish/2.0.0</c> endpoint the delete actually went to.
/// When that endpoint is absent and the delete fell back to the raw server URL, a 404 cannot be
/// interpreted and stays a <see cref="PackageVersionStatus.Failed"/> outcome.
/// </para>
/// </remarks>
public sealed class HttpPackageVersionDeleter : IPackageVersionDeleter
{
    private const string NugetApiKeyHeaderName = "X-NuGet-ApiKey";

    private readonly IHttpClientFactory _clientFactory;

    public HttpPackageVersionDeleter(IHttpClientFactory clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = clientFactory;
    }

    /// <summary>
    /// Deletes (delists) a single package version via the NuGet HTTP delete endpoint.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint serving the package.</param>
    /// <param name="packageId">The identifier of the package the version belongs to.</param>
    /// <param name="version">The specific version of the package to delete.</param>
    /// <param name="apiKey">The API key required for authentication with the NuGet service.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The closed outcome of the delete attempt for this version.</returns>
    public async ValueTask<PackageVersionOutcome> DeleteAsync(
        string nugetApiUrl, string packageId, NuGetVersion version,
        string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);

        try
        {
            SourceRepository repoInfo = Repository.Factory.GetCoreV3(nugetApiUrl);

            ServiceIndexResourceV3? serviceIndex =
                await repoInfo.GetResourceAsync<ServiceIndexResourceV3>(cancellationToken)
                    .ConfigureAwait(false);

            Uri? publishUrl = serviceIndex?.GetServiceEntryUri("PackagePublish/2.0.0");

            // A 404 only means "already delisted" when the delete actually went to the server's
            // declared publish endpoint. When the service index did not advertise one and the run
            // fell back to the raw server URL, a 404 says nothing about the version's state, so the
            // delete stays a plain failure instead of a false success.
            bool resolvedPublishUrl = publishUrl is not null;

            publishUrl ??= new Uri(nugetApiUrl);

            HttpClient client = _clientFactory.CreateClient();

            client.DefaultRequestHeaders.Add(NugetApiKeyHeaderName, [apiKey]);
            client.BaseAddress = new Uri(publishUrl.AbsoluteUri.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromMinutes(2);

            string relativeUrl = $"{packageId}/{version.ToNormalizedString()}";

            using HttpResponseMessage response =
                await client.DeleteAsync(relativeUrl, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Delisted);
            }

            if ((int)response.StatusCode == 404 && resolvedPublishUrl)
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.AlreadyDelisted);
            }

            if ((int)response.StatusCode == 429)
            {
                return new PackageVersionOutcome(packageId, version, PackageVersionStatus.RateLimited);
            }

            return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Failed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            // Server-side faults become a Failed outcome. A cancellation of the caller's token is
            // deliberately not converted: it escapes so the composing service and the command
            // boundary can report the run as interrupted instead of as a wall of Failed outcomes.
            // An OperationCanceledException that is not caused by the caller's token (for example
            // the HttpClient timeout) stays a Failed outcome.
            return new PackageVersionOutcome(packageId, version, PackageVersionStatus.Failed);
        }
    }
}
