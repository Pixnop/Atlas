#!/usr/bin/env python3
"""Check that the README Quickstart still works, against freshly packed packages.

Docs drift from the code they describe silently: nothing fails when a Quickstart snippet
stops compiling, or when a packaging change reintroduces a bug the Quickstart exists to avoid
(the 0.14.1 install report this script was written for: NU1108 cycles from project naming,
missing Assert, xUnit v3 producing a bare CS0433). This packs the three consumer-facing
projects PACK_PROJECTS names, the same way release.yml packs them, then runs three cheap
end-to-end checks against the result:

1. quickstart   - the README's own csproj and scenario snippets (between HTML comment
   markers, invisible on GitHub), written out verbatim and run with `dotnet test`. Catches a
   Quickstart that stops compiling (the ImplicitUsings/using System.Threading.Tasks bug this
   script was written after) or stops passing.
2. assert-without-xunit - a project referencing Pixnop.Atlas.XUnit, Microsoft.NET.Test.Sdk
   and xunit.runner.visualstudio, but not the `xunit` package, compiles a file calling
   Assert.Equal. Catches Pixnop.Atlas.XUnit losing its own xunit.assert dependency.
3. xunit-v3-rejected - a project that also references an xunit.v3 package fails the build
   with Atlas's own ATLAS001 error, not the ambiguous-FactAttribute CS0433 a newcomer would
   otherwise have to decode unassisted.

Every dotnet invocation here uses an isolated NUGET_PACKAGES under --work-dir and restores
from a local folder feed of the packages this run just packed, plus nuget.org for everything
else (source-mapped, never the machine's shared NuGet cache or config).

Usage:
    check-quickstart.py --repo-root <checkout> --work-dir <scratch dir>

Needs the VINTAGE_STORY environment variable set to a real Vintage Story install: the
quickstart check boots a headless server the way `dotnet test` would for any Atlas consumer.
"""

import argparse
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

CSPROJ_MARKERS = ("<!-- quickstart-csproj-start -->", "<!-- quickstart-csproj-end -->")
ASSEMBLY_MARKERS = ("<!-- quickstart-assembly-start -->", "<!-- quickstart-assembly-end -->")
SCENARIO_MARKERS = ("<!-- quickstart-scenario-start -->", "<!-- quickstart-scenario-end -->")

PACK_PROJECTS = ["src/Atlas/Atlas.csproj", "src/Atlas.Bridge/Atlas.Bridge.csproj", "src/Atlas.XUnit/Atlas.XUnit.csproj"]

ASSERT_ONLY_CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <PackageReference Include="Pixnop.Atlas.XUnit" Version="{atlas_version}" />
  </ItemGroup>

</Project>
"""

ASSERT_ONLY_CS = """using Xunit;

public class AssertUsage
{
    [Fact]
    public void Check()
    {
        Assert.Equal(1, 1);
    }
}
"""

# Version pinned via an open lower bound, `[4.0.0,)`: NuGet resolves that to the lowest
# matching version, so this always restores 4.0.0, deterministically, not whatever is
# latest. This is a smoke check that xUnit v3 gets rejected, not a pin on a specific xUnit v3
# release. xunit.v3 is the v3 metapackage, the same family the xunit3 project template
# references. OutputType is Exe, matching the xunit3 template: v3's own
# Microsoft.Testing.Platform runner needs the project to build as an executable.
XUNIT_V3_CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit.v3" Version="[4.0.0,)" />
    <PackageReference Include="Pixnop.Atlas.XUnit" Version="{atlas_version}" />
  </ItemGroup>

</Project>
"""

# Proves the ATLAS001 guard runs before CoreCompile: without a [Fact], nothing forces
# FactAttribute resolution and the "no CS0433" half of check_xunit_v3_rejected could never fail.
XUNIT_V3_CS = """using Xunit;

public class Placeholder
{
    [Fact]
    public void Check()
    {
        Assert.True(true);
    }
}
"""

# Pixnop.* is never served by nuget.org here: source mapping makes a version mismatch between
# the packed nupkgs and a csproj's pin (e.g. a release bump to the props version that forgot
# this script, or the README) fail restore loudly instead of silently falling back to whatever
# that id/version happens to be on nuget.org.
NUGET_CONFIG = """<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="atlas-local" value="{artifacts}" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="atlas-local">
      <package pattern="Pixnop.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"""


def read_atlas_version(repo_root):
    props_text = (repo_root / "Directory.Build.props").read_text()
    match = re.search(r"<Version>([^<]+)</Version>", props_text)
    if not match:
        sys.exit("Directory.Build.props: no <Version> element found")
    return match.group(1)


def check_readme_pin(readme_text, atlas_version):
    if f'Include="Pixnop.Atlas.XUnit" Version="{atlas_version}"' not in readme_text:
        sys.exit(
            f"README.md does not pin Pixnop.Atlas.XUnit to {atlas_version} (the version in "
            "Directory.Build.props). Update the Quickstart csproj snippet's version."
        )


def extract_snippet(readme_text, markers, label):
    start_marker, end_marker = markers
    try:
        start = readme_text.index(start_marker) + len(start_marker)
        end = readme_text.index(end_marker, start)
    except ValueError:
        sys.exit(f"{label}: markers {start_marker!r} / {end_marker!r} not found in README.md")
    fence = re.search(r"```[a-z]*\n(.*?)```", readme_text[start:end], re.DOTALL)
    if not fence or not fence.group(1).strip():
        sys.exit(f"{label}: no non-empty fenced code block between the markers")
    return fence.group(1)


