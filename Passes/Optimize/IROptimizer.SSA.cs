using NIR.Backends;
using NIR.Instructions;
using NIR.Passes.X64Specific;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NIR.Passes.Optimize
{
    public partial class IROptimizer : IRPass
    {
        static void UnlinkBlock(List<IRBasicBlock> blocks, IRBasicBlock block)
        {
            foreach (IRBasicBlock parent in block.Parents)
                parent.Children.Remove(block);

            foreach (IRBasicBlock child in block.Children)
                child.Parents.Remove(block);

            blocks.Remove(block);
        }

        public static void FixSSAVersions(IRFunction func, IRBasicBlock startBlock, string name)
        {
            TraverseDominatorTree(func, startBlock, (IRBasicBlock b) =>
            {
                // Update SSA versions
                Func<IROp, IROperand, IROperand> replace = (y, x) =>
                {
                    if (x is IRName n && SSABaseName(n) == SSABaseName(name) && SSAVersion(n) >= SSAVersion(name))
                        return new IRName(SSADecrement(n, 1));
                    return x;
                };

                IROpSearch.ReplaceOperands(replace, replace, new List<IRBasicBlock> { b });
            });
        }

        public static void ConstructSSA(IRFunction func)
        {
            /* Split linear set of instructions into basic blocks
                (separated by branch instructions, return instructions and labels) */
            List<IRBasicBlock> blocks = func.Blocks = new List<IRBasicBlock>();
            IRBasicBlock currentBlock = new IRBasicBlock();
            bool lastLabel = false;
            int id = 0;

            foreach (IROp op in func.Operations)
            {
                if (!lastLabel && op is IRLabel && currentBlock.Operations.Count > 0)
                {
                    blocks.Add(currentBlock);
                    currentBlock = new IRBasicBlock();
                    lastLabel = true;
                }

                if (!(op is IRLabel))
                    lastLabel = false;

                op.Block = currentBlock;
                op.Id = id++;
                currentBlock.Operations.Add(op);

                if (op is IRRetOp || op is IRBranchOp)
                {
                    blocks.Add(currentBlock);
                    currentBlock = new IRBasicBlock();
                }
            }

            /* Figure out how blocks are linked together */
            for (int i = 0; i < blocks.Count; i++)
            {
                IRBasicBlock block = blocks[i];
                IRBranchOp branch = null;

                if (block.Operations.Last() is IRBranchOp bra)
                {
                    branch = bra;

                    /* Create link between the branch and its target */
                    for (int j = 0; j < blocks.Count; j++)
                    {
                        /* Check if any of the labels that start a block match the one we're looking for */
                        for (int k = 0; k < blocks[j].Operations.Count &&
                            blocks[j].Operations[k] is IRLabel; k++)
                        {
                            if ((blocks[j].Operations[k] as IRLabel).Name == bra.Destination)
                            {
                                //if (blocks[j] != block)
                                {
                                    blocks[j].Parents.Add(block);
                                    block.Children.Add(blocks[j]);
                                }
                            }
                        }
                    }
                }

                /* Create links between adjacent blocks unless
                   the first block jumps unconditionally to another one */
                if ((branch == null || branch.Condition != IRBranchOp.Enum.Always) &&
                    !(block.Operations.Last() is IRRetOp) && i != blocks.Count - 1)
                {
                    blocks[i + 1].Parents.Add(block);
                    block.Children.Add(blocks[i + 1]);
                }
            }

            func.RegenerateOpIds();
            ConstructSSARename(func);
        }

        public static void ConstructSSARename(IRFunction func)
        {
            func.CalculateDominatorTree();


            // Remove unreachable or empty blocks
            bool changed;

            do
            {
                changed = false;
                for (int i = 0; i < func.Blocks.Count; i++)
                {
                    if (!func.DominatorTree.ContainsKey(func.Blocks[i]) || func.Blocks[i].Operations.Count == 0)
                    {
                        foreach (IRBasicBlock child in func.Blocks[i].Children)
                        {
                            child.Parents.Remove(func.Blocks[i]);
                            changed = true;
                        }

                        func.Blocks.RemoveAt(i--);
                        changed = true;
                    }
                }
            }
            while (changed);


            IRBasicBlock startNode = func.Blocks[0];

            /* Compute dominance frontiers for each block */
            foreach (IRBasicBlock b in func.Blocks)
            {
                /* All dominance frontier members
                   must have two of more parents (predecessors),
                   meaning they must be join points */
                if (b.Parents.Count >= 2)
                {
                    foreach (IRBasicBlock p in b.Parents)
                    {
                        IRBasicBlock runner = p;

                        while (runner.OValue != func.DominatorTree[b].OValue)
                        {
                            runner.DominanceFrontier.Add(b);
                            runner = func.DominatorTree[runner];
                        }
                    }
                }
            }

            var variableNodes = new Dictionary<string, List<IRBasicBlock>>();
            var phiPlacements = new Dictionary<string, List<IRBasicBlock>>();
            var counters = new Dictionary<string, int>();
            var stacks = new Dictionary<string, Stack<int>>();
            void GenName(string var) => stacks[var].Push(counters[var]++);

            foreach (var outside in func.OutsideSymbols)
                func.Blocks[0].InsertOp(0, new IROutsideOp(outside.Item2, outside.Item3));

            foreach (var arg in func.Arguments)
                func.Blocks[0].InsertOp(0, new IROutsideOp(arg.Item1, arg.Item2));

            foreach (IRBasicBlock block in func.Blocks)
            {
                foreach (IROp op in block.Operations)
                {
                    IRName name = null;

                    switch (op)
                    {
                        case IRLocalOp local:
                            variableNodes[local.Name] = new List<IRBasicBlock>();
                            stacks[local.Name] = new Stack<int>();
                            counters[local.Name] = 0;
                            GenName(local.Name);
                            break;
                        case IROutsideOp ext:
                            variableNodes[ext.Dest] = new List<IRBasicBlock>();
                            stacks[ext.Dest] = new Stack<int>();
                            counters[ext.Dest] = 0;
                            GenName(ext.Dest);
                            name = ext.Dest;
                            break;
                        case IRStackAllocOp alloc:
                            name = alloc.Dest;
                            break;
                        case IRAddrOfOp addrof:
                            name = addrof.Dest;
                            break;
                        case IRCallOp call when !call.IndirectStore:
                            name = call.Dest;
                            break;
                        case IRMovFlagOp flag when !flag.Indirect:
                            name = flag.Dest;
                            break;
                        case IRMovOp mov when !mov.Indirect:
                            name = mov.Dest;
                            break;
                        case IRLoadIndirectOp load:
                            name = load.Dest;
                            break;
                        case IRTwoOp two when !two.IndirectStore:
                            name = two.Dest;
                            break;
                        case IRThreeOp three when !three.IndirectStore:
                            name = three.Dest;
                            break;
                    }

                    if (name != null && variableNodes.ContainsKey(name) &&
                        !variableNodes[name].Contains(block))
                        variableNodes[name].Add(block);
                }
            }

            /* Figure out at which blocks to insert phi nodes for every variable */
            foreach (string key in variableNodes.Keys)
            {
                var S = variableNodes[key].ToList();
                if (S.Count > 1)
                {
                    if (!S.Contains(func.Blocks[0]))
                        S.Add(func.Blocks[0]);

                    for (int count = 0; ;)
                    {
                        var df = new List<IRBasicBlock>();

                        foreach (IRBasicBlock bb in S)
                            df = df.Union(bb.DominanceFrontier).ToList();

                        if (df.Count == count)
                        {
                            phiPlacements[key] = df;
                            break;
                        }

                        S = S.Union(df).ToList();
                        count = df.Count;
                    }
                }
                else
                    phiPlacements[key] = new List<IRBasicBlock>();
            }

            /* Insert phi nodes */
            foreach (string key in phiPlacements.Keys)
            {
                foreach (IRBasicBlock bb in phiPlacements[key])
                {
                    if (bb.Parents.Count >= 2)
                    {
                        int i = 0;
                        for (; i < bb.Operations.Count && bb.Operations[i] is IRLabel; i++);

                        /* Create phi node */
                        bb.InsertOp(i,
                            new IRPhiNode(key, new Dictionary<IRBasicBlock, IRName>()
                        ));
                    }
                }
            }

            void Rename(IRBasicBlock b)
            {
                if (b.Marked.Peek()) return;

                b.Marked.Pop();
                b.Marked.Push(true);

                /* Rename phi nodes */
                foreach (IROp op in b.Operations)
                {
                    if (op is IRPhiNode phi)
                    {
                        GenName(phi.MapTo);
                        phi.MapTo = $"{phi.MapTo}.{stacks[phi.MapTo].Peek()}";
                    }
                }

                IROperand RenameSrc(IROperand src)
                {
                    if (src is IRName name && stacks.ContainsKey(name))
                        return new IRName($"{name}.{stacks[name].Peek()}");
                    return src;
                }

                IROperand RenameDest(IROperand src)
                {
                    if (src is IRName name && stacks.ContainsKey(name.Name))
                    {
                        GenName(name);
                        return new IRName($"{name}.{stacks[name.Name].Peek()}");
                    }
                    return src;
                }

                /* Rename variables */
                foreach (IROp op in b.Operations)
                {
                    switch (op)
                    {
                        case IRStackAllocOp stack:
                            RenameDest(stack.Dest);
                            break;
                        case IRAddrOfOp addrof:
                            addrof.Variable = (IRName)RenameSrc(addrof.Variable);
                            addrof.Dest = (IRName)RenameDest(addrof.Dest);
                            break;
                        case IRCallOp call:
                            call.Callee = (IRName)RenameSrc(call.Callee);
                            call.Arguments = call.Arguments.Select(RenameSrc).ToList();
                            call.Dest = call.IndirectStore
                                ? (IRName)RenameSrc(call.Dest)
                                : (IRName)RenameDest(call.Dest);
                            break;
                        case IRMovFlagOp flag:
                            flag.Dest = flag.Indirect
                                ? (IRName)RenameSrc(flag.Dest)
                                : (IRName)RenameDest(flag.Dest);
                            break;
                        case IRCmpOp cmp:
                            cmp.A = RenameSrc(cmp.A);
                            cmp.B = RenameSrc(cmp.B);
                            break;
                        case IRMovOp mov:
                            mov.Src = RenameSrc(mov.Src);
                            mov.Dest = mov.Indirect
                                ? (IRName)RenameSrc(mov.Dest)
                                : (IRName)RenameDest(mov.Dest);
                            break;
                        case IROutsideOp ext:
                            ext.Dest = (IRName)RenameDest(ext.Dest);
                            break;
                        case IRLoadIndirectOp load:
                            load.Src = RenameSrc(load.Src);
                            load.Dest = (IRName)RenameDest(load.Dest);
                            break;
                        case IRTwoOp two:
                            two.Src = RenameSrc(two.Src);
                            two.Dest = two.IndirectStore
                                ? (IRName)RenameSrc(two.Dest)
                                : (IRName)RenameDest(two.Dest);
                            break;
                        case IRThreeOp three:
                            three.A = RenameSrc(three.A);
                            three.B = RenameSrc(three.B);
                            three.Dest =
                                three.IndirectStore
                                ? (IRName)RenameSrc(three.Dest) :
                                 (IRName)RenameDest(three.Dest);
                            break;
                        case IRChkOp chk:
                            chk.Operand = RenameSrc(chk.Operand);
                            break;
                        case IRRetOp ret:
                            ret.Value = RenameSrc(ret.Value);
                            break;
                    }
                }

                string GetPhiMapTo(IRPhiNode phi)
                    => phi.MapTo.Name.Split('.')[0];

                /* Assign variable choices in phi nodes */
                foreach (IRBasicBlock s in b.Children)
                    foreach (IROp op in s.Operations)
                        if (op is IRPhiNode phi)
                            phi.Choices[b] =
                                stacks[GetPhiMapTo(phi)].Count == 0 ? "" :
                                $"{GetPhiMapTo(phi)}.{stacks[GetPhiMapTo(phi)].Peek()}";

                /* Recurse through the dominator tree */
                foreach (IRBasicBlock block in func.DominatorTree.Keys)
                    if (block != b && func.DominatorTree[block] == b)
                        Rename(block);

                /* Unwind stack when done with this node */
                foreach (IROp op in b.Operations)
                {
                    void Pop(IROperand operand)
                    {
                        if (operand is IRName name)
                            if (name.Name.Contains('.'))
                                stacks[name.Name.Split('.')[0]].Pop();
                    }

                    switch (op)
                    {
                        case IRPhiNode phi:
                            Pop(phi.MapTo);
                            break;
                        case IRStackAllocOp alloc:
                            Pop(alloc.Dest);
                            break;
                        case IRAddrOfOp addrof:
                            Pop(addrof.Dest);
                            break;
                        case IRCallOp call when !call.IndirectStore:
                            Pop(call.Dest);
                            break;
                        case IRMovFlagOp flag when !flag.Indirect:
                            Pop(flag.Dest);
                            break;
                        case IRMovOp mov when !mov.Indirect:
                            Pop(mov.Dest);
                            break;
                        case IROutsideOp ext:
                            Pop(ext.Dest);
                            break;
                        case IRLoadIndirectOp load:
                            Pop(load.Dest);
                            break;
                        case IRTwoOp two when !two.IndirectStore:
                            Pop(two.Dest);
                            break;
                        case IRThreeOp three when !three.IndirectStore:
                            Pop(three.Dest);
                            break;
                    }
                }
            }

            func.PushMarked();
            Rename(startNode);
            func.PopMarked();
        }

        public static bool PhiReductionSSA(IRFunction func)
        {
            bool optimized = false;

            foreach (IRBasicBlock bb in func.Blocks)
            {
                for (int i = 0; i < bb.Operations.Count; i++)
                {
                    IROp op = bb.Operations[i];

                    if (op is IRPhiNode phi)
                    {
                        int original = phi.Choices.Count;
                        /* Filter out the entries of the phi node
                           and remove the ones are never assigned in any place in the function
                           (dead entries) */
                        phi.Choices = phi.Choices.Where(
                            kvp =>
                            IROpSearch.TraverseDstOperands((y, x) => (x as IRName).Name == kvp.Value.Name, func.Blocks)
                        ).ToDictionary(i => i.Key, i => i.Value);

                        if (phi.Choices.Count != original)
                            optimized = true;

                        if (phi.Choices.Count == 1)
                        {
                            bb.ReplaceAt(i, new IRMovOp(phi.MapTo, phi.Choices.Values.First(), false));
                            optimized = true;
                        }
                        else if (phi.Choices.Count == 0)
                        {
                            bb.Operations[i--].Unlink();
                            optimized = true;

                            //FixSSAVersions(func, phi.Block, phi.MapTo);
                        }
                    }
                }
            }

            return optimized;
        }

        public static bool ConstantPropagationSSA(IRFunction func)
        {
            bool optimized = false;

            foreach (IRBasicBlock bb in func.Blocks)
            {
                for (int i = 0; i < bb.Operations.Count; i++)
                {
                    IROp op = bb.Operations[i];

                    /* Reduce three-ops with both primitive operands to mov's */
                    if (op is IRThreeOp redThree)
                    {
                        dynamic value;
                        if (redThree.A is IRPrimitiveOperand a && redThree.B is IRPrimitiveOperand b)
                        {
                            value = redThree.Type switch
                            {
                                ThreeOpType.Add => (dynamic)a.Value + (dynamic)b.Value,
                                ThreeOpType.Sub => (dynamic)a.Value - (dynamic)b.Value,
                                ThreeOpType.Mul => (dynamic)a.Value * (dynamic)b.Value,
                                ThreeOpType.Div => (dynamic)a.Value / (dynamic)b.Value,
                                ThreeOpType.Mod => (dynamic)a.Value % (dynamic)b.Value,
                                ThreeOpType.Shl => (dynamic)a.Value << (b.Value is ulong u
                                    ? (int)u : (int)(long)b.Value),
                                ThreeOpType.Shr => (dynamic)a.Value >> (b.Value is ulong u
                                    ? (int)u : (int)(long)b.Value),
                                ThreeOpType.Rol => (dynamic)a.Value << (dynamic)b.Value,
                                ThreeOpType.Ror => (dynamic)a.Value >> (dynamic)b.Value,
                                ThreeOpType.Xor => (dynamic)a.Value ^ (dynamic)b.Value,
                                ThreeOpType.And => (dynamic)a.Value & (dynamic)b.Value,
                                ThreeOpType.Or => (dynamic)a.Value | (dynamic)b.Value,
                                _ => 0ul
                            };

                            IROp newOp = new IRMovOp(redThree.Dest,
                                new IRPrimitiveOperand(value), redThree.IndirectStore);
                            newOp.Block = op.Block;
                            op.Block.Operations.Replace(op, newOp);
                            optimized = true;
                        }
                    }
                    /* Reduce two-ops with a primitive operand to mov's */
                    else if (op is IRTwoOp redTwo &&
                        (redTwo.Type == TwoOpType.Not ||
                        redTwo.Type == TwoOpType.Neg))
                    {
                        if (redTwo.Src is IRPrimitiveOperand prim)
                        {
                            dynamic value;
                            if (redTwo.Type == TwoOpType.Not)
                                value = ~(dynamic)prim.Value;
                            else
                                value = -(dynamic)prim.Value;

                            IROp newOp = new IRMovOp(redTwo.Dest, new IRPrimitiveOperand(value), redTwo.IndirectStore);
                            newOp.Block = op.Block;
                            op.Block.Operations.Replace(op, newOp);
                            optimized = true;
                        }
                    }
                    /* If the chk is on a constant operand, evaluate it and
                     * either make the following branch unconditional or delete it */
                    else if (op is IRChkOp chk && chk.Operand is IRPrimitiveOperand prim &&
                        i == bb.Operations.Count - 2 &&
                        bb.Operations.Last() is IRBranchOp branch)
                    {
                        bool pos = (dynamic)prim.Value > 0;
                        bool neg = (dynamic)prim.Value < 0;
                        bool zero = (dynamic)prim.Value == 0;

                        bool isTrue = false;

                        switch (branch.Condition)
                        {
                            case IRBranchOp.Enum.Positive: isTrue = pos;   break;
                            case IRBranchOp.Enum.Negative: isTrue = neg;   break;
                            case IRBranchOp.Enum.Zero:     isTrue = zero;  break;
                            case IRBranchOp.Enum.NotZero:  isTrue = !zero; break;
                            default: continue;
                        }

                        // Remove chk operation
                        bb.Operations.RemoveAt(bb.Operations.Count - 2);

                        // Decide the branch's fate
                        if (isTrue)
                        {
                            branch.Condition = IRBranchOp.Enum.Always;

                            for (int c = 0; c < bb.Children.Count; c++)
                                if (!branch.PointsTo(bb.Children[c]))
                                    bb.Children.RemoveAt(c--);

                            if (branch.PointsTo(bb) && !bb.Parents.Contains(bb))
                                bb.Parents.Add(bb);
                        }
                        else
                        {
                            bb.Operations.RemoveAt(bb.Operations.Count - 1);

                            for (int c = 0; c < bb.Children.Count; c++)
                                if (branch.PointsTo(bb.Children[c]))
                                    bb.Children.RemoveAt(c--);

                            if (branch.PointsTo(bb))
                                bb.Parents.Remove(bb);
                        }
                    }
                }
            }

            if (optimized)
                func.CalculateDominatorTree();

            return optimized;
        }

        static string SSABaseName(string name)
            => name.Split('.')[0];

        static int SSAVersion(string name)
            => int.Parse(name.Split('.')[1]);

        static string SSADecrement(string name, int by = 1) => $"{SSABaseName(name)}.{SSAVersion(name) - by}";

        static void TraverseDominatorTree(IRFunction func, IRBasicBlock start, Action<IRBasicBlock> action)
        {
            void VisitBlock(IRBasicBlock bb)
            {
                if (bb.Marked.Peek())
                    return;

                bb.Marked.Pop();
                bb.Marked.Push(true);

                action(bb);

                foreach (IRBasicBlock block in func.DominatorTree.Keys)
                    if (block != bb && func.DominatorTree[block] == bb)
                        VisitBlock(block);
            }


            func.PushMarked();
            VisitBlock(start);
            func.PopMarked();
        }


        public static bool CopyPropagationSSA(IRFunction func, ArchitectureInfo arch)
        {
            bool optimized = false;
            Stack<IROp> worklist = new Stack<IROp>();

            var useCounts = new Dictionary<string, int>();

            bool UsedAtLeastOnce(string name)
                => useCounts.ContainsKey(name) && useCounts[name] >= 1;

            IROpSearch.TraverseSrcOperands((y, x) =>
            {
                if (x is IRName n)
                {
                    if (!useCounts.ContainsKey(n.Name))
                        useCounts[n.Name] = 0;
                    useCounts[n.Name]++;
                }
                return true;
            },
            func.Blocks);

            foreach (IRBasicBlock bb in func.Blocks)
                foreach (IROp op in bb.Operations)
                    if (!op.Volatile)
                        worklist.Push(op);

            bool IsNewerVersion(IROperand operand, IROperand dest)
            {
                return operand is IRName opName && dest is IRName destName &&
                    SSABaseName(destName) == SSABaseName(opName) &&
                    SSAVersion(destName) > SSAVersion(opName);
            }

            while (worklist.Count > 0)
            {
                IROp op = worklist.Pop();
                IROperand operand = null;
                IRType operandType = null;
                IROperand find = null;
                bool indirectLoad = false;
                HashSet<string> stopAtNextVersion = new HashSet<string>();

                if (op is IRMovOp mov && !mov.Indirect)
                {
                    find = mov.Dest;
                    operand = mov.Src;
                    if (arch is X64ArchitectureInfo
                        && mov.Src is IRPrimitiveOperand prim
                        && X64LargeConstantEliminator.OutOfBounds(prim.Value))
                        continue;
                }
                /*else if (op is IRLoadIndirectOp load)
                {
                    operandType = load.SrcType;
                    find = load.Dest;
                    operand = load.Src;
                    indirectLoad = true;
                }*/
                else
                    continue;


                bool everExitEarly = false;
                bool ignored = false;
                bool newerVersion = false;
                IRName oldDest;

                void UpdateUse()
                {
                    if (operand is IRName n)
                        useCounts[n.Name]++;
                    useCounts[(find as IRName).Name]--;
                }

                void VisitBlock(IRBasicBlock bb)
                {
                    bool exitEarly = false;

                    void HandleNewVer(string name)
                    {
                        if (stopAtNextVersion.Contains(SSABaseName(name)) || UsedAtLeastOnce(name))
                            exitEarly = true;
                        else
                            stopAtNextVersion.Add(SSABaseName(name));
                    }

                    for (int i = 0; i < bb.Operations.Count; i++)
                    {
                        IROp t = bb.Operations[i];
                        bool replaced = false;

                        switch (t)
                        {
                            case IRStackAllocOp alloc:
                                if (IsNewerVersion(find, alloc.Dest))
                                    newerVersion = true;
                                break;
                            case IRAddrOfOp addrof:
                                if (IsNewerVersion(find, addrof.Dest))
                                    newerVersion = true;
                                if (addrof.Variable.Name == (find as IRName).Name)
                                    ignored = true;
                                break;
                            case IRPhiNode phi:
                                if (IsNewerVersion(find, phi.MapTo))
                                    newerVersion = true;
                                if (phi.Choices.Values.Any(y => y.Name == (find as IRName).Name))
                                    ignored = true;
                                break;
                            case IRCallOp call:
                                if (call.IndirectStore && call.Dest.Equals(find))
                                {
                                    if (call.Volatile || indirectLoad || func.RegistersAllocated &&
                                        MemToMemEliminator.IsMemOperand(operand))
                                        ignored = true;
                                    else
                                    {
                                        replaced = true;
                                        UpdateUse();
                                        call.Dest = (IRName)operand;
                                    }
                                }

                                if (call.Callee.Equals(find))
                                {
                                    if (call.Volatile || indirectLoad)
                                        ignored = true;
                                    else
                                    {
                                        replaced = true;
                                        UpdateUse();
                                        call.Callee = (IRName)operand;
                                    }
                                }

                                call.Arguments = call.Arguments.Select(
                                    (x, i) =>
                                    {
                                        if (x.Equals(find))
                                        {
                                            if (call.Volatile || indirectLoad)
                                            {
                                                ignored = true;
                                                return x;
                                            }
                                            else
                                            {
                                                replaced = true;
                                                UpdateUse();
                                                return operand;
                                            }
                                        }
                                        else
                                            return x;
                                    }
                                ).ToList();
                                break;
                            case IRCmpOp cmp:
                                if (cmp.A.Equals(find))
                                {
                                    if (cmp.Volatile || indirectLoad ||
                                        func.RegistersAllocated &&
                                        MemToMemEliminator.IsMemToMem(cmp.B, operand))
                                        ignored = true;
                                    else
                                    {
                                        replaced = true;
                                        cmp.A = operand;
                                        UpdateUse();
                                    }
                                }
                                if (cmp.B.Equals(find))
                                {
                                    if (cmp.Volatile || indirectLoad ||
                                        func.RegistersAllocated &&
                                        MemToMemEliminator.IsMemToMem(cmp.A, operand))
                                        ignored = true;
                                    else
                                    {
                                        replaced = true;
                                        cmp.B = operand;
                                        UpdateUse();
                                    }
                                }
                                break;
                            case IRMovFlagOp flag:
                                if (IsNewerVersion(find, flag.Dest))
                                    newerVersion = true;
                                if (flag.Dest.Equals(find))
                                {
                                    if (flag.Volatile || indirectLoad)
                                        ignored = true;
                                    else if (flag.Indirect)
                                    {
                                        if (func.RegistersAllocated && MemToMemEliminator.IsMemOperand(operand))
                                            ignored = true;
                                        else
                                        {
                                            replaced = true;
                                            flag.Dest = operand as IRName;
                                            UpdateUse();
                                        }
                                    }
                                }
                                if (!flag.Indirect && IsNewerVersion(operand, flag.Dest))
                                    HandleNewVer(flag.Dest.Name);
                                break;
                            case IRMovOp mv:
                                if (IsNewerVersion(find, mv.Dest))
                                    newerVersion = true;
                                oldDest = mv.Dest;
                                if (mv.Src.Equals(find))
                                {
                                    if (mv.Volatile)
                                        ignored = true;
                                    else if (indirectLoad)
                                    {
                                        if (func.RegistersAllocated && (mv.Indirect || MemToMemEliminator.IsMemOperand(mv.Dest)))
                                            ignored = true;
                                        else
                                        {
                                            replaced = true;
                                            bb.ReplaceAt(i,
                                                new IRLoadIndirectOp(mv.Dest, operandType, operand));
                                            UpdateUse();
                                        }
                                    }
                                    else
                                    {
                                        if (func.RegistersAllocated &&
                                            MemToMemEliminator.IsMemToMem(mv.Dest, operand, mv.Indirect))
                                            ignored = true;
                                        else
                                        {
                                            replaced = true;
                                            mv.Src = operand;
                                            UpdateUse();
                                        }
                                    }
                                }
                                else if (mv != op && mv.Dest.Equals(find))
                                {
                                    if (mv.Volatile || indirectLoad || !(operand is IRName) ||
                                        !(find is IRName name) ||
                                        func.RegistersAllocated &&
                                        (mv.Indirect && MemToMemEliminator.IsMemOperand(operand) ||
                                        MemToMemEliminator.IsMemToMem(operand, mv.Src, mv.Indirect)))
                                        ignored = true;
                                    else
                                    {
                                        replaced = true;
                                        mv.Dest = operand as IRName;
                                        UpdateUse();
                                    }
                                }
                                if (IsNewerVersion(operand, oldDest))
                                    HandleNewVer(oldDest.Name);
                                break;
                            case IRLoadIndirectOp load:
                                if (IsNewerVersion(find, load.Dest))
                                    newerVersion = true;
                                if (load.Src.Equals(find))
                                {
                                    if (!(operand is IRName) || load.Volatile || indirectLoad ||
                                        func.RegistersAllocated &&
                                        MemToMemEliminator.IsMemOperand(operand))
                                        ignored = true;
                                    else
                                    {
                                        replaced = true;
                                        load.Src = operand;
                                        UpdateUse();
                                    }
                                }
                                if (IsNewerVersion(operand, load.Dest))
                                    HandleNewVer(load.Dest.Name);
                                break;
                            case IRTwoOp two:
                                if (IsNewerVersion(find, two.Dest))
                                    newerVersion = true;
                                oldDest = two.Dest;
                                if (two.Src.Equals(find))
                                {
                                    if (two.Volatile)
                                        ignored = true;
                                    else if (indirectLoad)
                                    {
                                        if (func.RegistersAllocated && (two.IndirectStore || MemToMemEliminator.IsMemOperand(two.Dest)))
                                            ignored = true;
                                        else
                                        {
                                            replaced = true;
                                            bb.ReplaceAt(i,
                                                new IRLoadIndirectOp(two.Dest, operandType, operand));
                                            UpdateUse();
                                        }
                                    }
                                    else
                                    {
                                        if (!(operand is IRName name) ||
                                            func.RegistersAllocated &&
                                                MemToMemEliminator.IsMemToMem(two.Dest, operand, two.IndirectStore))
                                            ignored = true;
                                        else
                                        {
                                            replaced = true;
                                            two.Src = operand;
                                            UpdateUse();
                                        }
                                    }
                                }
                                else if (func.RegistersAllocated && MemToMemEliminator.IsMemToMem(two.Dest, operand))
                                    ignored = true;
                                else if (two.Dest.Equals(find))
                                {
                                    if (two.Volatile || indirectLoad ||
                                        !(operand is IRName name) ||
                                        func.RegistersAllocated &&
                                         MemToMemEliminator.IsMemToMem(operand, two.Src, two.IndirectStore))
                                        ignored = true;
                                    else
                                    {
                                        replaced = true;
                                        two.Dest = operand as IRName;
                                        UpdateUse();
                                    }
                                }
                                if (IsNewerVersion(operand, oldDest))
                                    HandleNewVer(oldDest.Name);
                                break;
                            case IRThreeOp three:
                                if (IsNewerVersion(find, three.Dest))
                                    newerVersion = true;

                                oldDest = three.Dest;

                                if (three.IndirectStore && three.Dest.Equals(find))
                                {
                                    if (three.Volatile || indirectLoad || operand is IRPrimitiveOperand prim)
                                        ignored = true;
                                    else
                                    {
                                        replaced = true;
                                        three.Dest = operand as IRName;
                                        UpdateUse();
                                    }
                                }

                                if (three.A.Equals(find))
                                {
                                    if (three.Volatile || indirectLoad ||
                                        func.RegistersAllocated &&
                                        (three.DestType != operandType ||
                                        operand.ToString().Split('.')[0] != three.Dest.ToString().Split('.')[0]))
                                        ignored = true;
                                    else
                                    {
                                        replaced = true;
                                        three.A = operand;
                                        UpdateUse();
                                    }
                                }

                                if (three.B.Equals(find))
                                {
                                    if (three.Volatile || indirectLoad ||
                                        func.RegistersAllocated &&
                                        (!X64DivArgSpiller.OptAllowed(three, operand) || MemToMemEliminator.IsMemToMem(three.Dest, operand)))
                                        ignored = true;
                                    else
                                    {
                                        replaced = true;
                                        three.B = operand;
                                        UpdateUse();
                                    }
                                }
                                if (IsNewerVersion(operand, oldDest))
                                    HandleNewVer(oldDest.Name);
                                break;
                            case IRChkOp chk:
                                if (chk.Operand.Equals(find))
                                {
                                    if (chk.Volatile || indirectLoad)
                                        ignored = true;
                                    else
                                    {
                                        replaced = true;
                                        chk.Operand = operand;
                                        UpdateUse();
                                    }
                                }
                                break;
                            case IRRetOp ret:
                                if (ret.Value != null && ret.Value.Equals(find))
                                {
                                    if (ret.Volatile || indirectLoad)
                                        ignored = true;
                                    else
                                    {
                                        replaced = true;
                                        ret.Value = operand;
                                        UpdateUse();
                                    }
                                }
                                break;
                        }

                        if (replaced)
                        {
                            optimized = true;
                            worklist.Push(t);
                        }

                        if (exitEarly)
                        {
                            everExitEarly = true;
                            return;
                        }
                    }
                }

                TraverseDominatorTree(func, func.Blocks[0], VisitBlock);

                if ((newerVersion || !func.OutsideSymbols.Any(x => x.Item2 == SSABaseName((find as IRName).Name))) &&
                    !everExitEarly &&
                    !ignored)
                {
                    op.Unlink();
                    optimized = true;

                    //FixSSAVersions(func, op.Block, find as IRName);
                }
            }

            return optimized;
        }

        public static bool RedundancySSA(IRFunction func)
        {
            bool optimized = false;
            var lastAssigned = new Dictionary<string, (IROp, IROperand)>();

            void VisitBlock(IRBasicBlock bb)
            {
                List<(int, string)> nopList = new List<(int, string)>();

                IROpSearch.TraverseDstOperands((y, x) =>
                {
                    if (y.Volatile)
                        return false;

                    string bname = SSABaseName(x as IRName);
                    IROperand src = null;

                    if (y is IRMovOp mov && !mov.Indirect)
                    {
                        src = mov.Src;
                        if (src is IRName name && SSABaseName(name) == SSABaseName(mov.Dest))
                        {
                            nopList.Add((bb.Operations.IndexOf(y), mov.Dest));
                            return false;
                        }
                    }
                    else if (y is IRAddrOfOp addrof)
                        src = addrof.Variable;
                    else
                    {
                        if (lastAssigned.ContainsKey(bname))
                            lastAssigned.Remove(bname);
                        return false;
                    }

                    if (!lastAssigned.ContainsKey(bname))
                        lastAssigned[bname] = (y, src);
                    else
                    {
                        (IROp oldOp, IROperand oldOper) = lastAssigned[bname];

                        if (!oldOper.Equals(src) || oldOp.GetType() != y.GetType())
                            lastAssigned[bname] = (y, src);
                        else
                            nopList.Add((bb.Operations.IndexOf(y), x as IRName));
                    }

                    return false;
                }, new List<IRBasicBlock> { bb });

                for (int j = 0; j < nopList.Count; j++)
                {
                    (int i, string name) = nopList[j];

                    for (int k = j + 1; k < nopList.Count; k++)
                        if (SSABaseName(name) == SSABaseName(nopList[k].Item2) && SSAVersion(nopList[k].Item2) > SSAVersion(name))
                            nopList[k] = (nopList[k].Item1, SSADecrement(nopList[k].Item2));

                    void UpdateVersions(IRBasicBlock b)
                    {
                        // Update SSA versions
                        Func<IROp, IROperand, IROperand> replace = (y, x) =>
                        {
                            if (x is IRName n && SSABaseName(n) == SSABaseName(name) && SSAVersion(n) >= SSAVersion(name))
                                return new IRName(SSADecrement(n, 1));
                            return x;
                        };
                        IROpSearch.ReplaceOperands(replace, replace, new List<IRBasicBlock> { b });
                    }

                    optimized = true;
                    bb.ReplaceAt(i, new IRNoOp());

                    TraverseDominatorTree(func, bb, UpdateVersions);
                }

                /*var current = new KeyValuePair<string, (int, string)>();

                void UpdateVersions(IRBasicBlock b)
                {
                    // Update SSA versions
                    Func<IROp, IROperand, IROperand> replace = (y, x) =>
                    {
                        if (x is IRName n && SSAVersion(n) >= SSAVersion(current.Value.Item2))
                            return new IRName(SSADecrement(n, current.Value.Item1));
                        return x;
                    };
                    IROpSearch.ReplaceOperands(replace, replace, new List<IRBasicBlock> { b });
                }

                var dict = new Dictionary<string, (int, string)>();

                foreach ((int i, string name) in nopList)
                {
                    string bs = SSABaseName(name);
                    if (dict.ContainsKey(bs)) dict[bs] = (dict[bs].Item1 + 1, name);
                    else dict[bs] = (1, name);
                }

                foreach (KeyValuePair<string, (int, string)> kvp in dict)
                {
                    current = kvp;
                    TraverseDominatorTree(func, bb, UpdateVersions);
                }*/
            }

            TraverseDominatorTree(func, func.Blocks[0], VisitBlock);

            return optimized;
        }

        public static bool ConditionChecksSSA(IRFunction func)
        {
            bool optimized = false;

            foreach (IRBasicBlock block in func.Blocks)
            {
                if (block.Operations.Count >= 3)
                {
                    int start = block.Operations.Count - 3;
                    if (block.Operations[start + 0] is IRMovFlagOp flag && !flag.Volatile &&
                        block.Operations[start + 1] is IRChkOp chk && !chk.Volatile &&
                        block.Operations[start + 2] is IRBranchOp branch && !branch.Volatile)
                    {
                        if (branch.Condition == IRBranchOp.Enum.Zero)
                        {
                            block.Operations.RemoveAt(start);
                            block.Operations.RemoveAt(start);
                            branch.Condition = (IRBranchOp.Enum)((int)flag.Flag ^ 1);
                            optimized = true;
                        }
                        else if (branch.Condition == IRBranchOp.Enum.NotZero)
                        {
                            block.Operations.RemoveAt(start);
                            block.Operations.RemoveAt(start);
                            branch.Condition = flag.Flag;
                            optimized = true;
                        }
                    }
                }
            }

            return optimized;
        }

        // Optimizes things like
        // mov b.0, a.0
        // add b.1, b.0, 1
        // mov a.1, b.1
        // to
        // add a.1, a.0, 1
        public static bool RedundantVariableSSA(IRFunction func)
        {
            bool optimized = false;

            foreach (IRBasicBlock block in func.Blocks)
            {
                for (int i = 0; i < block.Operations.Count; i++)
                {
                    IRMovOp mov2 = null;

                    if (block.Operations[i] is IRMovOp mov && !mov.Volatile && !mov.Indirect && mov.Src is IRName)
                    {
                        IRName main = mov.Src as IRName;
                        IRName redundantFirst = mov.Dest;

                        int j;
                        bool found = false;

                        for (j = i + 1; j < block.Operations.Count; j++)
                        {
                            if (block.Operations[j] is IRMovOp _mov2 &&
                                !_mov2.Indirect &&
                                !_mov2.Volatile &&
                                _mov2.Src is IRName redundantSecond)
                            {
                                mov2 = _mov2;
                                bool a = SSABaseName(mov2.Dest) == SSABaseName(main);
                                bool b = SSABaseName(redundantSecond) == SSABaseName(redundantFirst);

                                if (a && !b)
                                    break;
                                if (a && b)
                                {
                                    found = true;
                                    break;
                                }
                            }

                            bool stop = false;

                            IROpSearch.TraverseOperands(
                                (y, x) =>
                                {
                                    if (x is IRName dstName && SSABaseName(dstName) == SSABaseName(main))
                                        stop = true;
                                },
                                (y, x) =>
                                {
                                    if (x is IRName srcName && srcName == main.Name)
                                        stop = true;
                                },
                                block.Operations[j]);

                            if (stop)
                                break;
                        }

                        bool FindConflicts(IRBasicBlock block)
                        {
                            if (block.Marked.Peek())
                                return false;

                            block.Marked.Pop();
                            block.Marked.Push(true);

                            bool newerVersion = false;
                            bool detected = false;
                            bool checkMemToMem = func.RegistersAllocated && MemToMemEliminator.IsMemOperand(main);

                            string bname = SSABaseName(mov2.Src as IRName);

                            IROpSearch.TraverseOperands(
                                (y, x) =>
                                {
                                    if (!detected && checkMemToMem && x is IRName && SSABaseName(x as IRName) == bname)
                                    {
                                        if (y is IRMovOp mv && MemToMemEliminator.IsMemOperand(mv.Src) ||
                                            y is IRThreeOp three && MemToMemEliminator.IsMemOperand(three.B) ||
                                            y is IRLoadIndirectOp load ||
                                            y is IRCallOp call && call.IndirectStore)
                                            detected = true;
                                    }

                                    if (x is IRName d && SSABaseName(d) == SSABaseName(main) &&
                                        SSAVersion(d) > SSAVersion(mov2.Dest))
                                        newerVersion = true;
                                },
                                (y, x) =>
                                {
                                    if (!detected && checkMemToMem && x is IRName && SSABaseName(x as IRName) == bname)
                                    {
                                        if (y is IRMovOp mv)
                                            detected |= mv.Indirect || MemToMemEliminator.IsMemOperand(mv.Dest);
                                        if (y is IRTwoOp two)
                                            detected |= two.IndirectStore || MemToMemEliminator.IsMemOperand(two.Dest);
                                        else if (y is IRCmpOp cmp)
                                        {
                                            if (cmp.A is IRName n && SSABaseName(n) == bname)
                                                detected |= MemToMemEliminator.IsMemOperand(cmp.B);
                                            else if (cmp.B is IRName n2 && SSABaseName(n2) == bname)
                                                detected |= MemToMemEliminator.IsMemOperand(cmp.A);
                                        }
                                        else if (y is IRThreeOp three && three.B is IRName n)
                                            detected |= SSABaseName(n) == bname && MemToMemEliminator.IsMemOperand(three.A);
                                        else if (y is IRLoadIndirectOp load || y is IRMovFlagOp flag && flag.Indirect)
                                            detected = true;
                                    }

                                    if (!detected && newerVersion && x is IRName s && s.Name == (mov2.Src as IRName).Name)
                                        detected = true;
                                }, new List<IRBasicBlock> { block });

                            if (detected)
                                return true;

                            foreach (IRBasicBlock b in func.DominatorTree.Keys)
                                if (block != b && func.DominatorTree[block] == b)
                                    if (FindConflicts(b))
                                        return true;

                            return false;
                        }

                        bool conflicts = false;

                        if (found)
                        {
                            func.PushMarked();
                            conflicts = FindConflicts(block);
                            func.PopMarked();
                        }

                        // Check if there are no references to the final version of the redundant variable
                        // other than it being assigned to the main variable
                        if (found && !conflicts)
                        {
                            block.Operations.RemoveAt(j);
                            block.Operations.RemoveAt(i);

                            int ver = SSAVersion(main);
                            int oldVer = ver;

                            IROp lastDestReplaced = null;

                            for (int k = i; k <= j - 2; k++)
                            {
                                IROpSearch.ReplaceOperands(
                                    (y, x) =>
                                    {
                                        if (x is IRName name && SSABaseName(name) == SSABaseName(redundantFirst))
                                        {
                                            lastDestReplaced = y;
                                            return new IRName($"{SSABaseName(main)}.{++ver}");
                                        }
                                        return x;
                                    },
                                    (y, x) =>
                                    {
                                        if (x is IRName name && SSABaseName(name) == SSABaseName(redundantFirst))
                                            return new IRName($"{SSABaseName(main)}.{ver}");
                                        return x;
                                    },
                                    block.Operations[k]);
                            }

                            int diff = ver - SSAVersion(mov2.Dest);
                            int increment = Math.Max(0, diff);

                            if (lastDestReplaced != null && diff < 0)
                                IROpSearch.ReplaceOperands((y, x) => new IRName(mov2.Dest.Name), (y, x) => x, lastDestReplaced);

                            Func<IROp, IROperand, IROperand> replace = (y, x) =>
                            {
                                if (x is IRName name)
                                {
                                    if (SSABaseName(name) == SSABaseName(main.Name)
                                        && SSAVersion(name) >= SSAVersion(mov2.Dest))
                                        return new IRName($"{SSABaseName(name)}.{SSAVersion(name) + increment}");
                                    else if (y != mov2 && name.Name == (mov2.Src as IRName).Name)
                                        return new IRName(mov2.Dest.Name);
                                }
                                return x;
                            };

                            IROpSearch.ReplaceOperands(replace, replace, func.Blocks);

                            optimized = true;
                        }
                    }
                }
            }

            return optimized;
        }

        public static bool DeadCodeSSA(IRFunction func)
        {
            bool optimized = false;
            var defined = new Dictionary<IRName, IROp>();
            var used = new List<IRName>();

            /* Remove nops */
            foreach (IRBasicBlock block in func.Blocks)
            {
                for (int i = 0; i < block.Operations.Count; i++)
                {
                    if (block.Operations[i] is IRNoOp)
                    {
                        block.Operations.RemoveAt(i--);
                        optimized = true;
                    }
                }
            }

            IROpSearch.TraverseOperands(
                (op, x) =>
                {
                    if (x is IRName name)
                    {
                        if (op is IRMovOp mov      && mov.Indirect && x == mov.Dest ||
                            op is IRTwoOp two      && two.IndirectStore && x == two.Dest ||
                            op is IRThreeOp three  && three.IndirectStore && x == three.Dest ||
                            op is IRMovFlagOp flag && flag.Indirect && x == flag.Dest)
                        {
                            if (!used.Contains(name))
                                used.Add(name);
                        }
                        else if (!defined.ContainsKey(name) && !(op is IRCallOp))
                            defined[name] = op;
                    }
                },

                (op, x) => 
                {
                    if (x is IRName name && !used.Contains(name))
                        used.Add(name);
                },
                func.Blocks);

            foreach (KeyValuePair<IRName, IROp> kvp in defined)
            {
                if (!kvp.Value.Volatile && !used.Contains(kvp.Key) && !func.OutsideSymbols.Any(x => x.Item2 == SSABaseName(kvp.Key)))
                {
                    /* No uses for definition, can unlink operation */
                    kvp.Value.Unlink();
                    optimized = true;

                    //FixSSAVersions(func, kvp.Value.Block, kvp.Key);
                }
            }

            return optimized;
        }

        public static void DestroySSA(IRFunction func)
        {
            /* Remove version names */
            Func<IROp, IROperand, IROperand> replacement = (IROp op, IROperand operand) =>
                (operand is IRName name) ?
                    new IRName(name.ToString().Split('.')[0]) :
                    operand;

            IROpSearch.ReplaceOperands(replacement, replacement, func.Blocks);

            /* Eliminate phi nodes by inserting copies at its predecessors */
            foreach (IRBasicBlock bb in func.Blocks)
            {
                for (int i = 0; i < bb.Operations.Count; i++)
                {
                    if (bb.Operations[i] is IRPhiNode phi)
                    {
                        foreach (KeyValuePair<IRBasicBlock, IRName> kvp in phi.Choices)
                        {
                            /* If the PHI variable choice's base name differs from the
                               PHI variable's name, we should insert a copy at the end of
                               this block's predecessor */
                            if (phi.MapTo.Name != kvp.Value.Name)
                            {
                                int j, opIndex = kvp.Key.Operations.Count - 1;

                                for (j = 0; j < kvp.Key.Operations.Count; j++)
                                {
                                    if (kvp.Key.Operations[j] is IRBranchOp branch && branch.PointsTo(bb))
                                    {
                                        opIndex = j;
                                        break;
                                    }
                                }

                                kvp.Key.InsertOp(opIndex, new IRMovOp(phi.MapTo, kvp.Value));
                            }
                        }

                        i--;
                        phi.Unlink();
                    }
                }
            }
            func.RegenerateOpIds();
        }
    }
}