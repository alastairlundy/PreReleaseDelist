using System.Text.Json;
using EnhancedLinq.Deferred;

namespace PreReleaseDelistLib.Detectors;

public class PackageAvailabilityDetector : IPackageAvailabilityDetector
{
    public async Task<bool> CheckPackageExistsAsync(string nugetApiUrl, string packageId, CancellationToken cancellationToken)
    {
        SourceRepository repository = Repository.Factory.GetCoreV3(nugetApiUrl);

        try
        {
            using var cacheContext = new SourceCacheContext();

            FindPackageByIdResource? searchResource =
                await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);

            if (searchResource is null)
                throw new InvalidOperationException($"The FindPackageById resource is unavailable for '{nugetApiUrl}'.");

            IEnumerable<NuGetVersion>? packageVersions = await searchResource.GetAllVersionsAsync(packageId,
                cacheContext,
                NullLogger.Instance, cancellationToken);

            return packageVersions is not null && packageVersions.Any();
        }
        catch(ArgumentException)
        {
            return false;
        }
    }
}
