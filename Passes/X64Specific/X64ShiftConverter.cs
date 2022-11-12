using NIR.Backends;
using NIR.Instructions;
using NIR.Passes.RegAlloc;
using System;
using System.Collections.Generic;
using System.Text;

namespace NIR.Passes.X64Specific
{
    public class X64ShiftConverter : IRPass
    {
        public override void Perform(IRFunction func, ArchitectureInfo arch)
        {
            foreach (IRBasicBlock block in func.Blocks)
            {
                for (int i = 0; i < block.Operations.Count; i++)
                {
                    if (block.Operations[i] is IRThreeOp three &&
                        (three.Type == ThreeOpType.Shl ||
                        three.Type == ThreeOpType.Shr ||
                        three.Type == ThreeOpType.Rol ||
                        three.Type == ThreeOpType.Ror) &&
                        !(three.B is IRPrimitiveOperand))
                    {
                        IRName tmpName = IRGreedyAlloc.GenTemp();
                        block.InsertOp(i++, new IRLocalOp(tmpName, new IRIntegerType(false, 8), true,
                            new List<uint> { X64ArchitectureInfo.Rcx }));
                        block.InsertOp(i++, new IRMovOp(tmpName, three.B));
                        three.B = tmpName;
                    }
                }
            }

            func.RegenerateOpIds();
        }
    }
}