# Agent instructions

## Running tests

`dotnet test` is the test entry point. `global.json` opts the repository into SDK 10's
native Microsoft.Testing.Platform command mode (`"test": { "runner": "Microsoft.Testing.Platform" }`),
and the suite itself is TUnit (MTP-only, 25 fuzz properties).

Verified commands:

```bash
# simplest full run
dotnet test tests/PreReleaseDelistLib.FuzzTests

# exact .github/workflows/tests.yml sequence (build the solution first)
dotnet build -c Release "src/PreRelease DeList.slnx"
dotnet test tests/PreReleaseDelistLib.FuzzTests --no-build -c Release

# exact .github/workflows/publish.yml gate (runs before restore/pack/push)
dotnet test "src/PreRelease DeList.slnx" -c Release

# class-scoped run (TUnit treenode filter; native mode takes MTP options directly, no -- separator)
dotnet test tests/PreReleaseDelistLib.FuzzTests --treenode-filter "/*/*/VersionFilterProperties/*"
```

`dotnet test --help` lists everything the wrapper consumes in this mode. Any other
argument is forwarded to the test application, which rejects options it has not
registered.

### Never pass `--nologo`

`dotnet test` has no `--nologo` option in native MTP mode. The flag is forwarded to
the test application, which rejects it (`Unknown option '--nologo'`) and exits 5 —
and the wrapper swallows that message, so the only visible symptom is the misleading:

```text
Zero tests ran
error: 1
```

If you see that, re-check your `dotnet test` arguments against `dotnet test --help`
before suspecting the tests themselves. The reference behaviour is running the built
module directly, which prints the real error:

```bash
dotnet tests/PreReleaseDelistLib.FuzzTests/bin/Debug/net10.0/PreReleaseDelistLib.FuzzTests.dll
```
