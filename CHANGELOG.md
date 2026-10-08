# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## Unreleased

### Added
- `--dry-run`, which resolves and prints the delist plan — *would delist* / *already delisted* /
  *not on server* — without sending any delete request (network reads still happen; nothing is
  written). Exit is bucket-aware: `0` only when every requested version exists on the server, `1`
  when any is absent, `2` when the package itself does not exist. No API key is required in this
  mode, because delete requests are the only credential-bearing path.
- `--output json`: newline-delimited JSON on stdout, one object per version with exactly `package`,
  `version`, and `status` in that fixed order — no envelope, no array wrapper, no extra fields —
  with stderr left human-readable.
- A documented exit-code contract: `0` success, `1` any per-version failure including
  not-on-server, `2` usage or validation, `3` rate-limited, `4` cancelled, `130` Ctrl-C. Dry-run
  reuses `0`/`1`/`2`; the four previous `return -1` exits are gone.
- `DelistOptions`: precedence resolution (`--option` → `PRERELEASEDELIST_*` → `NUGET_*` → default)
  and mode-aware validation (dry-run skips the API-key requirement) extracted from the command into
  a pure, testable type.
- A second distribution channel: self-contained single-file binaries for `win-x64`, `win-arm64`,
  `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`, attached to GitHub Releases alongside the
  primary `dotnet tool` package, with CI-generated SHA-256 checksums — one file per artifact plus a
  combined `sha256.txt` — for `sha256sum -c` verification. The RID binaries run the HTTP backend
  with no .NET installed; the SDK backend still requires a host SDK installation.
- `DelistPlanning`, which partitions requested versions into *to delist*, *already delisted*, and
  *not on server* — the distinction that fixes the false-success bug above.

### Changed
- Rate limiting now fails fast: when the server rate-limits the run, dispatching stops, the results
  collected so far are reported (remaining versions as `not-attempted`), and the process exits `3`.
  No retry and no graceful-failure path; detection is by HTTP status code on the `http` backend and
  best-effort on the `sdk` backend.
- Stream routing: results, summaries, and dry-run plans print to stdout; every error and diagnostic
  prints to stderr (including the missing-API-key message); the API key is never written to any
  stream.
- The per-version delete path is consolidated into a single seam shared by the HTTP and SDK
  backends, with planning composed in front of it via `DelistPlanning.Partition`; cross-cutting
  behaviour installs as decorators around the seam.
- `--non-interactive` lines render `Version=<normalized> Status=<kebab-case>` from the closed
  status vocabulary (`delisted`, `already-delisted`, `not-on-server`, `rate-limited`, `failed`,
  `not-attempted`); the old `Success`/`Failure` text and its free-text `Info=`/`Error=` fields are
  gone, with diagnostics on stderr.
- README documents the new flags, the exit-code table, both distribution channels (including the
  SDK backend's host-SDK floor and musl coverage via the tool channel), and the fail-fast
  rate-limit story.
- Packaging metadata: package tags corrected and the README packed once from the repository root.
- `GeneratePackageOnBuild` is replaced by an explicit `dotnet pack` step in `publish.yml`. Every
  local `dotnet build` no longer produces a `.nupkg`.
- Removed the `EnhancedLinq` dependency. It was only ever used for one `Exclude` call per delist
  service, and both now share `DelistPlanning.Partition`.

### Removed
- The unauthorized ADR 0001, deleted along with the csproj comments citing it and this changelog's
  earlier link to it; no orphaned path references remain, and the trim/AOT blocker mechanism now
  lives only in the decision ledger.
- The `IsTrimmable` / `IsAoTCompatible` claims. Both a trimmed and a Native AOT publish build
  successfully and then fail at runtime, because `NuGet.Protocol` deserialises repository signature
  resources with Newtonsoft.Json reflection that the trimmer removes. The properties stay absent
  until a publish-and-run against a real feed passes.

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
