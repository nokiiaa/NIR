using System;
using System.Collections.Generic;
using System.Text;
using NIR.Instructions;

namespace NIR
{
    public class IRBasicBlock
    {
        public List<IROp> Operations { get; set; } = new List<IROp>();

        public List<IRBasicBlock> Parents { get; set; } = new List<IRBasicBlock>();

        public List<IRBasicBlock> Children { get; set; } = new List<IRBasicBlock>();

        public int OValue { get; set; }

        public Stack<bool> Marked { get; set; } = new Stack<bool>();

        public List<IRBasicBlock> DominanceFrontier { get; set; }
            = new List<IRBasicBlock>();

        public List<IRName> LiveIn { get; set; } = new List<IRName>();

        public List<IRName> LiveOut { get; set; } = new List<IRName>();

        public override string ToString() => OValue.ToString();

        public void InsertOp(int index, IROp op)
        {
            op.Block = this;
            Operations.Insert(index, op);
        }

        public void ReplaceAt(int index, IROp op)
        {
            op.Block = this;
            Operations[index] = op;
        }

        public void Unlink()
        {
            foreach (IRBasicBlock parent in Parents)
                parent.Children.Remove(this);

            foreach (IRBasicBlock child in Children)
                child.Parents.Remove(this);
        }
    }
}