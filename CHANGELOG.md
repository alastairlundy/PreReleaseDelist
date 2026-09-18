# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
