using NIR.Instructions;
using NIR.Passes;
using NIR.Passes.X64Specific;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace NIR.Backends
{
    public class X64ArchitectureInfo : ArchitectureInfo
    {
        public const int
            Rax = 0, Rcx = 1, Rdx = 2, R8 = 3, R9 = 4, R10 = 5, R11 = 6,
            Rbx = 7, Rbp = 8, Rsi = 9, Rdi = 10, R12 = 11, R13 = 12, R14 = 13, R15 = 14;

        public override int Bitness => 64;

        /* TODO: move these four properties to their own calling convention class */
        public override int[] ArgumentRegisters => new int[] { Rcx, Rdx, R8, R9 };

        public override int MaxArgumentRegisters => 4;

        public override int AvailableRegisters => 14;

        /* In all x86 calling conventions, eax, ecx and edx 
           should be generally considered
           as trashed after a function call. */
        public override int[] VolatileRegisters => new int[] { Rax, Rcx, Rdx, R8, R9, R10, R11 };

        public override int ReturnValRegister => Rax;

        public override int FirstNonVolatileRegister => Rbx;

        public override bool[] NeedsTwoAddressCode => new bool[]
        {
            true, true, true,
            true, true, true, true,
            true, true, true, true,
            true
        };

        public override IBackend Backend => new X64Backend();

        public override IRPass[] ArchSpecificPasses =>
            new IRPass[]
            {
                new ThreeToTwoAc(),
                new MemToMemEliminator(),
                new X64DestMulEliminator(),
                new X64ShiftConverter(),
                new X64DivArgSpiller(),
                new X64DivOptimizer(),
                new X64LargeConstantEliminator(),
            };
    }

    public class X64Backend : IBackend
    {
        public X64Backend() { }

        private StringBuilder AssemblyString = new StringBuilder();

        readonly static string[] RegTable8 = new string[]
        {
            "al", "cl", "dl", "r8b", "r9b", "r10b", "r11b",
            "bl", "bpl", "sil", "dil", "r12b", "r13b", "r14b",
            "r15b"
        };

        readonly static string[] RegTable16 = new string[]
        {
            "ax", "cx", "dx", "r8w", "r9w", "r10w", "r11w",
            "bx", "bp", "si", "di", "r12w", "r13w", "r14w",
            "r15w"
        };

        readonly static string[] RegTable32 = new string[]
        {
            "eax", "ecx", "edx", "r8d", "r9d", "r10d", "r11d",
            "ebx", "ebp", "esi", "edi", "r12d", "r13d", "r14d",
            "r15d"
        };

        readonly static string[] RegTable64 = new string[]
        {
            "rax", "rcx", "rdx", "r8", "r9", "r10", "r11",
            "rbx", "rbp", "rsi", "rdi", "r12", "r13", "r14",
            "r15"
        };

        readonly static string[] BranchConditionTable = new string[]
        {
            "jz", "jnz", "jl", "jge", "jg", "jle",
            "js", "jns", "jo", "jno", "jmp"
        };

        readonly static string[] SetConditionTable = new string[]
        {
            "setz", "setnz", "setl", "setge", "setg", "setle",
            "sets", "setns", "seto", "setno", ""
        };

        readonly static string[][] Op3Table = new string[][]
        {
            new string[] {
                "add", "sub", "imul", "div", "div",
                "shl", "shr", "rol", "ror", "xor", "and", "or"
            },
            new string[] {
                "add", "sub", "imul", "idiv", "idiv",
                "shl", "sar", "rol", "ror", "xor", "and", "or"
            }
        };

        readonly static string[] Op2Table = new string[]
        {
            "not", "neg"
        };

        string FormatIdentifier(string ident)
        {
            StringBuilder newIdent = new StringBuilder();

            foreach (char c in ident)
            {
                if (c >= '0' && c <= '9' || c == '_' || char.ToLower(c) >= 'a' && char.ToLower(c) <= 'z')
                    newIdent.Append(c);
                else
                    newIdent.Append($"_{(int)c:X}");
            }

            return newIdent.ToString();
        }

        readonly static Dictionary<int, string> SizeTable = new Dictionary<int, string>
        {
            { 8,  "byte"  },
            { 16, "word"  },
            { 32, "dword" },
            { 64, "qword" }
        };

        readonly static Dictionary<int, string[]> RegTables = new Dictionary<int, string[]>
        {
            { 8,  RegTable8  },
            { 16, RegTable16 },
            { 32, RegTable32 },
            { 64, RegTable64 }
        };

        readonly static Dictionary<int, string> DivSignExtendTable = new Dictionary<int, string>
        {
            { 16, "cwd" },
            { 32, "cdq" },
            { 64, "cqo" }
        };

        public static X64ArchitectureInfo ArchInfo { get; set; } = new X64ArchitectureInfo();

        private void AddLabel(string label) => AssemblyString.AppendLine($"{label}:");

        private void AddInstruction(string ins) => AssemblyString.AppendLine($"\t{ins}");

        public void CompileFunction(IRFunction func)
        {
            AddInstruction($"global {FormatIdentifier(func.Name)}");
            AddLabel(FormatIdentifier(func.Name));

            int stackAllocSpace = 0;
            int localVarSpace = 0;
            int calleeArgSpace = 0;
            var nonVolUsed = new List<int>();

            foreach (IRBasicBlock block in func.Blocks)
            {
                foreach (IROp op in func.Operations)
                {
                    if (op is IRCallOp call)
                        calleeArgSpace = Math.Max(calleeArgSpace, Math.Max(4, call.Arguments.Count) * 8);
                    else if (op is IRStackAllocOp alloc)
                        stackAllocSpace += (alloc.Bytes + 7) & -7;

                    Action<IROp, IROperand> traverse = (y, x) =>
                    {
                        if (x is IRName name)
                        {
                            int num;

                            if (name.Name.StartsWith("#r"))
                            {
                                num = int.Parse(name.Name.Substring(2));
                                if (!ArchInfo.VolatileRegisters.Contains(num) && !nonVolUsed.Contains(num))
                                    nonVolUsed.Add(num);
                            }
                            else if (name.Name.StartsWith("#s"))
                            {
                                num = int.Parse(name.Name.Substring(2));
                                localVarSpace = Math.Max(localVarSpace, (num + 1) * 8);
                            }
                        }
                    };

                    IROpSearch.TraverseOperands(traverse, traverse, op);
                }
            }


            int nonVolCount = 0;
            // Generate prolog
            foreach (int nonVol in nonVolUsed)
            {
                nonVolCount++;
                AddInstruction($"push {RegTable64[nonVol]}");
            }

            int retAddrStart = calleeArgSpace + stackAllocSpace + localVarSpace;
            int localsStart = stackAllocSpace + calleeArgSpace;
            int stackAllocStart = calleeArgSpace;
            int stackAllocPtr = stackAllocStart;
            int calleeArgStart = 0;

            if (retAddrStart > 0)
            {
                if ((retAddrStart + nonVolCount * 8 & 15) == 0)
                    retAddrStart += 8;
                AddInstruction($"sub rsp, {retAddrStart}");
            }

            string ConvertIRRegister(out bool phys, out int num, out int offset, string reg, int size = 64, bool addSize = false)
            {
                phys = false;
                offset = 0;

                if (!reg.StartsWith("#"))
                {
                    num = -1;
                    return FormatIdentifier(reg);
                }

                num = int.Parse(reg.Substring(2));

                if (reg[1] == 'a')
                {
                    offset = retAddrStart + (num + ArchInfo.ArgumentRegisters.Length + 1) * 8;
                    return $"{(addSize ? $"{SizeTable[size]} " : "")}[rsp+{offset}]";
                }
                else if (reg[1] == 'r')
                {
                    phys = true;
                    return RegTables[size][num];
                }
                else if (reg[1] == 's')
                {
                    offset = localsStart + num * 8;
                    return $"{(addSize ? $"{SizeTable[size]} " : "")}[rsp" + (offset != 0 ? $"+{offset}]" : "]");
                }

                return "";
            }

            void Epilog()
            {
                if (retAddrStart > 0) AddInstruction($"add rsp, {retAddrStart}");

                foreach (int nonVol in ((IEnumerable<int>)nonVolUsed).Reverse())
                    AddInstruction($"pop {RegTable64[nonVol]}");

                AddInstruction("ret");
            }

            bool lastRet = false;


            foreach (IRBasicBlock block in func.Blocks)
            {
                foreach (IROp op in block.Operations)
                {
                    lastRet = op is IRRetOp;

                    switch (op)
                    {
                        case IRAddrOfOp addrof:
                            {
                                string dest = ConvertIRRegister(out bool phys1, out int num1, out int offset1, addrof.Dest);
                                string var = ConvertIRRegister(out bool phys2, out int num2, out int offset2, addrof.Variable);
                                if (phys1)
                                    AddInstruction($"lea {dest}, {var}");
                                else
                                {
                                    if (!phys1)
                                    {
                                        AddInstruction($"mov {dest}, rsp");
                                        AddInstruction($"add {dest}, {offset2}");
                                    }
                                    else
                                        AddInstruction($"lea {dest}, {addrof.Variable.Name}");
                                }
                                break;
                            }

                        case IRStackAllocOp alloc:
                            {
                                string conved = ConvertIRRegister(out bool phys, out int num, out int offset, alloc.Dest, 64);

                                if (!phys)
                                {
                                    AddInstruction($"mov {conved}, rsp");
                                    AddInstruction($"add {conved}, {stackAllocPtr}");
                                }
                                else
                                    AddInstruction($"lea {conved}, [rsp+{stackAllocPtr}]");

                                stackAllocPtr += (alloc.Bytes + 7) & -7;
                                break;
                            }

                        case IRBranchOp branch:
                            AddInstruction($"{BranchConditionTable[(int)branch.Condition]} .{branch.Destination}");
                            break;

                        case IRCallOp call:
                            {
                                string ConvArg(IROperand operand, out bool phys, out int num, out int offset)
                                {
                                    phys = false;
                                    num = -1;
                                    offset = 0;
                                    return operand is IRName n ? ConvertIRRegister(out phys, out num, out offset, n) : operand.ToString();
                                }

                                List<int> regsPassed = new List<int>();

                                for (int i = 0; i < call.Arguments.Count; i++)
                                {
                                    ConvArg(call.Arguments[i], out bool phys1, out int num1, out int _);
                                    if (phys1 && !regsPassed.Contains(num1))
                                        regsPassed.Add(num1);
                                }

                                string conved;
                                string tempReg = RegTable64[ArchInfo.VolatileRegisters.Except(regsPassed).First()];

                                for (int i = 0; i < Math.Min(call.Arguments.Count, ArchInfo.ArgumentRegisters.Length); i++)
                                {
                                    string reg = RegTable64[ArchInfo.ArgumentRegisters[i]];
                                    conved = ConvArg(call.Arguments[i], out _, out _, out _);

                                    if (call.Arguments[i] is IRPrimitiveOperand prim && (dynamic)prim.Value == 0)
                                        AddInstruction($"xor {reg}, {reg}");
                                    else if (reg != conved)
                                        AddInstruction($"mov {reg}, {conved}");
                                }

                                for (int i = ArchInfo.ArgumentRegisters.Length; i < call.Arguments.Count; i++)
                                {
                                    conved = ConvArg(call.Arguments[i], out bool phys, out int num, out int offset);

                                    if (!phys)
                                    {
                                        if (call.Arguments[i] is IRPrimitiveOperand)
                                            phys = true;
                                        else
                                            AddInstruction($"mov {tempReg}, {conved}");
                                    }

                                    AddInstruction($"mov qword [rsp+{calleeArgStart + i * 8}], {(phys ? conved : tempReg)}");
                                }

                                AddInstruction($"call {ConvertIRRegister(out _, out _, out _, call.Callee)}");

                                string conv = ConvertIRRegister(out _, out _, out _, call.Dest);

                                if (call.IndirectStore)
                                    AddInstruction($"mov qword [{conv}], rax");
                                else if (conv != "rax")
                                    AddInstruction($"mov {conv}, rax");
                                break;
                            }

                        case IRChkOp chk:
                            {
                                if (chk.Operand is IRName name)
                                {
                                    string conved = ConvertIRRegister(out bool phys, out int num, out int offset, name,
                                        (chk.OperandType ?? new IRIntegerType(false, 64)).TypeSize(ArchInfo) * 8);

                                    if (!phys)
                                        AddInstruction($"test {conved}, -1");
                                    else
                                        AddInstruction($"test {conved}, {conved}");
                                }
                                else
                                {
                                    // ...Technically not supposed to happen
                                }
                                break;
                            }

                        case IRLoadIndirectOp load:
                            int size = load.SrcType.TypeSize(ArchInfo) * 8;
                            string convedDest = ConvertIRRegister(out bool _, out int _, out int _, load.Dest, size <= 32 ? 32 : 64);
                            string convedSrc = ConvertIRRegister(out bool _, out int _, out int _, load.Src as IRName);

                            AddInstruction($"mov{(size >= 32 ? "" : "zx")} {convedDest}, {SizeTable[size]} [{convedSrc}]");
                            break;

                        case IRCmpOp cmp:
                            {
                                if (!(cmp.A is IRName n))
                                {
                                    IROperand o = cmp.A;
                                    cmp.A = cmp.B;
                                    cmp.B = o;
                                }

                                size = (cmp.Type ?? new IRIntegerType(false, 64)).TypeSize(ArchInfo) * 8;

                                string convedA = ConvertIRRegister(out bool phys, out int num, out int offset, cmp.A as IRName, size, true);
                                string convedB = cmp.B is IRName n2
                                    ? ConvertIRRegister(out bool _, out int _, out int _, n2, size, true)
                                    : cmp.B.ToString();

                                AddInstruction($"cmp {convedA}, {convedB}");
                                break;
                            }

                        case IRMovFlagOp flag:
                            {
                                string conved = ConvertIRRegister(out bool phys, out int num, out int offset, flag.Dest, 8);

                                if (!phys)
                                    AddInstruction($"mov qword [rsp+{offset}], 0");

                                AddInstruction($"{SetConditionTable[(int)flag.Flag]} {conved}");

                                if (phys)
                                    AddInstruction($"movzx {RegTable64[num]}, {conved}");
                                break;
                            }

                        case IRMovOp mov:
                            {
                                int num2 = -1;
                                size = mov.Indirect ? 8 : (mov.DestType ?? new IRPointerType(new IRVoidType())).TypeSize(ArchInfo);
                                convedDest = ConvertIRRegister(out bool phys1, out int num1, out int offset1, mov.Dest, size * 8, true);
                                convedSrc = mov.Src is IRName n
                                    ? ConvertIRRegister(out bool phys2, out num2, out int offset2, n, size * 8)
                                    : mov.Src.ToString();

                                if (!mov.Indirect)
                                {
                                    if (phys1 && mov.Src is IRPrimitiveOperand prim && (dynamic)prim.Value == 0)
                                        AddInstruction($"xor {convedDest}, {convedDest}");
                                    else if (convedDest != convedSrc)
                                        AddInstruction($"mov {convedDest}, {convedSrc}");
                                }
                                else
                                {
                                    string type = "";

                                    if (mov.DestType is IRIntegerType it)
                                    {
                                        type = $"{SizeTable[it.Bits]} ";
                                        if (num2 != -1)
                                            convedSrc = RegTables[it.Bits][num2];
                                    }

                                    AddInstruction($"mov {type}[{convedDest}], {convedSrc}");
                                }
                                break;
                            }

                        case IRThreeOp three:
                            {
                                bool signed = three.DestType is not IRIntegerType integer || integer.Signed;
                                size = (three.DestType ?? new IRIntegerType(false, 64)).TypeSize(ArchInfo);
                                convedDest = ConvertIRRegister(out bool phys1, out int num1, out int offset1, three.Dest, size * 8, true);
                                convedSrc = three.B is IRName n
                                    ? ConvertIRRegister(out bool phys2, out int num2, out int offset2, n, size * 8, true)
                                    : three.B.ToString();

                                int i = 0;

                                if (three.Type == ThreeOpType.Div ||
                                    three.Type == ThreeOpType.Mod)
                                {
                                    if (three.B is IRPrimitiveOperand prim)
                                    {
                                        if (prim.Value is ulong ul && (ul & (ul - 1)) == 0 ||
                                            prim.Value is long l && (l & (l - 1)) == 0)
                                        {
                                            dynamic d = (dynamic)prim.Value;

                                            while (d != 0)
                                            {
                                                if ((d & 1) != 0) break;
                                                i++;
                                                d >>= 1;
                                            }

                                            if (i != 0)
                                                AddInstruction($"shr {convedDest}, {i}");
                                        }
                                        else
                                            throw new Exception("Division by non-power-of-two constant is currently unsupported (X64DivOptimizer pass failed?)");
                                    }
                                    else
                                    {
                                        string inReg = RegTables[size * 8][0];
                                        if (inReg != convedDest)
                                            AddInstruction($"mov {inReg}, {convedDest}");

                                        AddInstruction(DivSignExtendTable[size * 8]);
                                        AddInstruction($"{(signed ? "idiv" : "div")} {convedSrc}");

                                        string outReg = RegTables[size * 8][three.Type == ThreeOpType.Mod ? 2 : 0];
                                        if (outReg != convedDest)
                                            AddInstruction($"mov {convedDest}, {outReg}");
                                    }
                                }
                                else if (three.B is IRPrimitiveOperand pri
                                            && (three.Type is ThreeOpType.Add || three.Type is ThreeOpType.Sub)
                                            && ((dynamic)pri.Value == 1))
                                    AddInstruction($"{(three.Type == ThreeOpType.Add ? "inc" : "dec")} {convedDest}");
                                else if (three.B is IRPrimitiveOperand pr &&
                                    three.Type == ThreeOpType.Mul &&
                                    (pr.Value is ulong ul && (ul & (ul - 1)) == 0 ||
                                    pr.Value is long l && (l & (l - 1)) == 0))
                                {
                                    dynamic d = (dynamic)pr.Value;

                                    while (d != 0)
                                    {
                                        if ((d & 1) != 0) break;
                                        i++;
                                        d >>= 1;
                                    }

                                    if (i != 0)
                                        AddInstruction($"shl {convedDest}, {i}");
                                }
                                else
                                    AddInstruction($"{Op3Table[signed ? 1 : 0][(int)three.Type]} {convedDest}, {convedSrc}");
                            }
                            break;

                        case IRTwoOp two:
                            convedDest = ConvertIRRegister(out _, out _, out _, two.Dest,
                                (two.DestType ?? new IRIntegerType(false, 64)).TypeSize(ArchInfo) * 8, true);
                            AddInstruction($"{Op2Table[(int)two.Type]} {convedDest}");
                            break;

                        case IRLabel label:
                            AddLabel($".{label.Name}");
                            break;

                        case IRRetOp ret:
                            {
                                if (ret.Value != null)
                                {
                                    string value = ret.Value is IRName n3 ? ConvertIRRegister(out _, out _, out _, n3) : ret.Value.ToString();
                                    if (ret.Value is IRPrimitiveOperand prim && (dynamic)prim.Value == 0)
                                        AddInstruction("xor rax, rax");
                                    else if (value != "rax")
                                        AddInstruction($"mov rax, {value}");
                                }
                                Epilog();
                                break;
                            }
                    }
                }
            }

            if (!lastRet)
                Epilog();

            AddInstruction("");
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
            bool lastPrintable = false;
            foreach (char c in str)
            {
                if (!IsPrintable(c))
                {
                    if (lastPrintable)
                        sb.Append("', ");
                    lastPrintable = false;
                    sb.Append($"{(int)c}, ");
                }
                else
                {
                    if (!lastPrintable)
                        sb.Append("'");
                    lastPrintable = true;
                    sb.Append(c);
                }
            }

            if (lastPrintable) sb.Append("'");
            sb.Append(", 0");

            return sb.ToString();
        }

        public string CompileProgram(IRProgram program)
        {
            int Cmp(IROp o) => (o is IRGlobal || o is IRData) ? 0 : 1;

            program.Body.Sort((x, y) => Cmp(x).CompareTo(Cmp(y)));

            AddInstruction("section .data");

            bool enteredText = false;

            foreach (IROp op in program.Body)
            {
                if (op is IRGlobal global)
                    AddInstruction($"{global.Name} equ {global.Value}");
                else if (op is IRData data)
                {
                    AddLabel(data.Name);

                    foreach (IRDataFragment frag in data.Fragments)
                    {
                        switch (frag.Type)
                        {
                            case IRDataFragment.Enum.Byte: AddInstruction($"db {frag.Data}"); break;
                            case IRDataFragment.Enum.Word: AddInstruction($"dw {frag.Data}"); break;
                            case IRDataFragment.Enum.Dword: AddInstruction($"dd {frag.Data}"); break;
                            case IRDataFragment.Enum.Qword: AddInstruction($"dq {frag.Data}"); break;
                            case IRDataFragment.Enum.String: AddInstruction($"db {Escape(frag.Data as string)}"); break;
                            case IRDataFragment.Enum.WString: AddInstruction($"dw {Escape(frag.Data as string)}"); break;
                            case IRDataFragment.Enum.Float: AddInstruction($"dd {frag.Data}"); break;
                            case IRDataFragment.Enum.Double: AddInstruction($"dq {frag.Data}"); break;
                            case IRDataFragment.Enum.Name: AddInstruction($"dq {frag.Data}"); break;
                        }
                    }
                }
                else if (op is IRFunction func)
                {
                    if (!enteredText)
                    {
                        AddInstruction("section .text");
                        enteredText = true;
                    }

                    if (func.NoDefinition)
                        AddInstruction($"extern {func.Name}");
                    else
                        CompileFunction(func);
                }
            }

            string fname = $"tmp{FileCounter++:x8}";
            File.WriteAllText(fname + ".asm", AssemblyString.ToString());

            Console.WriteLine(AssemblyString.ToString());

            Process.Start(new ProcessStartInfo
            {
                FileName = "nasm",
                Arguments = $"-fwin64 {fname}.asm -o {fname}.o",
                WorkingDirectory = Environment.CurrentDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }).WaitForExit();

            File.Delete(fname + ".asm");

            return $"{fname}.o";
        }

        private static int FileCounter = new Random(Environment.TickCount).Next();

        public static string GenerateEntryStub(string entryPoint)
        {
            string fname = $"tmp{FileCounter++:x8}";
            File.WriteAllText(fname + ".asm", @$"
    section .text
    extern {entryPoint}
    global WinMain
WinMain:
    jmp {entryPoint}
");

            Process.Start(new ProcessStartInfo
            {
                FileName = "nasm",
                Arguments = $"-fwin64 {fname}.asm -o {fname}.o",
                WorkingDirectory = Environment.CurrentDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }).WaitForExit();

            File.Delete(fname + ".asm");

            return $"{fname}.o";
        }
    }
}