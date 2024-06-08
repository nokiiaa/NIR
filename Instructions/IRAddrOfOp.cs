namespace NIR.Instructions
{
    public class IRAddrOfOp : IROp
    {
        public IRName Dest { get; set; }
        public IRName Variable { get; set; }

        public IRAddrOfOp(IRName dest, IRName var)
        {
            Dest = dest;
            Variable = var;
        }

        public override string ToString() => $"\taddrof {Dest}, {Variable}";
    }
}
