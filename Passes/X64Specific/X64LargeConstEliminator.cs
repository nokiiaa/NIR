using NIR.Instructions;
using NIR.Passes.RegAlloc;

namespace NIR.Passes.X64Specific
{
    public class X64LargeConstantEliminator : IRPass
    {
        public static bool OutOfBounds(object o) =>
            o is ulong ul && ul > uint.MaxValue ||
            o is long l && (l > int.MaxValue || l < int.MinValue);

        public override void Perform(IRFunction func, ArchitectureInfo arch)
        {
            foreach (IRBasicBlock block in func.Blocks)
            {
                for (int i = 0; i < block.Operations.Count; i++)
                {
                    if (block.Operations[i] is IRThreeOp three)
                    {
                        if (three.B is IRPrimitiveOperand prim &&
                            prim.Type == IRPrimitiveOperand.Enum.Integer &&
                            OutOfBounds(prim.Value))
                        {
                            IRType type = three.DestType;
                            IRName tmpName = IRGreedyAlloc.GenTemp();
                            block.InsertOp(i++, new IRLocalOp(tmpName, type, forceReg: true));
                            block.InsertOp(i++, new IRMovOp(tmpName, three.B, false, type));
                            three.B = tmpName;
                        }
                    }
                    else if (block.Operations[i] is IRTwoOp two)
                    {
                        if (two.Src is IRPrimitiveOperand prim &&
                            prim.Type == IRPrimitiveOperand.Enum.Integer &&
                            OutOfBounds(prim.Value))
                        {
                            IRType type = two.DestType;
                            IRName tmpName = IRGreedyAlloc.GenTemp();
                            block.InsertOp(i++, new IRLocalOp(tmpName, type, forceReg: true));
                            block.InsertOp(i++, new IRMovOp(tmpName, two.Src, false, type));
                            two.Src = tmpName;
                        }
                    }
                    else if (block.Operations[i] is IRCmpOp cmp)
                    {
                        if (cmp.B is IRPrimitiveOperand prim &&
                            prim.Type == IRPrimitiveOperand.Enum.Integer &&
                            OutOfBounds(prim.Value))
                        {
                            IRType type = cmp.Type;
                            IRName tmpName = IRGreedyAlloc.GenTemp();
                            block.InsertOp(i++, new IRLocalOp(tmpName, type, forceReg: true));
                            block.InsertOp(i++, new IRMovOp(tmpName, cmp.B, false, type));
                            cmp.B = tmpName;
                        }
                    }
                }
            }

            func.RegenerateOpIds();
        }
    }
}