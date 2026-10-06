#!/usr/bin/env node
/*
 * audit-engine-mirror.js
 * ------------------------------------------------------------------------
 * ReplayLogger has two independent, hand-mirrored logging engines:
 *   Core/PantheonLogger.cs   (instance-based, multi-boss Pantheon runs)
 *   HoGSorting/HoGLogger.cs  (static, single Hall of Gods attempts)
 *
 * Shared low-level helpers were extracted into Utils/ over many past
 * sessions, but the actual FEATURE code (every hook, every tracker call)
 * is still written twice by hand, once per file - nothing in C# enforces
 * that the two copies stay in sync. See memory replaylogger-refactor-plan.md
 * for the full history.
 *
 * This script catches the two concrete ways that hand-mirroring silently
 * rots, without merging the two engines (that was a deliberate, separate
 * decision NOT to do - "two orchestrators + shared helpers" is accepted):
 *
 *   CHECK A - HOOK SYMMETRY
 *     A MonoMod/ModHooks subscription (`Target.Event += Handler;`) added to
 *     one engine's hook-registration code but never mirrored to the other
 *     means that engine's feature never fires AT ALL, silently - the worst
 *     failure mode, because nothing in the log even hints it's missing.
 *     Known, already-reviewed one-sided hooks (real, deliberate engine
 *     differences - e.g. Pantheon-only mod lifecycle, HoG-only boss-HP
 *     detection) are recorded in KNOWN_ONE_SIDED_HOOKS with the reason, so
 *     the report only bothers a human about genuinely NEW asymmetry.
 *
 *   CHECK B - BODY DRIFT
 *     A method that exists under the SAME NAME in both files (the two
 *     engines' hand-mirrored copy of one feature) whose body changed on
 *     ONE side but not the other since the last accepted baseline. This is
 *     NOT "these two methods differ" (many legitimately do, by design - a
 *     full method-by-method sweep already happened once this project and
 *     confirmed most same-named methods have real, intentional differences)
 *     - it only flags "this one CHANGED on one side only since we last
 *     looked," which is the actual footgun pattern (edited one copy,
 *     forgot the other).
 *
 * Usage:
 *   node tools/audit-engine-mirror.js
 *       Report only. Exit code 1 if there is anything to review, 0 if clean.
 *
 *   node tools/audit-engine-mirror.js --update-baseline
 *       After reading the report and confirming everything in it is fine,
 *       accept the current state as the new baseline for future runs.
 *
 * This is a manual dev tool, not part of the build (no CI in this repo) -
 * run it yourself after any session that touched both engines, before
 * committing/pushing.
 * ------------------------------------------------------------------------
 */

'use strict';

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const ROOT = path.join(__dirname, '..');
const PANTHEON_FILE = path.join(ROOT, 'Core', 'PantheonLogger.cs');
const HOG_FILE = path.join(ROOT, 'HoGSorting', 'HoGLogger.cs');
const BASELINE_FILE = path.join(__dirname, 'engine-mirror-baseline.json');

const ENGINE_A = { label: 'Pantheon', file: PANTHEON_FILE };
const ENGINE_B = { label: 'HoG', file: HOG_FILE };

// ---------------------------------------------------------------------------
// CHECK A: known, reviewed, legitimately one-sided hook registrations.
// Each entry is a real architectural difference confirmed by reading both
// files (2026-09-10) - see the audit report this script was built from.
// ---------------------------------------------------------------------------
const KNOWN_ONE_SIDED_HOOKS = {
  'On.SceneLoad.Begin':
    'Pantheon only: real mod entry point (opens the log file). HoG is bootstrapped BY Pantheon.Initialize(), owns no independent lifecycle.',
  'ModHooks.ApplicationQuitHook':
    'Pantheon only: global mod lifecycle hook, owned once by the real entry point.',
  'On.BossSequenceController.FinishLastBossScene':
    'Pantheon only: multi-boss Pantheon sequence progression. No HoG equivalent (HoG = single attempt, no sequence).',
  'On.BossSequenceController.SetupNewSequence':
    'Pantheon only: multi-boss Pantheon sequence progression. No HoG equivalent (HoG = single attempt, no sequence).',
  'On.BossSceneController.Update':
    "HoG only: HoG's own boss-HP-detection path (paired with HoGRoomConditions.BossHpDetected) - a deliberately different mechanism than Pantheon's.",
  'HoGRoomConditions.BossHpDetected':
    'HoG only: HoG-specific boss-HP-detection event. No Pantheon equivalent.',
};

