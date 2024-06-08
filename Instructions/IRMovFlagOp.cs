using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NIR.Instructions
{
    public class IRMovFlagOp : IROp
    {
        public IRMovFlagOp(IRName dest, IRBranchOp.Enum flag, bool indirect = false, IRType destType = null)
        {
            Indirect = indirect;
            Dest = dest;
            Flag = flag;
            DestType = destType;
        }

        public bool Indirect { get; set; }
        public IRType DestType { get; set; }
        public IRName Dest { get; set; }
        public IRBranchOp.Enum Flag { get; set; }

        readonly static string[] FlagStrings = { "z", "nz", "l", "ge", "g", "le", "s", "ns", "o", "no", ""};
        public override string ToString() =>
            $"\tst{FlagStrings[(int)Flag]} {(DestType == null ? "" : $"{DestType} ")}{(Indirect ? "*" : "")}{Dest}";
    }
}
