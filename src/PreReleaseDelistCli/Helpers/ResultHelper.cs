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