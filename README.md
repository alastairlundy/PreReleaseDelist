# PrereleaseDelist

A CLI to delist pre-release versions of your NuGet package(s).

[![NuGet latest version](https://img.shields.io/nuget/v/PreReleaseDelist.svg)][nuget]
[![NuGet downloads](https://img.shields.io/nuget/dt/PreReleaseDelist.svg)][nuget]
[![GitHub license](https://img.shields.io/github/license/alastairlundy/prerelease-delist.svg)][license]
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/alastairlundy/prerelease-delist/badge)][scorecard]

[nuget]: https://www.nuget.org/packages/PreReleaseDelist/
[license]: LICENSE
[scorecard]: https://api.scorecard.dev/projects/github.com/alastairlundy/prerelease-delist

## Features

- Delist specific pre-release versions of a NuGet package.
- Delist all pre-release versions of a NuGet package.
- Configure NuGet API Key and Server URL via environment variables.
- Distinguishes a version that is already delisted from a version the server has never heard of, so a mistyped version string is reported as a failure rather than a silent success.
- Preview a delist with `--dry-run` — plan only, no delete request sent, no API key required.
- Machine-readable stdout with `--output json`, plus a small documented exit-code contract for CI.

## Installation

PrereleaseDelist ships through two distribution channels.

### Channel 1 — the `dotnet tool` package (primary)

The primary distribution is the `dotnet tool` package on NuGet:

```bash
dotnet tool install --global PreReleaseDelist
```

After installing, the `prerelease-delist` command will be available on your PATH.

#### Prerequisites
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) — required by the `dotnet tool` channel.
- A .NET SDK installation on the host is additionally required only for the `sdk` backend (`--backend sdk`). The standalone binaries below cannot remove this floor: the SDK backend drives SDK tooling that packaging cannot bundle.

This channel runs both backends (`http` and `sdk`) and serves musl/Alpine users — no standalone `linux-musl-*` binary is published.

### Channel 2 — standalone binaries (GitHub Releases)

Self-contained, single-file binaries are attached to each GitHub Release for exactly six RIDs:

`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`

- **No .NET installation is required** — these binaries run the HTTP backend out of the box.
- The `sdk` backend still requires a host .NET SDK installation, which packaging cannot change; use the `dotnet tool` channel with an SDK installed if you need `--backend sdk`.
- Each release ships SHA-256 checksums: one checksum file per artifact plus a combined `sha256.txt` listing every pair, verifiable with `sha256sum -c`.
- musl/Alpine users are served by the `dotnet tool` channel; no `linux-musl-*` RID is built.

### Build

```bash
dotnet build -c Release
```

## Usage

### Arguments and Options

| Name                    | Type            | Required                            | Default                               | Description                                                                                                                                                              |
|-------------------------|-----------------|-------------------------------------|---------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `versions` (argument)   | string[]        | Yes (unless `--delist-all` is used) | —                                     | One or more pre-release version strings to delist, passed positionally — there is no `--versions` option. Only version strings starting with a digit are considered; others are ignored, except a token starting with `-`, which is rejected as an unknown option (see [Exit codes](#exit-codes)). |
| `--package-id`          | string          | Yes                                 | —                                     | The ID of the package to delist, e.g. `MyPackage`.                                                                                                                       |
| `--api-key`             | string          | Yes*                                | —                                     | The NuGet API key to authenticate with. If omitted, the CLI falls back to the `PRERELEASEDELIST_NuGetApiKey` or `NUGET_API_KEY` environment variables.                     |
| `--server-url`          | string          | No                                  | `https://api.nuget.org/v3/index.json` | The NuGet server's V3 service index URL. Useful for third-party NuGet servers. Falls back to the `PRERELEASEDELIST_NuGetServerUrl` or `NUGET_SERVER_URL` environment variables. |
| `--delist-all` | boolean         | No                                  | `false`                               | Delist all pre-release versions of the package instead of the explicitly listed `versions`.                                                                              |
| `--include-zero-major`  | boolean         | No                                  | `false`                               | With `--delist-all`, also include stable `0.x` (Major == 0) versions.                                                                                           |
| `--use-strict-parsing`  | boolean         | No                                  | `true`                                | When `true`, an invalid version string causes an error. When `false`, invalid version strings are silently skipped.                                                      |
| `--backend`             | `http` \| `sdk` | No                                  | `http`                                | Which delisting backend to use: the V3 `http` API, or the .NET SDK's package deprecation/delist support (`sdk`).                                                         |
| `--non-interactive`     | boolean         | No                                  | `false`                               | Print one machine-friendly `Version=<version> Status=<kebab-case>` line per version on stdout and exit non-zero if any version failed. Useful in CI.                                                                    |
| `--dry-run`             | boolean         | No                                  | `false`                               | Print the delist plan without sending any delete request. Works without an API key. Exits only with `0`, `1`, or `2` — see [Dry runs](#dry-runs).                                                                       |
| `--output`              | `text` \| `json` | No                                 | `text`                                | Output format for stdout: human-readable text, or newline-delimited JSON (one object per version). stderr is unaffected in both modes.                                                                                  |

\* Required either on the command line or via environment variable (see Configuration below), except under `--dry-run`, where no API key is needed at all.

### Version States

Every run resolves each requested version to exactly one member of a closed status vocabulary. The same kebab-case word appears in `--non-interactive` lines, in `--output json` payloads, and in the exit-code mapping:

| State                                   | `--non-interactive` output | JSON `status`      | Exit code contribution |
|-----------------------------------------|----------------------------|--------------------|------------------------|
| Listed on the server (now delisted)     | `Status=delisted`          | `delisted`         | 0                      |
| On the server but already delisted      | `Status=already-delisted`  | `already-delisted` | 0                      |
| Not known to the server at all          | `Status=not-on-server`     | `not-on-server`    | 1                      |
| Delete attempt failed                   | `Status=failed`            | `failed`           | 1                      |
| Server rate-limited the run             | `Status=rate-limited`      | `rate-limited`     | 3                      |
| Not attempted — the run stopped first   | `Status=not-attempted`     | `not-attempted`    | 4                      |

Each `--non-interactive` line is exactly `Version=<normalized version> Status=<kebab-case status>`. The older `Success`/`Failure` wording and its free-text `Info=`/`Error=` fields are gone; diagnostics print to stderr instead.

A version the server has never heard of — a typo, or a version that was never published — is
reported as a **failure** (`not-on-server`), not as "already delisted". Versions that do need
deleting are still processed, so one bad entry in a batch does not prevent the rest from being
delisted.

### Dry runs

`--dry-run` resolves and prints the *delist plan* without sending any delete request. Network reads still happen; nothing is written.

Each requested version lands in one of three buckets, printed as `Version=<version> Bucket=<bucket>`:

| Bucket            | Meaning                                                                       |
|-------------------|-------------------------------------------------------------------------------|
| `would-delist`    | The version exists and is listed — a real run would delete it.                 |
| `already-delisted`| The version exists but is unlisted.                                            |
| `not-on-server`   | The server has no record of the version (a typo, or a version never published).|

Exit is bucket-aware: `0` only when every requested version exists on the server, `1` when any version is `not-on-server`, and `2` when the package itself does not exist on the server. Dry runs exit only with `0`, `1`, or `2` — never `3` or `4`.

A dry run does not require an API key: delete requests are the only credential-bearing path, and a dry run sends none. With `--output json`, each plan line is a JSON object using the same closed status vocabulary — a `would-delist` version renders as `not-attempted`, because the delete attempt is never made.

```bash
prerelease-delist --package-id "MyPackage" --dry-run "1.0.0-alpha.1" "1.0.0-alpha.2"
```

```
Version=1.0.0-alpha.1 Bucket=would-delist
Version=1.0.0-alpha.2 Bucket=already-delisted
```

### JSON output

`--output json` switches stdout to newline-delimited JSON (NDJSON): one object per requested version, with exactly three fields in a fixed order — no envelope, no array wrapper, no additional fields:

```json
{"package":"MyPackage","version":"1.0.0-alpha.1","status":"delisted"}
{"package":"MyPackage","version":"1.0.0-alpha.2","status":"not-on-server"}
```

- `package` — the package id exactly as supplied to the run.
- `version` — the normalized version string.
- `status` — a kebab-case member of the status vocabulary in [Version States](#version-states).

stderr is unchanged by this mode: every error and diagnostic still prints there as human-readable text. In all modes, results, summaries, and the dry-run plan print to stdout; errors and diagnostics print to stderr; the API key is never written to any stream. When both are set, `--output json` takes precedence over `--non-interactive` for stdout.

### Exit codes

The CLI exits only with these codes:

| Code  | Meaning                                                                                                                                   |
|-------|-------------------------------------------------------------------------------------------------------------------------------------------|
| `0`   | Success — every requested version was delisted or was already delisted.                                                                     |
| `1`   | At least one version failed, including a version the server does not have (`not-on-server`); also command-line errors the argument parser rejects (see below). |
| `2`   | Validation failure detected by the CLI itself — an unknown option, an empty `--package-id`, an invalid `--backend` or `--output` value, no version strings without `--delist-all`, a missing API key on a real run, a package that does not exist on the server, or an invalid version string under strict parsing. |
| `3`   | The server rate-limited the run; the run stopped fail-fast and reported the results so far.                                                 |
| `4`   | The run was cancelled; the remaining versions were not attempted.                                                                          |
| `130` | Interrupted with Ctrl-C.                                                                                                                   |

Command lines that the argument parser rejects outright — a missing required option such as `--package-id`, or an option missing its value — never reach that validation: the parser writes its message to stderr and the process exits `1` without contacting the server. An unknown option is a different case: the parser hands it to the `versions` position, where the CLI detects it, writes `Error: Unrecognized option: '--typo'.` to stderr and exits `2` — a typo'd option is never silently dropped as a non-version string.

When a run produces more than one of these codes, the most severe wins: `3` beats `4`, which beats `1`, which beats `0`. `--dry-run` reuses `0`, `1`, and `2` only.

### Environment Variables

The CLI enables DotMake command-line directives, so you can set environment variables inline for a single run using the DotMake `[env:...]` directive:

```bash
prerelease-delist --package-id "MyPackage" --api-key "myApiKey" [env:NuGetServerUrl=https://my-server/api/v3/index.json]
```

### Configuration

Options can also be satisfied from environment variables instead of the command line. Precedence is: explicit `--api-key`/`--server-url` command-line options first, then `PRERELEASEDELIST_`-prefixed environment variables, then generic `NUGET_*` environment variables, then built-in defaults.

| Option       | `PRERELEASEDELIST_` env var        | Generic env var    |
|--------------|------------------------------------|--------------------|
| `--api-key`  | `PRERELEASEDELIST_NuGetApiKey`      | `NUGET_API_KEY`    |
| `--server-url` | `PRERELEASEDELIST_NuGetServerUrl` | `NUGET_SERVER_URL` |

```bash
# bash
export NUGET_API_KEY="myApiKey"
export NUGET_SERVER_URL="https://api.nuget.org/v3/index.json"
```

```powershell
# PowerShell
$env:NUGET_API_KEY = "myApiKey"
$env:NUGET_SERVER_URL = "https://api.nuget.org/v3/index.json"
```

The `--api-key` lookup order is therefore: `--api-key` option → `PRERELEASEDELIST_NuGetApiKey` → `NUGET_API_KEY` → error (the CLI exits with a message if no key is found). If neither server-URL variable is set, the CLI falls back to `https://api.nuget.org/v3/index.json`.

### Examples

Delist specific pre-release versions of a package:

```bash
prerelease-delist --package-id "MyPackage" --api-key "myApiKey" "1.0.0-alpha.1" "1.0.0-alpha.2"
```

Delist all pre-release versions of a package:

```bash
prerelease-delist --package-id "MyPackage" --delist-all true --api-key "myApiKey"
```

Delist all pre-release versions, including stable `0.x` versions:

```bash
prerelease-delist --package-id "MyPackage" --delist-all true --include-zero-major true --api-key "myApiKey"
```

Delist from a third-party NuGet server:

```bash
prerelease-delist --package-id "MyPackage" --api-key "myApiKey" --server-url "https://my-server/api/v3/index.json" "2.0.0-beta.4"
```

Use the .NET SDK backend instead of the HTTP API:

```bash
prerelease-delist --package-id "MyPackage" --api-key "myApiKey" --backend sdk "1.2.0-pre.1"
```

Run non-interactively (e.g. in CI), turning on lenient version parsing:

```bash
prerelease-delist --package-id "MyPackage" --api-key "myApiKey" --non-interactive true --use-strict-parsing false "0.9.0-beta" "1.0.0-alpha"
```

Preview what a delist would do — no API key, no delete request:

```bash
prerelease-delist --package-id "MyPackage" --dry-run "1.0.0-alpha.1" "1.0.0-alpha.2"
```

Emit newline-delimited JSON on stdout for machine consumption:

```bash
prerelease-delist --package-id "MyPackage" --api-key "myApiKey" --output json "1.0.0-alpha.1"
```

## Rate Limits
NuGet.org's NuGet server implementation has an API rate limit for delisting packages of [**250 package versions per hour**](https://learn.microsoft.com/en-gb/nuget/api/rate-limits) per API Key.

Third party NuGet servers may have their own rate limits. Please check your NuGet server's documentation for more information.

When the server rate-limits the run, this CLI **fails fast**: it stops sending further delete
requests, reports the results collected so far (versions not yet attempted are reported as
`not-attempted`), and exits with code `3`. It does not retry and does not wait for the rate-limit
window to reset — rerun once the window has elapsed. Rate-limit detection is authoritative by HTTP
status code on the `http` backend and best-effort on the `sdk` backend.

## License

The CLI project is licensed under the GNU GPL v3.0 or later – see the [LICENSE](LICENSE) file for details.

The library powering the CLI's NuGet-related functionality is licensed under the GNU LGPL v3.0 or later.
