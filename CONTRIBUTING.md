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

When an E2E class fails it keeps its scratch directory, and the server's own log is at
`<temp>/atlas/<guid>/Logs/server-main.log`. Set `ATLAS_KEEP_SCRATCH=1` to keep the green ones
too.

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
