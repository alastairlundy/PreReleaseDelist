# 1. Ship framework-dependent; do not trim or publish Native AOT

- **Status:** Accepted
- **Date:** 2026-10-04

## Context

`PreReleaseDelistCli` and `PreReleaseDelistLib` both declared `IsTrimmable` and `IsAoTCompatible`.
For a short-lived CLI, a Native AOT binary is an attractive goal: faster startup and a single
self-contained file with no framework dependency.

Those flags were never verified. Native AOT is attractive enough that a reviewer skimming the
project file would reasonably assume someone had checked.

## Decision

Neither trimmed nor Native AOT publishes are supported. The flags are removed and the tool ships
as a framework-dependent .NET global tool.

## Rationale

Both publish modes **build successfully and then fail at runtime**, against a real NuGet feed:

```
FatalProtocolException: Unable to get repository signature information for source
  https://api.nuget.org/v3-index/repository-signatures/5.0.0/index.json.
 ---> Newtonsoft.Json.JsonSerializationException: Unable to find a constructor to use for type
    NuGet.Protocol.RepositoryCertificateInfo ... Path 'signingCertificates[0].fingerprints'.
```

NuGet.Protocol resolves repository signature resources as part of `SourceRepository` resource
lookup, and deserialises `RepositoryCertificateInfo` using Newtonsoft.Json's reflection-based
model binding. The trimmer cannot see those constructors being used, so it removes them. The
`IL2026`/`IL3053` warnings emitted during publish predicted this exactly:

```
ILLink : Trim analysis warning IL2026: NuGet.Protocol.RepositorySignatureResourceProvider...
  Using member 'RepositorySignatureResource(JObject, SourceRepository)' which has
  'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code.
  Uses Newtonsoft.Json reflection-based deserialization.
```

A plain `dotnet build` reports `0 Warning(s)`, because the trim and AOT analyzers only run when a
publish RID is supplied. So nothing in the existing build or test pipeline would ever have
surfaced this.

Making it work would require either forking NuGet.Protocol to hand-write the deserialisation or
replacing it with raw HTTP calls against the V3 endpoints. Neither is proportionate for this tool.

## Consequences

- `IsTrimmable`, `IsAoTCompatible`, `EnableTrimAnalyzer` and `EnableAoTAnalyzer` are gone from both
  project files, with a comment pointing back at this record.
- `GeneratePackageOnBuild` is also removed. It made every local `dotnet build` produce a `.nupkg`,
  and it hard-blocks Native AOT publishing outright
  (`error : GeneratePackageOnBuild is not supported for native compilation`). Packing is now an
  explicit `dotnet pack` step in `publish.yml`.
- If Native AOT ever becomes worth it, the precondition is dropping the `NuGet.Protocol` dependency.
  Revisit only with an end-to-end run against nuget.org, not a build that merely succeeds.

## Alternatives considered

- **Suppress the warnings with `NoWarn`.** Rejected. The warnings are accurate; the trimmed
  binary is genuinely broken, so suppressing them would hide a real defect.
- **Keep the flags as intent.** Rejected. A property that asserts a capability the product does not
  have is worse than no property, because it is believed rather than verified.