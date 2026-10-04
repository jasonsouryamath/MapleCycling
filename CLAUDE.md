# MapleRide — Session Rules

## Start of every session (before doing anything else)
Read, from the project root. This is lean on purpose: reading all four files whole cost about 115k tokens per session.
1. `COORDINATION.md`: read the Rules, the "Token & context budget" section, your task rows, and the last ~40 Log lines.
   Older Log entries are in `docs/COORDINATION_LOG_ARCHIVE.md`.
2. `CLAUDE_HANDOFF.md`: read only the newest entry (the end of the file).
3. `COPILOT_HANDOFF.md` and `MAPLERIDE_PROJECT_CONTEXT.md`: don't read them whole. `grep` for the region, package
   or topic you're working on, and read those sections only.

If the MapleRide folder or any of these files can't be reached, say so plainly. Don't guess what they contain.
Follow the board's "Token & context budget" rules. When your context gets close to full, post a CHECKPOINT Log line
first, then `/compact` and carry on. If you hit a usage limit, post a PAUSED line with the reset time.

## Before ending a session
- Update `CLAUDE_HANDOFF.md` with what was done, the current state and the next steps.
- Add an entry to the Log section of `COORDINATION.md`.

## Git-connected working folder and sync
- The primary working project is `C:/Users/jason/OneDrive/Desktop/MapleRide`.
  This folder now contains the Git repository and Git LFS objects. Make game
  changes here so the existing Unity runners and agents continue using the same project.
- `origin` is `https://github.com/jasonsouryamath/MapleCycling.git`; the shared
  published branch is `main`. `C:/Users/jason/MapleCycling-upload` is an earlier
  upload snapshot, not the active working project.
- Sync happens through explicit commits, pushes and pulls, not automatic uploads.
  For completed user-requested work intended to sync, validate the task, inspect
  `git status --short`, stage only that task's files, commit, and run
  `git push origin main`. Verify the push succeeded before reporting it synced.
- Agents share this checkout and its Git index. Inspect staged changes before
  committing; never include another agent's staged or unstaged work accidentally.
  Preserve existing local edits and continue claiming files on `COORDINATION.md`.
- Use `git fetch origin` to check remote changes. Fast-forward updates only when
  the working tree and index are clean. Coordinate dirty-tree updates or branch
  divergence through the board; never force-push, discard edits, or stash another
  agent's work to make a sync succeed.
- Keep Git LFS enabled and preserve `.gitattributes` and `.gitignore`. Do not
  commit Unity caches, machine settings, logs, credentials or backup artifacts.

- **PLAYTIME:** if `tools/unity/PLAYTIME.json` exists, the user is playing MapleRide (Agent HQ 🎮 button). Save your work, post a CHECKPOINT line, stay out of Unity (the runner waits) and do only non-Unity work until a `PLAYTIME OVER` Log line. See board Rule 5.
