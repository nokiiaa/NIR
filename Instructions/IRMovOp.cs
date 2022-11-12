namespace NIR.Instructions
{
    public class IRMovOp : IROp
    {
        public IRMovOp(IRName dest, IROperand src, bool indirect = false, IRType destType = null)
        {
            Dest = dest;
            Src = src;
            Indirect = indirect;
            DestType = destType;
        }


        public IRName Dest { get; set; }

        public IROperand Src { get; set; }

        public bool Indirect { get; set; }

        public IRType DestType { get; set; }

        public override string ToString() =>
            $"\tmov {(DestType == null ? "" : $"{DestType} ")}" +
            $"{(Indirect ? "*" : "")}{Dest}, {Src}";
    }
}