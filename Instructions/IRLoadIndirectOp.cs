namespace NIR.Instructions
{
    public class IRLoadIndirectOp : IROp
    {
        public IRLoadIndirectOp(IRName dest, IRType srcType, IROperand src)
        {
            SrcType = srcType;
            Dest = dest;
            Src = src;
        }

        public IRName Dest { get; set; }

        public IROperand Src { get; set; }

        public IRType SrcType { get; set; }

        public override string ToString() => $"\tmov {Dest}" +
            $", {(SrcType == null ? "" : $"{SrcType} ")}*{Src}";
    }
}
