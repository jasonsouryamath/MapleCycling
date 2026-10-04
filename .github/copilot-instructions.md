# Copilot: read this first

Claude Code works on this project in parallel with you.

- **Before any task, read `COORDINATION.md`** lean: the Rules, the "Token & context budget" section, your task row,
  and the last ~40 Log lines. Only `grep` `COPILOT_HANDOFF.md` (145 KB) for the section you need; never read it whole.
- **Token & context:** follow the board's budget rules. When context gets close to full, post a CHECKPOINT Log line,
  then `/compact` and continue. If you hit a usage limit, post a PAUSED line with the reset time.
- **Unity: only one process at a time.** Always run Unity through
  `pwsh -NoProfile -File tools/unity/run_steps.ps1 "Method|copilot_x.log|1"`, which waits on a shared
  lock. Never launch or kill Unity.exe directly.
- Edit only the files the board gives you. Request anything else in the board's Log.
- After any C# change, run `WeatherMathValidation.Run` through the runner and fix `error CS`
  immediately: a broken script blocks Claude's Unity runs too.
- Log what you did and how you verified it in `COORDINATION.md`.

## Save AI credits (Copilot is our biggest spend)
Measured 2026-09-27 (the local usage log): every call re-sends the whole conversation, so **cost per call grows with
context**. It's about 4 AIC at under 100k tokens, but about 29 AIC at 600k+. Calls above 400k were 56% of all Copilot
spend.
- **Keep context small.** One task per session. When a task is DONE, start a fresh session (`/new` or `/clear`)
  instead of carrying 400k+ tokens into the next one. Compact early (around 150-200k), right after a CHECKPOINT Log line.
- **Don't poll.** Each `read_powershell` or status check is a full-price call at your current context. Use long waits
  (300-600 s), or let the runner notify you, and do real work in between.
- **Read narrowly.** Use `grep`/`view_range`; don't view whole large files. Grep Unity logs; never read them whole.
  Every token you read is re-sent on every later call.
- **Few images.** Each viewed PNG stays in context for the rest of the session. View the 1-3 frames that prove the change.
- **Pick the model by the job** (the full table is in the `mapleride-auto-router` skill):
  - Searches go to `explore` and builds, tests and runner batches go to `task`. Both run on `gpt-5.6-luna`, about 17x
    cheaper than Opus.
  - Fixes, QA and orchestration use `claude-sonnet-5`, about 2.2x cheaper.
  - Only new multi-system features and architecture go to `maplerider-builder` on `claude-opus-5.5`.
  - Never use `claude-opus-5` or `claude-opus-4.8`: they run at a 15x multiplier.
  - A running session that's still on Opus and is only coordinating or polling should switch with `/model claude-sonnet-5`.
- **Batch.** Put several edits in one call and several Unity steps in one runner call.

- **PLAYTIME:** if `tools/unity/PLAYTIME.json` exists, the user is playing MapleRide (Agent HQ 🎮 button). Save your work, post a CHECKPOINT line, stay out of Unity (the runner waits) and do only non-Unity work until a `PLAYTIME OVER` Log line. See board Rule 5.
