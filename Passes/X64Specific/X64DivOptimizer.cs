using NIR.Instructions;
using NIR.Passes.RegAlloc;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NIR.Passes.X64Specific
{
    public class X64DivOptimizer : IRPass
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
                        (three.Type == ThreeOpType.Div || three.Type == ThreeOpType.Mod))
                    {
                        if (
                        three.B is IRPrimitiveOperand prim &&
                        three.DestType is IRIntegerType integer)
                        {
                            double N = (double)(dynamic)prim.Value;
                            int size = three.DestType.TypeSize(arch) * 8;
                            int shift = size + (int)Math.Log(N, 2);
                            var extended = new IRIntegerType(integer.Signed, Math.Min(arch.Bitness, size * 2));
                            IRName tmp;

                            var mask = (BigInteger.One << arch.Bitness) - 1;

                            switch (three.Type)
                            {
                                case ThreeOpType.Div:
                                    if (integer.Signed)
                                    {
                                        var pow = new BigInteger(1) << shift - 1;
                                        var div = new BigInteger(N);
                                        var factor = pow / div + (pow % div == 0 ? 0 : 1);
                                        tmp = IRGreedyAlloc.GenTemp();
                                        block.Operations[i++] = new IRLocalOp(tmp, extended);
                                        block.InsertOp(i++, new IRMovOp(tmp, three.Dest, false, extended));
                                        block.InsertOp(i++, new IRThreeOp(ThreeOpType.Shr, tmp, tmp,
                                            new IRPrimitiveOperand(size - 1), false, extended));
                                        block.InsertOp(i++, new IRThreeOp(ThreeOpType.Mul, three.Dest, three.Dest,
                                            new IRPrimitiveOperand((long)(factor & mask)), false, extended));
                                        block.InsertOp(i++, new IRThreeOp(ThreeOpType.Shr, three.Dest, three.Dest,
                                            new IRPrimitiveOperand((long)(shift - 1) % extended.Bits), false, extended));
                                        block.InsertOp(i++, new IRThreeOp(ThreeOpType.Sub,
                                            three.Dest, three.Dest, tmp, false, extended));
                                    }
                                    else
                                    {
                                        var pow = new BigInteger(1) << shift;
                                        var div = new BigInteger(N);
                                        var factor = pow / div + (pow % div == 0 ? 0 : 1);
                                        block.Operations[i++] = new IRThreeOp(ThreeOpType.Mul, three.Dest, three.Dest,
                                            new IRPrimitiveOperand((long)(factor & mask)), false, extended);
                                        block.InsertOp(i++, new IRThreeOp(ThreeOpType.Shr, three.Dest, three.Dest,
                                            new IRPrimitiveOperand((long)shift % extended.Bits), false, extended));
                                    }
                                    break;

                                case ThreeOpType.Mod:
                                    tmp = IRGreedyAlloc.GenTemp();
                                    block.Operations[i] = new IRLocalOp(tmp, three.DestType);
                                    block.InsertOp(i + 1, new IRMovOp(tmp, three.Dest, false, three.DestType));
                                    block.InsertOp(i + 2, new IRThreeOp(ThreeOpType.Div, tmp, tmp,
                                        three.B, false, three.DestType));
                                    block.InsertOp(i + 3, new IRThreeOp(ThreeOpType.Mul, tmp, tmp,
                                        three.B, false, three.DestType));
                                    block.InsertOp(i + 4, new IRThreeOp(ThreeOpType.Sub, three.Dest, three.Dest, tmp, false, three.DestType));
                                    break;
                            }
                        }
                    }
                }

                func.RegenerateOpIds();
            }
        }
    }
}
