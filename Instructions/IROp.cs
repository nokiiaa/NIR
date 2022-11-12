namespace NIR.Instructions
{
    public abstract class IROp
    {
        public bool Volatile { get; set; }

        public int Id { get; set; }

        public IRBasicBlock Block { get; set; }

        public void Unlink() => Block.Operations.Remove(this);
    }
}
