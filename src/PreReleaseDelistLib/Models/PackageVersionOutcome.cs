namespace PreReleaseDelistLib.Models;

/// <summary>
/// The immutable result of deleting one package version, mirroring the machine-readable JSON
/// payload shape field-for-field.
/// </summary>
/// <param name="PackageId">The identifier of the package the version belongs to, exactly as supplied to the run.</param>
/// <param name="Version">The version the outcome describes, carried as its normalized string value in output.</param>
/// <param name="Status">The closed outcome status for this version.</param>
public sealed record PackageVersionOutcome(string PackageId, NuGetVersion Version, PackageVersionStatus Status);
