namespace FSharpDebuggerExtension

open Microsoft.VisualStudio.Debugger.ComponentInterfaces

module ExpressionCompiler =

    type FSharpExpressionCompiler() =
        interface IDkmClrExpressionCompiler with
            member this.CompileAssignment(expression, instructionAddress, lValue, error, result) =
                raise (System.NotImplementedException())

            member this.CompileExpression(expression, instructionAddress, inspectionContext, error, result) =
                raise (System.NotImplementedException())

            member this.GetClrLocalVariableQuery(inspectionContext, instructionAddress, argumentsOnly) =
                raise (System.NotImplementedException())