def run(cmd, cwd, env, expect_ok, label):
    print(f"\n== {label} ==\n$ {' '.join(cmd)}   (in {cwd})")
    result = subprocess.run(cmd, cwd=cwd, env=env, capture_output=True, text=True)
    print(result.stdout)
    print(result.stderr, file=sys.stderr)
    ok = (result.returncode == 0) == expect_ok
    print(f"-- {label}: {'PASS' if ok else 'FAIL'} (exit {result.returncode})")
    return ok, result.stdout + result.stderr


def pack_repo_packages(repo_root, artifacts, env):
    artifacts.mkdir(parents=True, exist_ok=True)
    for project in PACK_PROJECTS:
        ok, _ = run(
            ["dotnet", "pack", project, "-c", "Release", "-o", str(artifacts)],
            repo_root, env, expect_ok=True, label=f"pack {project}",
        )
        if not ok:
            sys.exit(f"packing {project} failed, see output above")


def write_project(project_dir, files, artifacts):
    project_dir.mkdir(parents=True)
    (project_dir / "nuget.config").write_text(NUGET_CONFIG.format(artifacts=artifacts))
    for name, content in files.items():
        (project_dir / name).write_text(content)


def check_quickstart(readme_text, artifacts, work_dir, env):
    csproj = extract_snippet(readme_text, CSPROJ_MARKERS, "quickstart csproj")
    assembly_info = extract_snippet(readme_text, ASSEMBLY_MARKERS, "quickstart assembly info")
    scenario = extract_snippet(readme_text, SCENARIO_MARKERS, "quickstart scenario")
    project_dir = work_dir / "quickstart"
    write_project(
        project_dir,
        {
            "MyMod.Tests.csproj": csproj,
            "AssemblyInfo.cs": assembly_info,
            "MarkerScenarios.cs": scenario,
        },
        artifacts,
    )
    ok, _ = run(["dotnet", "test", "-c", "Release"], project_dir, env, expect_ok=True, label="quickstart")
    return ok


def check_assert_without_xunit(artifacts, work_dir, env, atlas_version):
    project_dir = work_dir / "assert-without-xunit"
    csproj = ASSERT_ONLY_CSPROJ.format(atlas_version=atlas_version)
    write_project(project_dir, {"Project.csproj": csproj, "AssertUsage.cs": ASSERT_ONLY_CS}, artifacts)
    ok, _ = run(["dotnet", "build", "-c", "Release"], project_dir, env, expect_ok=True, label="assert-without-xunit")
    return ok


def check_xunit_v3_rejected(artifacts, work_dir, env, atlas_version):
    project_dir = work_dir / "xunit-v3-rejected"
    csproj = XUNIT_V3_CSPROJ.format(atlas_version=atlas_version)
    write_project(project_dir, {"Project.csproj": csproj, "Placeholder.cs": XUNIT_V3_CS}, artifacts)
    ok, output = run(["dotnet", "build", "-c", "Release"], project_dir, env, expect_ok=False, label="xunit-v3-rejected")
    if "ATLAS001" not in output:
        print("-- xunit-v3-rejected: FAIL, build did not fail with Atlas's ATLAS001 error")
        ok = False
    if "CS0433" in output:
        print("-- xunit-v3-rejected: FAIL, build fell through to the raw CS0433 ambiguity instead")
        ok = False
    return ok


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--repo-root", required=True, type=Path)
    parser.add_argument("--work-dir", required=True, type=Path)
    args = parser.parse_args()

    if not os.environ.get("VINTAGE_STORY"):
        sys.exit("VINTAGE_STORY must be set to a Vintage Story install; the quickstart check boots a headless server")

    repo_root = args.repo_root.resolve()
    readme_text = (repo_root / "README.md").read_text()
    atlas_version = read_atlas_version(repo_root)
    check_readme_pin(readme_text, atlas_version)

    work_dir = args.work_dir.resolve()
    if repo_root in work_dir.resolve().parents or work_dir.resolve() == repo_root:
        sys.exit(
            "--work-dir is inside --repo-root: the synthetic projects would inherit this "
            "repo's own Directory.Build.props (StyleCop, LangVersion 12). Use a directory "
            "outside the repo, e.g. under $RUNNER_TEMP or a scratch dir."
        )
    if work_dir.exists():
        shutil.rmtree(work_dir)
    work_dir.mkdir(parents=True)
    artifacts = work_dir / "artifacts"

    env = dict(os.environ)
    env["NUGET_PACKAGES"] = str(work_dir / "nuget-cache")
    env["DOTNET_CLI_UI_LANGUAGE"] = "en"
    env["DOTNET_NOLOGO"] = "1"
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"

    pack_repo_packages(repo_root, artifacts, env)

    results = {
        "quickstart": check_quickstart(readme_text, artifacts, work_dir, env),
        "assert-without-xunit": check_assert_without_xunit(artifacts, work_dir, env, atlas_version),
        "xunit-v3-rejected": check_xunit_v3_rejected(artifacts, work_dir, env, atlas_version),
    }

    print("\n== summary ==")
    for name, ok in results.items():
        print(f"{name}: {'PASS' if ok else 'FAIL'}")

    if not all(results.values()):
        sys.exit("one or more quickstart checks failed, see output above")


if __name__ == "__main__":
    main()
