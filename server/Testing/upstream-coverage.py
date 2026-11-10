"""Which closed vrcx-team/VRCX issues and PRs have a regression test, a not-applicable entry, or neither.

Usage (Python 3.11+, no packages):
    python -I server/Testing/upstream-coverage.py <path to the vrcx-github export> [--todo N] [--grep WORD]

The export (all_closed.json, issues/{n}.json, pulls/{n}.json) stays outside the repo; pass its
path. Tests are found by their [Issue(n)] / [Pr(n)] attributes under server/*Tests*/, and the
not-applicable ledger is server/API.IntegrationTests/Regressions/NotApplicable.cs.
--todo N prints the N oldest untriaged items; --grep WORD keeps only those whose title or body
mentions WORD (case-insensitive), for example --grep friend.
"""

import argparse
import json
import re
from pathlib import Path

SERVER = Path(__file__).resolve().parent.parent


def tested() -> set[int]:
    found: set[int] = set()
    for source in SERVER.glob("*Tests*/**/*.cs"):
        if "obj" in source.parts or "bin" in source.parts:
            continue
        for match in re.finditer(r"\b(?:Issue|Pr)\((\d+)\)", source.read_text(encoding="utf-8")):
            found.add(int(match.group(1)))
    return found


def not_applicable() -> set[int]:
    ledger = SERVER / "API.IntegrationTests" / "Regressions" / "NotApplicable.cs"
    return {int(n) for n in re.findall(r"^\s*\[(\d+)\]\s*=", ledger.read_text(encoding="utf-8"), re.M)}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("export", type=Path)
    parser.add_argument("--todo", type=int, default=0)
    parser.add_argument("--grep", default="")
    args = parser.parse_args()

    items = json.loads((args.export / "all_closed.json").read_text(encoding="utf-8"))
    done, skipped = tested(), not_applicable()
    todo = [i for i in items if i["number"] not in done and i["number"] not in skipped]
    if args.grep:
        word = args.grep.lower()
        todo = [i for i in todo if word in (i.get("title") or "").lower() or word in (i.get("body") or "").lower()]

    print(f"closed items: {len(items)}, tested: {len(done)}, not applicable: {len(skipped)}, untriaged: {len(todo)}")
    for item in sorted(todo, key=lambda i: i["number"])[: args.todo]:
        kind = "pr" if item.get("pull_request") else "issue"
        print(f"  #{item['number']:<5} {kind:<5} {item['title']}")


if __name__ == "__main__":
    main()
