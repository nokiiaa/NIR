namespace NIR.Instructions
{
    public class IRGlobal : IROp
    {
        public IRGlobal(string name, IRType type, IROperand value)
        {
            Name = name;
            Type = type;
            Value = value;
        }

        public string Name { get; set; }
        public IRType Type { get; set; }
        public IROperand Value { get; set; }

        public override string ToString() =>
            $"global {Type} {Name}{(Value == null ? "" : " = " + Value)}";
    }
}
