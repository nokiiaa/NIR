using NIR.Instructions;
using NIR.Passes.RegAlloc;
using System.Collections.Generic;

namespace NIR.Passes.X64Specific
{
    public class X64DestMulEliminator : IRPass
    {
        public override void Perform(IRFunction func, ArchitectureInfo arch)
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
                        three.Type == ThreeOpType.Mul &&
                        MemToMemEliminator.IsMemOperand(three.Dest))
                    {
                        IRType type = varTypes[three.Dest];
                        IRName tmpName = IRGreedyAlloc.GenTemp();
                        block.ReplaceAt(i, new IRMovOp(three.Dest, tmpName, false, three.DestType));
                        block.InsertOp(i++, new IRLocalOp(tmpName, type, forceReg: true));
                        block.InsertOp(i++, new IRMovOp(tmpName, three.A, destType: type));
                        block.InsertOp(i++, new IRThreeOp(three.Type, tmpName, tmpName, three.B, destType: type));
                    }
                }
            }

            func.RegenerateOpIds();
        }
    }
}