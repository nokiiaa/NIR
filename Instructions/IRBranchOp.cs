namespace NIR.Instructions
{
    public class IRBranchOp : IROp
    {
        public IRBranchOp(string dest, Enum condition = Enum.Always)
        {
            Condition = condition;
            Destination = dest;
        }

        public enum Enum
        {
            Zero, NotZero, Less, GreaterEqual, Greater,
            LessEqual, Negative, Positive,
            Overflow, NoOverflow, Always
        }

        static readonly string[] ConditionStrings =
        {
            "ifz ", "ifnz ", "ifl ", "ifge ", "ifg ", "ifle ",
            "ifs ", "ifns ", "ifof ", "ifnof ", ""
        };

        public Enum Condition { get; set; }

        public string Destination { get; set; }

        public override string ToString() => $"\t{ConditionStrings[(int)Condition]}branch {Destination}";

        public bool PointsTo(IRBasicBlock block)
        {
            foreach (IROp op in block.Operations)
                if (op is IRLabel label && label.Name == Destination)
                    return true;
            return false;
        }
    }
}
