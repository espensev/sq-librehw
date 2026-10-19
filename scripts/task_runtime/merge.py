from __future__ import annotations

import shutil
import subprocess
from pathlib import Path
from typing import Any, Callable

from .state import coerce_int

DEFAULT_WORKTREE_ROOT_CANDIDATES = (
    Path(".worktrees"),
    Path(".codex/worktrees"),
    Path(".claude/worktrees"),
    Path("worktrees"),
)


def resolve_recorded_path(path_text: str, *, root: Path) -> Path:
    candidate = Path(str(path_text or "").strip())
    if not candidate.is_absolute():
        candidate = root / candidate
    return candidate.resolve()


def display_runtime_path(path: Path, *, relative_path_fn: Callable[[Path], str]) -> str:
    try:
        return relative_path_fn(path)
    except (ValueError, OSError):
        return str(path)


def candidate_worktree_roots(
    state: dict,
    *,
    root: Path,
    resolve_recorded_path_fn: Callable[[str], Path],
) -> set[Path]:
    roots: set[Path] = set()
    # Prefer the provider-neutral Codex default, but keep legacy roots visible
    # during recovery so older installed runtimes can still be reconciled.
    for relative in DEFAULT_WORKTREE_ROOT_CANDIDATES:
        candidate = (root / relative).resolve()
        if candidate.exists():
            roots.add(candidate)
    # Task metadata is evidence to reconcile, never authority to scan or delete
    # arbitrary parents. Registered external worktrees are found by Git.
    return {r for r in roots if r.exists()}


def run_git_runtime(args: list[str], *, root: Path, timeout: int = 30) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=str(root),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
    )


def git_worktree_inventory(*, root: Path) -> dict:
    try:
        result = run_git_runtime(["worktree", "list", "--porcelain"], root=root)
    except (OSError, subprocess.SubprocessError) as exc:
        return {"available": False, "error": str(exc), "worktrees": []}

    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        return {"available": False, "error": detail, "worktrees": []}

    worktrees: list[dict] = []
    current: dict[str, object] = {}
    for raw_line in result.stdout.splitlines():
        line = raw_line.strip()
        if not line:
            if current:
                worktrees.append(current)
                current = {}
            continue
        key, _, value = line.partition(" ")
        if key == "worktree":
            current["path"] = str(Path(value).resolve())
        elif key == "HEAD":
            current["head"] = value
        elif key == "branch":
            current["branch"] = value.removeprefix("refs/heads/")
        elif key == "detached":
            current["detached"] = True
        elif key == "bare":
            current["bare"] = True
        elif key == "prunable":
            current["prunable"] = value
        elif key == "locked":
            current["locked"] = value
    if current:
        worktrees.append(current)

    return {"available": True, "error": "", "worktrees": worktrees}


def match_worktree_record(
    recorded_path: str,
    recorded_branch: str,
    inventory: dict,
    *,
    root: Path,
) -> dict | None:
    if not inventory.get("available"):
        return None
    branch = str(recorded_branch or "").strip()
    path = str(recorded_path or "").strip()
    normalized_path = ""
    if path:
        try:
            normalized_path = str(resolve_recorded_path(path, root=root))
        except OSError:
            normalized_path = str((root / path).resolve())

    for item in inventory.get("worktrees", []):
        item_path = str(item.get("path", "") or "")
        item_branch = str(item.get("branch", "") or "")
        if (normalized_path or branch) and (not normalized_path or Path(item_path) == Path(normalized_path)) and (not branch or item_branch == branch):
            return item
    return None


