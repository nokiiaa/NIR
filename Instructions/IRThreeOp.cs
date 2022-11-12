namespace NIR.Instructions
{
    public class IRThreeOp : IROp
    {
        public IRThreeOp(ThreeOpType type, IRName dest,
            IROperand a, IROperand b,
            bool indirectStore = false, IRType destType = null)
        {
            Type = type;
            Dest = dest;
            A = a;
            B = b;
            IndirectStore = indirectStore;
            DestType = destType;
        }

        public ThreeOpType Type { get; set; }

        public bool IndirectStore { get; set; }

        public IRType DestType { get; set; }

        public IRName Dest { get; set; }

        public IROperand A { get; set; }

        public IROperand B { get; set; }

        public override string ToString()
            => $"\t{Type.ToString().ToLower()} {(DestType == null ? "" : $"{DestType} ")}" +
            $"{(IndirectStore ? "*" : "")}{Dest}, {A}, {B}";
    }
}
