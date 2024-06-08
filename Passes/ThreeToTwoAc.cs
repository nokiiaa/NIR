using NIR.Instructions;
using NIR.Passes.RegAlloc;
using System.Collections.Generic;

namespace NIR.Passes
{
    public class ThreeToTwoAc : IRPass
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
                    else if (block.Operations[i] is IRTwoOp two &&
                        (two.IndirectStore || !two.Dest.Equals(two.Src)))
                    {
                        if (two.IndirectStore)
                        {
                            IRType type = varTypes[two.Dest];
                            IRName tmpName = IRGreedyAlloc.GenTemp();
                            block.ReplaceAt(i, new IRMovOp(two.Dest,
                                tmpName, true, two.DestType));
                            block.InsertOp(i++, new IRLocalOp(tmpName, type, forceReg: true));
                            block.InsertOp(i++, new IRMovOp(tmpName, two.Src, false, type));
                            block.InsertOp(i++, new IRTwoOp(two.Type, tmpName, tmpName, false, type));
                        }
                        else
                        {
                            block.InsertOp(i++, new IRMovOp(two.Dest, two.Src, false, two.DestType));
                            two.Src = two.Dest;
                        }
                    }
                    else if (block.Operations[i] is IRThreeOp three &&
                        (three.IndirectStore || !three.Dest.Equals(three.A)) &&
                        arch.NeedsTwoAddressCode[(int)three.Type])
                    {
                        if (three.B == three.Dest || three.IndirectStore)
                        {
                            IRType type = varTypes[three.Dest];
                            IRName tmpName = IRGreedyAlloc.GenTemp();
                            block.ReplaceAt(i, new IRMovOp(three.Dest,
                                tmpName, three.IndirectStore, three.DestType));
                            block.InsertOp(i++, new IRLocalOp(tmpName, type, forceReg: true));
                            block.InsertOp(i++, new IRMovOp(tmpName, three.A, false, type));
                            block.InsertOp(i++, new IRThreeOp(three.Type, tmpName, tmpName, three.B, false, type));
                        }
                        else
                        {
                            block.InsertOp(i++, new IRMovOp(three.Dest, three.A, three.IndirectStore, three.DestType));
                            three.A = three.Dest;
                        }
                    }
                }
            }
            func.RegenerateOpIds();
        }
    }
}