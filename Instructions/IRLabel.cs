namespace NIR.Instructions
{
    public class IRLabel : IROp
    {
        public IRLabel(string name) => Name = name;
        public string Name { get; set; }
        public override string ToString() => $"{Name}:";
    }
}
