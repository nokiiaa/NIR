namespace NIR.Backends
{
    public interface IBackend
    {
        public string CompileProgram(IRProgram program);
    }
}