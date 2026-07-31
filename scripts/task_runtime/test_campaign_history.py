"""Regression coverage for the campaign-history acceptance ledger.

The rules live in `docs/architecture/campaign-control-plane.md`. This module
enforces the ones that can be checked mechanically, and every rule function
takes a parsed ledger so it can be exercised against a fixture rather than only
against the real file.

The central rule: a campaign may not be recorded `accepted` or `closed` while
one of its exit criteria is still `open`, or is `waived` without a complete
waiver record. Plan status is a separate axis and cannot stand in for this --
`executed` means the tooling registered the agents, not that the work finished.
"""

from __future__ import annotations

import json
import re
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
LEDGER_PATH = REPO_ROOT / "docs" / "campaign-history.md"
PLANS_DIR = REPO_ROOT / "data" / "plans"

LEDGER_STATES = ("registered", "implemented", "accepted", "closed")
CRITERION_STATES = ("met", "open", "waived")
ACCEPTED_STATES = ("accepted", "closed")
WAIVER_FIELDS = ("waived by", "date", "reason", "accepted risk", "reopens if")

_PLAN_HEADING = re.compile(r"^##\s+(plan-\d+)\b")
_CELL_SPLIT = re.compile(r"(?<!\\)\|")


def _cells(line: str) -> list[str]:
    parts = _CELL_SPLIT.split(line.strip())
    if parts and parts[0].strip() == "":
        parts = parts[1:]
    if parts and parts[-1].strip() == "":
        parts = parts[:-1]
    return [part.replace("\\|", "|").strip() for part in parts]


def _is_separator(cells: list[str]) -> bool:
    return bool(cells) and all(set(cell) <= set("-: ") and "-" in cell for cell in cells)


def _normalize(text: str) -> str:
    return " ".join(text.replace("\\|", "|").split())


def parse_ledger(text: str) -> dict:
    """Read the ledger's Markdown tables into summary, detail, and waiver records."""
    summary: list[dict] = []
    details: dict[str, list[dict]] = {}
    waivers: list[dict] = []

    section = ""
    plan_id = ""
    header: list[str] | None = None

    for raw in text.splitlines():
        line = raw.strip()

        if line.startswith("##"):
            header = None
            heading = _PLAN_HEADING.match(line)
            if heading:
                section = "detail"
                plan_id = heading.group(1)
                details.setdefault(plan_id, [])
            elif line.lower().startswith("## ledger"):
                section = "summary"
            elif line.lower().startswith("## waiver"):
                section = "waivers"
            else:
                section = ""
            continue

        if not line.startswith("|"):
            header = None
            continue

        cells = _cells(line)
        if header is None:
            header = [cell.lower() for cell in cells]
            continue
        if _is_separator(cells):
            continue

        row = dict(zip(header, cells))
        if section == "summary":
            summary.append(row)
        elif section == "detail":
            row["plan"] = plan_id
            details[plan_id].append(row)
        elif section == "waivers":
            waivers.append(row)

    return {"summary": summary, "details": details, "waivers": waivers}


# ---------------------------------------------------------------------------
# Rule functions. Each returns a list of violation strings, empty when clean.
# ---------------------------------------------------------------------------


def unknown_ledger_states(ledger: dict) -> list[str]:
    return [
        f"{row.get('plan', '?')}: unknown ledger state {row.get('ledger state', '')!r}"
        for row in ledger["summary"]
        if row.get("ledger state", "") not in LEDGER_STATES
    ]


def unknown_criterion_states(ledger: dict) -> list[str]:
    violations = []
    for plan_id, rows in ledger["details"].items():
        for row in rows:
            if row.get("state", "") not in CRITERION_STATES:
                violations.append(f"{plan_id} criterion {row.get('#', '?')}: unknown state {row.get('state', '')!r}")
    return violations


def accepted_with_open_criteria(ledger: dict) -> list[str]:
    """A campaign may not be accepted or closed while a criterion is still open."""
    violations = []
    for row in ledger["summary"]:
        if row.get("ledger state", "") not in ACCEPTED_STATES:
            continue
        plan_id = row.get("plan", "")
        for detail in ledger["details"].get(plan_id, []):
            if detail.get("state", "") == "open":
                violations.append(
                    f"{plan_id} is {row['ledger state']} but criterion {detail.get('#', '?')} is open"
                )
    return violations


