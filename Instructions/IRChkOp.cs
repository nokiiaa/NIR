namespace NIR.Instructions
{
    public class IRChkOp : IROp
    {
        public IRChkOp(IROperand operand, IRType operandType = null)
        {
            Operand = operand;
            OperandType = operandType;
        }

        public IRType OperandType { get; set; }

        public IROperand Operand { get; set; }

        public override string ToString() => $"\tchk {Operand}";
    }
}