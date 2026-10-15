#!/usr/bin/env python3
"""Summarise Cobertura coverage reports as a Markdown table (standard library only).

Usage: python3 scripts/coverage-summary.py <directory with coverage.cobertura.xml files>

Prints line and branch coverage per assembly and in total. Written for the job summary of
.github/workflows/coverage.yml; reads the XML that coverlet.collector writes for `dotnet test
--collect:"XPlat Code Coverage"`. Test assemblies are left out - coverage of the tests says nothing.
"""
import pathlib
import sys
import xml.etree.ElementTree as ET


def main() -> int:
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else ".")
    reports = sorted(root.rglob("coverage.cobertura.xml"))
    if not reports:
        print("No coverage report found.")
        return 1

    # package name -> [lines covered, lines valid, branches covered, branches valid]
    totals: dict[str, list[int]] = {}
    for report in reports:
        for package in ET.parse(report).getroot().iter("package"):
            name = package.get("name", "?")
            if name.endswith(".Tests") or name.endswith("UiTests"):
                continue
            t = totals.setdefault(name, [0, 0, 0, 0])
            for line in package.iter("line"):
                t[1] += 1
                if int(line.get("hits", "0")) > 0:
                    t[0] += 1
                cond = line.get("condition-coverage")  # e.g. "50% (1/2)"
                if cond and "(" in cond:
                    covered, valid = cond.split("(")[1].rstrip(")").split("/")
                    t[2] += int(covered)
                    t[3] += int(valid)

    def pct(a: int, b: int) -> str:
        return f"{100 * a / b:.1f}%" if b else "—"

    print("| Assembly | Lines | Line coverage | Branch coverage |")
    print("|---|---:|---:|---:|")
    all_t = [0, 0, 0, 0]
    for name in sorted(totals):
        t = totals[name]
        all_t = [x + y for x, y in zip(all_t, t)]
        print(f"| `{name}` | {t[1]:,} | {pct(t[0], t[1])} | {pct(t[2], t[3])} |")
    print(f"| **Total** | **{all_t[1]:,}** | **{pct(all_t[0], all_t[1])}** | **{pct(all_t[2], all_t[3])}** |")
    return 0


if __name__ == "__main__":
    sys.exit(main())
