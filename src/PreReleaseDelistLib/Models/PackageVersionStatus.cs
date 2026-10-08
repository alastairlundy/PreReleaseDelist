namespace PreReleaseDelistLib.Models;

/// <summary>
/// The closed per-version outcome vocabulary crossing the delete seam.
/// </summary>
/// <remarks>
/// A plain enum on purpose: exhaustive switch handling must be a compiler feature, so new members
/// break compilation at every emission and exit-code mapping site until they are handled.
/// </remarks>
public enum PackageVersionStatus
{
    /// <summary>The version was deleted (delisted) by this call.</summary>
    Delisted,

    /// <summary>The version was already delisted before this call.</summary>
    AlreadyDelisted,

    /// <summary>The server does not have this version.</summary>
    NotOnServer,

    /// <summary>The server rate-limited the request.</summary>
    RateLimited,

    /// <summary>The delete attempt failed.</summary>
    Failed,

    /// <summary>The delete attempt was never made.</summary>
    NotAttempted
}
