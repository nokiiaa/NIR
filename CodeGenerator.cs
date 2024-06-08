using NIR.Instructions;
using NIR.Passes;
using NIR.Passes.Optimize;
using NIR.Passes.RegAlloc;
using System.Collections.Generic;

namespace NIR
{
    public class CodeGenerator
    {
        IRProgram Program;
        ArchitectureInfo Arch;

        public CodeGenerator(IRProgram program, ArchitectureInfo arch)
        {
            Program = program;
            Arch = arch;
        }

        public string Generate(bool optimize = true)
        {
            var symbols = new List<(IROp, IRName, IRType)>();

            foreach (IROp op in Program.Body)
            {
                if (op is IRData dat)
                    symbols.Add((dat, dat.Name, new IRPointerType(new IRVoidType())));
                else if (op is IRGlobal global)
                    symbols.Add((global, global.Name, global.Type));
                else if (op is IRFunction func)
                    symbols.Add((func, func.Name, new IRPointerType(new IRVoidType())));
            }

            foreach (IROp op in Program.Body)
            {
                if (op is IRFunction func && !func.NoDefinition)
                {
                    func.OutsideSymbols = symbols;

                    if (optimize)
                        new IROptimizer().Perform(func, Arch);

                    foreach (IRPass pass in Arch.ArchSpecificPasses)
                        pass.Perform(func, Arch);

                    new IRGreedyAlloc().Perform(func, Arch);
                }
            }

            return Arch.Backend.CompileProgram(Program);
        }
    }
}