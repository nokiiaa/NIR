using NIR.Instructions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NIR.Passes.Optimize
{
    public partial class IROptimizer : IRPass
    {
        public static List<IRLiveVariable> LiveIntervalAnalysis(IRFunction func)
        {
            var localVariableTable = new Dictionary<IRName, (IRType, IRLiveVariableFlags, List<uint>)>();
            var use = new Dictionary<IRBasicBlock, List<IRName>>();
            var def = new Dictionary<IRBasicBlock, List<IRName>>();

            def[func.Blocks.First()] = new List<IRName>();

            foreach ((string, IRType) kvp in func.Arguments)
            {
                localVariableTable[kvp.Item1] = (kvp.Item2, IRLiveVariableFlags.Argument, null);
                def[func.Blocks.First()].Add(kvp.Item1);
            }

            foreach (IRBasicBlock block in func.Blocks)
            {
                foreach (IROp op in block.Operations)
                {
                    if (op is IRLocalOp alloc)
                    {
                        localVariableTable[alloc.Name] =
                            (alloc.Type, alloc.ForceReg ? IRLiveVariableFlags.ForceReg : 0, alloc.RegChoices);
                    }
                    else if (op is IRAddrOfOp addrof)
                    {
                        if (localVariableTable.ContainsKey(addrof.Variable))
                        {
                            var vr = localVariableTable[addrof.Variable];
                            localVariableTable[addrof.Variable] = (vr.Item1, IRLiveVariableFlags.ForceStack, null);
                        }
                    }
                }

                use[block] = new List<IRName>();

                if (!def.ContainsKey(block))
                    def[block] = new List<IRName>();
            }

            /* Compute use and def for each block */
            foreach (IRBasicBlock block in func.Blocks)
            {
                IROpSearch.TraverseOperands
                (
                    (y, x) =>
                    {
                        if (x is IRName name &&
                            localVariableTable.ContainsKey(name) &&
                            !def[block].Contains(name))
                            def[block].Add(name);
                    },

                    (y, x) =>
                    {
                        if (x is IRName name &&
                            localVariableTable.ContainsKey(name) &&
                            !def[block].Contains(name) &&
                            !use[block].Contains(name))
                                use[block].Add(name);
                    }, new List<IRBasicBlock> { block }
                );
            }

            var comparer = new IRNameEqualityComparer();
            bool changed;

            do
            {
                changed = false;
                foreach (IRBasicBlock block in func.Blocks)
                {
                    foreach (IRBasicBlock child in block.Children)
                        block.LiveOut = block.LiveOut.Union(child.LiveIn, comparer).ToList();

                    /* in[B] = use[B] U (out[B] - def[B]) */
                    var newIn = use[block].Union(block.LiveOut.Except(def[block], comparer), comparer).ToList();

                    /* Check if this operation changed the in set */
                    if (!Enumerable.SequenceEqual(
                        newIn.OrderBy(e => e), block.LiveIn.OrderBy(e => e)))
                        changed = true;
                    block.LiveIn = newIn;
                }
            }
            while (changed);

            var variables = new List<IRLiveVariable>();

            foreach (KeyValuePair<IRName, (IRType Type, IRLiveVariableFlags Flags, List<uint> Choices)>
                kvp in localVariableTable)
                variables.Add(new IRLiveVariable(
                    kvp.Value.Type, kvp.Key, kvp.Value.Flags, kvp.Value.Choices));

            IRLiveVariable GetVar(IRName name) =>
                variables.Find(x => x.Name.ToString() == name.ToString());

            foreach (IRBasicBlock block in func.Blocks)
            {
                if (block.Operations.Count == 0)
                    continue;

                int first = block.Operations.First().Id;
                int last = block.Operations.Last().Id;

                var allVars = def[block].Union(block.LiveIn, comparer);
                List<IRName> survVars  = allVars.Intersect(block.LiveOut, comparer).ToList();

                var currentIntervals = new Dictionary<string, Interval>();
                var firstAssignment = new Dictionary<string, bool>();
                var mentioned = new Dictionary<string, bool>();

                foreach (IRName name in allVars)
                {
                    currentIntervals[name] = (first, last);
                    firstAssignment[name] = first != 0 || !func.Arguments.Any(y => y.Item1 == name.Name);
                    mentioned[name] = false;
                }

                IROpSearch.TraverseOperands(
                    (y, x) =>
                    {
                        if (x is IRName name && currentIntervals.ContainsKey(name.Name) && 
                            (!block.LiveIn.Contains(name.Name) || !firstAssignment[name.Name] || !mentioned[name.Name]))
                        {
                            if (!firstAssignment[name.Name] && mentioned[name.Name])
                            {
                                GetVar(name).AddInterval(currentIntervals[name.Name].Copy);
                                currentIntervals[name.Name].End = last;
                            }

                            currentIntervals[name.Name].Start = y.Id;
                            currentIntervals[name.Name].End   = y.Id;
                            firstAssignment[name.Name] = false;
                            mentioned[name.Name] = true;
                        }
                    },
                    (y, x) =>
                    {
                        if (x is IRName name && currentIntervals.ContainsKey(name.Name))
                        {
                            currentIntervals[name.Name].End = y.Id;
                            mentioned[name.Name] = true;
                        }
                    },
                    new List<IRBasicBlock> { block });

                foreach (var kvp in mentioned)
                {
                    if (survVars.Contains(kvp.Key))
                        currentIntervals[kvp.Key].End = last;

                    GetVar(kvp.Key).AddInterval(currentIntervals[kvp.Key]);
                }
            }

            return variables;
        }
    }
}
