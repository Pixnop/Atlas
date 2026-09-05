<!-- What changed and why. Link the issue if there is one. -->

- [ ] `CHANGELOG.md` has an entry under `## [Unreleased]`, or the change is invisible to package consumers (wiki, CI plumbing)
- [ ] `dotnet test tests/Atlas.Pure.Tests -c Release --filter "Category!=E2E"` is green
- [ ] E2E run if `src/Atlas`, `src/Atlas.Bridge` or `src/Atlas.XUnit` changed, with the exact command and result stated below
- [ ] Wiki page updated if the change is user-facing (the wiki is a separate repository)

<!-- Tests run locally, with their output: -->
