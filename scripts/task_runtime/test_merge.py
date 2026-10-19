"""Preservation regressions using disposable, real Git worktrees."""
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from . import merge, orchestration


class MergeSafetyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="lhm-merge-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "repo"
        self.root.mkdir()
        self.git("init", "-b", "main")
        self.git("config", "user.email", "fixture@example.invalid")
        self.git("config", "user.name", "Fixture")
        (self.root / "file.txt").write_text("original")
        (self.root / ".gitignore").write_text("ignored.txt\n")
        self.git("add", ".")
        self.git("commit", "-m", "base")
        self.lane = Path(self.temp.name) / "lane"
        self.git("worktree", "add", "-b", "lane", str(self.lane))

    def git(self, *args):
        result = merge.run_git_runtime(list(args), root=self.root)
        self.assertEqual(0, result.returncode, result.stderr)
        return result.stdout

    def inventory(self):
        return merge.git_worktree_inventory(root=self.root)

    def cleanup_lane(self, path=None, branch="lane", inventory_fn=None, run_fn=None):
        return merge.cleanup_task_worktree(
            str(path or self.lane), branch, self.inventory(), root=self.root,
            load_state_fn=lambda: {"tasks": {}},
            resolve_recorded_path_fn=lambda p: merge.resolve_recorded_path(p, root=self.root),
            candidate_worktree_roots_fn=lambda s: {Path(self.temp.name)},
            match_worktree_record_fn=lambda p, b, i: merge.match_worktree_record(p, b, i, root=self.root),
            git_worktree_inventory_fn=inventory_fn or self.inventory,
            run_git_runtime_fn=run_fn or (lambda args: merge.run_git_runtime(args, root=self.root)),
        )

    def assert_preserved(self, result):
        self.assertFalse(result["worktree_removed"])
        self.assertFalse(result["branch_removed"])
        self.assertTrue(result["warnings"])
        self.assertTrue(self.lane.exists())
        self.git("show-ref", "--verify", "refs/heads/lane")

    def test_cleanup_preserves_tracked_untracked_staged_and_ignored_files(self):
        for kind in ("tracked", "untracked", "staged", "ignored"):
            with self.subTest(kind=kind):
                name = "file.txt" if kind in ("tracked", "staged") else kind + ".txt"
                path = self.lane / name
                path.write_text("valuable " + kind)
                if kind == "staged":
                    self.git("-C", str(self.lane), "add", name)
                self.assert_preserved(self.cleanup_lane())
                self.assertEqual("valuable " + kind, path.read_text())
                if kind in ("tracked", "staged"):
                    self.git("-C", str(self.lane), "restore", "--staged", "--worktree", name)
                else:
                    path.unlink()

    def test_cleanup_preserves_unmerged_commits(self):
        (self.lane / "file.txt").write_text("unmerged")
        self.git("-C", str(self.lane), "commit", "-am", "unmerged")
        self.assert_preserved(self.cleanup_lane())

    def test_cleanup_removes_clean_merged_lane(self):
        result = self.cleanup_lane()
        self.assertTrue(result["worktree_removed"], result)
        self.assertTrue(result["branch_removed"], result)
        self.assertFalse(self.lane.exists())

    def test_cleanup_preserves_unregistered_directory(self):
        orphan = Path(self.temp.name) / "orphan"
        orphan.mkdir()
        (orphan / "valuable.txt").write_text("keep")
        self.assert_preserved(self.cleanup_lane(orphan, ""))
        self.assertEqual("keep", (orphan / "valuable.txt").read_text())

    def test_cleanup_rejects_mismatched_path_and_branch(self):
        self.assert_preserved(self.cleanup_lane(branch="main"))

    def test_cleanup_rejects_primary_and_locked_worktrees(self):
        self.assert_preserved(self.cleanup_lane(self.root, "main"))
        self.git("worktree", "lock", str(self.lane))
        self.assert_preserved(self.cleanup_lane())

    def test_cleanup_requires_fresh_inventory(self):
        self.assert_preserved(self.cleanup_lane(inventory_fn=lambda: {"available": False, "worktrees": []}))

    def test_cleanup_preserves_branch_if_post_remove_inventory_fails(self):
        fresh = self.inventory()
        with patch.object(self, "inventory", side_effect=[fresh, fresh, {"available": False, "worktrees": []}]):
            result = self.cleanup_lane(inventory_fn=self.inventory)
        self.assertTrue(result["worktree_removed"])
        self.assertFalse(result["branch_removed"])
        self.assertTrue(result["warnings"])
        self.git("show-ref", "--verify", "refs/heads/lane")

    def test_cleanup_does_not_force_when_files_arrive_after_status_check(self):
        def race(args):
            if args[:2] == ["worktree", "remove"]:
                (self.lane / "late.txt").write_text("keep")
            return merge.run_git_runtime(args, root=self.root)
        self.assert_preserved(self.cleanup_lane(run_fn=race))
        self.assertEqual("keep", (self.lane / "late.txt").read_text())

    def test_cleanup_keeps_branch_if_ordinary_delete_refuses(self):
        self.git("branch", "upstream")
        (self.lane / "file.txt").write_text("new")
        self.git("-C", str(self.lane), "commit", "-am", "new")
        self.git("merge", "--ff-only", "lane")
        self.git("branch", "--set-upstream-to=upstream", "lane")
        result = self.cleanup_lane()
        self.assertTrue(result["worktree_removed"])
        self.assertFalse(result["branch_removed"])
        self.assertTrue(result["warnings"])
        self.git("show-ref", "--verify", "refs/heads/lane")

    def test_recorded_paths_do_not_authorize_parent_directories(self):
        state = {"tasks": {"a": {"launch": {"worktree_path": str(self.lane)}}}}
        roots = merge.candidate_worktree_roots(
            state, root=self.root, resolve_recorded_path_fn=lambda p: Path(p).resolve())
        self.assertNotIn(self.lane.parent, roots)

    def merge_lane(self, files=None, inventory_fn=None):
        state = {"tasks": {"a": {"id": "a", "status": "done", "agent_result": {
            "status": "done", "files_modified": files or ["file.txt"],
            "worktree_path": str(self.lane), "branch": "lane"}}}}
        return merge.merge_runtime(
            load_state_fn=lambda: state, save_state_fn=lambda s: None,
            now_iso_fn=lambda: "2026-10-19T00:00:00Z",
            ensure_task_runtime_fields_fn=lambda t: False,
            normalize_string_list_fn=list, safe_resolve_fn=lambda p: self.root / p,
            persist_execution_manifest_fn=lambda *a, **k: None,
            resolve_recorded_path_fn=lambda p: Path(p).resolve(), display_runtime_path_fn=str,
            match_worktree_record_fn=lambda p, b, i: merge.match_worktree_record(p, b, i, root=self.root),
            cleanup_task_worktree_fn=lambda p, b, i: self.cleanup_lane(p, b),
            git_worktree_inventory_fn=inventory_fn or self.inventory,
            run_git_runtime_fn=lambda args: merge.run_git_runtime(args, root=self.root),
        )

    def test_merge_refuses_dirty_destination_without_stashing(self):
        for staged in (False, True):
            with self.subTest(staged=staged):
                (self.root / "file.txt").write_text("local work")
                if staged:
                    self.git("add", "file.txt")
                before = self.git("status", "--porcelain=v1", "--untracked-files=all")
                result = self.merge_lane()
                self.assertEqual("conflicts", result["status"])
                self.assertEqual("local work", (self.root / "file.txt").read_text())
                self.assertEqual(before, self.git("status", "--porcelain=v1", "--untracked-files=all"))
                self.assertEqual("", self.git("stash", "list"))
                self.assertTrue(self.lane.exists())

    def test_merge_refuses_untracked_destination(self):
        (self.root / "untracked.txt").write_text("keep")
        self.assertEqual("conflicts", self.merge_lane()["status"])
        self.assertEqual("keep", (self.root / "untracked.txt").read_text())

    def test_merge_refuses_existing_ignored_destination(self):
        (self.root / "ignored.txt").write_text("keep")
        (self.lane / "ignored.txt").write_text("replacement")
        self.assertEqual("conflicts", self.merge_lane(files=["ignored.txt"])["status"])
        self.assertEqual("keep", (self.root / "ignored.txt").read_text())
        self.assertTrue(self.lane.exists())

    def test_merge_refuses_unavailable_inventory(self):
        result = self.merge_lane(inventory_fn=lambda: {"available": False, "worktrees": []})
        self.assertEqual("conflicts", result["status"])
        self.assertEqual("original", (self.root / "file.txt").read_text())

    def test_merge_copies_committed_changes_but_retains_unmerged_source(self):
        (self.lane / "file.txt").write_text("new")
        self.git("-C", str(self.lane), "commit", "-am", "change")
        result = self.merge_lane()
        self.assertEqual("merged", result["status"])
        self.assertEqual("new", (self.root / "file.txt").read_text())
        self.assertTrue(self.lane.exists())
        self.assertFalse(result["cleanup"][0]["branch_removed"])

    def test_recovery_preserves_unregistered_orphans_and_reports_no_removal(self):
        roots = self.root / ".worktrees"
        orphan = roots / "orphan"
        orphan.mkdir(parents=True)
        (orphan / "valuable.txt").write_text("keep")
        result = orchestration.recover_runtime(
            prune_orphans=True, load_state_fn=lambda: {"tasks": {}}, save_state_fn=lambda s: None,
            now_iso_fn=lambda: "2026-10-19T00:00:00Z", ensure_task_runtime_fields_fn=lambda t: False,
            empty_agent_result_fn=dict, empty_launch_record_fn=dict, empty_merge_record_fn=dict,
            recompute_ready_fn=lambda s: None, sync_execution_manifest_after_recover_fn=lambda s, r: None,
            resolve_recorded_path_fn=lambda p: Path(p).resolve(), display_runtime_path_fn=str,
            candidate_worktree_roots_fn=lambda s: {roots},
            match_worktree_record_fn=lambda p, b, i: merge.match_worktree_record(p, b, i, root=self.root),
            cleanup_task_worktree_fn=lambda p, b, i: self.cleanup_lane(p, b),
            git_worktree_inventory_fn=self.inventory, safe_resolve_fn=lambda p: self.root / p,
        )
        self.assertTrue((orphan / "valuable.txt").exists())
        self.assertEqual("keep", (orphan / "valuable.txt").read_text())
        self.assertFalse(result["pruned_orphans"])
        self.assertTrue(result["orphan_details"][0]["cleanup"]["warnings"])


if __name__ == "__main__":
    unittest.main()