// ---------------------------------------------------------------------------
// Hook extraction (CHECK A)
// ---------------------------------------------------------------------------
function extractHookTargets(source) {
  // Matches lines like:  On.HeroController.TakeDamage += HeroController_TakeDamage;
  // or:                  ModHooks.HitInstanceHook += ModHooks_HitInstanceHook;
  // Deliberately restricted to "bare identifier" RHS so ordinary field/counter
  // += usage elsewhere in the file never matches.
  const re = /^[ \t]*([A-Za-z_][\w.]*\.[A-Za-z_]\w*)[ \t]*\+=[ \t]*[A-Za-z_]\w*;[ \t]*$/gm;
  const targets = new Set();
  let m;
  while ((m = re.exec(source)) !== null) {
    targets.add(m[1]);
  }
  return targets;
}

function runHookSymmetryCheck(sourceA, sourceB) {
  const hooksA = extractHookTargets(sourceA);
  const hooksB = extractHookTargets(sourceB);

  const onlyA = [...hooksA].filter((h) => !hooksB.has(h)).sort();
  const onlyB = [...hooksB].filter((h) => !hooksA.has(h)).sort();

  const explained = [];
  const unexplained = [];

  for (const h of onlyA) {
    const entry = { hook: h, side: ENGINE_A.label, reason: KNOWN_ONE_SIDED_HOOKS[h] };
    (entry.reason ? explained : unexplained).push(entry);
  }
  for (const h of onlyB) {
    const entry = { hook: h, side: ENGINE_B.label, reason: KNOWN_ONE_SIDED_HOOKS[h] };
    (entry.reason ? explained : unexplained).push(entry);
  }

  return {
    sharedCount: [...hooksA].filter((h) => hooksB.has(h)).length,
    explained,
    unexplained,
  };
}

// ---------------------------------------------------------------------------
// Method extraction (CHECK B)
// ---------------------------------------------------------------------------

// Skips a string or char literal starting at text[i] (which must be a quote
// character, ' or "; any @/$ prefix must already have been consumed by the
// caller and passed in via the verbatim/interpolated flags). Returns the
// index just past the closing quote. Handles nested interpolation holes
// ($"...{ expr }...") by recursing into the general skip logic for any
// strings that appear inside a hole, and by brace-counting the hole itself.
function skipStringLiteral(text, i, verbatim, interpolated) {
  const quote = text[i];
  i++; // past opening quote
  const n = text.length;
  while (i < n) {
    const c = text[i];
    if (interpolated && c === '{') {
      if (text[i + 1] === '{') { i += 2; continue; } // literal "{{"
      // Enter an interpolation hole: brace-count it, skipping over any
      // nested string/char literals encountered inside.
      let depth = 1;
      i++;
      while (i < n && depth > 0) {
        const hc = text[i];
        if (hc === '"' ) { i = skipStringLiteral(text, i, false, false); continue; }
        if (hc === '\'') { i = skipStringLiteral(text, i, false, false); continue; }
        if (hc === '{') { depth++; i++; continue; }
        if (hc === '}') { depth--; i++; continue; }
        i++;
      }
      continue;
    }
    if (interpolated && c === '}' && text[i + 1] === '}') { i += 2; continue; } // literal "}}"
    if (c === quote) {
      if (verbatim && text[i + 1] === quote) { i += 2; continue; } // escaped "" inside verbatim
      return i + 1; // real end of literal
    }
    if (!verbatim && c === '\\') { i += 2; continue; } // escaped char, skip both
    i++;
  }
  return n;
}

