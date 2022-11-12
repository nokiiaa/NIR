using System;

namespace NIR.Instructions
{
    public abstract class IROperand : IEquatable<IROperand>
    {
        public override bool Equals(object other) => other is IROperand oper && Equals(oper);

        public abstract bool Equals(IROperand other);

        public override int GetHashCode() => base.GetHashCode();

        public override string ToString() => base.ToString();
    }
}