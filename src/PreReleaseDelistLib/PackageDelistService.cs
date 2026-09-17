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

using EnhancedLinq.Deferred;

namespace PreReleaseDelistLib;

/// <summary>
/// Service for requesting package delisting from a NuGet server.
/// </summary>
public class PackageDelistService : IPackageDelistService
{
    private const string NugetApiKeyHeaderName = "X-NuGet-ApiKey";
    
    private readonly IHttpClientFactory _clientFactory;
    private readonly IPackageVersionService _packageVersionService;
    private readonly IPackageAvailabilityDetector _packageAvailabilityDetector;

    public PackageDelistService(IHttpClientFactory clientFactory, IPackageVersionService packageVersionService,
        IPackageAvailabilityDetector packageAvailabilityDetector)
    {
        _clientFactory =  clientFactory;
        _packageVersionService = packageVersionService;
        _packageAvailabilityDetector = packageAvailabilityDetector;
    }

    /// <summary>
    /// Requests the delisting of all prerelease versions of a NuGet package.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authenticating with the NuGet service.</param>
    /// <param name="packageId">The identifier of the NuGet package to delist versions for.</param>
    /// <param name="includeZeroMajorVersions">When true, stable versions with Major == 0 are also delisted alongside prerelease versions.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An asynchronous sequence of tuples containing the NuGet version, a boolean indicating the success of the delisting operation, and a response message from the service.</returns>
    public async IAsyncEnumerable<(NuGetVersion version, bool delistSuccess, string responseMessage)>
        RequestPackageDelistingAsync(string nugetApiUrl, string nugetApiKey, string packageId,
            bool includeZeroMajorVersions = false,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        NuGetVersion[] versionsToDelist = await _packageVersionService.GetPrereleasePackageVersionsAsync
            (nugetApiUrl, nugetApiKey, packageId, includeZeroMajorVersions, cancellationToken);
        
        IAsyncEnumerable<(NuGetVersion version, bool delistSuccess, string responseMessage)> result = RequestPackageDelistingAsync(nugetApiUrl,
            nugetApiKey, packageId, versionsToDelist, cancellationToken);

        await foreach ((NuGetVersion version, bool delistSuccess, string responseMessage) in result)
        {
            yield return (version, delistSuccess, responseMessage);
        }
    }

    /// <summary>
    /// Asynchronously requests the delisting of specified NuGet package versions from a package registry.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authenticating with the NuGet service.</param>
    /// <param name="packageId">The identifier of the NuGet package to delist versions for.</param>
    /// <param name="versions">The versions of the package to delist.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An asynchronous sequence of tuples containing the NuGet version, a boolean indicating the success of the delisting operation, and a response message from the service.</returns>
    public async IAsyncEnumerable<(NuGetVersion version, bool delistSuccess, string responseMessage)>
        RequestPackageDelistingAsync(string nugetApiUrl,
            string nugetApiKey, string packageId,
            IList<NuGetVersion> versions, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(nugetApiKey);
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentNullException.ThrowIfNull(versions);
        
        bool doesPackageExist = await _packageAvailabilityDetector.CheckPackageExistsAsync(nugetApiUrl, packageId, cancellationToken);

        if(!doesPackageExist)
            throw new ArgumentException(string.Format(Resources.Exceptions_Package_NotFoundOnServer, packageId, nugetApiUrl), nameof(packageId));
        
        IDictionary<NuGetVersion, bool> checkVersionsForDelist = await _packageVersionService.CheckPackageVersionsListedAsync(nugetApiUrl, nugetApiKey, packageId,
            true, versions, cancellationToken);

        NuGetVersion[] alreadyDelistedVersions = checkVersionsForDelist.Where(kvp => !kvp.Value).Select(kvp => kvp.Key)
            .ToArray();
        
        NuGetVersion[] versionsToDelist = versions.Exclude(alreadyDelistedVersions)
            .ToArray();
        
        foreach (NuGetVersion alreadyDelistedVersion in alreadyDelistedVersions)
        {
            yield return (alreadyDelistedVersion, true, 
                Resources.Info_Package_AlreadyDelisted);
        }

        if (versionsToDelist.Length == 0)
            yield break;
        
        SourceRepository repoInfo = Repository.Factory.GetCoreV3(nugetApiUrl);

        using var sourceCacheContext = new SourceCacheContext();

        ServiceIndexResourceV3? serviceIndex =
            await repoInfo.GetResourceAsync<ServiceIndexResourceV3>(cancellationToken);

        if (serviceIndex is null)
            throw new InvalidOperationException($"Service index resource is not available for this source: {nugetApiUrl}");

        Uri? publishUrl = serviceIndex.GetServiceEntryUri("PackagePublish/2.0.0");

        if (publishUrl is null)
            publishUrl = new Uri(nugetApiUrl);

        HttpClient client = _clientFactory.CreateClient();
        
        client.DefaultRequestHeaders.Add(NugetApiKeyHeaderName, [nugetApiKey]);
        client.BaseAddress = new Uri(publishUrl.AbsoluteUri.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromMinutes(2);

        foreach (NuGetVersion version in versionsToDelist)
        {
            string relativeUrl = $"{packageId}/{version.ToNormalizedString()}";

            (NuGetVersion version, bool delistSuccess, string responseMessage) result = await DeletePackageVersionAsync(client, relativeUrl, version, cancellationToken);

            yield return result;
        }
    }

    private static async Task<(NuGetVersion version, bool delistSuccess, string responseMessage)> DeletePackageVersionAsync(
        HttpClient client, string relativeUrl, NuGetVersion version, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await client.DeleteAsync(relativeUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return (version, true, "");
            }

            string failureMessage = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
            return (version, false, failureMessage);
        }
        catch (HttpRequestException ex)
        {
            return (version, false, ex.Message);
        }
    }
}
