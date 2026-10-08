# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## Unreleased

### Fixed
- `--delist-all` now works without supplying any `versions`. The `versions` argument was marked
  required, so every documented `--delist-all` invocation failed with
  `Required argument missing for command`. The README examples now run as written.
- Versions the server does not know about are reported as failures instead of successes. A missing
  entry in the server's version metadata was previously indistinguishable from "present but
  unlisted", so a typo'd or never-published version was reported as
  `Status=Success Info='Package already de-listed.'` and exited 0. Both the `http` and `sdk`
  backends were affected.
- `--include-zero-major` is now honoured when passed to `CheckPackageVersionsListedAsync`, which
  previously ignored it and always searched including pre-releases.

### Changed
- Removed the `IsTrimmable` / `IsAoTCompatible` claims. Both a trimmed and a Native AOT publish
  build successfully and then fail at runtime, because `NuGet.Protocol` deserialises repository
  signature resources with Newtonsoft.Json reflection that the trimmer removes.
- `GeneratePackageOnBuild` is replaced by an explicit `dotnet pack` step in `publish.yml`. Every
  local `dotnet build` no longer produces a `.nupkg`.
- Removed the `EnhancedLinq` dependency. It was only ever used for one `Exclude` call per delist
  service, and both now share `DelistPlanning.Partition`.

### Added
- `DelistPlanning`, which partitions requested versions into *to delist*, *already delisted*, and
  *not on server* — the distinction that fixes the false-success bug above.

## 0.1.0 - 2026-09-18

Initial release.

### Added
- `prerelease-delist` .NET global tool for delisting pre-release versions of NuGet packages.
- Delist explicitly listed pre-release versions via the `versions` argument.
- `--delist-all` to delist all pre-release versions of a package, with `--include-zero-major` to also include `0.x` versions.
- `--api-key` / `--server-url` options with `PRERELEASEDELIST_`-prefixed and generic `NUGET_*` environment variable fallback, plus third-party NuGet server support.
- `--backend` selection (`http` V3 API or .NET SDK-backed delisting).
- `--non-interactive` machine-friendly output mode for CI usage.
- `--use-strict-parsing` control over invalid version string handling.
