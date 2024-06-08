using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NIR.Instructions
{
    public class IRDataFragment
    {
        public enum Enum
        {
            Byte,
            Word,
            Dword,
            Qword,
            String,
            WString,
            Float,
            Double,
            Name
        }

        public Enum Type { get; set; }

        public object Data { get; set; }

        public IRDataFragment(Enum type, object data)
        {
            Type = type;
            Data = data;
        }

        private static readonly UnicodeCategory[] NonRenderingCategories
            = new UnicodeCategory[]
        {
            UnicodeCategory.Control,
            UnicodeCategory.OtherNotAssigned,
            UnicodeCategory.Surrogate
        };

        bool IsPrintable(char c) =>
            char.IsWhiteSpace(c) ||
            !NonRenderingCategories.Contains(char.GetUnicodeCategory(c));

        string Escape(string str)
        {
            var sb = new StringBuilder();
            foreach (char c in str)
            {
                if (!IsPrintable(c))
                    sb.Append($"\\x{((int)c).ToString(c > 0xFF ? "X4" : "X2")}");
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public override string ToString() =>
            Type switch
            {
                Enum.Byte    => $"db {Data}",
                Enum.Word    => $"dw {Data}",
                Enum.Dword   => $"dd {Data}",
                Enum.Qword   => $"dq {Data}",
                Enum.String  => $"ds \"{Escape(Data as string)}\"",
                Enum.WString => $"dws \"{Escape(Data as string)}\"",
                Enum.Name    => Data.ToString(),
                _ => null
            };
    }

    public class IRData : IROp
    {
        public string Name { get; set; }

        public List<IRDataFragment> Fragments { get; set; }

        public override string ToString() => $"data {Name}:\n\t{string.Join("\n\t", Fragments)}";

        public IRData(string name, List<IRDataFragment> fragments)
        {
            Name = name;
            Fragments = fragments ?? new List<IRDataFragment>();
        }
    }
}
