using NIR.Instructions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NIR
{
    public class IROpSearch
    {
        public static bool TraverseDstOperands(Func<IROp, IROperand, bool> pred, IROp op)
        {
            bool found = false;

            switch (op)
            {
                case IROutsideOp ext:
                    if (pred(ext, ext.Dest)) found = true;
                    break;
                case IRStackAllocOp alloc:
                    if (pred(alloc, alloc.Dest)) found = true;
                    break;
                case IRCallOp call when !call.IndirectStore:
                    if (pred(call, call.Dest)) found = true;
                    break;
                case IRPhiNode phi:
                    if (pred(phi, phi.MapTo)) found = true;
                    break;
                case IRAddrOfOp addrof:
                    if (pred(addrof, addrof.Dest)) found = true;
                    break;
                case IRMovOp mov when !mov.Indirect:
                    if (pred(mov, mov.Dest)) found = true;
                    break;
                case IRMovFlagOp flag when !flag.Indirect:
                    if (pred(flag, flag.Dest)) found = true;
                    break;
                case IRLoadIndirectOp load:
                    if (pred(load, load.Dest)) found = true;
                    break;
                case IRTwoOp two when !two.IndirectStore:
                    if (pred(two, two.Dest)) found = true;
                    break;
                case IRThreeOp three when !three.IndirectStore:
                    if (pred(three, three.Dest)) found = true;
                    break;
            }

            return found;
        }
        public static bool TraverseDstOperands(Func<IROp, IROperand, bool> pred, List<IRBasicBlock> blocks)
        {
            bool found = false;
            foreach (IRBasicBlock bb in blocks)
                foreach (IROp op in bb.Operations)
                    found |= TraverseDstOperands(pred, op);
            return found;
        }

        public static bool TraverseSrcOperands(Func<IROp, IROperand, bool> pred, IROp op)
        {
            bool found = false;

            switch (op)
            {
                case IRPhiNode phi:
                    foreach (IRName name in phi.Choices.Values)
                        if (pred(phi, name))
                            found = true;
                    break;
                case IRAddrOfOp addrof:
                    if (pred(addrof, addrof.Variable)) found = true;
                    break;
                case IRCallOp call:
                    if (call.IndirectStore && pred(call, call.Dest)) found = true;
                    if (pred(call, call.Callee)) found = true;
                    foreach (IROperand operand in call.Arguments)
                        if (pred(call, operand))
                            found = true;
                    break;
                case IRCmpOp cmp:
                    if (pred(cmp, cmp.A)) found = true;
                    if (pred(cmp, cmp.B)) found = true;
                    break;
                case IRMovOp mov:
                    if (mov.Indirect && pred(mov, mov.Dest)) found = true;
                    if (pred(mov, mov.Src)) found = true;
                    break;
                case IRMovFlagOp flag when flag.Indirect:
                    if (pred(flag, flag.Dest)) found = true;
                    break;
                case IRLoadIndirectOp load:
                    if (pred(load, load.Src)) found = true;
                    break;
                case IRTwoOp two:
                    if (two.IndirectStore && pred(two, two.Dest)) found = true;
                    if (pred(two, two.Src)) found = true;
                    break;
                case IRThreeOp three:
                    if (three.IndirectStore && pred(three, three.Dest)) found = true;
                    if (pred(three, three.A)) found = true;
                    if (pred(three, three.B)) found = true;
                    break;
                case IRChkOp chk:
                    if (pred(chk, chk.Operand)) found = true;
                    break;
                case IRRetOp ret:
                    if (pred(ret, ret.Value)) found = true;
                    break;
            }

            return found;
        }

        public static bool TraverseSrcOperands(Func<IROp, IROperand, bool> pred, List<IRBasicBlock> blocks)
        {
            bool found = false;
            foreach (IRBasicBlock bb in blocks)
                foreach (IROp op in bb.Operations)
                    found |= TraverseSrcOperands(pred, op);
            return found;
        }

        public static void TraverseOperands(
            Action<IROp, IROperand> funcDst,
            Action<IROp, IROperand> funcSrc,
            IROp op)
        {
            switch (op)
            {
                case IRStackAllocOp alloc:
                    funcDst(alloc, alloc.Dest);
                    break;
                case IRPhiNode phi:
                    foreach (IRName name in phi.Choices.Values)
                        funcSrc(phi, name);
                    funcDst(phi, phi.MapTo);
                    break;
                case IRAddrOfOp addrof:
                    funcSrc(addrof, addrof.Variable);
                    funcDst(addrof, addrof.Dest);
                    break;
                case IRCallOp call:
                    funcSrc(call, call.Callee);
                    call.Arguments.ForEach(x => funcSrc(call, x));
                    (call.IndirectStore ? funcSrc : funcDst)(call, call.Dest);
                    break;
                case IRCmpOp cmp:
                    funcSrc(cmp, cmp.A);
                    funcSrc(cmp, cmp.B);
                    break;
                case IRMovOp mov:
                    funcSrc(mov, mov.Src);
                    (mov.Indirect ? funcSrc : funcDst)(mov, mov.Dest);
                    break;
                case IRMovFlagOp flag:
                    (flag.Indirect ? funcSrc : funcDst)(flag, flag.Dest);
                    break;
                case IRLoadIndirectOp load:
                    funcSrc(load, load.Src);
                    funcDst(load, load.Dest);
                    break;
                case IRTwoOp two:
                    funcSrc(two, two.Src);
                    (two.IndirectStore ? funcSrc : funcDst)(two, two.Dest);
                    break;
                case IRThreeOp three:
                    funcSrc(three, three.A);
                    funcSrc(three, three.B);
                    (three.IndirectStore ? funcSrc : funcDst)(three, three.Dest);
                    break;
                case IRChkOp chk:
                    funcSrc(chk, chk.Operand);
                    break;
                case IRRetOp ret:
                    funcSrc(ret, ret.Value);
                    break;
            }
        }

        public static void TraverseOperands(
            Action<IROp, IROperand> funcDst,
            Action<IROp, IROperand> funcSrc,
            List<IRBasicBlock> blocks)
        {
            foreach (IRBasicBlock bb in blocks)
                foreach (IROp op in bb.Operations)
                    TraverseOperands(funcDst, funcSrc, op);
        }

        public static void ReplaceOperands(
            Func<IROp, IROperand, IROperand> funcDst,
            Func<IROp, IROperand, IROperand> funcSrc,
            IROp op)
        {
            switch (op)
            {
                case IRStackAllocOp alloc:
                    alloc.Dest = (IRName)funcDst(alloc, alloc.Dest);
                    break;
                case IRPhiNode phi:
                    phi.Choices = phi.Choices.Select(kvp =>
                        new KeyValuePair<IRBasicBlock, IRName>(kvp.Key, (IRName)funcSrc(phi, kvp.Value)))
                        .ToDictionary(x => x.Key, x => x.Value);
                    phi.MapTo = (IRName)funcDst(phi, phi.MapTo);
                    break;
                case IRAddrOfOp addrof:
                    addrof.Variable = (IRName)funcSrc(addrof, addrof.Variable);
                    addrof.Dest = (IRName)funcDst(addrof, addrof.Dest);
                    break;
                case IRCallOp call:
                    call.Callee = (IRName)funcSrc(call, call.Callee);
                    for (int i = 0; i < call.Arguments.Count; i++)
                        call.Arguments[i] = funcSrc(call, call.Arguments[i]);
                    call.Dest = (IRName)(call.IndirectStore ? funcSrc : funcDst)(call, call.Dest);
                    break;
                case IRCmpOp cmp:
                    cmp.A = funcSrc(cmp, cmp.A);
                    cmp.B = funcSrc(cmp, cmp.B);
                    break;
                case IRMovOp mov:
                    mov.Src = funcSrc(mov, mov.Src);
                    mov.Dest = (IRName)(mov.Indirect ? funcSrc : funcDst)(mov, mov.Dest);
                    break;
                case IRMovFlagOp flag:
                    flag.Dest = (IRName)(flag.Indirect ? funcSrc : funcDst)(flag, flag.Dest);
                    break;
                case IRLoadIndirectOp load:
                    load.Src = funcSrc(load, load.Src);
                    load.Dest = (IRName)funcDst(load, load.Dest);
                    break;
                case IRTwoOp two:
                    two.Src = funcSrc(two, two.Src);
                    two.Dest = (IRName)(two.IndirectStore ? funcSrc : funcDst)(two, two.Dest);
                    break;
                case IRThreeOp three:
                    three.A = funcSrc(three, three.A);
                    three.B = funcSrc(three, three.B);
                    three.Dest = (IRName)(three.IndirectStore ? funcSrc : funcDst)(three, three.Dest);
                    break;
                case IRChkOp chk:
                    chk.Operand = funcSrc(chk, chk.Operand);
                    break;
                case IRRetOp ret:
                    ret.Value = funcSrc(ret, ret.Value);
                    break;
            }
        }

        public static void ReplaceOperands(
            Func<IROp, IROperand, IROperand> funcDst,
            Func<IROp, IROperand, IROperand> funcSrc,
            List<IRBasicBlock> blocks)
        {
            foreach (IRBasicBlock bb in blocks)
                foreach (IROp op in bb.Operations)
                    ReplaceOperands(funcDst, funcSrc, op);
        }
    }
}