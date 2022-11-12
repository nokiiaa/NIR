using NIR.Instructions;

namespace NIR.Passes
{
    public abstract class IRPass
    {
        public abstract void Perform(IRFunction func, ArchitectureInfo arch);
    }
}