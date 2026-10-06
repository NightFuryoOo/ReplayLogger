# GodhomeQoL Boss Manipulate — real phase-threshold reference

**Correction (2026-09-23), re-tested against a real fresh Pantheon 5 log the same day - only 3 of 7
bosses actually needed the fix, not all 7.** Original finding: the "Gorb's part resolved... benign
FSM-settling jitter" conclusion below was incomplete for Gorb specifically - a user log showed Gorb
settling to 444/254 (not the flat vanilla=700/400) with a clean, consistent ratio (444/700=0.634,
254/400=0.635 - the same "identical ratio across every threshold" signature already used to diagnose
Galien/Pure Vessel/Nightmare King Grimm). Decompiling `GorbHelper.cs`/`XeroHelper.cs`/
`MarkothHelper.cs`/`WingedNoskHelper.cs`/`BrokenVesselHelper.cs`/`LostKinHelper.cs`/`NoEyesHelper.cs`
found each has its own `P5<Boss>Hp` constant below `Default<Boss>MaxHp` and disables custom-phase
override when P5 mode engages, so `declaredMaxHp:` scaling (the Galien/Pure Vessel/NKG mechanism) was
added to all 7 as a hypothesis.

**A fresh real P5 log the same day proved the hypothesis only holds for 3 of them.** Gorb (455/260),
Xero (325), and Nosk Hornet (375) now match their scaled vanilla exactly - confirmed correct, kept.
**Broken Vessel, Lost Kin, and No Eyes reverted** - the same log showed their REAL observed values
still exactly equal to the flat, un-scaled default (Broken Vessel 420/370/220/110, Lost Kin Phase2
1150, No Eyes 150/90), while the newly-added scaling computed a lower number and flagged a brand new
false "differs from vanilla" that didn't exist before this session touched them. So having a distinct
`P5<Boss>Hp` constant + `UseCustomPhase = false` in GodhomeQoL's own source is **not** a reliable
predictor of whether a boss's phase threshold actually scales down in real gameplay - Dung
Defender/White Defender already demonstrated this exact false-positive-constant trap on 2026-09-12,
and this pass shows it isn't limited to just those two. **Markoth: confirmed by the user's own known-correct game values (2026-09-23), still pending live log
confirmation.** This same log's Markoth fight never reached the phase-check state (FSM Inventory
fallback fired instead, "mapping not found this fight"), so it couldn't be checked against a real log
directly. But the user independently confirmed the real numbers from personal knowledge: Markoth is
650 max HP / 325 phase-transition HP in genuine Pantheon 5, versus 950 max HP / 475 phase-transition
HP in HoG on Ascended+Radiant difficulty - the exact 475/950 = 325/650 = 0.5 ratio (phase transition
is always half of max HP) confirms genuine proportional scaling, and `475 × 650 / 950 = 325.0`
(clean, no rounding) exactly matches the real P5 value. This means the currently-hardcoded
`vanilla=475` on `GG_Ghost_Markoth_V` was calibrated for the HoG Ascended+Radiant context sharing that
same arena name, not real Pantheon 5 - the same category of mismatch as Gorb/Xero/Nosk Hornet, just
against a different wrong baseline (a different real HoG difficulty combo, not "no scaling at all").
`declaredMaxHp: 950` should therefore produce the correct 325 the moment `ResolveReferenceMaxHp` reads
Markoth's real live max HP as 650 during an actual P5 fight - kept in place with high confidence, but
still worth a real log confirmation like the other six got, since Broken Vessel/Lost Kin/No Eyes
already showed source-code reasoning alone isn't always enough.

