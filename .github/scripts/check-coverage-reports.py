#!/usr/bin/env python3
"""Check the OpenCover reports handed to the SonarCloud scanner.

The scanner analysis job builds the sources itself but imports most of its coverage from
reports produced by other jobs. The one way that can go wrong quietly is a report the
scanner cannot line up with the sources it analysed: it drops the file and publishes a
smaller number instead of failing, which moves the quality gate without saying so.

This runs in the report job, before `sonarscanner end`, and turns that into an error. It
reads every report, keeps the sequence points that belong to files under <root>/src, unions
them across reports the way the scanner does, and fails when a report points somewhere else,
covers nothing, or when the union lands under the floor.

The percentage printed here is plain line coverage over src, not SonarCloud's own metric
(which blends lines and conditions), so the two differ by a point or so. The floor is set to
catch a report that went missing or landed unresolvable, which costs tens of points, not to
police a small drift.

Usage:
    check-coverage-reports.py --root <checkout> --min <percent> report...
    check-coverage-reports.py --self-check
"""

import argparse
import glob
import sys
import tempfile
from pathlib import Path
from xml.etree import ElementTree


def read(path, prefix):
    """Return (covered lines, all lines) as sets of (file, line) under prefix."""
    covered, total = set(), set()
    for module in ElementTree.parse(path).getroot().iter("Module"):
        files = {f.get("uid"): f.get("fullPath") for f in module.iter("File")}
        for point in module.iter("SequencePoint"):
            name = files.get(point.get("fileid"))
            if not name or not name.startswith(prefix):
                continue
            line = (name, point.get("sl"))
            total.add(line)
            if int(point.get("vc", "0")) > 0:
                covered.add(line)
    return covered, total


def percent(covered, total):
    return 100.0 * len(covered) / len(total) if total else 0.0


REPORT = """<CoverageSession><Modules><Module><Files>
  <File uid="1" fullPath="{root}/src/Atlas/A.cs" />
  <File uid="2" fullPath="{root}/tests/Atlas.Pure.Tests/T.cs" />
</Files><Classes><Class><Methods><Method><SequencePoints>
  <SequencePoint sl="1" vc="{a}" fileid="1" />
  <SequencePoint sl="2" vc="{b}" fileid="1" />
  <SequencePoint sl="1" vc="1" fileid="2" />
</SequencePoints></Method></Methods></Class></Classes></Module></Modules></CoverageSession>"""


def self_check():
    """Two reports covering one line each: the union is 100%, not 50% and not 25%."""
    with tempfile.TemporaryDirectory() as root:
        left = Path(root, "left.xml")
        right = Path(root, "right.xml")
        left.write_text(REPORT.format(root=root, a=1, b=0))
        right.write_text(REPORT.format(root=root, a=0, b=3))
        prefix = root + "/src/"

        covered, total = read(left, prefix)
        assert (len(covered), len(total)) == (1, 2), (covered, total)  # tests/ stays out
        other, more = read(right, prefix)
        assert percent(covered, total) == 50.0
        assert percent(covered | other, total | more) == 100.0
        assert read(left, root + "/nowhere/") == (set(), set())  # a report that resolves nowhere
    print("self-check ok")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", help="checkout the scanner analyses")
    parser.add_argument("--min", type=float, help="floor for the union, percent")
    parser.add_argument("--self-check", action="store_true", help="check the union maths and exit")
    parser.add_argument("reports", nargs="*", help="report paths or globs")
    args = parser.parse_args()

    if args.self_check:
        self_check()
        return
    if not (args.root and args.min is not None and args.reports):
        parser.error("--root, --min and at least one report are required")

    prefix = args.root.rstrip("/") + "/src/"
    paths = sorted({p for pattern in args.reports for p in glob.glob(pattern, recursive=True)})
    if not paths:
        sys.exit(f"no coverage report matched {args.reports}")

    union_covered, union_total = set(), set()
    for path in paths:
        covered, total = read(path, prefix)
        if not total:
            sys.exit(
                f"{path} carries no sequence point under {prefix}. Its source paths do not "
                f"match the checkout the scanner analyses, so the scanner would drop it."
            )
        print(f"{percent(covered, total):6.2f}%  {len(covered):6d}/{len(total):6d}  {path}")
        union_covered |= covered
        union_total |= total

    merged = percent(union_covered, union_total)
    label = f"union of {len(paths)} reports"
    print(f"{merged:6.2f}%  {len(union_covered):6d}/{len(union_total):6d}  {label}")
    if merged < args.min:
        sys.exit(f"union line coverage {merged:.2f}% is under the {args.min:.2f}% floor")


if __name__ == "__main__":
    main()
