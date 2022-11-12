namespace NIR.Instructions
{
    public class IRRetOp : IROp
    {
        public IROperand Value { get; set; }

        public IRRetOp(IROperand value = null) => Value = value;

        public override string ToString() => $"\tret{(Value != null ? $" {Value}" : "")}";
    }
}