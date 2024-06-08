using System.Collections.Generic;
using System.Linq;

namespace NIR.Instructions
{

    public class IRPhiNode : IROp
    {
        public IRPhiNode(IRName mapTo, Dictionary<IRBasicBlock, IRName> choices)
        {
            MapTo = mapTo;
            Choices = choices;
        }

        public IRName MapTo { get; set; }

        public Dictionary<IRBasicBlock, IRName> Choices { get; set; }

        public override string ToString() =>
            $"\tphi {MapTo}, ({string.Join(", ", Choices.Keys.Select(x => $"{Choices[x]} ({x})"))})";
    }
}