def incomplete_waivers(ledger: dict) -> list[str]:
    """Every waived criterion needs a waiver record carrying all five fields."""
    records = {}
    for row in ledger["waivers"]:
        key = (row.get("plan", ""), row.get("criterion", ""))
        missing = [field for field in WAIVER_FIELDS if not row.get(field, "").strip()]
        records[key] = missing

    violations = []
    for plan_id, rows in ledger["details"].items():
        for detail in rows:
            if detail.get("state", "") != "waived":
                continue
            key = (plan_id, detail.get("#", ""))
            if key not in records:
                violations.append(f"{plan_id} criterion {detail.get('#', '?')} is waived with no waiver record")
            elif records[key]:
                violations.append(
                    f"{plan_id} criterion {detail.get('#', '?')} waiver is missing: {', '.join(records[key])}"
                )
    return violations


def count_mismatches(ledger: dict) -> list[str]:
    """The summary counts must equal the detail table they summarize."""
    violations = []
    for row in ledger["summary"]:
        plan_id = row.get("plan", "")
        states = [detail.get("state", "") for detail in ledger["details"].get(plan_id, [])]
        for column, state in (("met", "met"), ("open", "open"), ("waived", "waived")):
            declared = row.get(column, "")
            actual = states.count(state)
            if not declared.isdigit() or int(declared) != actual:
                violations.append(f"{plan_id}: summary {column}={declared!r} but detail table has {actual}")
    return violations


def load_plan(plan_id: str) -> dict:
    return json.loads((PLANS_DIR / f"{plan_id}.json").read_text(encoding="utf-8"))


class CampaignHistoryLedgerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.text = LEDGER_PATH.read_text(encoding="utf-8") if LEDGER_PATH.exists() else ""
        cls.ledger = parse_ledger(cls.text) if cls.text else {"summary": [], "details": {}, "waivers": []}
        cls.plan_files = sorted(PLANS_DIR.glob("plan-*.json"))

    def test_ledger_exists(self):
        """The ledger is a required tracked artifact, so a missing file fails rather than skips."""
        self.assertTrue(LEDGER_PATH.exists(), f"missing required ledger: {LEDGER_PATH}")
        self.assertTrue(self.text.strip(), "ledger is empty")

    def test_one_row_per_plan_both_directions(self):
        """Every plan file has a ledger row and every ledger row names a real plan file."""
        recorded = {row.get("plan", "") for row in self.ledger["summary"]}
        on_disk = {path.stem for path in self.plan_files}
        self.assertEqual(on_disk - recorded, set(), "plan files with no ledger row")
        self.assertEqual(recorded - on_disk, set(), "ledger rows with no plan file")

    def test_ledger_states_are_known(self):
        """Ledger state is one of registered, implemented, accepted, closed."""
        self.assertEqual(unknown_ledger_states(self.ledger), [])

    def test_criterion_states_are_known(self):
        """Criterion state is one of met, open, waived."""
        self.assertEqual(unknown_criterion_states(self.ledger), [])

    def test_criteria_match_the_plan_contract(self):
        """Ledger criterion text and count match plan_elements.exit_criteria exactly."""
        for path in self.plan_files:
            plan_id = path.stem
            expected = load_plan(plan_id)["plan_elements"]["exit_criteria"]
            rows = self.ledger["details"].get(plan_id, [])
            self.assertEqual(
                len(rows), len(expected), f"{plan_id}: ledger lists {len(rows)} criteria, plan JSON has {len(expected)}"
            )
            for index, criterion in enumerate(expected):
                self.assertEqual(
                    _normalize(rows[index].get("criterion", "")),
                    _normalize(criterion),
                    f"{plan_id} criterion {index + 1} text differs from the plan contract",
                )

    def test_accepted_campaigns_have_no_open_criteria(self):
        """The central transition rule."""
        self.assertEqual(accepted_with_open_criteria(self.ledger), [])

    def test_waived_criteria_have_complete_waiver_records(self):
        """A waiver missing any of the five required fields is not a waiver."""
        self.assertEqual(incomplete_waivers(self.ledger), [])

    def test_summary_counts_match_detail_tables(self):
        self.assertEqual(count_mismatches(self.ledger), [])

    def test_plan_001_attended_criterion_is_still_open(self):
        """Targeted regression on the known live case.

        plan-001's automated work is complete but its attended normal-user smoke
        is not. If plan-001 is ever legitimately accepted, this test is the
        deliberate place where that decision has to be made consciously.
        """
        row = next((item for item in self.ledger["summary"] if item.get("plan") == "plan-001"), None)
        self.assertIsNotNone(row, "plan-001 has no ledger row")
        self.assertNotIn(row["ledger state"], ACCEPTED_STATES, "plan-001 must not be accepted or closed")

        details = self.ledger["details"].get("plan-001", [])
        ninth = next((item for item in details if item.get("#") == "9"), None)
        self.assertIsNotNone(ninth, "plan-001 criterion 9 is missing from the ledger")
        self.assertEqual(ninth["state"], "open", "plan-001 criterion 9 is the attended smoke and is still open")

    def test_plan_001_contract_is_still_partial(self):
        """Automated `verified` is not attended acceptance; the plan contract stays partial."""
        plan = load_plan("plan-001")
        self.assertEqual(plan["status"], "partial")
        self.assertEqual(plan["executed_at"], "")


