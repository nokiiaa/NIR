using NIR.Backends;
using NIR.Instructions;
using NIR.Passes.Optimize;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace NIR.Passes.RegAlloc
{
    public class IRPhysicalRegister
    {
        public IRPhysicalRegister(uint number) => Number = number;

        public uint Number { get; set; }

        public const uint StackSlotBit = 1u << 30;

        public const uint ArgStackSlotBit = 1u << 31;

        public const uint StackNumMask = ~(3u << 30);

        public const uint ArgNumMask = ~(1u << 31);

        public List<IRLiveVariable> Occupied { get; set; } = new List<IRLiveVariable>();
    }

    public class IRGreedyAlloc : IRPass
    {
        uint VirtualCounter = 0;
        List<IRPhysicalRegister> Registers;
        ArchitectureInfo Architecture;
        List<IRLiveVariable> IntervalList;
        List<IRLiveVariable> AllocationQueue;
        IRFunction Function;
        uint StackSlot = 0;
        bool PostOptimize = false;
        bool SecondPass = false;

        private static int TempCounter = 0;

        public static string GenTemp() => "!t" + TempCounter++;

        public IRGreedyAlloc(bool postOptimize = true, bool secondPass = false)
        {
            PostOptimize = postOptimize;
            SecondPass = secondPass;
        }

        static int AllocationOrder(IRLiveVariable var)
            => var.Flags.HasFlag(IRLiveVariableFlags.ForceReg) ?
            (var.AllowedRegisters.Count > 0 ? var.AllowedRegisters.Count : int.MaxValue - 1) : int.MaxValue;

        private IRLiveVariable GenerateVirtualRegister()
            => new IRLiveVariable(
                new IRIntegerType(true, Architecture.Bitness),
                new IRInternalName(IRInternalNameType.VirtualVariable, VirtualCounter++),
                0);

        public override void Perform(IRFunction func, ArchitectureInfo arch)
        {
            Registers = new List<IRPhysicalRegister>();

            for (uint i = 0; i < arch.AvailableRegisters; i++)
                Registers.Add(new IRPhysicalRegister(i));

            Architecture = arch;
            Function = func;
            IntervalList = IROptimizer.LiveIntervalAnalysis(func);

            uint args = 0, stackArgs = 0;

            if (!SecondPass)
            {
                /* Precolor arguments to their respective registers/stack slots */
                foreach (IRLiveVariable live in IntervalList)
                {
                    if (live.Flags.HasFlag(IRLiveVariableFlags.Argument))
                    {
                        IRPhysicalRegister reg;

                        if (args >= arch.MaxArgumentRegisters)
                        {
                            reg = new IRPhysicalRegister(
                                IRPhysicalRegister.ArgStackSlotBit | stackArgs++);
                            Registers.Add(reg);
                        }
                        else
                            reg = Registers[arch.ArgumentRegisters[args]];

                        TryAssignTo(live, reg);
                        args++;
                    }
                }
            }

            foreach (IRBasicBlock block in Function.Blocks)
            {
                foreach (IROp op in block.Operations)
                {
                    if (op is IRCallOp call)
                    {
                        /* Assign a virtual variable to each volatile register with the lifespan of one instruction
                           whenever a function call occurs */
                        foreach (int vol in arch.VolatileRegisters)
                        {
                            var live = GenerateVirtualRegister();

                            if (vol == arch.ReturnValRegister)
                                live.ShareAllowed.Add(call.Dest);

                            live.AddInterval(new Interval(op.Id, op.Id));
                            TryAssignTo(live, Registers[vol]);
                        }
                    }
                    else if (arch is X64ArchitectureInfo &&
                        op is IRThreeOp three &&
                        (three.Type == ThreeOpType.Div || three.Type == ThreeOpType.Mod))
                    {
                        var veax = GenerateVirtualRegister();
                        veax.ShareAllowed.Add(three.Dest);
                        veax.AddInterval(new Interval(op.Id, op.Id));
                        TryAssignTo(veax, Registers[X64ArchitectureInfo.Rax]);
                    }
                }
            }

            AllocationQueue = IntervalList.Where(x =>
                !x.Flags.HasFlag(IRLiveVariableFlags.Argument)).ToList();

            AllocationQueue.Sort((x, y) => -x.TotalLength.CompareTo(y.TotalLength));

            AllocationQueue.Sort((x, y) => AllocationOrder(x).CompareTo(AllocationOrder(y)));

            AllocateRegisters();
        }

        public IRLiveVariable Dequeue()
        {
            IRLiveVariable var = AllocationQueue[0];
            AllocationQueue.RemoveAt(0);
            return var;
        }

        public bool TryAssignTo(IRLiveVariable var, IRPhysicalRegister reg)
        {
            if (!reg.Occupied.Any(x => x.InterferesWith(var)))
            {
                /* No interferences detected */
                var.Register = reg.Number;
                reg.Occupied.Add(var);
                return true;
            }
            return false;
        }

        public void TryAssign(IRLiveVariable var)
        {
            bool forceStack = var.Flags.HasFlag(IRLiveVariableFlags.ForceStack);

            if (!forceStack)
            {
                IEnumerable<IRPhysicalRegister> regs = Registers;

                if (var.AllowedRegisters.Count > 0)
                    regs = var.AllowedRegisters.Select(x => Registers.Find(y => y.Number == x));

                foreach (IRPhysicalRegister reg in regs)
                {
                    if (!reg.Occupied.Any(x => x.InterferesWith(var)))
                    {
                        /* No interferences detected */
                        var.Register = reg.Number;
                        reg.Occupied.Add(var);
                        return;
                    }
                }
            }

            if (!forceStack &&
                var.Flags.HasFlag(IRLiveVariableFlags.ForceReg))
            {
                foreach (uint i in var.AllowedRegisters.Where(
                    x => (x & IRPhysicalRegister.ArgStackSlotBit) != 0))
                {
                    if (!Registers.Any(y => y.Number == i))
                    {
                        var reg = new IRPhysicalRegister(i);
                        if (var.Register == 0)
                        {
                            var.Register = i;
                            reg.Occupied.Add(var);
                        }
                        Registers.Add(reg);
                    }
                }

                if (var.Register == 0)
                    throw new Exception("Register allocation failed");
            }
            else
            {
                var newReg = new IRPhysicalRegister(
                    IRPhysicalRegister.StackSlotBit | StackSlot++);
                var.Register = newReg.Number;
                newReg.Occupied.Add(var);
                Registers.Add(newReg);
            }
        }

        private void CollectRegUsages(out Dictionary<IRName, IRType> usedRegs, out Dictionary<IRName, IROp> firstUsage)
        {
            var uR = new Dictionary<IRName, IRType>();
            var fU = new Dictionary<IRName, IROp>();

            int maxArg = Architecture.MaxArgumentRegisters;

            for (int i = 0; i < Function.Arguments.Count; i++)
            {
                string reg = i >= maxArg ? $"#a{i - maxArg}" : $"#r{Architecture.ArgumentRegisters[i]}";

                if (!uR.ContainsKey(reg))
                {
                    uR[reg] = new IRIntegerType(
                        Function.Arguments[i].Item2 is IRIntegerType type && type.Signed,
                        Architecture.Bitness);
                    fU[reg] = Function.Blocks.First(x => x.Operations.Count != 0).Operations[0];
                }
            }

            Action<IROp, IROperand> collect = (y, x) =>
            {
                if (x is IRName name && (name.ToString().StartsWith("#r") || name.ToString().StartsWith("#s")))
                {
                    if (!fU.ContainsKey(name))
                        fU[name] = y;
                    uR[name] = new IRIntegerType(true, Architecture.Bitness);
                }
            };

            IROpSearch.TraverseOperands(collect, collect, Function.Blocks);

            usedRegs = uR;
            firstUsage = fU;
        }

        private void PruneLocalOps()
        {
            foreach (IRBasicBlock block in Function.Blocks)
            {
                for (int i = 0; i < block.Operations.Count; i++)
                {
                    if (block.Operations[i] is IRLocalOp local)
                    {
                        local.Unlink();
                        i--;
                    }
                }
            }
        }

        /*public void CompressRegisterRanges()
        {
            var volRegRange    = new List<int>();
            var nonVolRegRange = new List<int>();
            var stackRange = new List<int>();

            int getIndex(string s, int number)
            {
                var range = getRange(s, number);
                int index = range.IndexOf(number);
                return range == volRegRange || range == stackRange
                    ? index
                    : index + Architecture.FirstNonVolatileRegister;
            }

            List<int> getRange(string s, int number)
                => s[1] == 'r' ? Architecture.VolatileRegisters.Contains(number) ? volRegRange : nonVolRegRange : stackRange;

            Action<IROp, IROperand> collect = (y, x) =>
            {
                if (x is IRName name)
                {
                    string s = name.ToString();
                    if (s[0] == '#' && int.TryParse(name.ToString().Substring(2), out int number))
                    {
                        var range = getRange(s, number);

                        if (!s.StartsWith("#a") && !range.Contains(number))
                            range.Add(number);
                    }
                }
            };

            Func<IROp, IROperand, IROperand> rewrite = (y, x) =>
            {
                if (x is IRName name)
                {
                    string s = name.ToString();
                    if (s[0] == '#' && int.TryParse(name.ToString().Substring(2), out int number))
                    {
                        var range = getRange(s, number);
                        if ((!s.StartsWith("#r") || !Architecture.ArgumentRegisters.Contains(number))
                            && !s.StartsWith("#a"))
                            return new IRName(
                                s.Substring(0, 2) + getIndex(s, number)
                            );
                    }
                }

                return x;
            };

            IROpSearch.TraverseOperands(collect, collect, Function.Blocks);
            IROpSearch.ReplaceOperands(rewrite, rewrite, Function.Blocks);
        }*/

        private List<IRLiveVariable> AllocateRegisters()
        {
            while (AllocationQueue.Count != 0)
                TryAssign(Dequeue());

            Func<IROp, IROperand, IROperand> replace = (y, x) =>
            {
                if (x is IRName name)
                {
                    var live = IntervalList.Find(x => x.Name.Equals(name));

                    if (live == null)
                        return x;

                    return new IRInternalName(
                        IRInternalNameType.MachineRegister, live.Register);
                }
                return x;
            };

            foreach (IRBasicBlock bb in Function.Blocks)
            {
                for (int i = 0; i < bb.Operations.Count; i++)
                {
                    IROp t = bb.Operations[i];

                    if (t is IRLocalOp alloc)
                    {
                        i--;
                        alloc.Unlink();
                    }
                }
            }

            foreach (IRPhysicalRegister phys in Registers)
                Function.Blocks[0].InsertOp(0, new IROutsideOp(
                    new IRInternalName(
                        IRInternalNameType.MachineRegister, phys.Number),
                        new IRIntegerType(false, Architecture.Bitness)));

            Function.RegenerateOpIds();
            IROpSearch.ReplaceOperands(replace, replace, Function.Blocks);

            Function.RegistersAllocated = true;

            if (PostOptimize)
            {
                CollectRegUsages(out var usedRegs, out var firstUsage);

                foreach (var kvp in usedRegs)
                {
                    IROp usage = firstUsage[kvp.Key];
                    usage.Block.InsertOp(usage.Block.Operations.IndexOf(usage), new IRLocalOp(kvp.Key, kvp.Value));
                }

                Function.RegenerateOpIds();

                Function.Operations = Function.BlockOperationList;

                new IROptimizer().Perform(Function, Architecture);

                PruneLocalOps();
                CollectRegUsages(out var usedRegs2, out var firstUsage2);

                foreach (var kvp in usedRegs)
                {
                    string regName = kvp.Key.ToString();

                    if (uint.TryParse(regName.Substring(2), out uint regNum))
                    {
                        bool cpu = regName.StartsWith("#r");
                        bool arg = regName.StartsWith("#a");
                        bool forceReg = cpu || arg;
                        List<uint> allowed = null;

                        if (forceReg)
                        {
                            if (regName.StartsWith("#r"))
                            {
                                int index = Array.IndexOf(Architecture.ArgumentRegisters, (int)regNum);
                                if (index != -1 && index < Function.Arguments.Count)
                                    allowed = new List<uint> { regNum };
                            }
                            else
                                allowed = new List<uint> { IRPhysicalRegister.ArgStackSlotBit | regNum };
                        }

                        Function.Blocks[0].InsertOp(0, new IRLocalOp(kvp.Key, kvp.Value,
                            forceReg,
                            allowed
                        ));
                        Function.Blocks[0].InsertOp(1, new IROutsideOp(kvp.Key, kvp.Value));
                    }
                }

                Function.RegenerateOpIds();

                new IRGreedyAlloc(postOptimize: false, secondPass: true).Perform(Function, Architecture);
            }

            PruneLocalOps();

            Function.Operations = Function.BlockOperationList;
            Function.RegenerateOpIds();

            return IntervalList;
        }
    }
}