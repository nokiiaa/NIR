using NIR.Instructions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NIR
{
    [Flags]
    public enum IRLiveVariableFlags
    {
        Argument   = 1,
        ForceReg   = 2,
        ForceStack = 4
    }

    public class IRLiveVariable
    {
        public int AllocationPriority { get; set; }

        public List<string> ShareAllowed { get; set; }

        public List<uint> AllowedRegisters { get; set; }

        public int SpillWeight { get; set; }

        public uint Register { get; set; }

        public IRType Type { get; set; }

        public IRName Name { get; set; }

        public IRLiveVariableFlags Flags { get; set; }

        public List<Interval> Intervals { get; set; } = new List<Interval>();

        public List<IRBasicBlock> AssociatedBlocks { get; set; } = new List<IRBasicBlock>();

        public bool Null => Intervals.Count == 0;

        public IRLiveVariable(IRType type, IRName name, IRLiveVariableFlags flags,
            List<uint> allowedRegisters = null, List<string> shareAllowed = null)
        {
            ShareAllowed = shareAllowed ?? new List<string>();
            AllowedRegisters = allowedRegisters ?? new List<uint>();
            Type = type;
            Name = name;
            Flags = flags;
        }

        public bool InterferesWith(IRLiveVariable other)
        {
            if (other.ShareAllowed.Contains(Name) || ShareAllowed.Contains(other.Name))
                return false;

            int i = 0, j = 0;

            while (i < Intervals.Count && j < other.Intervals.Count)
            {
                int l = Math.Max(Intervals[i].Start, other.Intervals[j].Start);
                int r = Math.Min(Intervals[i].End, other.Intervals[j].End);

                if (l <= r)
                    return true;

                if (r == Intervals[i].End)
                    i++;
                else
                    j++;
            }
            return false;
        }

        public void AddInterval(Interval interval) => Intervals.Add(interval);

        public int TotalLength => Intervals.Sum(x => x.End - x.Start + 1);
    }
}