def cleanup_task_worktree(
    path_text: str,
    branch_text: str,
    inventory: dict,
    *,
    root: Path,
    load_state_fn: Callable[[], dict],
    resolve_recorded_path_fn: Callable[[str], Path],
    candidate_worktree_roots_fn: Callable[[dict], set[Path]],
    match_worktree_record_fn: Callable[[str, str, dict], dict | None],
    git_worktree_inventory_fn: Callable[[], dict],
    run_git_runtime_fn: Callable[[list[str]], subprocess.CompletedProcess[str]],
) -> dict:
    warnings: list[str] = []
    result: dict[str, object] = {
        "worktree_removed": False,
        "branch_removed": False,
        "warnings": warnings,
    }
    path_text = str(path_text or "").strip()
    branch_text = str(branch_text or "").strip()
    if not path_text and not branch_text:
        return result
    try:
        if not path_text or not branch_text:
            raise ValueError("cleanup requires both recorded path and branch")
        resolved_path = resolve_recorded_path_fn(path_text)
        fresh = git_worktree_inventory_fn()
        if not fresh.get("available") or not fresh.get("worktrees"):
            raise ValueError("cannot verify current Git worktree inventory")
        inventory.update(fresh)
        matched = match_worktree_record_fn(path_text, branch_text, fresh)
        if not matched or Path(str(matched.get("path", ""))) != resolved_path or matched.get("branch") != branch_text:
            raise ValueError("recorded path and branch do not match one registered worktree")
        primary = Path(str(fresh["worktrees"][0]["path"]))
        if resolved_path in (root.resolve(), primary) or any(key in matched for key in ("locked", "detached", "bare", "prunable")):
            raise ValueError("primary, current, locked, detached, or unavailable worktree is protected")
        status = run_git_runtime_fn(["-C", str(resolved_path), "status", "--porcelain=v1", "--untracked-files=all", "--ignored"])
        if status.returncode != 0 or status.stdout.strip():
            raise ValueError("worktree is dirty, contains ignored files, or its status is unavailable")
        head = str(matched.get("head", ""))
        tip = run_git_runtime_fn(["rev-parse", "--verify", f"refs/heads/{branch_text}"])
        if not head or tip.returncode != 0 or tip.stdout.strip() != head:
            raise ValueError("branch tip changed or cannot be verified")
        ancestor = run_git_runtime_fn(["merge-base", "--is-ancestor", head, "HEAD"])
        if ancestor.returncode != 0:
            raise ValueError("worktree commit is not proven merged into destination HEAD")
        remove_result = run_git_runtime_fn(["worktree", "remove", str(resolved_path)])
        if remove_result.returncode != 0:
            raise ValueError(f"git worktree remove failed: {(remove_result.stderr or remove_result.stdout).strip()}")
        result["worktree_removed"] = True
        refreshed = git_worktree_inventory_fn()
        inventory.update(refreshed)
        if not refreshed.get("available") or any(item.get("branch") == branch_text for item in refreshed.get("worktrees", [])):
            raise ValueError("cannot prove branch is detached after worktree removal; branch retained")
        tip = run_git_runtime_fn(["rev-parse", "--verify", f"refs/heads/{branch_text}"])
        if tip.returncode != 0 or tip.stdout.strip() != head:
            raise ValueError("branch tip changed after worktree removal; branch retained")
        delete_result = run_git_runtime_fn(["branch", "-d", "--", branch_text])
        if delete_result.returncode != 0:
            raise ValueError(f"git branch -d failed: {(delete_result.stderr or delete_result.stdout).strip()}")
        result["branch_removed"] = True
    except (ValueError, OSError, subprocess.SubprocessError) as exc:
        warnings.append(f"Cleanup retained remaining state for {path_text}: {exc}")
    return result


