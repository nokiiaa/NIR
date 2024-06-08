namespace NIR.Instructions
{
    public class IRTwoOp : IROp
    {
        public IRTwoOp(TwoOpType type, IRName dest,
            IROperand src, bool indirectStore = false, IRType destType = null)
        {
            Type = type;
            Dest = dest;
            Src = src;
            IndirectStore = indirectStore;
            DestType = destType;
        }

        public TwoOpType Type { get; set; }

        public bool IndirectStore { get; set; }
        
        public IRType DestType { get; set; }

        public IRName Dest { get; set; }

        public IROperand Src { get; set; }

        public override string ToString()
            => $"\t{Type.ToString().ToLower()} {(DestType == null ? "" : $"{DestType} ")}" +
            $"{(IndirectStore ? "*" : "")}{Dest}, {Src}";
    }
}