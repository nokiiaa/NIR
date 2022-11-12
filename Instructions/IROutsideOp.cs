namespace NIR.Instructions
{
    public class IROutsideOp : IROp
    {
        public IROutsideOp(IRName dest, IRType destType = null)
        {
            Dest = dest;
            DestType = destType;
        }

        public IRName Dest { get; set; }

        public IRType DestType { get; set; }

        public override string ToString() =>
            $"\toutside {(DestType == null ? "" : $"{DestType} ")}{Dest}";
    }
}