using NIR.Instructions;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace NIR
{
    public class Interval
    {
        public Interval(int start, int end)
        {
            Start = start;
            End = end;
        }
        public static Interval Null => new Interval(-1, -1);
        public int Start { get; set; }
        public int End { get; set; }
        public Interval Intersect(Interval other)
        {
            if (other.Start > End || Start > other.End)
                return Null;
            return (Math.Max(Start, other.Start), Math.Min(End, other.End));
        }
        public static Interval operator &(Interval a, Interval b) => a.Intersect(b);
        public static implicit operator Interval((int start, int end) tuple) =>
            new Interval(tuple.start, tuple.end);
        public static implicit operator Interval(int i) => Null;

        public Interval Copy => new Interval(Start, End);
    }
}
