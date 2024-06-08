using NIR.Backends;
using NIR.Passes;
using System;
using System.Collections.Generic;
using System.Text;

namespace NIR
{
    public abstract class ArchitectureInfo
    {
        public virtual int Bitness { get; }
        public virtual int[] ArgumentRegisters { get; }
        public virtual int MaxArgumentRegisters { get; }
        public virtual int AvailableRegisters { get; }
        public virtual int[] VolatileRegisters { get; }
        public virtual int FirstNonVolatileRegister { get; }
        public virtual int ReturnValRegister { get; }
        public virtual bool[] NeedsTwoAddressCode { get; }
        public virtual string[] RegisterArray { get; }
        public virtual IBackend Backend { get; }
        public virtual IRPass[] ArchSpecificPasses { get; }
    }
}