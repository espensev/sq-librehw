import unittest

from .verify import verify_runtime


class VerifyRuntimeCriteriaTests(unittest.TestCase):
    def run_verify(self, command_passed: bool) -> tuple[dict, list[dict]]:
        state = {
            "tasks": {
                "a": {
                    "id": "a",
                    "name": "implemented-task",
                    "status": "done",
                    "merge": {
                        "status": "merged",
                        "conflicts": [],
                    },
                }
            }
        }
        plan = {"id": "plan-001"}
        persisted: list[dict] = []

        result = verify_runtime(
            recover_runtime_fn=lambda **_kwargs: {},
            sync_state_fn=lambda: state,
            resolve_plan_summary_for_runtime_fn=lambda _plan_id: {},
            load_plan_from_summary_fn=lambda _summary: plan,
            resolve_plan_for_verify_fn=lambda _state: plan,
            explain_verify_resolution_failure_fn=lambda _state: "not found",
            normalize_verify_profile_fn=lambda profile: profile or "default",
            plan_owned_files_fn=lambda _plan, _state: [],
            commands_cfg_fn=lambda: {"test": "test-command"},
            configured_runtime_commands_fn=lambda **_kwargs: [
                ("test", "test-command")
            ],
            placeholder_command_reason_fn=lambda _command: "",
            run_runtime_command_fn=lambda label, command: {
                "label": label,
                "command": command,
                "passed": command_passed,
            },
            plan_exit_criteria_fn=lambda _plan: [
                "Automated tests pass.",
                "An attended normal-user smoke is recorded.",
            ],
            persist_execution_manifest_fn=lambda _state, **kwargs: persisted.append(
                kwargs
            ),
            save_state_fn=lambda _state: None,
            now_iso_fn=lambda: "2026-07-31T00:00:00+00:00",
        )
        return result, persisted

    def test_passed_automation_does_not_claim_exit_criteria(self) -> None:
        result, persisted = self.run_verify(command_passed=True)

        self.assertTrue(result["passed"])
        self.assertEqual("passed", result["status"])
        self.assertEqual(
            [
                {
                    "criterion": "Automated tests pass.",
                    "status": "not_evaluated",
                    "passed": None,
                },
                {
                    "criterion": "An attended normal-user smoke is recorded.",
                    "status": "not_evaluated",
                    "passed": None,
                },
            ],
            result["criteria"],
        )
        self.assertEqual("verified", persisted[0]["status"])

    def test_failed_automation_still_leaves_criteria_unevaluated(self) -> None:
        result, persisted = self.run_verify(command_passed=False)

        self.assertFalse(result["passed"])
        self.assertEqual("failed", result["status"])
        self.assertTrue(all(item["passed"] is None for item in result["criteria"]))
        self.assertEqual("verification_failed", persisted[0]["status"])


if __name__ == "__main__":
    unittest.main()