// Finds the index just past the closing brace matching the '{' at openIndex,
// skipping over string/char literals and comments so braces inside them
// (format strings, log text, etc.) never corrupt the depth count.
function findMatchingBrace(text, openIndex) {
  let depth = 0;
  let i = openIndex;
  const n = text.length;
  while (i < n) {
    const c = text[i];
    if (c === '/' && text[i + 1] === '/') {
      const j = text.indexOf('\n', i);
      i = j === -1 ? n : j + 1;
      continue;
    }
    if (c === '/' && text[i + 1] === '*') {
      const j = text.indexOf('*/', i + 2);
      i = j === -1 ? n : j + 2;
      continue;
    }
    // string/char literal prefixes: "...", @"...", $"...", $@"..."/@$"..."
    if (c === '$' && text[i + 1] === '@' && text[i + 2] === '"') { i = skipStringLiteral(text, i + 2, true, true); continue; }
    if (c === '@' && text[i + 1] === '$' && text[i + 2] === '"') { i = skipStringLiteral(text, i + 2, true, true); continue; }
    if (c === '@' && text[i + 1] === '"') { i = skipStringLiteral(text, i + 1, true, false); continue; }
    if (c === '$' && text[i + 1] === '"') { i = skipStringLiteral(text, i + 1, false, true); continue; }
    if (c === '"') { i = skipStringLiteral(text, i, false, false); continue; }
    if (c === '\'') { i = skipStringLiteral(text, i, false, false); continue; }
    if (c === '{') { depth++; i++; continue; }
    if (c === '}') {
      depth--;
      i++;
      if (depth === 0) return i;
      continue;
    }
    i++;
  }
  return -1; // unbalanced - caller must handle
}

