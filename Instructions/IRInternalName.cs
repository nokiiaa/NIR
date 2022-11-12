using NIR.Passes.RegAlloc;
using System;

namespace NIR.Instructions
{
    public enum IRInternalNameType
    {
        VirtualVariable,
        MachineRegister
    }

    public class IRInternalName : IRName
    {
        public IRInternalName(IRInternalNameType type, uint number)
        {
            Type = type;
            Number = number;
        }
        public IRInternalName(string name) : base(name) { }
        public IRInternalNameType Type { get; set; }
        public uint Number { get; set; }
        public override string Name
        {
            get
            {
                return Type switch
                {
                    IRInternalNameType.VirtualVariable => $"#v{Number}",
                    IRInternalNameType.MachineRegister =>
                        (Number & IRPhysicalRegister.ArgStackSlotBit) != 0 ?
                            $"#a{Number & IRPhysicalRegister.ArgNumMask}" :
                        (Number & IRPhysicalRegister.StackSlotBit) != 0 ?
                            $"#s{Number & IRPhysicalRegister.StackNumMask}" :
                            $"#r{Number}",
                    _ => "<invalid-internal-name>"
                };
            }

            set
            {
                if (value.Length >= 3 &&
                    value[0] == '#' &&
                    (value[1] == 'v' || value[1] == 'r' || value[1] == 's' || value[1] == 'a') &&
                    uint.TryParse(value.Substring(2), out uint n))
                {
                    Type = value[1] == 'v'
                        ? IRInternalNameType.VirtualVariable
                        : IRInternalNameType.MachineRegister;
                    Number = n;
                    if (value[1] == 's')
                        Number &= IRPhysicalRegister.StackNumMask;
                    else if (value[1] == 'a')
                        Number &= IRPhysicalRegister.ArgNumMask;
                }
                else
                    throw new ArgumentException("Invalid internal name");
            }
        }
    }
}