class CampaignHistoryRuleTests(unittest.TestCase):
    """Prove the rules bite, by running them against deliberately broken fixtures."""

    LEDGER = "\n".join(
        [
            "## Ledger",
            "",
            "| Plan | Campaign | Plan status | Ledger state | Met | Open | Waived | Evidence |",
            "| --- | --- | --- | --- | --- | --- | --- | --- |",
            "| plan-900 | Fixture | executed | {state} | 1 | 1 | 0 | none |",
            "",
            "## plan-900 — Fixture",
            "",
            "| # | Criterion | State | Evidence |",
            "| --- | --- | --- | --- |",
            "| 1 | first | met | done |",
            "| 2 | second | {second} | pending |",
            "",
            "## Waiver records",
            "",
            "| Plan | Criterion | Waived by | Date | Reason | Accepted risk | Reopens if |",
            "| --- | --- | --- | --- | --- | --- | --- |",
            "{waiver}",
        ]
    )

    def _ledger(self, state="implemented", second="open", waiver=""):
        return parse_ledger(self.LEDGER.format(state=state, second=second, waiver=waiver))

    def test_parser_reads_all_three_sections(self):
        ledger = self._ledger()
        self.assertEqual(len(ledger["summary"]), 1)
        self.assertEqual(len(ledger["details"]["plan-900"]), 2)
        self.assertEqual(ledger["waivers"], [])

    def test_open_criterion_is_allowed_while_implemented(self):
        self.assertEqual(accepted_with_open_criteria(self._ledger(state="implemented")), [])

    def test_accepted_with_open_criterion_is_rejected(self):
        violations = accepted_with_open_criteria(self._ledger(state="accepted"))
        self.assertEqual(len(violations), 1)
        self.assertIn("criterion 2 is open", violations[0])

    def test_closed_with_open_criterion_is_rejected(self):
        self.assertTrue(accepted_with_open_criteria(self._ledger(state="closed")))

    def test_waived_without_a_record_is_rejected(self):
        violations = incomplete_waivers(self._ledger(second="waived"))
        self.assertEqual(len(violations), 1)
        self.assertIn("no waiver record", violations[0])

    def test_waived_with_an_incomplete_record_is_rejected(self):
        partial = "| plan-900 | 2 | someone | 2026-07-31 |  | none |  |"
        violations = incomplete_waivers(self._ledger(second="waived", waiver=partial))
        self.assertEqual(len(violations), 1)
        self.assertIn("missing", violations[0])

    def test_waived_with_a_complete_record_is_accepted(self):
        complete = "| plan-900 | 2 | someone | 2026-07-31 | not reachable here | none | hardware arrives |"
        self.assertEqual(incomplete_waivers(self._ledger(second="waived", waiver=complete)), [])

    def test_unknown_states_are_rejected(self):
        self.assertTrue(unknown_ledger_states(self._ledger(state="finished")))
        self.assertTrue(unknown_criterion_states(self._ledger(second="probably")))

    def test_count_mismatch_is_rejected(self):
        # The fixture header declares met 1 / open 1; flipping the second
        # criterion to met makes the declared open count wrong.
        violations = count_mismatches(self._ledger(second="met"))
        self.assertTrue(any("open" in item for item in violations))


if __name__ == "__main__":
    unittest.main()