**Takeaway for next time:** a `P5<Boss>Hp` constant existing in GodhomeQoL's decompiled source is not
sufficient evidence on its own to add `declaredMaxHp:` scaling - it must be confirmed against a real
log showing the boss's phase threshold value itself differing from default in a live, untouched P5
fight (not just inferred from the mod's own constants), the same way Gorb/Xero/Nosk Hornet were
confirmed and Broken Vessel/Lost Kin/No Eyes were refuted here.

**Implementation status (2026-09-11): COMPLETE for HoG.** `Tracking/BossPhaseThresholdTracker.cs`
implements this - every Pattern A boss the user asked for is wired and confirmed live in real
test logs. Per-boss status:

**2026-09-12: extended beyond HoG.** `BossPhaseThresholdTracker` was only ever hooked into
`HoGSorting/HoGLogger.cs` (single-boss HoG practice-room challenges). It's now ALSO wired into
`Core/PantheonLogger.cs` (which is actually the main `ReplayLogger` partial class, and drives both
Pantheon runs entered via `GG_Boss_Door`, and manual/regular boss recordings started by hotkey
anywhere - both share the same `isPlayChalange`-gated `HeroController_FixedUpdate`/`Close()`
machinery, see `Core/ReplayLogger.ManualLogging.cs`). **No changes were needed to `KnownBosses` or
the arena-name matching** - Pantheon runs reuse the exact same `GG_*` scene names as standalone HoG
statues, so every already-wired spec applies unchanged the moment the tracker is actually called.
Scenes outside Godhome are explicitly out of scope (per the user's own call) and simply produce no
data - the arena name never matches any known spec, which is the correct, silent behavior.
Reset() was added at all 5 places `GodhomeQolTracker.Reset()` already fires in those two files
(Pantheon session start, abort/delete cleanup, normal `Close()`, manual-logging session start, and
its start-failure rollback) - deliberately NOT on the mid-pantheon boss-to-boss transition, since
the tracker's own `trackedStates` list already supports multiple different arenas accumulating
within one continuous log, which is exactly what a Pantheon run's final report should look like
(one section per boss actually fought that run).

**Confirmed live 2026-09-12 across all 5 Pantheons (P1-P5).** The section correctly appeared in
every log, accumulating one block per boss actually encountered that run, with `(none)` for
untouched thresholds exactly as designed. One real finding along the way, now fixed - see the
"Vanilla scales with max HP" section below.

**Vanilla scales with max HP for Galien / Pure Vessel / Nightmare King Grimm (fixed 2026-09-12).**
The P5 (Pantheon of Hallownest) test showed all three persistently "differing from vanilla" for
the whole fight with zero logged Changes - looked at first like a leaked Boss Manipulate setting.
Root-caused by computing the ratio: Galien's two thresholds both scaled by EXACTLY 0.6500, Pure
Vessel's by ~0.8653, Nightmare King Grimm's three by ~0.758 - the same ratio across every one of a
given boss's thresholds is the signature of a genuine max-HP difference, not a literal override
(an override wouldn't produce a clean shared ratio - and Dung Defender/Gorb's numbers in the same
logs correctly did NOT fit this pattern, see the open question below). The ratios exactly match
GodhomeQoL's own declared "P5*Hp" constants - a dedicated reduced-max-HP mode specifically for
Pantheon 5 (`P5GalienHp=650` vs normal 1000, `P5PureVesselHp=1600` vs 1850,
`P5NightmareKingGrimmHp=1250` vs 1650) - these three bosses get a smaller max HP there and their
phase thresholds scale by the identical ratio. Fixed by adding `BossSpec.DeclaredMaxHp` (each
boss's own declared max-HP constant) and `BossState.ReferenceMaxHp` (the boss's real
`HealthManager.hp` read once at BossState creation, same "hp at first sight" convention `infoBoss`
already uses) - the displayed `vanilla=X` is now `VanillaDefault * ReferenceMaxHp / DeclaredMaxHp`
for these 3 bosses, 0 elsewhere. This only affects the cosmetic baseline shown on the initial state
line - real tamper detection (`Changes:`) was never affected, since it diffs the live value against
itself between polls, never against this baseline. **Not yet re-tested** with the fix in place.

**Dung Defender's part of the mystery resolved 2026-09-12 - another wrong hardcoded baseline, not
tampering.** A Pantheon 1/5 retest with Boss Manipulate confirmed fully OFF still showed Rage HP
transitioning 350->400 about 500ms into the fight. Since `moduleActive=false` makes every
DungDefenderHelper handler early-exit, GodhomeQoL's own code physically cannot have caused this,
ruling out a guard leak. Added a one-off diagnostic dumping every `IntCompare` in the "Rage?" state
on change: found exactly ONE compare, its operand still named "Rage HP" (never blanked), holding
400 - ruling out the "reading a second unrelated compare" theory too. The user independently
confirmed in-game that the real phase transition happens at 400 HP. Conclusion: 350 was simply the
wrong hardcoded constant (probably valid for a different context, e.g. HoG-Radiant, that was never
actually checked against a live Pantheon fight); 400 is correct. Fixed the `VanillaDefault` directly
(400, no proportional scaling needed here - this one's just a flat wrong number) and removed the
diagnostic. White Defender's own real Pantheon value (currently still 600) has not been
independently checked the same way - worth a look if it ever shows the same "differs from vanilla"
pattern in a real Pantheon log.

**Gorb's part resolved 2026-09-12 - benign FSM-settling jitter, same class as Dung Defender's
original transient, not a shared-ratio max-HP scaling issue.** Gorb showed ratios of 0.20 and 0.175
on its two thresholds in the P1/P5 logs (not a shared ratio like Galien/Pure Vessel/NKG, so ruled
out as proportional max-HP scaling) - but retesting showed the exact transient values were
inconsistent across otherwise-identical runs depending on poll timing (140->455 in one run,
70->260 in another), the signature of the game's own non-GodhomeQoL initialization logic briefly
holding a placeholder value for up to ~500ms after room entry before setting the real one - the
same phenomenon behind Dung Defender's original 350->400/~500ms mystery, just not yet named as a
general class at the time that section above was written. Not a bug, not tampering, and not fixable
by adjusting a constant (there is no single "correct" transient value to fix it to). The user
explicitly accepted this as inherent, benign noise a reviewer has to judge by eye - the same call
already made for the invincibility grace-period noise in the mana/soul-telemetry work - rather than
something to keep chasing with more diagnostics.

| Arena | Bosses/thresholds | Status |
|---|---|---|
| `GG_Radiance` | Absolute Radiance, all 4 phases | **Confirmed live** (2 separate real tests, incl. one showing an ~18s delayed real application vs. the mod's own claimed-instant setting) |
| `GG_Broken_Vessel` | Phase2 (Spawn Balloon) | **Confirmed live** |
| `GG_Broken_Vessel` | Token 1/2/3 (Shake Token Control → Phase3/4/5) | **Confirmed live** - root-caused (was NamedVariable, blanked on custom same as Dung/White Defender) and fixed to StateIntCompare+FilterName="HP". Retested: Spawn 990, Token1 930, Token2 880, Token3 830, all correctly flagged vs vanilla. |
| `GG_Dung_Defender` / `GG_White_Defender` | Rage HP | **Confirmed live** (StateIntCompare fix retested on both - Dung Defender: 900 vs vanilla=350, incl. a real live mid-fight change 900→550→900 captured correctly; White Defender: 1400 vs vanilla=600, matches the mod's own self-report) |
| `GG_Ghost_Galien` | Phase2/3 (Summon HP1/HP2) | **Confirmed live** (901/700, 801/400) |
| `GG_Ghost_Gorb_V` / `GG_Ghost_Gorb` | Phase2/3 (Double/Triple HP) | **Confirmed live** on both arenas, incl. real mid-fight changes captured (900↔700/800↔400 full arena, 500↔455/400↔260 P1 arena) |
| `GG_Grimm_Nightmare` | Rage Phase1/2/3 (HP1/2/3) | **Confirmed live** (1278/1238, 886/826, 474/414) |
| `GG_Ghost_Xero_V` / `GG_Ghost_Xero` | Phase2 (Half HP) | **Confirmed live** (800→450 live change on full arena; 325/325 - no change - on P1 arena) |
| `GG_Ghost_No_Eyes_V` / `GG_Ghost_No_Eyes` | Phase2/3 (Esc 1/2) | **Confirmed live** on both arenas (751/150+701/90 full; 531/150+501/90 P1) |
| `GG_Ghost_Markoth_V` / `GG_Ghost_Markoth` | Phase2 (Rage HP) | **Confirmed live** on both arenas, real mid-fight changes captured (900→475 full, 600→325 P1) |
| `GG_Lost_Kin` | Phase2 (Spawn) | **Confirmed live** (1500 vs vanilla=1150) |
| `GG_Lost_Kin` | Token 1/2/3 (Shake Token Control) | Wired, but stayed N/A in the one live test (Spawn on the same boss worked fine). Likely the real FSM uses different state/operand names than Broken Vessel's identical C# template assumes. **Deprioritized per user's call ("забей, там нормально что ловит 2 фазы")** - Phase2 alone is enough, not chasing this further. |
| `GG_Oblobbles` | — | **Excluded 2026-09-11 (user's call)**: phase transition happens when one of the two live bees is killed, not a gated HP check - nothing here worth hiding. Removed from `KnownBosses`. |
| `GG_Sly` (Nailsage Sly) | — | **Excluded 2026-09-11 (user's call)**: phase transition happens the instant phase-1 HP hits 0, same as Oblobbles - nothing here worth hiding. Removed from `KnownBosses`. |
| `GG_Grimm` (Troupe Master Grimm) | Rage Phase1/2/3 | **Confirmed live** (951/750, 901/500, 851/250) - first real-world confirmation of `StateIntTestToBoolByOrder` |
| `GG_Hollow_Knight` (Pure Vessel) | Phase2/3 (Half/Quarter HP) | **Confirmed live** (1800/1232, 1750/616) - first real-world confirmation of `StateIntCompareByOrder` |
| `GG_Traitor_Lord` | Phase2 Hp | **Confirmed live** (1250 vs vanilla=500) - first real-world confirmation of `StateIntCompareExcludeOperand` |
| `GG_Nosk_Hornet` (Winged Nosk) | Phase2 (Half HP) | **Confirmed live** (1000 vs vanilla=525), incl. `FindFsmWithState` correctly resolving the FSM with no fixed name (log shows `[state:Choose Attack]`) |
| `GG_Nosk_V` (Nosk) | Phase2 Hp | **Confirmed live** (930 vs vanilla=560), `[state:Roof Jump?]` in the log confirms `FindFsmWithState` |
| `GG_Nosk` (NoskP2) | Phase2 Hp | **Confirmed live** (630 vs vanilla=560) |

**Key gotcha found (2026-09-11), applies to any future boss added here:** GodhomeQoL doesn't
always leave the FSM variable's `Name` alone. `DungDefenderHelper`/`WhiteDefenderHelper`'s
`SetPhase2ThresholdOnFsm`, when applying a CUSTOM (non-vanilla) threshold, does
`((NamedVariable)thresholdOperand).Name = string.Empty;` on the IntCompare operand - detaching it
from the named variable and turning it into a bare literal. Confirmed live via a diagnostic dump of
every FsmInt on the FSM: an *unnamed* (`""`) variable held exactly the custom value, while
`FsmVariables.GetFsmInt("Rage HP")` returned nothing findable by that name anymore. **This means
`FsmVariables.GetFsmInt(name)` (NamedVariable strategy) is only safe once you've checked whether
that boss's own apply code re-asserts the name every time (Absolute Radiance's `SetCheckStateThreshold`
does - safe) or blanks it when custom (Dung/White Defender - unsafe, must use StateIntCompare
instead, reading the IntCompare action's operand directly regardless of its current name).** Always
check the boss's actual `Set*ThresholdOnFsm`/`Apply*` method for this before trusting NamedVariable.

**Diagnostic tool built into the tracker itself:** when a known arena's spec produces zero tracked
data for a whole fight, `BossPhaseThresholdTracker` automatically dumps every `PlayMakerFSM` under
the boss object (name, hierarchy path, active/inactive) AND every `FsmInt` variable on each (name +
live value) into the log, under a section literally called
`"Boss Phase Thresholds - FSM Inventory (mapping not found this fight):"`. This is how both the
Dung/White Defender name-blanking issue and Broken Vessel's real FSM presence were found - use a
failing test's own log output first before guessing or re-decompiling.

**Resolved: Broken Vessel's Token 1/2/3 "never arm" mystery (2026-09-11).** The earlier hypothesis
("boss may not use these phases in real gameplay") was wrong - re-reading `BrokenVesselHelper.
SetThresholdOnCheckState` in full found it does the *exact same* name-blanking on custom thresholds
as Dung/White Defender, gated by `IsHpCompare` (i.e. it only touches the IntCompare action whose
operand is named "HP" - `FilterName="HP"` in tracker terms). The tracker was reading it as
NamedVariable, which stopped finding anything the moment a custom value got applied - identical
root cause, just not yet spotted on this boss. Fixed by switching to StateIntCompare with
FilterName="HP" (same pattern as the Spawn state above it). Not yet re-tested live.

**Tooling used (session-specific, may need recreating in a future session):** `ilspycmd` (global
dotnet tool, `ilspycmd -p -usepdb -o <dir> GodhomeQoL.dll` decompiles the whole assembly with real
names since the mod ships a matching `.pdb`). A small throwaway console app
(`ReplayVerifier.Core`-referencing, decrypts a `.log` with the private key and prints plaintext) was
used to check live test results - recreate from `ReplayVerifier.Core.Security.EncryptedReplayLogReader`
if the scratchpad copy is gone.

**All "clean StateIntCompare/NamedVariable" Pattern A bosses are wired AND confirmed live** (Galien,
Gorb, GorbP1, Nightmare King Grimm, Xero, XeroP2, No Eyes, NoEyesP4, Markoth, MarkothP4, Lost Kin -
Phase2 only, see Token 1/2/3 note above), verified against GodhomeQoL's actual apply code first, then
retested in 12 real logs on 2026-09-11. This closes out every boss reachable with the current
NamedVariable/StateIntCompare(+FilterName) model. (Oblobbles and Nailsage Sly were also wireable this
way but deliberately removed - see the table above.)

**Deliberately excluded, not a gap:** Oblobbles and Nailsage Sly. Both were technically wireable
(plain NamedVariable, no name-blanking risk) but the user's own call was that neither has anything
worth hiding - their "phase 2" is reached by killing a bee / hitting 0 HP, not by a threshold an
attacker could quietly move.

**The last 6 bosses are now wired too (2026-09-11), with 3 new read strategies added to
`TryReadThreshold`:**
- **`StateIntCompareByOrder` / `StateIntTestToBoolByOrder`** - some bosses pick which of several
  same-type actions in one state holds which threshold purely by occurrence order (`OccurrenceIndex`
  0/1/2...), never by an operand name. **Pure Vessel** (`GG_Hollow_Knight`, "Phase?" state, two
  `IntCompare` actions: Half HP/Quarter HP) and **Troupe Master Grimm** (`GG_Grimm`, "Balloon?"
  state, three `IntTestToBool` actions - a genuinely different PlayMaker action type, field `int2`
  not `integer2` - Rage HP1/2/3) both work this way.
- **`StateIntCompareExcludeOperand`** - the inverse of the existing FilterName matching: **Traitor
  Lord** (`GG_Traitor_Lord`), **Winged Nosk** (`GG_Nosk_Hornet`), **Nosk/NoskP2**
  (`GG_Nosk_V`/`GG_Nosk`) blank the THRESHOLD operand's Name on every apply (even a vanilla
  restore), while the boss's own live HP operand (named "HP") is never touched - so this reads
  whichever operand is NOT named "HP" instead of assuming integer2.
- **`ThresholdSpec.FsmName == null`** now means "no fixed FSM name for this boss - find any FSM
  containing this state instead" (via the new `FindFsmWithState`), for Winged Nosk/Nosk/NoskP2 which
  GodhomeQoL itself only ever identifies by state presence, never by name.

Traitor Lord's vanilla default is a plain constant (500) regardless of max HP, unlike most other
bosses where it scales with max HP.

**All three new strategies confirmed live in one pass (2026-09-11)** - all 6 bosses/7 arenas worked
on the first real test after the fix, no further debugging needed. **This closes out the entire
Pattern A roster** - every boss the user asked for is now wired and confirmed live, except the two
deliberately-out-of-scope pieces noted above (Lost Kin's Token 1/2/3, Oblobbles, Nailsage Sly).

Pattern B (False Knight/Failed Champion) and Pattern C (single-phase bosses) remain explicitly OUT of
scope per the user's decision - see below.

---

Extracted 2026-09-11 by decompiling `GodhomeQoL.dll` (with its matching `.pdb`, so names below are
the mod's real source names, not decompiler guesses) via `ilspycmd -p -usepdb`. Goal: find where
each boss's REAL phase-transition threshold lives in the game's own code, independent of
GodhomeQoL's own `[LocalSetting]` fields (which are just the mod's self-reported configuration —
see `Tracking/GodhomeQolBossManipulateTracker.cs`, which already reads those). Reading the values
below directly would catch a threshold changed by any means (GodhomeQoL, a hidden mod, or an
external memory editor), not just changes made through GodhomeQoL's own menu.

## How GodhomeQoL itself applies an override (confirmed via `AbsoluteRadianceHelper`/`BrokenVesselHelper`)

The boss's own `PlayMakerFSM` component (a real Team Cherry component, not part of GodhomeQoL) has
an `FsmInt` variable holding the HP threshold, checked by an `IntCompare` action inside a specific
FSM state (usually named `"Check 1"`, `"Check 2"`, etc.) that decides when to transition phases.
GodhomeQoL's `Set*ThresholdsOnFsm`-style methods write to BOTH the named `FsmVariables.GetFsmInt(name)`
AND the literal `IntCompare.integer2.Value` inside that check state (belt-and-suspenders - some
bosses' wiring uses one, some the other). Reading either (ideally both, for robustness) gives the
real, ground-truth value the boss's own logic actually consults.

## Pattern A — clean FsmInt variable(s) in a named FSM, checked by IntCompare in "Check N" states

The most common and cleanest pattern. `FsmVariables.GetFsmInt(<Variable>).Value` on the named FSM
is the real threshold.

| Boss module | FSM name | Variable name(s) | Vanilla default(s) |
|---|---|---|---|
| `AbsoluteRadianceHelper` | `Phase Control` | `P2 Spike Waves`, `P3 A1 Rage`, `P4 Stun1`, `P5 Acend` | 2600 / 2150 / 1850 / 1100 |
| `BrokenVesselHelper` | `Shake Token Control` | `Token 1`, `Token 2`, `Token 3` (+ `HP` for summon HP) | Phase2=420, Phase3=370, Phase4=220, Phase5=110 |
| `LostKinHelper` | `Shake Token Control` | `Token 1`, `Token 2`, `Token 3` (+ `HP`) | Phase2=1150, Phase3=550, Phase4=350, Phase5=175 |
| `DungDefenderHelper` | `Dung Defender` | `Rage HP` (check state `"Rage?"`), `Raged` | Phase2=350 |
| `WhiteDefenderHelper` | `Dung Defender` (shares Dung Defender's FSM structure) | `Rage HP` (check state `"Rage?"`) | Phase2=600 |
| `GalienHelper` | `Summon Minis` | `Summon HP1`, `Summon HP2` (states `Idle`/`Idle 2`/`Summon Antic`/`Summon Antic 2`) | Phase2=700, Phase3=400 |
| `GorbHelper` / `GorbP1Helper` | `Attacking` | `Double HP`, `Triple HP` (transition states `Double Pause`/`Anim`) | Full: 700/400, P1: 455/260 |
| `MarkothHelper` / `MarkothP4Helper` | `Rage Check` (state `Check`); also `Attacking`/`Shield Attack` hold `Rage` | `Rage HP` | Full: 475, P4: 325 |
| `NightmareKingGrimmHelper` | (rage-phase FSM, name not separately declared - shares `TroupeMasterGrimmHelper`'s constants) | `Rage HP 1`, `Rage HP 2`, `Rage HP 3` | 1238 / 826 / 414 |
| `TroupeMasterGrimmHelper` | (check state `"Balloon?"`) | `Rage HP 1`, `Rage HP 2`, `Rage HP 3` | 750 / 500 / 250 |
| `PureVesselHelper` | (not separately named) | `Half HP`, `Quarter HP` | Phase2=1232, Phase3=616 |
| `TraitorLordHelper` | `Mantis` (check state `"Slam?"`) | (no separately named variable const - likely inline; needs deeper look) | Phase2=500 |
| `WingedNoskHelper` | (check state `"Choose Attack"`; summon states `Summon`/`Summon Roar`) | `Half HP`, `HP`, `Enemy Count` | Phase2=525, Summon=1(custom)/8(vanilla) |
| `XeroHelper` / `XeroP2Helper` | `Sword Summon` (check state `Check`); `Attacking` holds `Rage` | `Half HP`, `Rage` | Full: 450, P2: 325 |
| `NoEyesHelper` / `NoEyesP4Helper` | `Escalation` (states `Idle`/`Idle 2`) | `Esc 1`, `Esc 2` | 150 / 90 (same for both variants) |
| `OblobblesHelper` | `Set Rage` | `HP Add`, `HP Max` | vanilla Add=200, Max=750; per-side Phase1=750/750 |
| `NailsageSlyHelper` | (not separately named) | `Ascended HP` (current), `Acended HP` (legacy misspelled variable name - old logs may use either) | Phase1=1200, Phase2=600 |
| `NoskHelper` / `NoskP2Helper` | (check state `"Roof Jump?"`) | (no separately named variable const found - needs deeper look) | Phase2=560 (both variants) |

## Pattern B — armor/state-transition based (no simple IntCompare threshold)

False Knight and Failed Champion use a fundamentally different mechanic: phases are driven by named
FSM STATE transitions (`"To Phase 2"`, `"To Phase 3"`, `"Opened 2"`, `"Hit 2"`) plus counters
(`"Recover HP"`, `"Rages"`, `"Stunned Amount"`), not a single HP-threshold int compare. Reading
"has this boss reached FSM state X" (or the counter variables) is the real signal here, not a
phase-HP int. Needs its own read strategy, different from Pattern A.

| Boss module | States involved | Variables | Vanilla `DefaultArmorPhaseHp` |
|---|---|---|---|
| `FalseKnightHelper` | `To Phase 2`, `To Phase 3`, `Opened 2`, `Hit 2` | `Recover HP`, `Rages`, `Stunned Amount` | 560 |
| `FailedChampionHelper` | same state/variable names as False Knight | same | 600 |

## Pattern C — single-phase bosses (only Max HP, no phase thresholds to catch)

No `PhaseN` concept applies - only the boss's own `HealthManager` HP (or summon HP for the ones
with adds) matters, which is a different, simpler ground-truth target (read `HealthManager.hp`
directly) than an FSM phase-threshold. Not a gap for THIS feature, just doesn't need it:

`BroodingMawlekHelper`/`BroodingMawlekP1Helper`, `CrystalGuardianHelper`, `EnragedGuardianHelper`,
`ElderHuHelper`, `GruzMotherHelper`/`GruzMotherP1Helper`, `HornetProtectorHelper`, `MarmuHelper`/
`MarmuP2Helper`, `MassiveMossChargerHelper`, `PaintmasterSheoHelper`, `SoulWarriorHelper`/
`SoulWarriorP1Helper`, `VengeflyKingP1Helper`, `WatcherKnightHelper`.

## Needs a second, deeper pass — has PhaseN Hp defaults but no FSM/Variable name constant found

The quick constant-grep (`grep -E "FsmName|VariableName|StateName"`) didn't turn up a declared name
for these - either they reference the FSM/variable by an inline string literal instead of a named
const (most likely), or a naming convention this pass's regex didn't match. Each of these DOES have
real per-phase `DefaultXHp` constants (so the phases definitely exist), just not yet traced to the
exact FSM/variable location:

`HiveKnightHelper` (Phase2=580, Phase3=350), `GodTamerHelper` (Lobster/Lancer Hp=1000/1000),
`MantisLordHelper` (Phase1=500, Phase2S1/S2=600/600), `OroMatoHelper` (Oro Phase1/2=800/1000,
Mato=1000), `SoulMasterHelper` (Phase1/2=900/600), `SoulTyrantHelper` (Phase1/2=1200/650),
`SisterOfBattleHelper` (Phase1=600, Phase2 S1/S2/S3=950 each), `HornetSentinelHelper` (Phase2=480),
`VengeflyKing` (full, non-P1 - Left/Right/SummonMax/SummonLimit/SummonAttackLimit, a multi-target
boss, structurally different again).

## Already covered separately

`ZoteHelper` (has its own dedicated tracker, `Tracking/ZoteHelperSettingsTracker.cs` - Boss HP,
Flying/Hopping HP, Summon Limit, no multi-phase HP thresholds since Zote isn't phase-gated by HP
the same way) and `CollectorPhases` (Collector Hp/Buzzer/Roller/Spitter Hp, Custom Summon Limit -
also not phase-HP-threshold shaped, it's a fixed set of summon-related values).

## Next steps (not started)

1. Deeper pass on the "needs a second look" list above - check the actual `Apply*`/`Set*OnFsm`
   method bodies (not just declared constants) for inline FSM/variable name literals.
2. Design the actual ReplayLogger-side reader: for Pattern A bosses, reflect into the boss's
   `PlayMakerFSM` component by FSM name, then `FsmVariables.GetFsmInt(variableName).Value` - same
   general reflection style already used elsewhere in this codebase (`ReflectionMemberAccessCache`),
   just targeting PlayMaker's FSM object graph instead of GodhomeQoL's own static fields.
3. Decide how deep to go on Pattern B (False Knight/Failed Champion - different shape) and Pattern C
   (single-phase bosses - would need `HealthManager.hp` reading instead, a separate mechanism).
4. Wire into the "state + Changes" polling model already used everywhere (see
   `Tracking/GodhomeQolBossManipulateTracker.cs`), diffing the REAL value the same way, so a
   real-time change (by any means - GodhomeQoL, hidden mod, or external memory editor) gets caught
   with a timestamp, exactly like every other tracker added this session.
