import tomllib
import unittest
from pathlib import Path

from .specs import configured_runtime_commands


ROOT = Path(__file__).resolve().parents[2]


class ProjectControlConfigTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.config = tomllib.loads(
            (ROOT / ".codex" / "skills" / "project.toml").read_text(
                encoding="utf-8"
            )
        )

    def test_generic_verification_cannot_create_a_release_candidate(self) -> None:
        commands = configured_runtime_commands(self.config["commands"])

        self.assertEqual(["compile", "build", "test"], [item[0] for item in commands])
        self.assertTrue(all(item[1].startswith("dotnet ") for item in commands))
        self.assertFalse(
            any("New-LhmRelease.ps1" in command for _label, command in commands)
        )

        candidate_gate = self.config["build-gate"]["release-candidate"]
        self.assertIn("New-LhmRelease.ps1", candidate_gate["build"])

    def test_campaign_runtime_tests_are_mapped_to_owned_implementations(self) -> None:
        mappings = self.config["smart-test"]["mappings"]

        self.assertNotIn("scripts/task_runtime/*.py", mappings)
        self.assertEqual(
            ["scripts/task_runtime/test_execution.py"],
            mappings["scripts/task_runtime/execution.py"],
        )
        self.assertEqual(
            ["scripts/task_runtime/test_verify.py"],
            mappings["scripts/task_runtime/verify.py"],
        )

    def test_docs_sync_includes_nested_architecture_documents(self) -> None:
        self.assertIn(
            "docs/**/*.md",
            self.config["docs-sync"]["tier2"]["files"],
        )


if __name__ == "__main__":
    unittest.main()
