# prerelease-delist

A CLI to delist pre-release versions of your NuGet package(s).

[![NuGet latest version](https://img.shields.io/nuget/v/AlastairLundy.PreReleaseDelist.svg)][nuget]
[![NuGet downloads](https://img.shields.io/nuget/dt/AlastairLundy.PreReleaseDelist.svg)][nuget]
[![GitHub license](https://img.shields.io/github/license/alastairlundy/prerelease-delist.svg)][license]
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/alastairlundy/prerelease-delist/badge)][scorecard]

[nuget]: https://www.nuget.org/packages/AlastairLundy.PreReleaseDelist/
[license]: LICENSE
[scorecard]: https://api.scorecard.dev/projects/github.com/alastairlundy/prerelease-delist

## Features

- Delist specific pre-release versions of a NuGet package.
- Delist all pre-release versions of a NuGet package.
- Configure NuGet API Key and Server URL via configuration files.

## Installation

### Install (as a .NET Global Tool)

The CLI is published as a `dotnet` global tool:

```bash
dotnet tool install --global PreReleaseDelist
```

After installing, the `prerelease-delist` command will be available on your PATH.

### Prerequisites
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build

```bash
dotnet build -c Release
```

## Usage

### Arguments and Options

| Name                    | Type            | Required                                     | Default                               | Description                                                                                                                                                              |
|-------------------------|-----------------|----------------------------------------------|---------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `versions` (argument)   | string[]        | Yes (unless `--delist-all-versions` is used) | —                                     | One or more pre-release version strings to delist. Only version strings starting with a digit are considered; others are ignored.                                        |
| `--package-id`          | string          | Yes                                          | —                                     | The ID of the package to delist, e.g. `MyPackage`.                                                                                                                       |
| `--api-key`             | string          | Yes*                                         | —                                     | The NuGet API key to authenticate with. If omitted, the CLI falls back to the `NuGetApiKey` value from `appsettings.json`.                                               |
| `--server-url`          | string          | No                                           | `https://api.nuget.org/v3/index.json` | The NuGet server's V3 service index URL. Useful for third-party NuGet servers. Falls back to the `NuGetServerUrl` value from `appsettings.json`.                         |
| `--delist-all-versions` | boolean         | No                                           | `false`                               | Delist all pre-release versions of the package instead of the explicitly listed `versions`.                                                                              |
| `--include-zero-major`  | boolean         | No                                           | `false`                               | With `--delist-all-versions`, also include stable `0.x` (Major == 0) versions.                                                                                           |
| `--use-strict-parsing`  | boolean         | No                                           | `true`                                | When `true`, an invalid version string causes an error. When `false`, invalid version strings are silently skipped.                                                      |
| `--backend`             | `http` \| `sdk` | No                                           | `http`                                | Which delisting backend to use: the V3 `http` API, or the .NET SDK's package deprecation/delist support (`sdk`).                                                         |
| `--non-interactive`     | boolean         | No                                           | `false`                               | Print a machine-friendly `Version=<version> Status=<Success\|Failure> ...` line per version and exit with a non-zero code if any version failed to delist. Useful in CI. |

\* Required either on the command line or via `appsettings.json`.

### Environment Variables

The CLI enables DotMake command-line directives, so you can set environment variables inline for a single run using the DotMake `[env:...]` directive:

```bash
prerelease-delist --package-id "MyPackage" --api-key "myApiKey" [env:NuGetServerUrl=https://my-server/api/v3/index.json]
```

### Configuration

Options can also be satisfied from .NET configuration instead of the command line. Precedence is: explicit `--api-key`/`--server-url` command-line options first, then `appsettings.json` values, then built-in defaults.

The recognised configuration keys are:

| Key              | Used by                 |
|------------------|-------------------------|
| `NuGetServerUrl` | `--server-url` fallback |

For `appsettings.json` (searched in the current directory, then the tool's install directory):

```json
{
  "NuGetApiKey": "myApiKey",
  "NuGetServerUrl": "https://api.nuget.org/v3/index.json"
}
```

The `--api-key` lookup order is therefore: `--api-key` option → `appsettings.json` → error (the CLI exits with a message if no key is found).

### Examples

Delist specific pre-release versions of a package:

```bash
prerelease-delist --package-id "MyPackage" --versions "1.0.0-alpha.1" "1.0.0-alpha.2" --api-key "myApiKey"
```

Delist all pre-release versions of a package:

```bash
prerelease-delist --package-id "MyPackage" --delist-all-versions true --api-key "myApiKey"
```

Delist all pre-release versions, including stable `0.x` versions:

```bash
prerelease-delist --package-id "MyPackage" --delist-all-versions true --include-zero-major true --api-key "myApiKey"
```

Delist from a third-party NuGet server:

```bash
prerelease-delist --package-id "MyPackage" --versions "2.0.0-beta.4" --api-key "myApiKey" --server-url "https://my-server/api/v3/index.json"
```

Use the .NET SDK backend instead of the HTTP API:

```bash
prerelease-delist --package-id "MyPackage" --versions "1.2.0-pre.1" --api-key "myApiKey" --backend sdk
```

Run non-interactively (e.g. in CI), turning on lenient version parsing:

```bash
prerelease-delist --package-id "MyPackage" --versions "0.9.0-beta" "1.0.0-alpha" --api-key "myApiKey" --non-interactive true --use-strict-parsing false
```

## Rate Limits
NuGet.org's NuGet server implementation has an API rate limit for delisting packages of [**250 package versions per hour**](https://learn.microsoft.com/en-gb/nuget/api/rate-limits) per API Key.

Third party NuGet servers may have their own rate limits. Please check your NuGet server's documentation for more information.

This CLI tries to gracefully fail (and inform you) if you exceed the API rate limit.

## License

The CLI project is licensed under the GNU GPL v3.0 or later – see the [LICENSE](LICENSE) file for details.

The library powering the CLI's NuGet-related functionality is licensed under the GNU LGPL v3.0 or later.
