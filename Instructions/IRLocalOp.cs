using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NIR.Instructions
{
    public class IRLocalOp : IROp
    {
        /// <summary>
        /// Specifies whether this local variable is required to be assigned to a particular range of registers or not.
        /// </summary>
        public bool ForceReg { get; set; }

        /// <summary>
        /// The registers that this local variable can be assigned to if ForceReg is true.
        /// An empty list means any CPU register is allowed.
        /// </summary>
        public List<uint> RegChoices { get; set; }

        public IRType Type { get; set; }

        public string Name { get; set; }

        public IRLocalOp(string name, IRType type, bool forceReg = false, List<uint> physRegChoices = null)
        {
            ForceReg = forceReg;
            RegChoices = physRegChoices ?? new List<uint>();
            Name = name;
            Type = type;
        }

        public override string ToString() =>
            $"\tlocal{(ForceReg ? $"pr{(RegChoices.Count > 0 ? $"({string.Join(", ", RegChoices)})" : "")}" : "")} {Type} {Name} ";
    }
}
