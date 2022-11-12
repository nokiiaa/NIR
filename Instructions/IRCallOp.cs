using System.Collections.Generic;
using System.Linq;

namespace NIR.Instructions
{
    public class IRCallOp : IROp
    {
        public IRCallOp(string dest, string callee, List<IROperand> args,
            bool indirectStore = false, IRType destType = null)
        {
            IndirectStore = indirectStore;
            Arguments = args;
            Dest = dest;
            Callee = callee;
            DestType = destType;
        }

        public bool IndirectStore { get; set; }

        public IRType DestType { get; set; }

        public IRName Dest { get; set; }

        public IRName Callee { get; set; }

        public List<IROperand> Arguments { get; set; }

        public override string ToString() =>
            $"\tcall {(DestType == null ? "" : $"{DestType} ")}{Callee}({string.Join(", ", Arguments)})" +
            (!string.IsNullOrEmpty(Dest.ToString()) ? $" -> {(IndirectStore ? "*" : "")}{Dest}" : "");
    }
}
