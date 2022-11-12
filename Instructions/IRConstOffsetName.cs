using System;

namespace NIR.Instructions
{
    public class IRConstOffsetName : IROperand
    {
        public IRName Name { get; set; }

        public long Offset { get; set; }

        public IRConstOffsetName(IRName name, long offset = 0)
        {
            Name = name;
            Offset = offset;
        }

        public override string ToString() => Offset == 0 ? Name.ToString() :
            $"{Name} {(Offset >= 0 ? "+ " : "- ")}{Math.Abs(Offset)}";

        public override bool Equals(IROperand other) =>
            other is IRConstOffsetName n && n.Name == Name && n.Offset == Offset;
    }
}
