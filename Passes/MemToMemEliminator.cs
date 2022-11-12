using NIR.Instructions;
using NIR.Passes.RegAlloc;
using System.Collections.Generic;

namespace NIR.Passes
{
    /// <summary>
    /// Modifies the code not to contain instructions that perform operations between two memory locations.
    /// This is achieved by creating variables to store the source operand of the instruction.
    /// Since this step is done before register allocation, all variables are considered to be potentially
    /// stored in memory rather than registers.
    /// This is why superfluous variable creations are likely to be reduced during optimization passes.
    /// The code is assumed to already be reduced to two-address form.
    /// </summary>
    public class MemToMemEliminator : IRPass
    {
        public static bool IsMemOperand(IROperand operand)
            => operand is IRName name && !name.Name.StartsWith("#r");
            
        public static bool IsMemToMem(IROperand a, IROperand b,
            bool leftIndirect = false, bool rightIndirect = false)
            => (leftIndirect  || IsMemOperand(a)) &&
               (rightIndirect || IsMemOperand(b));

        public override void Perform(IRFunction func, ArchitectureInfo arch)
        {
            var varTypes = new Dictionary<IRName, IRType>();

            foreach (var sym in func.OutsideSymbols)
                varTypes[sym.Item2] = sym.Item3;
            foreach (var arg in func.Arguments)
                varTypes[arg.Item1] = arg.Item2;

            foreach (IRBasicBlock block in func.Blocks)
                for (int i = 0; i < block.Operations.Count; i++)
                    if (block.Operations[i] is IRLocalOp alloc)
                        varTypes[alloc.Name] = alloc.Type;

                    IRName tmp1, tmp2;

            foreach (IRBasicBlock block in func.Blocks)
            {
                for (int i = 0; i < block.Operations.Count; i++)
                {
                    if (block.Operations[i] is IRMovOp mov)
                    {
                        if (!(mov.Src is IRPrimitiveOperand))
                        {
                            IRType type = varTypes[mov.Src as IRName];
                            tmp1 = IRGreedyAlloc.GenTemp();
                            block.InsertOp(i++, new IRLocalOp(
                                tmp1, type, forceReg: true));
                            block.InsertOp(i++, new IRMovOp(tmp1, mov.Src, destType: type));
                            mov.Src = tmp1;
                        }

                        if (mov.Indirect)
                        {
                            IRType type = varTypes[mov.Dest];
                            tmp2 = IRGreedyAlloc.GenTemp();
                            block.InsertOp(i++, new IRLocalOp(
                                tmp2,
                                varTypes[mov.Dest], forceReg: true));
                            block.InsertOp(i++, new IRMovOp(tmp2, mov.Dest, destType: type));
                            mov.Dest = tmp2;
                        }
                    }
                    else if (block.Operations[i] is IRCallOp call && call.IndirectStore)
                    {
                        IRType type = varTypes[call.Dest];
                        tmp1 = IRGreedyAlloc.GenTemp();
                        block.InsertOp(i++, new IRLocalOp(
                            tmp1, type, forceReg: true));
                        block.InsertOp(i++, new IRMovOp(tmp1, call.Dest, destType: type));
                        call.Dest = tmp1;
                    }
                    else if (block.Operations[i] is IRMovFlagOp flag && flag.Indirect)
                    {
                        IRType type = varTypes[flag.Dest];
                        tmp1 = IRGreedyAlloc.GenTemp();
                        block.InsertOp(i++, new IRLocalOp(
                            tmp1, type, forceReg: true));
                        block.InsertOp(i++, new IRMovOp(tmp1, flag.Dest, destType: type));
                        flag.Dest = tmp1;
                    }
                    else if (block.Operations[i] is IRLoadIndirectOp load)
                    {
                        IRType ptdType = load.SrcType;
                        IRType ptrType = new IRPointerType(ptdType);
                        tmp1 = IRGreedyAlloc.GenTemp();
                        tmp2 = IRGreedyAlloc.GenTemp();
                        block.ReplaceAt(i, new IRMovOp(load.Dest, tmp1, destType: ptdType));

                        block.InsertOp(i++, new IRLocalOp(
                            tmp2, ptrType, forceReg: true));
                        block.InsertOp(i++, new IRMovOp(tmp2, load.Src, destType: ptrType));

                        block.InsertOp(i++, new IRLocalOp(
                            tmp1,
                            ptdType, forceReg: true));
                        block.InsertOp(i++, new IRLoadIndirectOp(tmp1, ptdType, tmp2));
                    }
                    else if (block.Operations[i] is IRCmpOp cmp && !(cmp.B is IRPrimitiveOperand))
                    {
                        IRType type = varTypes[cmp.B as IRName];
                        tmp1 = IRGreedyAlloc.GenTemp();
                        block.InsertOp(i++, new IRLocalOp(
                            tmp1, type, forceReg: true));
                        block.InsertOp(i++, new IRMovOp(tmp1, cmp.B, destType: type));
                        cmp.B = tmp1;
                    }
                    else if (block.Operations[i] is IRThreeOp three && !(three.B is IRPrimitiveOperand))
                    {
                        IRType type = varTypes[three.B as IRName];
                        tmp1 = IRGreedyAlloc.GenTemp();
                        block.InsertOp(i++, new IRLocalOp(
                            tmp1, type, forceReg: true));
                        block.InsertOp(i++, new IRMovOp(tmp1, three.B, destType: type));
                        three.B = tmp1;
                    }
                }
            }

            func.RegenerateOpIds();
        }
    }
}
