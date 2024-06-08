namespace NIR.Instructions
{
    public class IRStackAllocOp : IROp
    {
        public IRStackAllocOp(IRName dest, int bytes)
        {
            Dest = dest;
            Bytes = bytes;
        }

        public IRName Dest { get; set; }
        public int Bytes { get; set; }

        public override string ToString()
            => $"stackalloc {Dest}, {Bytes}";
    }
}