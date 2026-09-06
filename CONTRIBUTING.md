# Contributing to Atlas

## Build environment

You need the .NET 10 SDK and a Vintage Story install, 1.21.0 or newer. `VINTAGE_STORY` must
point at the folder that contains `VintagestoryAPI.dll`, the game's binaries folder.

```sh
export VINTAGE_STORY=/path/to/vintagestory
dotnet build Atlas.slnx -c Release
```

`TreatWarningsAsErrors` is on, so a warning is a failed build.

## Tests

Three suites, the same three CI runs:

```sh
dotnet test tests/Atlas.Pure.Tests   -c Release --filter "Category!=E2E"
dotnet test tests/Atlas.Engine.Tests -c Release --filter "Category=E2E"
dotnet test samples/Sample.Scenarios -c Release --filter "Category=E2E"
```

The pure suite needs no game install and runs in seconds. Run it on every change. The two E2E
suites boot a real headless server: the samples take about twenty seconds, the engine suite
takes minutes. Run the engine suite when you touch `src/Atlas`, `src/Atlas.Bridge` or
`src/Atlas.XUnit`; narrowing it with `--filter FullyQualifiedName~YourTestClass` is fine as long
as the pull request says what you actually ran.

`EngineContractTests` in the pure suite checks every engine shape Atlas resolves by reflection
against real game assemblies, one row per install, without booting anything. Point
`ATLAS_COMPAT_INSTALLS` at the extra installs you keep around, separated the way `PATH` is (`:`
on Linux and macOS, `;` on Windows), and the theory adds a row for each on top of the
`VINTAGE_STORY` one. It skips itself when no install is there, so a single-install machine still
gets a green pure suite.

```sh
export ATLAS_COMPAT_INSTALLS=/opt/vs/1.20.12:/opt/vs/1.21.7:/opt/vs/1.22.7
```

When an E2E class fails it keeps its scratch directory, and the server's own log is at
`<temp>/atlas/<guid>/Logs/server-main.log`. Set `ATLAS_KEEP_SCRATCH=1` to keep the green ones
too.

A pull request runs more than those three commands. `ci.yml` builds and runs the pure suite
once, with `ATLAS_COMPAT_INSTALLS` pointed at 1.21.7 and 1.22.7 so the contract theory covers
three installs, then runs the engine E2E suite on the floor 1.21.7 and the latest stable
1.22.7, three whole-class shards per version, next to the `prebuilt-cross-install` lane that
runs the samples on two installs from a single build. When the pull request touches `src/` or
the pure suite, `mutation.yml` runs Stryker over `Atlas.Cli`, `Atlas` and `Atlas.XUnit`, each
breaking under its own score threshold (73, 35 and 45 percent), so deleting an assertion
instead of the code it covers turns that job red. SonarCloud runs inside `ci.yml` as well and
takes its coverage from the shard reports, which makes its quality gate the last check to
report and means a red shard skips it entirely. The full verdict lands in about nine to ten
minutes.

## Branches, commits, language

Branch off `main` using one of the prefixes `feat/`, `fix/`, `docs/`, `ci/`, `chore/`,
`release/`. Commit subjects are conventional commits (`fix: stop the registry reusing a
superseded host`), imperative mood.

Everything committed is in English: code, comments, documentation, commit messages, pull
request text.

## Changelog

Anything a package consumer notices gets an entry under `## [Unreleased]` in `CHANGELOG.md`:
public API, observable behaviour, the public XML docs that reach IntelliSense, the samples
shipped in the repo. Wiki pages and CI plumbing do not. Follow the style already there: the
changed API or file in backticks, then one paragraph saying what changed and why.

## Versions

Do not bump a version in a feature pull request. The `<Version>` property in
`Directory.Build.props` moves only when a release is cut.

## Cutting a release

1. Move the `## [Unreleased]` entries under a new `## [x.y.z] - YYYY-MM-DD` heading and update
   the reference links at the top of `CHANGELOG.md`.
2. Set `<Version>` in `Directory.Build.props`. It is the only version property there: the
   package, assembly, file and informational versions all derive from it. The pack step passes
   the version from the tag, so a stale `<Version>` never reaches nuget.org, but leaving it
   behind makes the checked-in file lie.
3. Bump the pinned package version in three places: the `Pixnop.Atlas.XUnit` PackageReference in
   `README.md`, and the wiki's `Getting-Started.md` and `Home.md`.
4. Merge, then push the tag `vx.y.z`. `release.yml` packs, publishes through nuget.org trusted
   publishing, and creates the GitHub release with notes taken from the changelog section whose
   heading matches the tag. It copies that section verbatim up to the next `## [`, so keep the
   section body to entries only.
