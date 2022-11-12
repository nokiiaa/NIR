using NIR.Instructions;
using NIR.Passes.RegAlloc;
using System;
using System.Collections.Generic;
using System.Text;

namespace NIR.Passes.X64Specific
{
    public class X64DivArgSpiller : IRPass
    {
        public static bool OptAllowed(IRThreeOp three, IROperand replacement)
            => (three.Type != ThreeOpType.Div &&
                three.Type != ThreeOpType.Mod) || !(replacement is IRName name) ||
                !name.Name.StartsWith("#r0.") && !name.Name.StartsWith("#r2.");

        public override void Perform(IRFunction func, ArchitectureInfo arch)
        {
            if (func.Arguments.Count >= 2)
            {
                var varTypes = new Dictionary<IRName, IRType>();

                foreach (var arg in func.Arguments)
                    varTypes[arg.Item1] = arg.Item2;

                foreach (IRBasicBlock block in func.Blocks)
                {
                    for (int i = 0; i < block.Operations.Count; i++)
                    {
                        if (block.Operations[i] is IRLocalOp alloc)
                            varTypes[alloc.Name] = alloc.Type;
                        else if (block.Operations[i] is IRThreeOp three &&
                            (three.Type == ThreeOpType.Div || three.Type == ThreeOpType.Mod))
                        {
                            bool FindRefsTo2ndArg(IRBasicBlock bb)
                            {
                                if (bb.Marked.Peek())
                                    return false;

                                bb.Marked.Pop();
                                bb.Marked.Push(true);

                                bool found = false;

                                void find(IROp y, IROperand x)
                                {
                                    if (y.Block == block && y.Id < three.Id)
                                        return;
                                    if (x is IRName name && name.Name == func.Arguments[1].Item1)
                                        found = true;
                                }

                                IROpSearch.TraverseOperands(find, find, new List<IRBasicBlock> { bb });

                                if (found)
                                    return true;

                                foreach (IRBasicBlock b in func.DominatorTree.Keys)
                                    if (bb != b && func.DominatorTree[bb] == b)
                                        if (FindRefsTo2ndArg(b))
                                            return true;

                                return false;
                            }

                            func.PushMarked();
                            bool found = FindRefsTo2ndArg(block);
                            func.PopMarked();

                            if (found)
                            {
                                IRType type = func.Arguments[1].Item2;
                                IRName tmpName = IRGreedyAlloc.GenTemp();

                                if (three.B is IRName name && name.Name == func.Arguments[1].Item1)
                                    three.B = tmpName;

                                block.InsertOp(i++, new IRLocalOp(tmpName, type));
                                block.InsertOp(i++, new IRMovOp(tmpName, new IRName(func.Arguments[1].Item1), destType: type) { Volatile = true });
                                block.InsertOp(++i, new IRMovOp(new IRName(func.Arguments[1].Item1), tmpName, destType: type) { Volatile = true });
                            }
                        }
                    }
                }

                func.RegenerateOpIds();
            }
        }
    }
}
