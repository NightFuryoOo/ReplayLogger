using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace ReplayLogger
{
    internal sealed class BossPhaseThresholdTracker : ITrackerLifecycle
    {
        private static readonly BossSpec[] KnownBosses =
        [
            new BossSpec("GG_Radiance",
            [
                new ThresholdSpec("P2 Spike Waves", "Phase Control", ReadStrategy.NamedVariable, "P2 Spike Waves", null, 2600),
                new ThresholdSpec("P3 A1 Rage", "Phase Control", ReadStrategy.NamedVariable, "P3 A1 Rage", null, 2150),
                new ThresholdSpec("P4 Stun1", "Phase Control", ReadStrategy.NamedVariable, "P4 Stun1", null, 1850),
                new ThresholdSpec("P5 Acend", "Phase Control", ReadStrategy.NamedVariable, "P5 Acend", null, 1100),
            ]),
            new BossSpec("GG_Broken_Vessel",
            [
                new ThresholdSpec("Phase2 Hp (Spawn)", "Spawn Balloon", ReadStrategy.StateIntCompare, "Spawn", "HP", 420),

                new ThresholdSpec("Token 1 (Phase3)", "Shake Token Control", ReadStrategy.StateIntCompare, "Check", "HP", 370),
                new ThresholdSpec("Token 2 (Phase4)", "Shake Token Control", ReadStrategy.StateIntCompare, "Check 2", "HP", 220),
                new ThresholdSpec("Token 3 (Phase5)", "Shake Token Control", ReadStrategy.StateIntCompare, "Check 3", "HP", 110),
            ]),
            new BossSpec("GG_Dung_Defender",
            [

                new ThresholdSpec("Rage HP", "Dung Defender", ReadStrategy.StateIntCompare, "Rage?", null, 400),
            ]),
            new BossSpec("GG_White_Defender",
            [

                new ThresholdSpec("Rage HP", "Dung Defender", ReadStrategy.StateIntCompare, "Rage?", null, 600),
            ]),

            new BossSpec("GG_Ghost_Galien",
            [
                new ThresholdSpec("Phase2 Hp (Summon HP1)", "Summon Minis", ReadStrategy.StateIntCompare, "Check", null, 700),
                new ThresholdSpec("Phase3 Hp (Summon HP2)", "Summon Minis", ReadStrategy.StateIntCompare, "Check 2", null, 400),
            ], declaredMaxHp: 1000),
            new BossSpec("GG_Ghost_Gorb_V",
            [

                new ThresholdSpec("Phase2 Hp (Double HP)", "Attacking", ReadStrategy.NamedVariable, "Double HP", null, 700),
                new ThresholdSpec("Phase3 Hp (Triple HP)", "Attacking", ReadStrategy.NamedVariable, "Triple HP", null, 400),
            ], declaredMaxHp: 1000),
            new BossSpec("GG_Ghost_Gorb",
            [

                new ThresholdSpec("Phase2 Hp (Double HP)", "Attacking", ReadStrategy.NamedVariable, "Double HP", null, 455),
                new ThresholdSpec("Phase3 Hp (Triple HP)", "Attacking", ReadStrategy.NamedVariable, "Triple HP", null, 260),
            ]),

            new BossSpec("GG_Grimm_Nightmare",
            [
                new ThresholdSpec("Rage Phase1 (HP1)", "Control", ReadStrategy.NamedVariable, "Rage HP 1", null, 1238),
                new ThresholdSpec("Rage Phase2 (HP2)", "Control", ReadStrategy.NamedVariable, "Rage HP 2", null, 826),
                new ThresholdSpec("Rage Phase3 (HP3)", "Control", ReadStrategy.NamedVariable, "Rage HP 3", null, 414),
            ], declaredMaxHp: 1650),
            new BossSpec("GG_Ghost_Xero_V",
            [

                new ThresholdSpec("Phase2 Hp (Half HP)", "Sword Summon", ReadStrategy.StateIntCompare, "Check", null, 450),
            ], declaredMaxHp: 900),
            new BossSpec("GG_Ghost_Xero",
            [

                new ThresholdSpec("Phase2 Hp (Half HP)", "Sword Summon", ReadStrategy.StateIntCompare, "Check", null, 325),
            ]),
            new BossSpec("GG_Ghost_No_Eyes_V",
            [

                new ThresholdSpec("Phase2 Hp (Esc 1)", "Escalation", ReadStrategy.StateIntCompare, "Check", null, 150),
                new ThresholdSpec("Phase3 Hp (Esc 2)", "Escalation", ReadStrategy.StateIntCompare, "Check 2", null, 90),
            ]),
            new BossSpec("GG_Ghost_No_Eyes",
            [

                new ThresholdSpec("Phase2 Hp (Esc 1)", "Escalation", ReadStrategy.StateIntCompare, "Check", null, 150),
                new ThresholdSpec("Phase3 Hp (Esc 2)", "Escalation", ReadStrategy.StateIntCompare, "Check 2", null, 90),
            ]),
            new BossSpec("GG_Ghost_Markoth_V",
            [

                new ThresholdSpec("Phase2 Hp (Rage HP)", "Rage Check", ReadStrategy.StateIntCompare, "Check", null, 475),
            ], declaredMaxHp: 950),
            new BossSpec("GG_Ghost_Markoth",
            [

                new ThresholdSpec("Phase2 Hp (Rage HP)", "Rage Check", ReadStrategy.StateIntCompare, "Check", null, 325),
            ]),
            new BossSpec("GG_Lost_Kin",
            [

                new ThresholdSpec("Phase2 Hp (Spawn)", "Spawn Balloon", ReadStrategy.StateIntCompare, "Spawn", "HP", 1150),
                new ThresholdSpec("Token 1 (Phase3)", "Shake Token Control", ReadStrategy.StateIntCompare, "Check", "HP", 550),
                new ThresholdSpec("Token 2 (Phase4)", "Shake Token Control", ReadStrategy.StateIntCompare, "Check 2", "HP", 350),
                new ThresholdSpec("Token 3 (Phase5)", "Shake Token Control", ReadStrategy.StateIntCompare, "Check 3", "HP", 175),
            ]),

            new BossSpec("GG_Grimm",
            [

                new ThresholdSpec("Rage Phase1 (Rage HP1)", "Control", ReadStrategy.StateIntTestToBoolByOrder, "Balloon?", null, 750, 0),
                new ThresholdSpec("Rage Phase2 (Rage HP2)", "Control", ReadStrategy.StateIntTestToBoolByOrder, "Balloon?", null, 500, 1),
                new ThresholdSpec("Rage Phase3 (Rage HP3)", "Control", ReadStrategy.StateIntTestToBoolByOrder, "Balloon?", null, 250, 2),
            ]),

            new BossSpec("GG_Hollow_Knight",
            [
                new ThresholdSpec("Phase2 Hp (Half HP)", "Control", ReadStrategy.StateIntCompareByOrder, "Phase?", null, 1232, 0),
                new ThresholdSpec("Phase3 Hp (Quarter HP)", "Control", ReadStrategy.StateIntCompareByOrder, "Phase?", null, 616, 1),
            ], declaredMaxHp: 1850),
            new BossSpec("GG_Traitor_Lord",
            [

                new ThresholdSpec("Phase2 Hp", "Mantis", ReadStrategy.StateIntCompareExcludeOperand, "Slam?", "HP", 500),
            ]),
            new BossSpec("GG_Nosk_Hornet",
            [

                new ThresholdSpec("Phase2 Hp (Half HP)", null, ReadStrategy.StateIntCompareExcludeOperand, "Choose Attack", "HP", 525),
            ], declaredMaxHp: 1050),
            new BossSpec("GG_Nosk_V",
            [

                new ThresholdSpec("Phase2 Hp", null, ReadStrategy.StateIntCompareExcludeOperand, "Roof Jump?", "HP", 560),
            ]),
            new BossSpec("GG_Nosk",
            [

                new ThresholdSpec("Phase2 Hp", null, ReadStrategy.StateIntCompareExcludeOperand, "Roof Jump?", "HP", 560),
            ]),
        ];

        private const long ThrottleMs = 500;

        private readonly List<BossState> trackedStates = new();
        private readonly HashSet<string> unresolvedArenasThisFight = new(StringComparer.Ordinal);
        private long lastPollUnixTime;

        private readonly Dictionary<string, List<string>> fsmInventoryByArena = new(StringComparer.Ordinal);

        internal bool HasData => trackedStates.Count > 0 || fsmInventoryByArena.Count > 0;

        void ITrackerLifecycle.Reset() => Reset();
        void ITrackerLifecycle.Write(StreamWriter writer) => Write(writer);

        internal void Reset()
        {
            trackedStates.Clear();
            unresolvedArenasThisFight.Clear();
            fsmInventoryByArena.Clear();
            lastPollUnixTime = 0;
        }

        internal void Update(string arenaName, IEnumerable<GameObject> bossObjects, long lastUnixTime, long nowUnixTime)
        {
            if (string.IsNullOrWhiteSpace(arenaName) || unresolvedArenasThisFight.Contains(arenaName))
            {
                return;
            }

            if (lastPollUnixTime > 0 && nowUnixTime - lastPollUnixTime < ThrottleMs)
            {
                return;
            }

            lastPollUnixTime = nowUnixTime;

            BossSpec? spec = FindSpec(arenaName);
            if (spec == null)
            {
                unresolvedArenasThisFight.Add(arenaName);
                return;
            }

            BossState existingState = FindState(arenaName);
            Dictionary<string, PlayMakerFSM> fsmCache = existingState?.CachedFsms ?? new Dictionary<string, PlayMakerFSM>(StringComparer.Ordinal);

            var current = new Optional<int>[spec.Value.Thresholds.Length];
            bool anyAvailable = false;
            for (int i = 0; i < spec.Value.Thresholds.Length; i++)
            {
                current[i] = TryReadThreshold(fsmCache, bossObjects, spec.Value.Thresholds[i]);
                anyAvailable |= current[i].HasValue;
            }

            if (existingState == null)
            {
                if (!anyAvailable)
                {

                    if (!fsmInventoryByArena.TryGetValue(arenaName, out List<string> inventory) || inventory.Count == 0)
                    {
                        fsmInventoryByArena[arenaName] = CaptureFsmInventory(bossObjects);
                    }

                    return;
                }

                fsmInventoryByArena.Remove(arenaName);
                int referenceMaxHp = spec.Value.DeclaredMaxHp > 0 ? ResolveReferenceMaxHp(bossObjects) : 0;
                trackedStates.Add(new BossState(arenaName, spec.Value, current, nowUnixTime, fsmCache, referenceMaxHp));
                return;
            }

            for (int i = 0; i < current.Length; i++)
            {
                Optional<int> previous = existingState.CurrentValues[i];
                Optional<int> latest = current[i];
                if (!latest.HasValue || previous == latest)
                {
                    continue;
                }

                if (previous.HasValue)
                {

                    long delta = existingState.BaseUnixTime > 0 ? nowUnixTime - existingState.BaseUnixTime : 0;
                    existingState.Changes.Add($"|{arenaName}|+{delta}|{spec.Value.Thresholds[i].Label}: {previous.Value} -> {latest.Value}");
                }
                else
                {

                    existingState.InitialValues[i] = latest;
                }
            }

            existingState.CurrentValues = current;
        }

        private static Optional<int> TryReadThreshold(
            Dictionary<string, PlayMakerFSM> fsmCache,
            IEnumerable<GameObject> bossObjects,
            ThresholdSpec spec)
        {

            string cacheKey = spec.FsmName ?? "$state:" + spec.Key;
            if (!fsmCache.TryGetValue(cacheKey, out PlayMakerFSM fsm) || fsm == null)
            {
                fsm = spec.FsmName != null
                    ? FindNamedFsm(bossObjects, spec.FsmName)
                    : FindFsmWithState(bossObjects, spec.Key);
                if (fsm == null)
                {
                    return Optional<int>.None;
                }

                fsmCache[cacheKey] = fsm;
            }

            if (spec.Strategy == ReadStrategy.NamedVariable)
            {
                FsmInt fsmInt = fsm.FsmVariables.GetFsmInt(spec.Key);

                if (fsmInt == null || fsmInt.Value <= 0)
                {
                    return Optional<int>.None;
                }

                return new Optional<int>(fsmInt.Value);
            }

            Fsm realFsm = fsm.Fsm;
            FsmState state = realFsm?.GetState(spec.Key);
            if (state?.Actions == null)
            {
                return Optional<int>.None;
            }

            FsmStateAction[] actions = state.Actions;

            if (spec.Strategy == ReadStrategy.StateIntCompareByOrder)
            {
                int occurrence = 0;
                for (int i = 0; i < actions.Length; i++)
                {
                    if (actions[i] is not IntCompare compare || compare.integer2 == null)
                    {
                        continue;
                    }

                    if (occurrence == spec.OccurrenceIndex)
                    {
                        return compare.integer2.Value > 0 ? new Optional<int>(compare.integer2.Value) : Optional<int>.None;
                    }

                    occurrence++;
                }

                return Optional<int>.None;
            }

            if (spec.Strategy == ReadStrategy.StateIntTestToBoolByOrder)
            {
                int occurrence = 0;
                for (int i = 0; i < actions.Length; i++)
                {
                    if (actions[i] is not IntTestToBool compare || compare.int2 == null)
                    {
                        continue;
                    }

                    if (occurrence == spec.OccurrenceIndex)
                    {
                        return compare.int2.Value > 0 ? new Optional<int>(compare.int2.Value) : Optional<int>.None;
                    }

                    occurrence++;
                }

                return Optional<int>.None;
            }

            if (spec.Strategy == ReadStrategy.StateIntCompareExcludeOperand)
            {
                for (int i = 0; i < actions.Length; i++)
                {
                    if (actions[i] is not IntCompare compare)
                    {
                        continue;
                    }

                    FsmInt operand = ResolveExcludeOperand(compare, spec.FilterName);
                    if (operand == null)
                    {
                        continue;
                    }

                    return operand.Value > 0 ? new Optional<int>(operand.Value) : Optional<int>.None;
                }

                return Optional<int>.None;
            }

            for (int i = 0; i < actions.Length; i++)
            {
                if (actions[i] is not IntCompare compare || compare.integer2 == null)
                {
                    continue;
                }

                if (spec.FilterName != null && !MatchesFilterName(compare, spec.FilterName))
                {
                    continue;
                }

                return compare.integer2.Value > 0 ? new Optional<int>(compare.integer2.Value) : Optional<int>.None;
            }

            return Optional<int>.None;
        }

        private static bool MatchesFilterName(IntCompare compare, string filterName)
        {
            return string.Equals(compare.integer1?.Name, filterName, StringComparison.Ordinal)
                || string.Equals(compare.integer2?.Name, filterName, StringComparison.Ordinal);
        }

        private static FsmInt ResolveExcludeOperand(IntCompare compare, string excludeName)
        {
            bool integer1Matches = compare.integer1 != null && string.Equals(compare.integer1.Name, excludeName, StringComparison.Ordinal);
            bool integer2Matches = compare.integer2 != null && string.Equals(compare.integer2.Name, excludeName, StringComparison.Ordinal);

            if (integer1Matches)
            {
                return compare.integer2 ?? compare.integer1;
            }

            if (integer2Matches)
            {
                return compare.integer1 ?? compare.integer2;
            }

            return null;
        }

        internal void Write(StreamWriter writer)
        {
            if (writer == null || !HasData)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(16 + trackedStates.Count * 8);
            try
            {
                if (trackedStates.Count > 0)
                {
                    batch.Add("Boss Phase Thresholds (Real):");
                    for (int i = 0; i < trackedStates.Count; i++)
                    {
                        BossState state = trackedStates[i];
                        batch.Add($"  Arena: {state.ArenaName}");
                        batch.Add("  State:");
                        for (int j = 0; j < state.Spec.Thresholds.Length; j++)
                        {
                            ThresholdSpec threshold = state.Spec.Thresholds[j];
                            Optional<int> value = state.InitialValues[j];
                            int effectiveVanilla = ComputeEffectiveVanilla(threshold.VanillaDefault, state.Spec.DeclaredMaxHp, state.ReferenceMaxHp);
                            string valueText = value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "N/A";
                            string flag = value.HasValue && value.Value != effectiveVanilla ? " (differs from vanilla)" : string.Empty;
                            string fsmLabel = threshold.FsmName ?? $"state:{threshold.Key}";
                            batch.Add($"    {threshold.Label} [{fsmLabel}]: {valueText} [vanilla={effectiveVanilla}]{flag}");
                        }

                        batch.Add("  Changes:");
                        if (state.Changes.Count == 0)
                        {
                            batch.Add("    (none)");
                        }
                        else
                        {
                            for (int j = 0; j < state.Changes.Count; j++)
                            {
                                batch.Add($"    {state.Changes[j]}");
                            }
                        }
                    }

                    batch.Add("---------------------------------------------------");
                }

                if (fsmInventoryByArena.Count > 0)
                {
                    batch.Add("Boss Phase Thresholds - FSM Inventory (mapping not found this fight):");
                    foreach (KeyValuePair<string, List<string>> entry in fsmInventoryByArena)
                    {
                        batch.Add($"  Arena: {entry.Key}");
                        for (int i = 0; i < entry.Value.Count; i++)
                        {
                            batch.Add($"  {entry.Value[i]}");
                        }
                    }

                    batch.Add("---------------------------------------------------");
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        private BossState FindState(string arenaName)
        {
            for (int i = 0; i < trackedStates.Count; i++)
            {
                if (string.Equals(trackedStates[i].ArenaName, arenaName, StringComparison.Ordinal))
                {
                    return trackedStates[i];
                }
            }

            return null;
        }

        private static BossSpec? FindSpec(string arenaName)
        {
            for (int i = 0; i < KnownBosses.Length; i++)
            {
                if (string.Equals(KnownBosses[i].ArenaName, arenaName, StringComparison.Ordinal))
                {
                    return KnownBosses[i];
                }
            }

            return null;
        }

        private static List<string> CaptureFsmInventory(IEnumerable<GameObject> bossObjects)
        {
            List<string> lines = new();
            if (bossObjects == null)
            {
                return lines;
            }

            foreach (GameObject bossObject in bossObjects)
            {
                if (bossObject == null)
                {
                    continue;
                }

                lines.Add($"boss root: {GetHierarchyPath(bossObject)}");
                PlayMakerFSM[] candidates = bossObject.GetComponentsInChildren<PlayMakerFSM>(includeInactive: true);
                for (int i = 0; i < candidates.Length; i++)
                {
                    PlayMakerFSM candidate = candidates[i];
                    string activeFlag = candidate.gameObject.activeInHierarchy ? "active" : "inactive";
                    lines.Add($"  FSM \"{candidate.FsmName}\" on {GetHierarchyPath(candidate.gameObject)} [{activeFlag}]");

                    FsmInt[] intVariables = candidate.FsmVariables?.IntVariables;
                    if (intVariables != null)
                    {
                        for (int j = 0; j < intVariables.Length; j++)
                        {
                            lines.Add($"      int \"{intVariables[j].Name}\" = {intVariables[j].Value}");
                        }
                    }
                }
            }

            return lines;
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            List<string> segments = new();
            Transform current = gameObject.transform;
            while (current != null)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        private static PlayMakerFSM FindNamedFsm(IEnumerable<GameObject> bossObjects, string fsmName)
        {
            if (bossObjects == null)
            {
                return null;
            }

            foreach (GameObject bossObject in bossObjects)
            {
                if (bossObject == null)
                {
                    continue;
                }

                PlayMakerFSM[] candidates = bossObject.GetComponentsInChildren<PlayMakerFSM>(includeInactive: true);
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (string.Equals(candidates[i].FsmName, fsmName, StringComparison.Ordinal))
                    {
                        return candidates[i];
                    }
                }
            }

            return null;
        }

        private static int ResolveReferenceMaxHp(IEnumerable<GameObject> bossObjects)
        {
            if (bossObjects == null)
            {
                return 0;
            }

            foreach (GameObject bossObject in bossObjects)
            {
                if (bossObject == null)
                {
                    continue;
                }

                HealthManager healthManager = bossObject.GetComponent<HealthManager>();
                if (healthManager != null && healthManager.hp > 0)
                {
                    return healthManager.hp;
                }
            }

            return 0;
        }

        private static int ComputeEffectiveVanilla(int declaredDefault, int declaredMaxHp, int referenceMaxHp)
        {
            if (declaredMaxHp <= 0 || referenceMaxHp <= 0)
            {
                return declaredDefault;
            }

            return (int)Math.Round(declaredDefault * (double)referenceMaxHp / declaredMaxHp, MidpointRounding.AwayFromZero);
        }

        private static PlayMakerFSM FindFsmWithState(IEnumerable<GameObject> bossObjects, string stateName)
        {
            if (bossObjects == null)
            {
                return null;
            }

            foreach (GameObject bossObject in bossObjects)
            {
                if (bossObject == null)
                {
                    continue;
                }

                PlayMakerFSM[] candidates = bossObject.GetComponentsInChildren<PlayMakerFSM>(includeInactive: true);
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i]?.Fsm?.GetState(stateName) != null)
                    {
                        return candidates[i];
                    }
                }
            }

            return null;
        }

        private enum ReadStrategy
        {
            NamedVariable,
            StateIntCompare,
            StateIntCompareExcludeOperand,
            StateIntCompareByOrder,
            StateIntTestToBoolByOrder
        }

        private readonly record struct ThresholdSpec(
            string Label,
            string FsmName,
            ReadStrategy Strategy,
            string Key,
            string FilterName,
            int VanillaDefault,
            int OccurrenceIndex = 0);

        private readonly struct BossSpec
        {
            internal BossSpec(string arenaName, ThresholdSpec[] thresholds, int declaredMaxHp = 0)
            {
                ArenaName = arenaName;
                Thresholds = thresholds;
                DeclaredMaxHp = declaredMaxHp;
            }

            internal string ArenaName { get; }
            internal ThresholdSpec[] Thresholds { get; }

            internal int DeclaredMaxHp { get; }
        }

        private sealed class BossState
        {
            internal BossState(string arenaName, BossSpec spec, Optional<int>[] initialValues, long baseUnixTime, Dictionary<string, PlayMakerFSM> cachedFsms, int referenceMaxHp)
            {
                ArenaName = arenaName;
                Spec = spec;
                InitialValues = initialValues;
                CurrentValues = initialValues;
                BaseUnixTime = baseUnixTime;
                CachedFsms = cachedFsms;
                ReferenceMaxHp = referenceMaxHp;
            }

            internal string ArenaName { get; }
            internal BossSpec Spec { get; }
            internal Optional<int>[] InitialValues { get; }
            internal Optional<int>[] CurrentValues { get; set; }
            internal long BaseUnixTime { get; }
            internal Dictionary<string, PlayMakerFSM> CachedFsms { get; }
            internal List<string> Changes { get; } = new();

            internal int ReferenceMaxHp { get; }
        }
    }
}
