// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// One expression-to-input binding, surfaced to the remote app: which expression an action set drives,
    /// the set's stable id (to rebind or remove it), and the raw control path currently bound. Display
    /// formatting of <see cref="binding"/> is left to the remote app (shared with the Actions page).
    /// </summary>
    [Serializable]
    public class ExpressionBindingInfo
    {
        public string expression;
        public string setId;
        public string binding;
    }

    /// <summary>
    /// Bridges facial expressions onto the generic <see cref="ActionManager"/> event base: an expression
    /// "key binding" is just an <see cref="ActionSet"/> whose <see cref="KeyInputSource"/> drives a
    /// <see cref="SetExpressionAction"/>. Keeps all expression awareness on this (expression) side so the
    /// manager stays feature-agnostic and only its generic API is used.
    /// </summary>
    public static class ExpressionBindingSystem
    {
        /// <summary>Creates an action set bound to the given expression (momentary Button-mode key input by
        /// default) and returns its id, or null when no manager is active. The key is assigned afterwards via
        /// <see cref="StartRebind"/>.</summary>
        public static string AddBinding(string expressionName)
        {
            var manager = ActionManager.current;
            if (manager == null) return null;

            var set = manager.AddActionSet(
                new KeyInputSource(),
                new SetExpressionAction { expression = expressionName });
            set.name = expressionName;
            return set.id;
        }

        /// <summary>Removes the action set with the given id (the binding's set).</summary>
        public static void RemoveBinding(string setId) => ActionManager.current?.RemoveActionSet(setId);

        /// <summary>Starts interactive key capture for the binding's action set, writing the pressed control
        /// into its key input. No-op when the set is missing or its input is not a key source.</summary>
        public static void StartRebind(string setId)
        {
            var manager = ActionManager.current;
            if (manager == null) return;

            var set = _Find(manager.actionSets, setId);
            (set?.input as KeyInputSource)?.StartRebind();
        }

        /// <summary>All current expression bindings derived from the active manager's action sets.</summary>
        public static ExpressionBindingInfo[] GetBindings() => CollectBindings(ActionManager.current?.actionSets);

        /// <summary>Pure projection of action sets to expression bindings: one entry per
        /// <see cref="SetExpressionAction"/> found in a set. Sets without an expression action are ignored.
        /// Static and list-based so it is unit-testable without play mode.</summary>
        internal static ExpressionBindingInfo[] CollectBindings(List<ActionSet> sets)
        {
            var result = new List<ExpressionBindingInfo>();
            if (sets == null) return result.ToArray();

            for (int i = 0; i < sets.Count; i++)
            {
                var set = sets[i];
                if (set?.actions == null) continue;

                string binding = (set.input as KeyInputSource)?.binding ?? string.Empty;
                for (int j = 0; j < set.actions.Count; j++)
                {
                    if (set.actions[j] is SetExpressionAction expr)
                    {
                        result.Add(new ExpressionBindingInfo
                        {
                            expression = expr.expression,
                            setId = set.id,
                            binding = binding,
                        });
                    }
                }
            }
            return result.ToArray();
        }

        private static ActionSet _Find(List<ActionSet> sets, string setId)
        {
            if (sets == null || string.IsNullOrEmpty(setId)) return null;
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i] != null && sets[i].id == setId) return sets[i];
            }
            return null;
        }
    }
}