def merge_runtime(
    agent_ids: list[str] | None = None,
    *,
    plan_id: str | None = None,
    load_state_fn: Callable[[], dict],
    save_state_fn: Callable[[dict], None],
    now_iso_fn: Callable[[], str],
    ensure_task_runtime_fields_fn: Callable[[dict], bool],
    normalize_string_list_fn: Callable[[object], list[str]],
    safe_resolve_fn: Callable[[str], Path],
    persist_execution_manifest_fn: Callable[..., None],
    resolve_recorded_path_fn: Callable[[str], Path],
    display_runtime_path_fn: Callable[[Path], str],
    match_worktree_record_fn: Callable[[str, str, dict], dict | None],
    cleanup_task_worktree_fn: Callable[[str, str, dict], dict],
    git_worktree_inventory_fn: Callable[[], dict],
    run_git_runtime_fn: Callable[[list[str]], subprocess.CompletedProcess[str]],
) -> dict:
    state = load_state_fn()
    inventory = git_worktree_inventory_fn()
    selected = {item.lower() for item in (agent_ids or []) if str(item or "").strip()}
    candidates = [
        task for task in state.get("tasks", {}).values() if task.get("status") == "done" and (not selected or task.get("id") in selected)
    ]
    candidates.sort(key=lambda item: (coerce_int(item.get("group", 0) or 0), item.get("id", "")))

    owners: dict[str, dict] = {}
    merged: list[dict] = []
    skipped: list[dict] = []
    conflicts: list[dict] = []
    cleanup: list[dict] = []
    now = now_iso_fn()

    backup_method = "none"
    # Never move user work into an implicit stash. Fail before copying anything
    # if the destination cannot be proven clean; source worktrees remain intact.
    try:
        if not inventory.get("available"):
            raise ValueError("Git inventory unavailable; merge refused")
        status = run_git_runtime_fn(["status", "--porcelain=v1", "--untracked-files=all"])
        if status.returncode != 0 or status.stdout.strip():
            raise ValueError("Destination is dirty or status unavailable; commit or preserve changes before merging")
    except (ValueError, OSError, subprocess.SubprocessError) as exc:
        return {"status": "conflicts", "merged": [], "skipped": [],
                "conflicts": [{"id": task["id"], "files": [], "reason": str(exc)} for task in candidates],
                "cleanup": [], "backup_method": backup_method, "detail": str(exc)}

    for task in candidates:
        ensure_task_runtime_fields_fn(task)
        existing_merge = task.get("merge", {})
        if str(existing_merge.get("status", "") or "") in {"merged", "noop"} and str(existing_merge.get("merged_at", "") or ""):
            skipped.append({"id": task["id"], "reason": "already_merged", "status": existing_merge.get("status", "")})
            continue

        task_result = task.get("agent_result", {})
        if str(task_result.get("status", "") or "").lower() != "done":
            skipped.append({"id": task["id"], "reason": "no_done_result"})
            continue

        files = [item.replace("\\", "/") for item in normalize_string_list_fn(task_result.get("files_modified", []))]
        worktree_text = str(task_result.get("worktree_path", "") or task.get("launch", {}).get("worktree_path", "") or "")
        branch_text = str(task_result.get("branch", "") or task.get("launch", {}).get("branch", "") or "")
        if not files:
            task["merge"] = {
                "status": "noop",
                "applied_files": [],
                "conflicts": [],
                "merged_at": now,
                "detail": "No files were reported by the agent result.",
            }
            merged.append({"id": task["id"], "status": "noop", "applied_files": []})
            cleanup_entry = cleanup_task_worktree_fn(worktree_text, branch_text, inventory)
            cleanup.append({"id": task["id"], **cleanup_entry})
            continue

        if not worktree_text:
            task["merge"] = {
                "status": "conflict",
                "applied_files": [],
                "conflicts": files,
                "merged_at": now,
                "detail": "Missing recorded worktree path for merge.",
            }
            conflicts.append({"id": task["id"], "files": files, "reason": "missing_worktree"})
            continue

        matched = match_worktree_record_fn(worktree_text, branch_text, inventory)
        if matched:
            worktree_root = Path(str(matched.get("path", "")))
            if matched.get("branch"):
                branch_text = str(matched.get("branch", "") or "")
        else:
            conflicts.append({"id": task["id"], "files": files, "reason": "recorded path/branch is not a registered worktree"})
            continue
        if not worktree_root.exists():
            task["merge"] = {
                "status": "conflict",
                "applied_files": [],
                "conflicts": files,
                "merged_at": now,
                "detail": f"Recorded worktree path not found: {worktree_text}",
            }
            conflicts.append({"id": task["id"], "files": files, "reason": "missing_worktree_path"})
            continue

        applied_files: list[str] = []
        task_conflicts: list[str] = []
        notes: list[str] = []
        group = coerce_int(task.get("group", 0) or 0)
        for rel_path in files:
            prior = owners.get(rel_path)
            if prior and prior["id"] != task["id"] and group <= prior["group"]:
                task_conflicts.append(rel_path)
                notes.append(f"{rel_path} also modified by Agent {prior['id'].upper()} in group {prior['group']}")
                continue
            source = (worktree_root / Path(rel_path)).resolve()
            try:
                source.relative_to(worktree_root.resolve())
            except ValueError:
                task_conflicts.append(rel_path)
                notes.append(f"{rel_path} escapes worktree root")
                continue
            if not source.exists() or not source.is_file():
                task_conflicts.append(rel_path)
                notes.append(f"{rel_path} missing from recorded worktree")
                continue
            dest = safe_resolve_fn(rel_path)
            ignored = run_git_runtime_fn(["check-ignore", "--", str(dest)])
            if ignored.returncode not in (0, 1) or (ignored.returncode == 0 and dest.exists()):
                task_conflicts.append(rel_path)
                notes.append(f"{rel_path} is an existing ignored destination or could not be checked")
                continue
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, dest)
            applied_files.append(rel_path)
            owners[rel_path] = {"id": task["id"], "group": group}
            if prior and prior["id"] != task["id"] and group > prior["group"]:
                notes.append(f"{rel_path} superseded Agent {prior['id'].upper()} by dependency order")

        merge_status = "merged"
        if task_conflicts:
            merge_status = "conflict"
        elif not applied_files:
            merge_status = "noop"
        task["merge"] = {
            "status": merge_status,
            "applied_files": applied_files,
            "conflicts": task_conflicts,
            "merged_at": now,
            "detail": "; ".join(notes) if notes else "",
        }
        if task_conflicts:
            conflicts.append({"id": task["id"], "files": task_conflicts, "reason": task["merge"]["detail"] or "merge conflict"})
        else:
            merged.append({"id": task["id"], "status": merge_status, "applied_files": applied_files})
            cleanup_entry = cleanup_task_worktree_fn(worktree_text, branch_text, inventory)
            cleanup.append({"id": task["id"], **cleanup_entry})

    overall_merge_status = "nothing_to_merge"
    if conflicts:
        overall_merge_status = "conflicts"
    elif merged:
        if any(item.get("status") == "merged" for item in merged):
            overall_merge_status = "merged"
        else:
            overall_merge_status = "noop"
    elif skipped and all(item.get("reason") == "already_merged" for item in skipped):
        overall_merge_status = "already_merged"

    if plan_id:
        persist_execution_manifest_fn(
            state,
            plan_id=plan_id,
            status="merge_conflicts" if overall_merge_status == "conflicts" else "merged",
            merge={
                "status": overall_merge_status,
                "completed_at": now,
                "merged_agents": [item["id"] for item in merged],
                "conflict_agents": [item["id"] for item in conflicts],
                "cleanup": cleanup,
            },
        )

    save_state_fn(state)
    return {
        "status": overall_merge_status,
        "merged": merged,
        "skipped": skipped,
        "conflicts": conflicts,
        "cleanup": cleanup,
        "backup_method": backup_method,
    }


def cmd_merge(
    args: Any,
    *,
    merge_runtime_fn: Callable[..., dict],
    emit_json_fn: Callable[[dict], None],
) -> None:
    raw_agents = str(getattr(args, "agents", "") or "").strip().lower()
    agent_ids = None
    if raw_agents and raw_agents != "all":
        agent_ids = [item.strip().lower() for item in raw_agents.split(",") if item.strip()]
    payload = merge_runtime_fn(agent_ids)
    if getattr(args, "json", False):
        emit_json_fn(payload)
        return
    print(f"Merged {len(payload['merged'])} task(s); {len(payload['conflicts'])} conflict set(s); {len(payload['skipped'])} skipped.")
