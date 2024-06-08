using System;
using System.Collections.Generic;

namespace NIR.Instructions
{
    public class IRNameEqualityComparer : IEqualityComparer<IRName>
    {
        public bool Equals(IRName x, IRName y) => x.Equals(y);

        public int GetHashCode(IRName obj) => obj.Name.GetHashCode();
    }

    public class IRName : IROperand, IComparable<IRName>, IEquatable<IRName>
    {
        public IRName(string name = null)
        {
            if (name != null)
                Name = name;
        }

        public virtual string Name { get; set; }

        public int CompareTo(IRName other) => Name.CompareTo(other.Name);

        public override bool Equals(IROperand other) => other is IRName n && n.Name == Name;

        public bool Equals(IRName other) => other.Name == Name;

        public override string ToString() => Name;

        public override int GetHashCode() => Name.GetHashCode();

        public static implicit operator IRName(string s) => new IRName(s);

        public static implicit operator string(IRName i) => i.Name;
    }
}