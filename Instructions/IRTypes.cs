using System.Collections.Generic;
using System.Linq;

namespace NIR.Instructions
{
    public abstract class IRType
    {
        public abstract int TypeSize(ArchitectureInfo arch);
    }

    public class IRVoidType : IRType
    {
        public override string ToString()
            => "void";

        public override int TypeSize(ArchitectureInfo arch) => 0;
    }

    public class IRIntegerType : IRType
    {
        public IRIntegerType(bool signed, int bits)
        {
            Signed = signed;
            Bits = bits;
        }

        public bool Signed { get; set; }

        public int Bits { get; set; }

        public override string ToString()
            => $"{(Signed ? "i" : "u")}{Bits}";

        public override int TypeSize(ArchitectureInfo arch) => Bits / 8;
    }

    public class IRFloatType : IRType
    {
        public IRFloatType(bool @double) => Double = @double;

        public bool Double { get; set; }

        public override string ToString()
            => Double ? "double" : "float";

        public override int TypeSize(ArchitectureInfo arch) => Double ? 8 : 4;
    }

    public class IRPointerType : IRType
    {
        public IRPointerType(IRType to) => To = to;

        public IRType To { get; set; }

        public override string ToString() => $"{To}*";

        public override int TypeSize(ArchitectureInfo arch) => arch.Bitness / 8;
    }

    public class IRStructureType : IRType
    {
        public int Size { get; set; }

        public IRStructureType(int size) => Size = size;

        public override string ToString() => $"struct[{Size}]";

        public override int TypeSize(ArchitectureInfo arch) => Size;
    }

    public class IRFuncPointerType : IRType
    {
        public IRFuncPointerType(IRType returnType, Dictionary<string, IRType> arguments)
        {
            ReturnType = returnType;
            Arguments = arguments;
        }

        public IRType ReturnType { get; set; }

        public Dictionary<string, IRType> Arguments { get; set; }

        public override string ToString() =>
            $"{ReturnType}({string.Join(", ", Arguments.Select(kvp => $"{kvp.Value} {kvp.Key}"))})";

        public override int TypeSize(ArchitectureInfo arch) => arch.Bitness / 8;
    }
}