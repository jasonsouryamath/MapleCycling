# MapleRide: instructions for ChatGPT / Codex (and any AGENTS.md-aware agent)

Several AI agents (Copilot CLI x5, Claude Code, ChatGPT/Codex, Gemini/Antigravity) work in this Unity 6 HDRP project
at once and coordinate through **`COORDINATION.md`**. Sign your board posts as **`ChatGPT`**.

- **Start lean:** read the board's Rules, its "Token & context budget" section, your task row and the last ~40 Log
  lines. Older Log entries are in `docs/COORDINATION_LOG_ARCHIVE.md`. `grep` the handoff files; never read them whole.
- **Claim before editing:** set your row's status and append a Log line. Edit only the files your row lists.
- **Unity only through** `pwsh -NoProfile -File tools/unity/run_steps.ps1 "Class.Method|chatgpt_<name>.log|1"`. Never
  start or stop Unity.exe. After C# changes, run `tools/unity/offline_compile.ps1`, then `WeatherMathValidation.Run`,
  and fix `error CS` at once.
- **Context nearly full:** post a CHECKPOINT Log line (done / next / files), let Codex compact, then continue.
- **Usage limit hit:** post `PAUSED <task>: usage limit, resets <time>; state: ...`. If the reset is more than 2 h
  away, set the row to RELEASED.

- **PLAYTIME:** if `tools/unity/PLAYTIME.json` exists, the user is playing MapleRide (Agent HQ 🎮 button). Save your work, post a CHECKPOINT line, stay out of Unity (the runner waits) and do only non-Unity work until a `PLAYTIME OVER` Log line. See board Rule 5.
