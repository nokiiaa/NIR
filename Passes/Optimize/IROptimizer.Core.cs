using NIR.Instructions;
using System;
using System.Collections.Generic;

namespace NIR.Passes.Optimize
{
    public static class ListExt
    {
        public static int Replace<T>(this IList<T> source, T oldValue, T newValue)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var index = source.IndexOf(oldValue);
            if (index != -1)
                source[index] = newValue;
            return index;
        }
    }
    public partial class IROptimizer : IRPass
    {

#if false
        static void UpAndMark(IRBasicBlock block, IRName var)
        {
            if (!Analysis.TraverseDstOperand((y, x) => !(y is IRPhiNode) && x == var,
                new List<IRBasicBlock> { block }) &&
                !block.LiveIn.Contains(var))
            {
                block.LiveIn.Add(var);
                foreach (IROp op in block.Operations)
                    if (op is IRPhiNode phi)
                        if (phi.MapTo.Name == var.Name)
                            return;
                foreach (IRBasicBlock parent in block.Parents)
                {
                    if (!parent.LiveOut.Contains(var))
                        parent.LiveOut.Add(var);
                    UpAndMark(parent, var);
                }
            }
        }

        static void LivenessAnalysis(List<IRBasicBlock> blocks)
        {
            foreach (IRBasicBlock bb in blocks)
            {
                List<IRName> phiUses = new List<IRName>();
                foreach (IRBasicBlock child in bb.Children)
                {
                    Analysis.TraverseSrcOperand((y, x) =>
                    {
                        if (y is IRPhiNode phi)
                        {
                            IRName name = x as IRName;
                            if (!phiUses.Contains(name))
                            {
                                if (!bb.LiveOut.Contains(name))
                                    bb.LiveOut.Add(name);
                                UpAndMark(bb, name);
                                phiUses.Add(name);
                            }
                            return true;
                        }
                        return false;
                    }, new List<IRBasicBlock> { child });
                }
                Analysis.TraverseSrcOperand((y, x) =>
                {
                    if (!(y is IRPhiNode) && x is IRName name)
                    {
                        UpAndMark(bb, name);
                        return true;
                    }
                    return false;
                }, new List<IRBasicBlock> { bb });
            }
        }
#endif

        public static bool RemoveNoops(IRFunction func)
        {
            bool optimized = false;

            foreach (IRBasicBlock block in func.Blocks)
            {
                for (int i = 0; i < block.Operations.Count; i++)
                {
                    if (block.Operations[i] is IRMovOp mov &&
                        !mov.Indirect &&
                        mov.Src is IRName name &&
                        mov.Dest == name.Name)
                    {
                        optimized = true;
                        block.Operations[i--].Unlink();
                    }
                }
            }

            return optimized;
        }

        public static bool RemoveDeadAllocs(IRFunction func)
        {
            bool optimized = false;

            foreach (IRBasicBlock bb in func.Blocks)
            {
                for (int i = 0; i < bb.Operations.Count; i++)
                {
                    IROp t = bb.Operations[i];

                    if (t is IRLocalOp alloc)
                    {
                        bool used = false;
                        IROpSearch.TraverseOperands(
                            (y, x) => { if (y is IRCallOp && x is IRName name && name.Name == alloc.Name) used = true; },
                            (y, x) => { if (x is IRName name && name.Name == alloc.Name) used = true; },
                            func.Blocks);
                        /* If no uses found of this variable whatsoever, unlink the alloc operation */
                        if (!used)
                        {
                            i--;
                            alloc.Unlink();
                            optimized = true;
                        }
                    }
                }
            }

            if (optimized)
                func.RegenerateOpIds();
            return optimized;
        }

        public override void Perform(IRFunction func, ArchitectureInfo info)
        {
            if (!func.NoDefinition)
            {
                ConstructSSA(func);

                /* Run optimization passes until no more optimization can be done */
                while (PhiReductionSSA(func)       |
                    ConstantPropagationSSA(func)   |
                    CopyPropagationSSA(func, info) |
                    DeadCodeSSA(func)              | 
                    RedundancySSA(func)            |
                    ConditionChecksSSA(func)       |
                    RedundantVariableSSA(func));

                func.RegenerateOpIds();
                DestroySSA(func);
                RemoveNoops(func);
                RemoveDeadAllocs(func);

                List<IROp> reassembled = new List<IROp>();
                foreach (IRBasicBlock bb in func.Blocks)
                    reassembled.AddRange(bb.Operations);

                func.Operations = reassembled;
           }
        }
    }
}
