namespace NIR.Instructions
{
    public class IRCmpOp : IROp
    {
        public IRCmpOp(IROperand a, IROperand b, IRType type = null)
        {
            Type = type;
            A = a;
            B = b;
        }

        public IRType Type { get; set; }
        public IROperand A { get; set; }
        public IROperand B { get; set; }

        public override string ToString() => $"\tcmp {(Type == null ? Type + " " : "")}{A}, {B}";
    }
}