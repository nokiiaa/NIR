namespace NIR.Instructions
{
    public class IRPrimitiveOperand : IROperand
    {
        public IRPrimitiveOperand(object value, bool wstring = false)
        {
            if (value is ulong || value is long || value is uint || value is int ||
                value is ushort || value is short || value is byte || value is char)
                Type = Enum.Integer;

            switch (value)
            {
                case ulong ul: Value = ul; break;
                case long l: Value = l; break;
                case uint ui: Value = (ulong)ui; break;
                case int i: Value = (long)i; break;
                case ushort us: Value = (ulong)us; break;
                case short s: Value = (long)s; break;
                case byte b: Value = (ulong)b; break;
                case char c: Value = (ulong)c; break;
                case float _: case double _:
                    Type = Enum.Float;
                    Value = value;
                    break;
                case string _:
                    Type = wstring ? Enum.WString : Enum.String;
                    Value = value;
                    break;
            }
        }
        public static implicit operator IRPrimitiveOperand(ulong  ul) => new IRPrimitiveOperand(ul);
        public static implicit operator IRPrimitiveOperand(long    l) => new IRPrimitiveOperand( l);
        public static implicit operator IRPrimitiveOperand(uint   ui) => new IRPrimitiveOperand(ui);
        public static implicit operator IRPrimitiveOperand(int     i) => new IRPrimitiveOperand( i);
        public static implicit operator IRPrimitiveOperand(ushort us) => new IRPrimitiveOperand(us);
        public static implicit operator IRPrimitiveOperand(short   s) => new IRPrimitiveOperand( s);
        public static implicit operator IRPrimitiveOperand(byte   ub) => new IRPrimitiveOperand(ub);
        public static implicit operator IRPrimitiveOperand(char    b) => new IRPrimitiveOperand( b);

        public enum Enum { Integer, Float, Double, String, WString }

        public object Value { get; set; }

        public Enum Type { get; set; }

        public override string ToString() =>
            Type switch
            {
                Enum.Integer => Value.ToString(),
                Enum.Float => (Value is double ? Value.ToString() : $"{Value}f"),
                Enum.String => $"\"{Value}\"",
                _ => $"L\"{Value}\""
            };

        public override bool Equals(IROperand other) => other is IRPrimitiveOperand prim
            && prim.Type == Type
            && prim.Value.Equals(Value);
    }
}
