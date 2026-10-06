# tools/

Maintenance scripts for ReplayLogger. Not part of the build (the .csproj
does not reference this folder) - run manually from the repo root with
Node.js.

## audit-engine-mirror.js

Guards against silent divergence between the two hand-mirrored logging
engines, `Core/PantheonLogger.cs` and `HoGSorting/HoGLogger.cs` (see
`replaylogger-refactor-plan` memory for why they're separate files and not
merged). Nothing in C# enforces that a feature added to one is also added
to the other - this script is that check, run by hand.

Two independent checks:

- **Hook symmetry** - flags a MonoMod/ModHooks subscription registered in
  one engine but not the other. Known, legitimate one-sided hooks (real
  architectural differences, e.g. Pantheon's multi-boss sequence hooks or
  HoG's own boss-HP-detection event) are listed in `KNOWN_ONE_SIDED_HOOKS`
  inside the script with the reason - only genuinely new asymmetry is
  reported as actionable.
- **Body drift** - flags a method that exists under the same name in both
  files whose body changed on only one side since the last accepted
  baseline (`engine-mirror-baseline.json`, committed to the repo). This is
  not "these two differ" (many legitimately do) - only "this one changed
  here and not there since we last checked."

### When to run it

After any session that touches both `PantheonLogger.cs` and `HoGLogger.cs`,
before committing/pushing.

### Usage

```
node tools/audit-engine-mirror.js
```

Reports findings, exits 1 if there's anything to review, 0 if clean.

If a reported item is a genuine bug (forgot to mirror a change), go fix the
other engine, then re-run.

If a reported item is fine as-is (a deliberate one-sided hook - add it to
`KNOWN_ONE_SIDED_HOOKS`; a deliberate one-sided method change - just
accept it), run:

```
node tools/audit-engine-mirror.js --update-baseline
```

to record the current state as the new baseline for future runs.

`node tools/audit-engine-mirror.js --debug-list-methods` dumps the full
list of currently-tracked shared method names, for debugging the script
itself.
