using Microsoft.VisualStudio.Debugger.Clr;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Evaluation.ClrCompilation;

namespace FSharpDebuggerExtension
{
    public sealed class ExpressionCompiler : IDkmClrExpressionCompiler
    {
        void IDkmClrExpressionCompiler.CompileAssignment(DkmLanguageExpression expression, DkmClrInstructionAddress instructionAddress, DkmEvaluationResult lValue, out string error, out DkmCompiledClrInspectionQuery result)
        {
            throw new System.NotImplementedException();
        }

        void IDkmClrExpressionCompiler.CompileExpression(DkmLanguageExpression expression, DkmClrInstructionAddress instructionAddress, DkmInspectionContext inspectionContext, out string error, out DkmCompiledClrInspectionQuery result)
        {
            throw new System.NotImplementedException();
        }

        DkmCompiledClrLocalsQuery IDkmClrExpressionCompiler.GetClrLocalVariableQuery(DkmInspectionContext inspectionContext, DkmClrInstructionAddress instructionAddress, bool argumentsOnly)
        {
            throw new System.NotImplementedException();
        }
    }
}
