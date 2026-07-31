import unittest

from .execution import _derive_status_payload


def done_tasks() -> dict[str, dict]:
    return {
        "a": {"id": "a", "status": "done"},
        "b": {"id": "b", "status": "done"},
    }


class DeriveStatusPayloadTests(unittest.TestCase):
    def test_verified_campaign_is_terminal_even_with_done_tasks(self) -> None:
        manifest = {
            "status": "verified",
            "merge": {"status": "merged"},
            "verify": {"status": "passed", "passed": True},
        }

        self.assertEqual(
            ("verified", "done"),
            _derive_status_payload(done_tasks(), manifest),
        )

    def test_merged_campaign_requests_verification(self) -> None:
        manifest = {
            "status": "",
            "merge": {"status": "merged"},
            "verify": {"status": "", "passed": None},
        }

        self.assertEqual(
            ("ready_for_verify", "verify"),
            _derive_status_payload(done_tasks(), manifest),
        )

    def test_unmerged_done_tasks_request_merge(self) -> None:
        manifest = {
            "status": "",
            "merge": {"status": ""},
            "verify": {"status": "", "passed": None},
        }

        self.assertEqual(
            ("ready_for_merge", "merge"),
            _derive_status_payload(done_tasks(), manifest),
        )

    def test_failed_verification_requests_review(self) -> None:
        manifest = {
            "status": "verification_failed",
            "merge": {"status": "merged"},
            "verify": {"status": "failed", "passed": False},
        }

        self.assertEqual(
            ("verification_failed", "review_blockers"),
            _derive_status_payload(done_tasks(), manifest),
        )

    def test_merge_conflict_overrides_stale_passed_verification(self) -> None:
        manifest = {
            "status": "merge_conflicts",
            "merge": {"status": "conflicts"},
            "verify": {"status": "passed", "passed": True},
        }

        self.assertEqual(
            ("merge_conflicts", "review_blockers"),
            _derive_status_payload(done_tasks(), manifest),
        )

    def test_verification_failure_overrides_stale_verified_status(self) -> None:
        manifest = {
            "status": "verified",
            "merge": {"status": "merged"},
            "verify": {"status": "failed", "passed": False},
        }

        self.assertEqual(
            ("verification_failed", "review_blockers"),
            _derive_status_payload(done_tasks(), manifest),
        )

    def test_merge_conflict_overrides_stale_verified_manifest(self) -> None:
        manifest = {
            "status": "verified",
            "merge": {"status": "conflicts"},
            "verify": {"status": "passed", "passed": True},
        }

        self.assertEqual(
            ("merge_conflicts", "review_blockers"),
            _derive_status_payload(done_tasks(), manifest),
        )


if __name__ == "__main__":
    unittest.main()
