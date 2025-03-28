// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace Conformance.Expressions

open Xunit
open FSharp.Test
open FSharp.Test.Compiler

module SequenceIteration =

    // This test was automatically generated (moved from FSharpQA suite - Conformance/Expressions/ControlFlowExpressions/SequenceIteration)
    
    [<Theory; FileInlineData("W_IncompleteMatchFor01.fs")>]
    let ``W_IncompleteMatchFor01_fs`` compilation =
        compilation
        |> getCompilation
        |> asFsx
        |> withOptions ["--test:ErrorRanges"]
        |> compile
        |> shouldFail
        |> withDiagnostics [
            (Warning 25, Line 10, Col 5, Line 10, Col 11, "Incomplete pattern matches on this expression. For example, the value 'None' may indicate a case not covered by the pattern(s). Unmatched elements will be ignored.")
            (Warning 25, Line 13, Col 5, Line 13, Col 11, "Incomplete pattern matches on this expression. For example, the value 'None' may indicate a case not covered by the pattern(s). Unmatched elements will be ignored.")
            (Warning 25, Line 15, Col 5, Line 15, Col 11, "Incomplete pattern matches on this expression. For example, the value 'Some (0)' may indicate a case not covered by the pattern(s). Unmatched elements will be ignored.")
            (Warning 25, Line 17, Col 5, Line 17, Col 6, "Incomplete pattern matches on this expression. For example, the value '0' may indicate a case not covered by the pattern(s). Unmatched elements will be ignored.")
            (Warning 25, Line 22, Col 9, Line 22, Col 17, "Incomplete pattern matches on this expression. For example, the value 'None' may indicate a case not covered by the pattern(s).")
            (Warning 25, Line 27, Col 20, Line 27, Col 28, "Incomplete pattern matches on this expression. For example, the value 'None' may indicate a case not covered by the pattern(s).")
        ]

