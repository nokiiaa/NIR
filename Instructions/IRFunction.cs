using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NIR.Instructions
{
    public class IRFunction : IROp
    {
        public List<IRBasicBlock> Blocks { get; set; }

        public bool NoDefinition => Operations?.Count == 0;

        public IRType ReturnType { get; set; }

        public string Name { get; set; }

        public List<IROp> Operations { get; set; }

        public bool Extern { get; set; }

        public bool RegistersAllocated { get; set; }

        public List<(string, IRType)> Arguments { get; set; }

        public Dictionary<IRBasicBlock, IRBasicBlock> DominatorTree { get; set; }
            = new Dictionary<IRBasicBlock, IRBasicBlock>();

        public List<(IROp, IRName, IRType)> OutsideSymbols { get; set; } = new List<(IROp, IRName, IRType)>();

        internal List<IROp> BlockOperationList
        {
            get
            {
                var ops = new List<IROp>();
                foreach (IRBasicBlock block in Blocks)
                    ops.AddRange(block.Operations);
                return ops;
            }
        }

        public IRFunction(string name, IRType ret, List<(string, IRType)> args,
            List<IROp> operations = null, bool @extern = false)
        {
            Name = name;
            ReturnType = ret;
            Operations = operations ?? new List<IROp>();
            Arguments = args ?? new List<(string, IRType)>();
            Extern = @extern;
        }

        public void Emit(IROp op) => Operations.Add(op);

        public void EmitLabel(string name) => Operations.Add(new IRLabel(name));

        public string Signature =>
            $"{(Extern ? "extern " : "")}{ReturnType} " +
            $"{Name}({string.Join(", ", Arguments.Select(x => $"{x.Item2} {x.Item1}"))})";

        public override string ToString() => Signature +
            (NoDefinition ? "" : $"\n{{\n{string.Join("\n", Operations)}\n}}");

        public void RegenerateOpIds()
        {
            int id = 0;

            foreach (IRBasicBlock bb in Blocks)
            {
                foreach (IROp op in bb.Operations)
                {
                    op.Block = bb;
                    op.Id = id++;
                }
            }
        }

        public void Print()
        {
            Console.WriteLine(Signature + "\n{");

            foreach (IRBasicBlock bb in Blocks)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"(basic block {bb})");
                Console.ForegroundColor = ConsoleColor.Gray;
                foreach (IROp op in bb.Operations)
                    Console.WriteLine($"{op.Id:x4}: {op}");
            }
            Console.WriteLine("}\n");
        }

        public void CalculateDominatorTree()
        {
            DominatorTree = new Dictionary<IRBasicBlock, IRBasicBlock>();

            int counter = 0;

            IRBasicBlock startNode = Blocks[0];
            DominatorTree[startNode] = startNode;
            bool changed;

            IEnumerable<IRBasicBlock> Postorder(IRBasicBlock block)
            {
                if (!block.Marked.Peek())
                {
                    block.Marked.Pop();
                    block.Marked.Push(true);

                    if (block.Children.Count >= 2)
                        foreach (IRBasicBlock bb in Postorder(block.Children[1]))
                            yield return bb;

                    if (block.Children.Count >= 1)
                        foreach (IRBasicBlock bb in Postorder(block.Children[0]))
                            yield return bb;

                    block.OValue = counter++;
                    yield return block;
                }
            }

            PushMarked();

            var revPostorder = Postorder(startNode).ToList();
            revPostorder.Reverse();

            /* Calculate immediate dominators for all basic blocks */
            do
            {
                changed = false;

                IRBasicBlock Intersect(IRBasicBlock b1, IRBasicBlock b2)
                {
                    while (b1.OValue != b2.OValue)
                    {
                        while (b1.OValue < b2.OValue)
                            b1 = DominatorTree[b1];
                        while (b2.OValue < b1.OValue)
                            b2 = DominatorTree[b2];
                    }
                    return b1;
                }

                foreach (IRBasicBlock bb in revPostorder)
                {
                    if (bb == startNode)
                        continue;
                    IRBasicBlock pred = bb.Parents[0];

                    for (int i = 1; i < bb.Parents.Count; i++)
                    {
                        if (DominatorTree.ContainsKey(bb.Parents[i]))
                            pred = Intersect(bb.Parents[i], pred);
                    }

                    if (!DominatorTree.ContainsKey(bb) || DominatorTree[bb] != pred)
                    {
                        DominatorTree[bb] = pred;
                        changed = true;
                    }
                }
            }
            while (changed);

            PopMarked();
        }

        public void PushMarked() => Blocks.ForEach(x => x.Marked.Push(false));

        public void PopMarked() => Blocks.ForEach(x => x.Marked.Pop());

        public void TraverseDominatorTree(IRFunction func, IRBasicBlock start, Action<IRBasicBlock> action)
        {
            void VisitBlock(IRBasicBlock bb)
            {
                if (bb.Marked.Peek())
                    return;

                bb.Marked.Pop();
                bb.Marked.Push(true);

                action(bb);

                foreach (IRBasicBlock block in func.DominatorTree.Keys)
                    if (block != bb && func.DominatorTree[block] == bb)
                        VisitBlock(block);
            }


            PushMarked();
            VisitBlock(start);
            PopMarked();
        }
    }
}
