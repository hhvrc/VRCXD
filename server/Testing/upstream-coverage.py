"""Which closed vrcx-team/VRCX issues and PRs have a regression test, a not-applicable entry, or neither.

Usage (Python 3.11+, no packages):
    python -I server/Testing/upstream-coverage.py <path to the vrcx-github export> [--todo N] [--grep WORD]

The export (all_closed.json, issues/{n}.json, pulls/{n}.json) stays outside the repo; pass its
path. Tests are found by the conventions in server/Testing/README.md:
  - server: [Issue(n)] / [Pr(n)] attributes under server/*Tests*/;
  - web client: a vitest describe/test/it title starting "upstream #n" under client/src/;
  - agent: a Rust test function named upstream_<n>_... under agent/src/.
The not-applicable ledger is server/API.IntegrationTests/Regressions/NotApplicable.cs.
--todo N prints the N oldest untriaged items; --grep WORD keeps only those whose title or body
mentions WORD (case-insensitive), for example --grep friend.
"""

import argparse
import json
import re
from pathlib import Path

SERVER = Path(__file__).resolve().parent.parent
ROOT = SERVER.parent
SKIP = {"obj", "bin", "node_modules", "target", ".svelte-kit", "build"}

SERVER_TAG = re.compile(r"\b(?:Issue|Pr)\((\d+)\)")
# describe('upstream #1178 ...'), test("upstream #1611 #1758 ..."): every #n right after "upstream".
CLIENT_TAG = re.compile(r"""\b(?:describe|test|it)(?:\.\w+)*\(\s*['"`]upstream((?:\s+#\d+)+)""")
# fn upstream_645_..., fn upstream_1611_1758_... (the test functions; the numbers before the words).
AGENT_TAG = re.compile(r"\bfn\s+upstream((?:_\d+)+)_")


def sources(top: Path, pattern: str):
    for source in top.glob(pattern):
        if SKIP.isdisjoint(source.relative_to(ROOT).parts):
            yield source


def tested() -> dict[str, set[int]]:
    found: dict[str, set[int]] = {"server": set(), "client": set(), "agent": set()}
    for source in sources(SERVER, "*Tests*/**/*.cs"):
        found["server"].update(int(n) for n in SERVER_TAG.findall(source.read_text(encoding="utf-8")))
    for source in sources(ROOT / "client" / "src", "**/*.test.ts"):
        for numbers in CLIENT_TAG.findall(source.read_text(encoding="utf-8")):
            found["client"].update(int(n) for n in re.findall(r"\d+", numbers))
    for source in sources(ROOT / "agent" / "src", "**/*.rs"):
        for numbers in AGENT_TAG.findall(source.read_text(encoding="utf-8")):
            found["agent"].update(int(n) for n in re.findall(r"\d+", numbers))
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
    closed = {i["number"] for i in items}
    by_part = tested()
    done = set().union(*by_part.values())
    skipped = not_applicable()
    todo = [i for i in items if i["number"] not in done and i["number"] not in skipped]
    if args.grep:
        word = args.grep.lower()
        todo = [i for i in todo if word in (i.get("title") or "").lower() or word in (i.get("body") or "").lower()]

    parts = ", ".join(f"{part} {len(numbers & closed)}" for part, numbers in by_part.items())
    print(f"closed items: {len(items)}, tested: {len(done & closed)} ({parts}), "
          f"not applicable: {len(skipped & closed)}, untriaged: {len(todo)}")
    if open_tagged := sorted(done - closed):
        print(f"tagged but not closed (yet): {', '.join(f'#{n}' for n in open_tagged)}")
    for item in sorted(todo, key=lambda i: i["number"])[: args.todo]:
        kind = "pr" if item.get("pull_request") else "issue"
        print(f"  #{item['number']:<5} {kind:<5} {item['title']}")


if __name__ == "__main__":
    main()