// Extracts top-level method definitions (private/public/internal/protected,
// optionally static/override/sealed/async) found at class-body indentation
// (8 spaces - namespace { class { ... } } in this codebase's style), so
// nested local functions/lambdas at deeper indentation are not picked up.
// Returns Map<methodName, { body, startLine }>. If more than one method
// shares a name in the SAME file (legitimate overloads), only the first is
// kept and a warning is recorded - overloads are rare enough here that
// flagging them for a human rather than guessing is the safer default.
const SIG_RE = /^ {8}(?:(?:public|private|internal|protected|static|override|sealed|async|virtual|readonly)\s+)+[\w<>\[\],\.\?]+\s+([A-Za-z_]\w*)\s*\(/gm;

function extractMethods(source) {
  const methods = new Map();
  const overloadWarnings = [];
  let m;
  SIG_RE.lastIndex = 0;
  while ((m = SIG_RE.exec(source)) !== null) {
    const name = m[1];
    // find the '(' that starts the parameter list, then its matching ')'
    const parenStart = source.indexOf('(', m.index);
    let depth = 1;
    let i = parenStart + 1;
    while (i < source.length && depth > 0) {
      if (source[i] === '(') depth++;
      else if (source[i] === ')') depth--;
      i++;
    }
    // skip whitespace / "where T : ..." constraints up to the opening brace
    // (or a ';' for an abstract/interface-only signature, which we skip)
    let braceStart = source.indexOf('{', i);
    const semiBeforeBrace = source.indexOf(';', i);
    if (semiBeforeBrace !== -1 && (braceStart === -1 || semiBeforeBrace < braceStart)) {
      continue; // no body (e.g. an interface/partial signature) - nothing to hash
    }
    if (braceStart === -1) continue;
    const bodyEnd = findMatchingBrace(source, braceStart);
    if (bodyEnd === -1) continue;
    const body = source.slice(braceStart, bodyEnd);
    if (methods.has(name)) {
      overloadWarnings.push(name);
      continue;
    }
    methods.set(name, { body, startLine: source.slice(0, m.index).split('\n').length });
  }
  return { methods, overloadWarnings };
}

function normalizeBody(body) {
  // Collapse all whitespace so pure reformatting never counts as a change -
  // we only care about token-level drift.
  return body.replace(/\s+/g, ' ').trim();
}

function hashBody(body) {
  return crypto.createHash('sha256').update(normalizeBody(body), 'utf8').digest('hex').slice(0, 16);
}

function runBodyDriftCheck(sourceA, sourceB) {
  const { methods: methodsA, overloadWarnings: overloadsA } = extractMethods(sourceA);
  const { methods: methodsB, overloadWarnings: overloadsB } = extractMethods(sourceB);

  const sharedNames = [...methodsA.keys()].filter((n) => methodsB.has(n)).sort();

  let baseline = {};
  if (fs.existsSync(BASELINE_FILE)) {
    baseline = JSON.parse(fs.readFileSync(BASELINE_FILE, 'utf8'));
  }

  const newPairs = [];
  const oneSidedChanges = [];
  const bothChanged = [];
  const currentBaseline = {};

  for (const name of sharedNames) {
    const hashA = hashBody(methodsA.get(name).body);
    const hashB = hashBody(methodsB.get(name).body);
    currentBaseline[name] = { [ENGINE_A.label]: hashA, [ENGINE_B.label]: hashB };

    const prev = baseline[name];
    if (!prev) {
      newPairs.push({ name, lineA: methodsA.get(name).startLine, lineB: methodsB.get(name).startLine });
      continue;
    }
    const changedA = prev[ENGINE_A.label] !== hashA;
    const changedB = prev[ENGINE_B.label] !== hashB;
    if (changedA && !changedB) {
      oneSidedChanges.push({ name, changedSide: ENGINE_A.label, quietSide: ENGINE_B.label, lineA: methodsA.get(name).startLine, lineB: methodsB.get(name).startLine });
    } else if (changedB && !changedA) {
      oneSidedChanges.push({ name, changedSide: ENGINE_B.label, quietSide: ENGINE_A.label, lineA: methodsA.get(name).startLine, lineB: methodsB.get(name).startLine });
    } else if (changedA && changedB) {
      bothChanged.push({ name });
    }
  }

  // methods that existed in the baseline but no longer appear as a shared
  // pair (removed/renamed on one side) - worth a mention, not an error.
  const droppedPairs = Object.keys(baseline).filter((n) => !sharedNames.includes(n));

  return {
    sharedTotal: sharedNames.length,
    newPairs,
    oneSidedChanges,
    bothChanged,
    droppedPairs,
    overloadsA,
    overloadsB,
    currentBaseline,
  };
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------
function main() {
  const updateBaseline = process.argv.includes('--update-baseline');
  const debugListMethods = process.argv.includes('--debug-list-methods');

  const sourceA = fs.readFileSync(PANTHEON_FILE, 'utf8');
  const sourceB = fs.readFileSync(HOG_FILE, 'utf8');

  if (debugListMethods) {
    const bodyResult = runBodyDriftCheck(sourceA, sourceB);
    console.log(Object.keys(bodyResult.currentBaseline).sort().join('\n'));
    console.log(`--- TOTAL: ${Object.keys(bodyResult.currentBaseline).length} ---`);
    return;
  }

  console.log('='.repeat(78));
  console.log('CHECK A - Hook registration symmetry');
  console.log('='.repeat(78));
  const hookResult = runHookSymmetryCheck(sourceA, sourceB);
  console.log(`Shared hooks (registered in both engines): ${hookResult.sharedCount}`);
  if (hookResult.explained.length) {
    console.log(`\nOne-sided, already reviewed/explained (${hookResult.explained.length}):`);
    for (const e of hookResult.explained) {
      console.log(`  [${e.side} only] ${e.hook}`);
      console.log(`      -> ${e.reason}`);
    }
  }
  if (hookResult.unexplained.length) {
    console.log(`\n!!! NEW / UNEXPLAINED one-sided hooks (${hookResult.unexplained.length}) - needs a human decision:`);
    for (const e of hookResult.unexplained) {
      console.log(`  [${e.side} only] ${e.hook}`);
      console.log('      -> not in KNOWN_ONE_SIDED_HOOKS. Either mirror it to the other engine,');
      console.log('         or add it to KNOWN_ONE_SIDED_HOOKS in this script with the reason it\'s intentional.');
    }
  } else {
    console.log('\nNo unexplained one-sided hooks. Clean.');
  }

  console.log('\n' + '='.repeat(78));
  console.log('CHECK B - Body drift between same-named methods');
  console.log('='.repeat(78));
  const bodyResult = runBodyDriftCheck(sourceA, sourceB);
  console.log(`Method names present in both files: ${bodyResult.sharedTotal}`);

  if (bodyResult.overloadsA.length || bodyResult.overloadsB.length) {
    console.log(`\nNote: overloaded method names skipped (only first definition tracked):`);
    if (bodyResult.overloadsA.length) console.log(`  Pantheon: ${bodyResult.overloadsA.join(', ')}`);
    if (bodyResult.overloadsB.length) console.log(`  HoG: ${bodyResult.overloadsB.join(', ')}`);
  }

  if (!fs.existsSync(BASELINE_FILE)) {
    console.log('\nNo baseline file yet - this is a first run.');
    console.log(`All ${bodyResult.sharedTotal} current shared-name method pairs will be recorded as the baseline.`);
  } else {
    if (bodyResult.newPairs.length) {
      console.log(`\nNew shared-name method pairs since last baseline (${bodyResult.newPairs.length}) - not yet reviewed:`);
      for (const p of bodyResult.newPairs) {
        console.log(`  ${p.name}  (Pantheon:${p.lineA}, HoG:${p.lineB})`);
      }
    }
    if (bodyResult.oneSidedChanges.length) {
      console.log(`\n!!! ONE-SIDED CHANGES since last baseline (${bodyResult.oneSidedChanges.length}) - the exact footgun this check exists for:`);
      for (const c of bodyResult.oneSidedChanges) {
        console.log(`  ${c.name}: changed in ${c.changedSide} only, ${c.quietSide}'s copy did NOT change.`);
        console.log(`      -> Pantheon:${c.lineA}, HoG:${c.lineB}. Check whether ${c.quietSide} needs the same fix.`);
      }
    } else {
      console.log('\nNo one-sided changes since last baseline. Clean.');
    }
    if (bodyResult.bothChanged.length) {
      console.log(`\nBoth sides changed together (${bodyResult.bothChanged.length}) - informational only, presumably intentional:`);
      console.log(`  ${bodyResult.bothChanged.map((c) => c.name).join(', ')}`);
    }
    if (bodyResult.droppedPairs.length) {
      console.log(`\nNo longer a shared pair (removed/renamed on one side, informational): ${bodyResult.droppedPairs.join(', ')}`);
    }
  }

  const hasIssues = hookResult.unexplained.length > 0 || bodyResult.oneSidedChanges.length > 0 || bodyResult.newPairs.length > 0;

  if (updateBaseline) {
    fs.writeFileSync(BASELINE_FILE, JSON.stringify(bodyResult.currentBaseline, null, 2) + '\n', 'utf8');
    console.log(`\nBaseline updated: ${BASELINE_FILE}`);
    process.exit(0);
  }

  console.log('\n' + '='.repeat(78));
  if (hasIssues) {
    console.log('Result: REVIEW NEEDED (see above). Once satisfied, run with --update-baseline to accept.');
    process.exit(1);
  } else {
    console.log('Result: clean.');
    process.exit(0);
  }
}

main();
