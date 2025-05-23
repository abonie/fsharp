﻿module FSharp.Compiler.Service.Tests.EditorTests

open Xunit
open FSharp.Test.Assert
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.EditorServices
open FSharp.Compiler.Service.Tests.Common
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open FSharp.Compiler.Tokenization

#nowarn "1182" // Unused bindings when ignored parsed results etc.

let stringMethods =
    [
        "Chars"; "Clone"; "CompareTo"; "Contains"; "CopyTo"; "EndsWith";
#if NETCOREAPP
        "EnumerateRunes";
#endif
        "Equals"; "GetEnumerator"; "GetHashCode";
#if NETCOREAPP
        "GetPinnableReference";
#endif
        "GetReverseIndex"; "GetType"; "GetTypeCode"; "IndexOf";
        "IndexOfAny"; "Insert"; "IsNormalized"; "LastIndexOf"; "LastIndexOfAny";
        "Length"; "Normalize"; "PadLeft"; "PadRight"; "Remove";
        "Replace";
#if NETCOREAPP
        "ReplaceLineEndings";
#endif
        "Split"; "StartsWith"; "Substring";
        "ToCharArray"; "ToLower"; "ToLowerInvariant"; "ToString"; "ToUpper";
        "ToUpperInvariant"; "Trim"; "TrimEnd"; "TrimStart";
#if NETCOREAPP
        "TryCopyTo"
#endif
]

let input =
  """
  open System

  let foo() =
    let msg = String.Concat("Hello"," ","world")
    if true then
      printfn "%s" msg.
  """

#if COMPILED
[<Fact(Skip="This isn't picking up changes in Fsharp.Core")>]
#else
[<Fact>]
#endif
let ``Intro test`` () =

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input)
    let identToken = FSharpTokenTag.IDENT
//    let projectOptions = checker.GetProjectOptionsFromScript(file, input) |> Async.RunImmediate

    // So we check that the messages are the same
    for msg in typeCheckResults.Diagnostics do
        printfn "Got an error, hopefully with the right text: %A" msg

    printfn "typeCheckResults.Diagnostics.Length = %d" typeCheckResults.Diagnostics.Length

    // We only expect one reported error. However,
    // on Unix, using filenames like /home/user/Test.fsx gives a second copy of all parse errors due to the
    // way the load closure for scripts is generated. So this returns two identical errors
    (match typeCheckResults.Diagnostics.Length with 1 | 2 -> true | _ -> false)  |> shouldEqual true

    // So we check that the messages are the same
    for msg in typeCheckResults.Diagnostics do
        printfn "Good! got an error, hopefully with the right text: %A" msg
        msg.Message.Contains("Missing qualification after '.'") |> shouldEqual true

    // Get tool tip at the specified location
    let tip = typeCheckResults.GetToolTip(4, 7, inputLines[1], ["foo"], identToken)
    // (sprintf "%A" tip).Replace("\n","") |> shouldEqual """ToolTipText [Single ("val foo: unit -> unitFull name: Test.foo",None)]"""
    // Get declarations (autocomplete) for a location
    let partialName = { QualifyingIdents = []; PartialIdent = "msg"; EndColumn = 22; LastDotPos = None }
    let decls =  typeCheckResults.GetDeclarationListInfo(Some parseResult, 7, inputLines[6], partialName, (fun _ -> []))
    let expected = [ for item in decls.Items -> item.NameInList ]
    Assert.Equal<string list>(stringMethods |> List.sort, expected |> List.sort)
    // Get overloads of the String.Concat method
    let methods = typeCheckResults.GetMethods(5, 27, inputLines[4], Some ["String"; "Concat"])

    methods.MethodName  |> shouldEqual "Concat"

    // Print concatenated parameter lists
    [ for mi in methods.Methods do
        yield methods.MethodName , [ for p in mi.Parameters do yield p.Display |> taggedTextToString ] ]
        |> shouldEqual
              [("Concat", ["[<ParamArray>] args: obj []"]);
               ("Concat", ["[<ParamArray>] values: string []"]);
               ("Concat", ["values: Collections.Generic.IEnumerable<'T>"]);
               ("Concat", ["values: Collections.Generic.IEnumerable<string>"]);
               ("Concat", ["arg0: obj"]); ("Concat", ["arg0: obj"; "arg1: obj"]);
               ("Concat", ["str0: string"; "str1: string"]);
               ("Concat", ["arg0: obj"; "arg1: obj"; "arg2: obj"]);
               ("Concat", ["str0: string"; "str1: string"; "str2: string"]);
#if !NETCOREAPP // TODO: check why this is needed for .NET Core testing of FSharp.Compiler.Service
               ("Concat", ["arg0: obj"; "arg1: obj"; "arg2: obj"; "arg3: obj"]);
#endif
               ("Concat", ["str0: string"; "str1: string"; "str2: string"; "str3: string"])]


[<Fact>]
let ``GetMethodsAsSymbols should return all overloads of a method as FSharpSymbolUse`` () =

    let extractCurriedParams (symbol:FSharpSymbolUse) =
        match symbol.Symbol with
        | :? FSharpMemberOrFunctionOrValue as mvf ->
            [for pg in mvf.CurriedParameterGroups do
                for p:FSharpParameter in pg do
                    yield p.DisplayName, p.Type.Format symbol.DisplayContext]
        | _ -> []

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input)
    let methodsSymbols = typeCheckResults.GetMethodsAsSymbols(5, 27, inputLines[4], ["String"; "Concat"])
    match methodsSymbols with
    | Some methods ->
        let results =
            [ for ms in methods do
                yield ms.Symbol.DisplayName, extractCurriedParams ms ]
            |> List.sortBy (fun (_name, parameters) -> parameters.Length, (parameters |> List.map snd ))
        let expected =
            [("Concat", [("values", "Collections.Generic.IEnumerable<'T>")]);
             ("Concat", [("values", "Collections.Generic.IEnumerable<string>")]);
#if NETCOREAPP
             ("Concat", [("args", "ReadOnlySpan<obj>")]);
             ("Concat", [("values", "ReadOnlySpan<string>")]);
#endif
             ("Concat", [("arg0", "obj")]);
             ("Concat", [("args", "obj array")]);
             ("Concat", [("values", "string array")]);
#if NETCOREAPP
             ("Concat", [("str0", "ReadOnlySpan<char>");("str1", "ReadOnlySpan<char>")]);
#endif
             ("Concat", [("arg0", "obj"); ("arg1", "obj")]);
             ("Concat", [("str0", "string"); ("str1", "string")]);
#if NETCOREAPP
             ("Concat", [("str0", "ReadOnlySpan<char>"); ("str1", "ReadOnlySpan<char>"); ("str2", "ReadOnlySpan<char>")]);
#endif
             ("Concat", [("arg0", "obj"); ("arg1", "obj"); ("arg2", "obj")]);
             ("Concat", [("str0", "string"); ("str1", "string"); ("str2", "string")]);
#if NETCOREAPP
             ("Concat", [("str0", "ReadOnlySpan<char>"); ("str1", "ReadOnlySpan<char>"); ("str2", "ReadOnlySpan<char>"); ("str3", "ReadOnlySpan<char>")]);
#endif
#if !NETCOREAPP // TODO: check why this is needed for .NET Core testing of FSharp.Compiler.Service
             ("Concat", [("arg0", "obj"); ("arg1", "obj"); ("arg2", "obj"); ("arg3", "obj")]);
#endif
             ("Concat", [("str0", "string"); ("str1", "string"); ("str2", "string"); ("str3", "string")])]
        
        results |> shouldEqual expected

    | None -> failwith "No symbols returned"


let input2 =
        """
[<System.CLSCompliant(true)>]
let foo(x, y) =
    let msg = String.Concat("Hello"," ","world")
    if true then
        printfn "x = %d, y = %d" x y
        printfn "%s" msg

type C() =
    member x.P = 1
        """

[<Fact>]
let ``Symbols basic test`` () =

    let file = "/home/user/Test.fsx"
    let untyped2, typeCheckResults2 = parseAndCheckScript(file, input2)

    let partialAssemblySignature = typeCheckResults2.PartialAssemblySignature

    partialAssemblySignature.Entities.Count |> shouldEqual 1  // one entity

[<Fact>]
let ``Symbols many tests`` () =

    let file = "/home/user/Test.fsx"
    let untyped2, typeCheckResults2 = parseAndCheckScript(file, input2)

    let partialAssemblySignature = typeCheckResults2.PartialAssemblySignature

    partialAssemblySignature.Entities.Count |> shouldEqual 1  // one entity
    let moduleEntity = partialAssemblySignature.Entities[0]

    moduleEntity.DisplayName |> shouldEqual "Test"

    let classEntity = moduleEntity.NestedEntities[0]

    let fnVal = moduleEntity.MembersFunctionsAndValues[0]

    fnVal.Accessibility.IsPublic |> shouldEqual true
    fnVal.Attributes.Count |> shouldEqual 1
    fnVal.CurriedParameterGroups.Count |> shouldEqual 1
    fnVal.CurriedParameterGroups[0].Count |> shouldEqual 2
    fnVal.CurriedParameterGroups[0].[0].Name.IsSome |> shouldEqual true
    fnVal.CurriedParameterGroups[0].[1].Name.IsSome |> shouldEqual true
    fnVal.CurriedParameterGroups[0].[0].Name.Value |> shouldEqual "x"
    fnVal.CurriedParameterGroups[0].[1].Name.Value |> shouldEqual "y"
    fnVal.DeclarationLocation.StartLine |> shouldEqual 3
    fnVal.DisplayName |> shouldEqual "foo"
    fnVal.DeclaringEntity.Value.DisplayName |> shouldEqual "Test"
    fnVal.DeclaringEntity.Value.DeclarationLocation.StartLine |> shouldEqual 1
    fnVal.GenericParameters.Count |> shouldEqual 0
    fnVal.InlineAnnotation |> shouldEqual FSharpInlineAnnotation.OptionalInline
    fnVal.IsActivePattern |> shouldEqual false
    fnVal.IsCompilerGenerated |> shouldEqual false
    fnVal.IsDispatchSlot |> shouldEqual false
    fnVal.IsExtensionMember |> shouldEqual false
    fnVal.IsPropertyGetterMethod |> shouldEqual false
    fnVal.IsImplicitConstructor |> shouldEqual false
    fnVal.IsInstanceMember |> shouldEqual false
    fnVal.IsMember |> shouldEqual false
    fnVal.IsModuleValueOrMember |> shouldEqual true
    fnVal.IsMutable |> shouldEqual false
    fnVal.IsPropertySetterMethod |> shouldEqual false
    fnVal.IsTypeFunction |> shouldEqual false

    fnVal.FullType.IsFunctionType |> shouldEqual true // int * int -> unit
    fnVal.FullType.GenericArguments[0].IsTupleType |> shouldEqual true // int * int
    let argTy1 = fnVal.FullType.GenericArguments[0].GenericArguments[0]

    argTy1.TypeDefinition.DisplayName |> shouldEqual "int" // int

    argTy1.HasTypeDefinition |> shouldEqual true
    argTy1.TypeDefinition.IsFSharpAbbreviation |> shouldEqual true // "int"

    let argTy1b = argTy1.TypeDefinition.AbbreviatedType
    argTy1b.TypeDefinition.Namespace |> shouldEqual (Some "Microsoft.FSharp.Core")
    argTy1b.TypeDefinition.CompiledName |> shouldEqual "int32"

    let argTy1c = argTy1b.TypeDefinition.AbbreviatedType
    argTy1c.TypeDefinition.Namespace |> shouldEqual (Some "System")
    argTy1c.TypeDefinition.CompiledName |> shouldEqual "Int32"

    let typeCheckContext = typeCheckResults2.ProjectContext

    typeCheckContext.GetReferencedAssemblies() |> List.exists (fun s -> s.FileName.Value.Contains(coreLibAssemblyName)) |> shouldEqual true


let input3 =
  """
let date = System.DateTime.Now.ToString().PadRight(25)
  """

[<Fact>]
let ``Expression typing test`` () =

    printfn "------ Expression typing test -----------------"
    // Split the input & define file name
    let inputLines = input3.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input3)
    let identToken = FSharpTokenTag.IDENT

    for msg in typeCheckResults.Diagnostics do
        printfn "***Expression typing test: Unexpected  error: %A" msg.Message

    typeCheckResults.Diagnostics.Length |> shouldEqual 0

    // Get declarations (autocomplete) for a location
    //
    // Getting the declarations at columns 42 to 43 with [], "" for the names and residue
    // gives the results for the string type.
    //
    for col in 42..43 do
        let decls =  typeCheckResults.GetDeclarationListInfo(Some parseResult, 2, inputLines[1], PartialLongName.Empty(col), (fun _ -> []))
        let autoCompleteSet = set [ for item in decls.Items -> item.NameInList ]
        autoCompleteSet |> shouldEqual (set stringMethods)

// The underlying problem is that the parser error recovery doesn't include _any_ information for
// the incomplete member:
//    member x.Test =

[<Fact(Skip = "SKIPPED: see #139")>]
let ``Find function from member 1`` () =
    let input =
      """
type Test() =
    let abc a b c = a + b + c
    member x.Test = """

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input)

    let decls = typeCheckResults.GetDeclarationListInfo(Some parseResult, 4, inputLines[3], PartialLongName.Empty(20), (fun _ -> []))
    let item = decls.Items |> Array.tryFind (fun d -> d.NameInList = "abc")
    decls.Items |> Seq.exists (fun d -> d.NameInList = "abc") |> shouldEqual true

[<Fact>]
let ``Find function from member 2`` () =
    let input =
      """
type Test() =
    let abc a b c = a + b + c
    member x.Test = a"""

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input)

    let decls = typeCheckResults.GetDeclarationListInfo(Some parseResult, 4, inputLines[3], PartialLongName.Empty(21), (fun _ -> []))
    let item = decls.Items |> Array.tryFind (fun d -> d.NameInList = "abc")
    decls.Items |> Seq.exists (fun d -> d.NameInList = "abc") |> shouldEqual true

[<Fact>]
let ``Find function from var`` () =
    let input =
      """
type Test() =
    let abc a b c = a + b + c
    let test = """

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input)

    let decls = typeCheckResults.GetDeclarationListInfo(Some parseResult, 4, inputLines[3], PartialLongName.Empty(14), (fun _ -> []))
    decls.Items |> Seq.exists (fun d -> d.NameInList = "abc") |> shouldEqual true


[<Fact>]
let ``Completion in base constructor`` () =
    let input =
      """
type A(foo) =
    class
    end

type B(bar) =
    inherit A(bar)"""

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input)

    let decls = typeCheckResults.GetDeclarationListInfo(Some parseResult, 7, inputLines[6], PartialLongName.Empty(17), (fun _ -> []))
    decls.Items |> Seq.exists (fun d -> d.NameInList = "bar") |> shouldEqual true



[<Fact>]
let ``Completion in do in base constructor`` () =
    let input =
      """
type A() =
    class
    end

type B(bar) =
    inherit A()

    do bar"""

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input)

    let decls = typeCheckResults.GetDeclarationListInfo(Some parseResult, 9, inputLines[8], PartialLongName.Empty(7), (fun _ -> []))
    decls.Items |> Seq.exists (fun d -> d.NameInList = "bar") |> shouldEqual true


[<Fact(Skip = "SKIPPED: see #139")>]
let ``Symbol based find function from member 1`` () =
    let input =
      """
type Test() =
    let abc a b c = a + b + c
    member x.Test = """

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input)

    let decls = typeCheckResults.GetDeclarationListSymbols(Some parseResult, 4, inputLines[3], PartialLongName.Empty(20), (fun () -> []))
    //decls |> List.map (fun d -> d.Head.Symbol.DisplayName) |> printfn "---> decls = %A"
    decls |> Seq.exists (fun d -> d.Head.Symbol.DisplayName = "abc") |> shouldEqual true

[<Fact>]
let ``Symbol based find function from member 2`` () =
    let input =
      """
type Test() =
    let abc a b c = a + b + c
    member x.Test = a"""

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input)

    let decls = typeCheckResults.GetDeclarationListSymbols(Some parseResult, 4, inputLines[3], PartialLongName.Empty(21), (fun () -> []))
    //decls |> List.map (fun d -> d.Head.Symbol.DisplayName) |> printfn "---> decls = %A"
    decls |> Seq.exists (fun d -> d.Head.Symbol.DisplayName = "abc") |> shouldEqual true

[<Fact>]
let ``Symbol based find function from var`` () =
    let input =
      """
type Test() =
    let abc a b c = a + b + c
    let test = """

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input)

    let decls = typeCheckResults.GetDeclarationListSymbols(Some parseResult, 4, inputLines[3], PartialLongName.Empty(14), (fun () -> []))
    //decls |> List.map (fun d -> d.Head.Symbol.DisplayName) |> printfn "---> decls = %A"
    decls |> Seq.exists (fun d -> d.Head.Symbol.DisplayName = "abc") |> shouldEqual true

[<Fact>]
let ``Printf specifiers for regular and verbatim strings`` () =
    let input =
      """let os = System.Text.StringBuilder()
let _ = Microsoft.FSharp.Core.Printf.printf "%A" 0
let _ = Printf.printf "%A" 0
let _ = Printf.kprintf (fun _ -> ()) "%A" 1
let _ = Printf.bprintf os "%A" 1
let _ = sprintf "%*d" 1
let _ = sprintf "%7.1f" 1.0
let _ = sprintf "%-8.1e+567" 1.0
let _ = sprintf @"%-5s" "value"
let _ = printfn @"%-A" -10
let _ = printf @"
            %-O" -10
let _ = sprintf "

            %-O" -10
let _ = List.map (sprintf @"%A
                           ")
let _ = (10, 12) ||> sprintf "%A
                              %O"
let _ = sprintf "\n%-8.1e+567" 1.0
let _ = sprintf @"%O\n%-5s" "1" "2"
let _ = sprintf "%%"
let _ = sprintf " %*%" 2
let _ = sprintf "  %.*%" 2
let _ = sprintf "   %*.1%" 2
let _ = sprintf "    %*s" 10 "hello"
let _ = sprintf "     %*.*%" 2 3
let _ = sprintf "      %*.*f" 2 3 4.5
let _ = sprintf "       %.*f" 3 4.5
let _ = sprintf "        %*.1f" 3 4.5
let _ = sprintf "         %6.*f" 3 4.5
let _ = sprintf "          %6.*%" 3
let _ =  printf "           %a" (fun _ _ -> ()) 2
let _ =  printf "            %*a" 3 (fun _ _ -> ()) 2
"""

    let file = System.IO.Path.Combine [| "home"; "user"; "Test.fsx" |]
    let parseResult, typeCheckResults = parseAndCheckScript(file, input)

    typeCheckResults.Diagnostics
        |> Array.map (fun d -> d.ErrorNumber, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn, d.Message)
        |> shouldEqual [|
            (3376, 23, 16, 23, 22, "Bad format specifier: '%'")
            (3376, 24, 16, 24, 24, "Bad format specifier: '%'")
            (3376, 25, 16, 25, 26, "Bad format specifier: '%'")
            (3376, 27, 16, 27, 28, "Bad format specifier: '%'")
            (3376, 32, 16, 32, 33, "Bad format specifier: '%'") |]

    typeCheckResults.GetFormatSpecifierLocationsAndArity()
    |> Array.map (fun (range,numArgs) -> range.StartLine, range.StartColumn, range.EndLine, range.EndColumn, numArgs)
    |> shouldEqual
         [|(2, 45, 2, 47, 1); (3, 23, 3, 25, 1); (4, 38, 4, 40, 1); (5, 27, 5, 29, 1);
          (6, 17, 6, 20, 2); (7, 17, 7, 22, 1); (8, 17, 8, 23, 1); (9, 18, 9, 22, 1);
          (10, 18, 10, 21, 1); (12, 12, 12, 15, 1); (15, 12, 15, 15, 1);
          (16, 28, 16, 30, 1); (18, 30, 18, 32, 1); (19, 30, 19, 32, 1);
          (20, 19, 20, 25, 1); (21, 18, 21, 20, 1); (21, 22, 21, 26, 1);
          (22, 17, 22, 19, 0); (23, 18, 23, 21, 1); (24, 19, 24, 23, 1);
          (25, 20, 25, 25, 1); (26, 21, 26, 24, 2); (27, 22, 27, 27, 2);
          (28, 23, 28, 28, 3); (29, 24, 29, 28, 2); (30, 25, 30, 30, 2);
          (31, 26, 31, 31, 2); (32, 27, 32, 32, 1); (33, 28, 33, 30, 2);
          (34, 29, 34, 32, 3)|]

[<Fact>]
let ``Printf specifiers for triple-quote strings`` () =
    let input =
      "
let _ = sprintf \"\"\"%-A\"\"\" -10
let _ = printfn \"\"\"
            %-A
                \"\"\" -10
let _ = List.iter(printfn \"\"\"%-A
                             %i\\n%O
                             \"\"\" 1 2)"

    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input)

    typeCheckResults.Diagnostics |> shouldEqual [||]
    typeCheckResults.GetFormatSpecifierLocationsAndArity()
    |> Array.map (fun (range,numArgs) -> range.StartLine, range.StartColumn, range.EndLine, range.EndColumn, numArgs)
    |> shouldEqual [|(2, 19, 2, 22, 1);
                     (4, 12, 4, 15, 1);
                     (6, 29, 6, 32, 1);
                     (7, 29, 7, 31, 1);
                     (7, 33, 7, 35,1 )|]

[<Fact>]
let ``Printf specifiers for user-defined functions`` () =
    let input =
      """
let debug msg = Printf.kprintf System.Diagnostics.Debug.WriteLine msg
let _ = debug "Message: %i - %O" 1 "Ok"
let _ = debug "[LanguageService] Type checking fails for '%s' with content=%A and %A.\nResulting exception: %A" "1" "2" "3" "4"
"""

    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input)

    typeCheckResults.Diagnostics |> shouldEqual [||]
    typeCheckResults.GetFormatSpecifierLocationsAndArity()
    |> Array.map (fun (range, numArgs) -> range.StartLine, range.StartColumn, range.EndLine, range.EndColumn, numArgs)
    |> shouldEqual [|(3, 24, 3, 26, 1);
                     (3, 29, 3, 31, 1);
                     (4, 58, 4, 60, 1);
                     (4, 75, 4, 77, 1);
                     (4, 82, 4, 84, 1);
                     (4, 108, 4, 110, 1)|]

#if ASSUME_PREVIEW_FSHARP_CORE
[<Fact>]
let ``Printf specifiers for regular and verbatim interpolated strings`` () =
    let input =
      """let os = System.Text.StringBuilder() // line 1
let _ = $"{0}"                                // line 2
let _ = $"%A{0}"                              // line 3
let _ = $"%7.1f{1.0}"                         // line 4
let _ = $"%-8.1e{1.0}+567"                    // line 5
let s = "value"                               // line 6
let _ = $@"%-5s{s}"                           // line 7
let _ = $@"%-A{-10}"                          // line 8
let _ = @$"
            %-O{-10}"                         // line 10
let _ = $"

            %-O{-10}"                         // line 13
let _ = List.map (fun x -> sprintf $@"%A{x}
                                      ")      // line 15
let _ = $"\n%-8.1e{1.0}+567"                  // line 16
let _ = $@"%O{1}\n%-5s{s}"                    // line 17
let _ = $"%%"                                 // line 18
let s2 = $"abc %d{s.Length} and %d{s.Length}def" // line 19
let s3 = $"abc %d{s.Length}
                and %d{s.Length}def"          // line 21
"""

    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScriptWithOptions(file, input, [| "/langversion:preview" |])

    typeCheckResults.Diagnostics |> shouldEqual [||]
    typeCheckResults.GetFormatSpecifierLocationsAndArity()
    |> Array.map (fun (range,numArgs) -> range.StartLine, range.StartColumn, range.EndLine, range.EndColumn, numArgs)
    |> shouldEqual [|
        (3, 10, 3, 12, 1); (4, 10, 4, 15, 1); (5, 10, 5, 16, 1); (7, 11, 7, 15, 1);
        (8, 11, 8, 14, 1); (13, 12, 13, 15, 1); (14, 38, 14, 40, 1);
        (16, 12, 16, 18, 1); (17, 11, 17, 13, 1); (17, 18, 17, 22, 1);
        (18, 10, 18, 12, 0); (19, 15, 19, 17, 1); (19, 32, 19, 34, 1);
        (20, 15, 20, 17, 1); (21, 20, 21, 22, 1)
    |]

[<Fact>]
let ``Printf specifiers for triple quote interpolated strings`` () =
    let input =
      "let _ = $\"\"\"abc %d{1} and %d{2+3}def\"\"\"
let _ = $$\"\"\"abc %%d{{1}} and %%d{{2}}def\"\"\"
let _ = $$$\"\"\"%% %%%d{{{4}}} % %%%d{{{5}}}\"\"\" "

    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScriptWithOptions(file, input, [| "/langversion:preview" |])

    typeCheckResults.Diagnostics |> shouldEqual [||]
    typeCheckResults.GetFormatSpecifierLocationsAndArity()
    |> Array.map (fun (range,numArgs) -> range.StartLine, range.StartColumn, range.EndLine, range.EndColumn, numArgs)
    |> shouldEqual
        [|(1, 16, 1, 18, 1); (1, 26, 1, 28, 1)
          (2, 17, 2, 20, 1); (2, 30, 2, 33, 1)
          (3, 17, 3, 21, 1); (3, 31, 3, 35, 1)|]
#endif // ASSUME_PREVIEW_FSHARP_CORE


[<Fact>]
let ``should not report format specifiers for illformed format strings`` () =
    let input =
      """
let _ = sprintf "%.7f %7.1A %7.f %--8.1f"
let _ = sprintf "ABCDE"
"""

    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input)
    typeCheckResults.GetFormatSpecifierLocationsAndArity()
    |> Array.map (fun (range, numArgs) -> range.StartLine, range.StartColumn, range.EndLine, range.EndColumn, numArgs)
    |> shouldEqual [||]

[<Fact>]
let ``Single case discriminated union type definition`` () =
    let input =
      """
type DU = Case1
"""

    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input)
    typeCheckResults.GetAllUsesOfAllSymbolsInFile()
    |> Array.ofSeq
    |> Array.map (fun su ->
        let r = su.Range
        r.StartLine, r.StartColumn, r.EndLine, r.EndColumn)
    |> shouldEqual [|(2, 10, 2, 15); (2, 5, 2, 7); (1, 0, 1, 0)|]

[<Fact>]
let ``Synthetic symbols should not be reported`` () =
    let input =
      """
let arr = [|1|]
let number1, number2 = 1, 2
let _ = arr.[0..number1]
let _ = arr.[..number2]
"""

    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input)
    typeCheckResults.GetAllUsesOfAllSymbolsInFile()
    |> Array.ofSeq
    |> Array.map (fun su ->
        let r = su.Range
        su.Symbol.ToString(), (r.StartLine, r.StartColumn, r.EndLine, r.EndColumn))
    |> shouldEqual
        [|("val arr", (2, 4, 2, 7))
          ("val number2", (3, 13, 3, 20))
          ("val number1", (3, 4, 3, 11))
          ("val arr", (4, 8, 4, 11))
          ("val number1", (4, 16, 4, 23))
          ("val arr", (5, 8, 5, 11))
          ("val number2", (5, 15, 5, 22))
          ("Test", (1, 0, 1, 0))|]

[<Fact>]
let ``Enums should have fields`` () =
    let input = """
type EnumTest = One = 1 | Two = 2 | Three = 3
let test = EnumTest.One
let test2 = System.StringComparison.CurrentCulture
let test3 = System.Text.RegularExpressions.RegexOptions.Compiled
"""
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input)
    let allSymbols = typeCheckResults.GetAllUsesOfAllSymbolsInFile()
    let enums =
        allSymbols
        |> Array.ofSeq
        |> Array.choose(fun s -> match s.Symbol with :? FSharpEntity as e when e.IsEnum -> Some e | _ -> None)
        |> Array.distinct
        |> Array.map(fun e -> (e.DisplayName, e.FSharpFields
                                              |> Seq.sortBy (fun f -> match f.LiteralValue with None -> -1 | Some x -> unbox x)
                                              |> Seq.map (fun f -> f.Name, f.LiteralValue)
                                              |> Seq.toList))

    enums |> shouldEqual
        [| "EnumTest", [ ("value__", None)
                         ("One", Some (box 1))
                         ("Two", Some (box 2))
                         ("Three", Some (box 3))
                       ]
           "StringComparison", [ ("value__", None)
                                 ("CurrentCulture", Some (box 0))
                                 ("CurrentCultureIgnoreCase", Some (box 1))
                                 ("InvariantCulture", Some (box 2))
                                 ("InvariantCultureIgnoreCase", Some (box 3))
                                 ("Ordinal", Some (box 4))
                                 ("OrdinalIgnoreCase", Some (box 5))
                               ]
           "RegexOptions", [ ("value__", None)
                             ("None", Some (box 0))
                             ("IgnoreCase", Some (box 1))
                             ("Multiline", Some (box 2))
                             ("ExplicitCapture", Some (box 4))
                             ("Compiled", Some (box 8))
                             ("Singleline", Some (box 16))
                             ("IgnorePatternWhitespace", Some (box 32))
                             ("RightToLeft", Some (box 64))
                             ("ECMAScript", Some (box 256))
                             ("CultureInvariant", Some (box 512))
#if NETCOREAPP
                             ("NonBacktracking", Some 1024)
#endif
                           ]
        |]

[<Fact>]
let ``IL enum fields should be reported`` () =
    let input =
      """
open System

let _ =
    match ConsoleKey.Tab with
    | ConsoleKey.OemClear -> ConsoleKey.A
    | _ -> ConsoleKey.B
"""

    let file = "/home/user/Test.fsx"
    let _, typeCheckResults = parseAndCheckScript(file, input)
    typeCheckResults.GetAllUsesOfAllSymbolsInFile()
    |> Array.ofSeq
    |> Array.map (fun su ->
        let r = su.Range
        su.Symbol.ToString(), (r.StartLine, r.StartColumn, r.EndLine, r.EndColumn))
    |> Array.distinct
    |> shouldEqual
        // note: these "System" symbol uses are not duplications because each of them corresponds to different namespaces
        [|("System", (2, 5, 2, 11))
          ("ConsoleKey", (5, 10, 5, 20));
          ("field Tab", (5, 10, 5, 24));
          ("ConsoleKey", (6, 6, 6, 16));
          ("field OemClear", (6, 6, 6, 25));
          ("ConsoleKey", (6, 29, 6, 39));
          ("field A", (6, 29, 6, 41));
          ("ConsoleKey", (7, 11, 7, 21));
          ("field B", (7, 11, 7, 23));
          ("Test", (1, 0, 1, 0))|]

[<Fact>]
let ``Literal values should be reported`` () =
    let input =
      """
module Module1 =
    let [<Literal>] ModuleValue = 1

    let _ =
        match ModuleValue + 1 with
        | ModuleValue -> ModuleValue + 2
        | _ -> 0

type Class1() =
    let [<Literal>] ClassValue = 1
    static let [<Literal>] StaticClassValue = 2

    let _ = ClassValue
    let _ = StaticClassValue

    let _ =
        match ClassValue + StaticClassValue with
        | ClassValue -> ClassValue + 1
        | StaticClassValue -> StaticClassValue + 2
        | _ -> 3
"""

    let file = "/home/user/Test.fsx"
    let _, typeCheckResults = parseAndCheckScript(file, input)
    typeCheckResults.GetAllUsesOfAllSymbolsInFile()
    |> Array.ofSeq
    |> Array.map (fun su ->
        let r = su.Range
        su.Symbol.ToString(), (r.StartLine, r.StartColumn, r.EndLine, r.EndColumn))
    |> shouldEqual
        [|("LiteralAttribute", (3, 10, 3, 17))
          ("member .ctor", (3, 10, 3, 17))
          ("val ModuleValue", (3, 20, 3, 31))
          ("val op_Addition", (6, 26, 6, 27))
          ("val ModuleValue", (6, 14, 6, 25))
          ("val ModuleValue", (7, 10, 7, 21))
          ("val op_Addition", (7, 37, 7, 38))
          ("val ModuleValue", (7, 25, 7, 36))
          ("Module1", (2, 7, 2, 14))
          ("Class1", (10, 5, 10, 11))
          ("member .ctor", (10, 5, 10, 11))
          ("LiteralAttribute", (11, 10, 11, 17))
          ("member .ctor", (11, 10, 11, 17))
          ("val ClassValue", (11, 20, 11, 30))
          ("LiteralAttribute", (12, 17, 12, 24))
          ("member .ctor", (12, 17, 12, 24))
          ("val StaticClassValue", (12, 27, 12, 43))
          ("val ClassValue", (14, 12, 14, 22))
          ("val StaticClassValue", (15, 12, 15, 28))
          ("val op_Addition", (18, 25, 18, 26))
          ("val ClassValue", (18, 14, 18, 24))
          ("val StaticClassValue", (18, 27, 18, 43))
          ("val ClassValue", (19, 10, 19, 20))
          ("val op_Addition", (19, 35, 19, 36))
          ("val ClassValue", (19, 24, 19, 34))
          ("val StaticClassValue", (20, 10, 20, 26))
          ("val op_Addition", (20, 47, 20, 48))
          ("val StaticClassValue", (20, 30, 20, 46))
          ("member .cctor", (10, 5, 10, 11))
          ("Test", (1, 0, 1, 0))|]

[<Fact>]
let ``IsConstructor property should return true for constructors`` () =
    let input =
      """
type T(x: int) =
    new() = T(0)
let x: T()
"""
    let file = "/home/user/Test.fsx"
    let _, typeCheckResults = parseAndCheckScript(file, input)
    typeCheckResults.GetAllUsesOfAllSymbolsInFile()
    |> Array.ofSeq
    |> Array.map (fun su ->
        let r = su.Range
        let isConstructor =
            match su.Symbol with
            | :? FSharpMemberOrFunctionOrValue as f -> f.IsConstructor
            | _ -> false
        su.Symbol.ToString(), (r.StartLine, r.StartColumn, r.EndLine, r.EndColumn), isConstructor)
    |> Array.distinct
    |> shouldEqual
        [|("T", (2, 5, 2, 6), false)
          ("int", (2, 10, 2, 13), false)
          ("val x", (2, 7, 2, 8), false)
          ("member .ctor", (2, 5, 2, 6), true)
          ("member .ctor", (3, 4, 3, 7), true)
          ("member .ctor", (3, 12, 3, 13), true)
          ("T", (4, 7, 4, 8), false)
          ("val x", (4, 4, 4, 5), false)
          ("Test", (1, 0, 1, 0), false)|]

[<Fact>]
let ``ValidateBreakpointLocation tests A`` () =
    let input =
      """
let f x =
    let y = z + 1
    y + y
        )"""
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input)
    let lines = input.Replace("\r", "").Split( [| '\n' |])
    let positions = [ for i,line in Seq.indexed lines do for j, c in Seq.indexed line do yield Position.mkPos (Line.fromZ i) j, line ]
    let results = [ for pos, line in positions do
                        match parseResult.ValidateBreakpointLocation pos with
                        | Some r -> yield ((line, pos.Line, pos.Column), (r.StartLine, r.StartColumn, r.EndLine, r.EndColumn))
                        | None -> ()]
    results |> shouldEqual
          [(("    let y = z + 1", 3, 0), (3, 4, 3, 17));
           (("    let y = z + 1", 3, 1), (3, 4, 4, 9));
           (("    let y = z + 1", 3, 2), (3, 4, 4, 9));
           (("    let y = z + 1", 3, 3), (3, 4, 4, 9));
           (("    let y = z + 1", 3, 4), (3, 4, 3, 17));
           (("    let y = z + 1", 3, 5), (3, 4, 3, 17));
           (("    let y = z + 1", 3, 6), (3, 4, 3, 17));
           (("    let y = z + 1", 3, 7), (3, 4, 3, 17));
           (("    let y = z + 1", 3, 8), (3, 4, 3, 17));
           (("    let y = z + 1", 3, 9), (3, 4, 3, 17));
           (("    let y = z + 1", 3, 10), (3, 4, 3, 17));
           (("    let y = z + 1", 3, 11), (3, 4, 3, 17));
           (("    let y = z + 1", 3, 12), (3, 4, 3, 17));
           (("    let y = z + 1", 3, 13), (3, 4, 3, 17));
           (("    let y = z + 1", 3, 14), (3, 4, 3, 17));
           (("    let y = z + 1", 3, 15), (3, 4, 3, 17));
           (("    let y = z + 1", 3, 16), (3, 4, 3, 17));
           (("    y + y", 4, 0), (4, 4, 4, 9)); (("    y + y", 4, 1), (3, 4, 4, 9));
           (("    y + y", 4, 2), (3, 4, 4, 9)); (("    y + y", 4, 3), (3, 4, 4, 9));
           (("    y + y", 4, 4), (4, 4, 4, 9)); (("    y + y", 4, 5), (4, 4, 4, 9));
           (("    y + y", 4, 6), (4, 4, 4, 9)); (("    y + y", 4, 7), (4, 4, 4, 9));
           (("    y + y", 4, 8), (4, 4, 4, 9))]


[<Fact>]
let ``ValidateBreakpointLocation tests for object expressions`` () =
// fsi.PrintLength <- 1000
    let input =
      """
type IFoo =
    abstract member Foo: int -> int

type FooBase(foo:IFoo) =
    do ()

type FooImpl() =
    inherit FooBase
        (
            {
                new IFoo with
                    member this.Foo x =
                        let y = x * x
                        z
            }
        )"""
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input)
    let lines = input.Replace("\r", "").Split( [| '\n' |])
    let positions = [ for i,line in Seq.indexed lines do for j, c in Seq.indexed line do yield Position.mkPos (Line.fromZ i) j, line ]
    let results = [ for pos, line in positions do
                        match parseResult.ValidateBreakpointLocation pos with
                        | Some r -> yield ((line, pos.Line, pos.Column), (r.StartLine, r.StartColumn, r.EndLine, r.EndColumn))
                        | None -> ()]
    printfn "%A" results
    results |> shouldEqual
          [(("type FooBase(foo:IFoo) =", 5, 0), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 1), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 2), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 3), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 4), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 5), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 6), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 7), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 8), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 9), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 10), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 11), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 12), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 13), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 14), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 15), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 16), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 17), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 18), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 19), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 20), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 21), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 22), (5, 5, 5, 12));
           (("type FooBase(foo:IFoo) =", 5, 23), (5, 5, 5, 12));
           (("    do ()", 6, 0), (6, 4, 6, 9)); (("    do ()", 6, 1), (6, 4, 6, 9));
           (("    do ()", 6, 2), (6, 4, 6, 9)); (("    do ()", 6, 3), (6, 4, 6, 9));
           (("    do ()", 6, 4), (6, 4, 6, 9)); (("    do ()", 6, 5), (6, 4, 6, 9));
           (("    do ()", 6, 6), (6, 4, 6, 9)); (("    do ()", 6, 7), (6, 4, 6, 9));
           (("    do ()", 6, 8), (6, 4, 6, 9));
           (("type FooImpl() =", 8, 0), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 1), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 2), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 3), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 4), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 5), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 6), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 7), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 8), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 9), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 10), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 11), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 12), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 13), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 14), (8, 5, 8, 12));
           (("type FooImpl() =", 8, 15), (8, 5, 8, 12));
           (("    inherit FooBase", 9, 0), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 1), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 2), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 3), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 4), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 5), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 6), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 7), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 8), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 9), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 10), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 11), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 12), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 13), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 14), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 15), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 16), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 17), (9, 4, 17, 9));
           (("    inherit FooBase", 9, 18), (9, 4, 17, 9));
           (("        (", 10, 0), (9, 4, 17, 9));
           (("        (", 10, 1), (9, 4, 17, 9));
           (("        (", 10, 2), (9, 4, 17, 9));
           (("        (", 10, 3), (9, 4, 17, 9));
           (("        (", 10, 4), (9, 4, 17, 9));
           (("        (", 10, 5), (9, 4, 17, 9));
           (("        (", 10, 6), (9, 4, 17, 9));
           (("        (", 10, 7), (9, 4, 17, 9));
           (("        (", 10, 8), (10, 8, 17, 9));
           (("            {", 11, 0), (10, 8, 17, 9));
           (("            {", 11, 1), (10, 8, 17, 9));
           (("            {", 11, 2), (10, 8, 17, 9));
           (("            {", 11, 3), (10, 8, 17, 9));
           (("            {", 11, 4), (10, 8, 17, 9));
           (("            {", 11, 5), (10, 8, 17, 9));
           (("            {", 11, 6), (10, 8, 17, 9));
           (("            {", 11, 7), (10, 8, 17, 9));
           (("            {", 11, 8), (10, 8, 17, 9));
           (("            {", 11, 9), (10, 8, 17, 9));
           (("            {", 11, 10), (10, 8, 17, 9));
           (("            {", 11, 11), (10, 8, 17, 9));
           (("            {", 11, 12), (10, 8, 17, 9));
           (("                new IFoo with", 12, 0), (10, 8, 17, 9));
           (("                new IFoo with", 12, 1), (10, 8, 17, 9));
           (("                new IFoo with", 12, 2), (10, 8, 17, 9));
           (("                new IFoo with", 12, 3), (10, 8, 17, 9));
           (("                new IFoo with", 12, 4), (10, 8, 17, 9));
           (("                new IFoo with", 12, 5), (10, 8, 17, 9));
           (("                new IFoo with", 12, 6), (10, 8, 17, 9));
           (("                new IFoo with", 12, 7), (10, 8, 17, 9));
           (("                new IFoo with", 12, 8), (10, 8, 17, 9));
           (("                new IFoo with", 12, 9), (10, 8, 17, 9));
           (("                new IFoo with", 12, 10), (10, 8, 17, 9));
           (("                new IFoo with", 12, 11), (10, 8, 17, 9));
           (("                new IFoo with", 12, 12), (10, 8, 17, 9));
           (("                new IFoo with", 12, 13), (10, 8, 17, 9));
           (("                new IFoo with", 12, 14), (10, 8, 17, 9));
           (("                new IFoo with", 12, 15), (10, 8, 17, 9));
           (("                new IFoo with", 12, 16), (10, 8, 17, 9));
           (("                new IFoo with", 12, 17), (10, 8, 17, 9));
           (("                new IFoo with", 12, 18), (10, 8, 17, 9));
           (("                new IFoo with", 12, 19), (10, 8, 17, 9));
           (("                new IFoo with", 12, 20), (10, 8, 17, 9));
           (("                new IFoo with", 12, 21), (10, 8, 17, 9));
           (("                new IFoo with", 12, 22), (10, 8, 17, 9));
           (("                new IFoo with", 12, 23), (10, 8, 17, 9));
           (("                new IFoo with", 12, 24), (10, 8, 17, 9));
           (("                new IFoo with", 12, 25), (10, 8, 17, 9));
           (("                new IFoo with", 12, 26), (10, 8, 17, 9));
           (("                new IFoo with", 12, 27), (10, 8, 17, 9));
           (("                new IFoo with", 12, 28), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 0), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 1), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 2), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 3), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 4), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 5), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 6), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 7), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 8), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 9), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 10), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 11), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 12), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 13), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 14), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 15), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 16), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 17), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 18), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 19), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 20), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 21), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 22), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 23), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 24), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 25), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 26), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 27), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 28), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 29), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 30), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 31), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 32), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 33), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 34), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 35), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 36), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 37), (10, 8, 17, 9));
           (("                    member this.Foo x =", 13, 38), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 0), (14, 24, 14, 37));
           (("                        let y = x * x", 14, 1), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 2), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 3), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 4), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 5), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 6), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 7), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 8), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 9), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 10), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 11), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 12), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 13), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 14), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 15), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 16), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 17), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 18), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 19), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 20), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 21), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 22), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 23), (10, 8, 17, 9));
           (("                        let y = x * x", 14, 24), (14, 24, 14, 37));
           (("                        let y = x * x", 14, 25), (14, 24, 14, 37));
           (("                        let y = x * x", 14, 26), (14, 24, 14, 37));
           (("                        let y = x * x", 14, 27), (14, 24, 14, 37));
           (("                        let y = x * x", 14, 28), (14, 24, 14, 37));
           (("                        let y = x * x", 14, 29), (14, 24, 14, 37));
           (("                        let y = x * x", 14, 30), (14, 24, 14, 37));
           (("                        let y = x * x", 14, 31), (14, 24, 14, 37));
           (("                        let y = x * x", 14, 32), (14, 24, 14, 37));
           (("                        let y = x * x", 14, 33), (14, 24, 14, 37));
           (("                        let y = x * x", 14, 34), (14, 24, 14, 37));
           (("                        let y = x * x", 14, 35), (14, 24, 14, 37));
           (("                        let y = x * x", 14, 36), (14, 24, 14, 37));
           (("                        z", 15, 0), (15, 24, 15, 25));
           (("                        z", 15, 1), (10, 8, 17, 9));
           (("                        z", 15, 2), (10, 8, 17, 9));
           (("                        z", 15, 3), (10, 8, 17, 9));
           (("                        z", 15, 4), (10, 8, 17, 9));
           (("                        z", 15, 5), (10, 8, 17, 9));
           (("                        z", 15, 6), (10, 8, 17, 9));
           (("                        z", 15, 7), (10, 8, 17, 9));
           (("                        z", 15, 8), (10, 8, 17, 9));
           (("                        z", 15, 9), (10, 8, 17, 9));
           (("                        z", 15, 10), (10, 8, 17, 9));
           (("                        z", 15, 11), (10, 8, 17, 9));
           (("                        z", 15, 12), (10, 8, 17, 9));
           (("                        z", 15, 13), (10, 8, 17, 9));
           (("                        z", 15, 14), (10, 8, 17, 9));
           (("                        z", 15, 15), (10, 8, 17, 9));
           (("                        z", 15, 16), (10, 8, 17, 9));
           (("                        z", 15, 17), (10, 8, 17, 9));
           (("                        z", 15, 18), (10, 8, 17, 9));
           (("                        z", 15, 19), (10, 8, 17, 9));
           (("                        z", 15, 20), (10, 8, 17, 9));
           (("                        z", 15, 21), (10, 8, 17, 9));
           (("                        z", 15, 22), (10, 8, 17, 9));
           (("                        z", 15, 23), (10, 8, 17, 9));
           (("                        z", 15, 24), (15, 24, 15, 25));
           (("            }", 16, 0), (10, 8, 17, 9));
           (("            }", 16, 1), (10, 8, 17, 9));
           (("            }", 16, 2), (10, 8, 17, 9));
           (("            }", 16, 3), (10, 8, 17, 9));
           (("            }", 16, 4), (10, 8, 17, 9));
           (("            }", 16, 5), (10, 8, 17, 9));
           (("            }", 16, 6), (10, 8, 17, 9));
           (("            }", 16, 7), (10, 8, 17, 9));
           (("            }", 16, 8), (10, 8, 17, 9));
           (("            }", 16, 9), (10, 8, 17, 9));
           (("            }", 16, 10), (10, 8, 17, 9));
           (("            }", 16, 11), (10, 8, 17, 9));
           (("            }", 16, 12), (10, 8, 17, 9));
           (("        )", 17, 0), (10, 8, 17, 9));
           (("        )", 17, 1), (10, 8, 17, 9));
           (("        )", 17, 2), (10, 8, 17, 9));
           (("        )", 17, 3), (10, 8, 17, 9));
           (("        )", 17, 4), (10, 8, 17, 9));
           (("        )", 17, 5), (10, 8, 17, 9));
           (("        )", 17, 6), (10, 8, 17, 9));
           (("        )", 17, 7), (10, 8, 17, 9));
           (("        )", 17, 8), (10, 8, 17, 9))]

let getBreakpointLocations (input: string) (parseResult: FSharpParseFileResults) =
    let lines = input.Replace("\r", "").Split( [| '\n' |])
    let positions = [ for i,line in Seq.indexed lines do for j, c in Seq.indexed line do yield Position.mkPos (Line.fromZ i) j, line ]
    [ for pos, line in positions do
        match parseResult.ValidateBreakpointLocation pos with
        | Some r -> 
            let text = 
                [ if r.StartLine = r.EndLine then
                      lines[r.StartLine-1][r.StartColumn..r.EndColumn-1]
                  else
                      lines[r.StartLine-1][r.StartColumn..]
                      for l in r.StartLine..r.EndLine-2 do 
                            lines[l]
                      lines[r.EndLine-1][..r.EndColumn-1] ]
                |> String.concat "$"
            ((pos.Line, pos.Column), (r.StartLine, r.StartColumn, r.EndLine, r.EndColumn, text))
        | None -> 
            ()]

[<Fact>]
let ``ValidateBreakpointLocation tests for pipe`` () =
    let input =
      """
let f () =
    [2]
    |> List.map (fun b -> b+1)
    |> List.map (fun b -> b+1)"""
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input)
    let results = getBreakpointLocations input parseResult
    printfn "%A" results
    results |> shouldEqual
        [((3, 0), (3, 4, 3, 7, "[2]")); ((3, 1), (3, 4, 3, 7, "[2]"));
         ((3, 2), (3, 4, 3, 7, "[2]")); ((3, 3), (3, 4, 3, 7, "[2]"));
         ((3, 4), (3, 4, 3, 7, "[2]")); ((3, 5), (3, 4, 3, 7, "[2]"));
         ((3, 6), (3, 4, 3, 7, "[2]"));
         ((4, 0), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 1), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 2), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 3), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 4), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 5), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 6), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 7), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 8), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 9), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 10), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 11), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 12), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 13), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 14), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 15), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 16), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 17), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 18), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 19), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 20), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 21), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 22), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 23), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 24), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 25), (4, 7, 4, 30, "List.map (fun b -> b+1)"));
         ((4, 26), (4, 26, 4, 29, "b+1")); ((4, 27), (4, 26, 4, 29, "b+1"));
         ((4, 28), (4, 26, 4, 29, "b+1")); ((4, 29), (4, 26, 4, 29, "b+1"));
         ((5, 0), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 1), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 2), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 3), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 4), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 5), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 6), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 7), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 8), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 9), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 10), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 11), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 12), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 13), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 14), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 15), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 16), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 17), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 18), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 19), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 20), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 21), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 22), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 23), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 24), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 25), (5, 7, 5, 30, "List.map (fun b -> b+1)"));
         ((5, 26), (5, 26, 5, 29, "b+1")); ((5, 27), (5, 26, 5, 29, "b+1"));
         ((5, 28), (5, 26, 5, 29, "b+1")); ((5, 29), (5, 26, 5, 29, "b+1"))]

[<Fact>]
let ``ValidateBreakpointLocation tests for pipe2`` () =
    let input =
      """
let f () =
    ([1],[2]) 
    ||> List.zip
    |> List.map (fun (b,c) -> (c,b))
    |> List.unzip"""
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input)
    let results = getBreakpointLocations input parseResult
    printfn "%A" results
    results |> shouldEqual 
        [((3, 0), (3, 5, 3, 8, "[1]")); ((3, 1), (3, 5, 3, 8, "[1]"));
         ((3, 2), (3, 5, 3, 8, "[1]")); ((3, 3), (3, 5, 3, 8, "[1]"));
         ((3, 4), (3, 5, 3, 8, "[1]")); ((3, 5), (3, 5, 3, 8, "[1]"));
         ((3, 6), (3, 5, 3, 8, "[1]")); ((3, 7), (3, 5, 3, 8, "[1]"));
         ((3, 8), (3, 5, 3, 8, "[1]")); ((3, 9), (3, 9, 3, 12, "[2]"));
         ((3, 10), (3, 9, 3, 12, "[2]")); ((3, 11), (3, 9, 3, 12, "[2]"));
         ((3, 12), (3, 9, 3, 12, "[2]")); ((3, 13), (3, 5, 3, 8, "[1]"));
         ((4, 0), (4, 8, 4, 16, "List.zip")); ((4, 1), (4, 8, 4, 16, "List.zip"));
         ((4, 2), (4, 8, 4, 16, "List.zip")); ((4, 3), (4, 8, 4, 16, "List.zip"));
         ((4, 4), (4, 8, 4, 16, "List.zip")); ((4, 5), (4, 8, 4, 16, "List.zip"));
         ((4, 6), (4, 8, 4, 16, "List.zip")); ((4, 7), (4, 8, 4, 16, "List.zip"));
         ((4, 8), (4, 8, 4, 16, "List.zip")); ((4, 9), (4, 8, 4, 16, "List.zip"));
         ((4, 10), (4, 8, 4, 16, "List.zip")); ((4, 11), (4, 8, 4, 16, "List.zip"));
         ((4, 12), (4, 8, 4, 16, "List.zip")); ((4, 13), (4, 8, 4, 16, "List.zip"));
         ((4, 14), (4, 8, 4, 16, "List.zip")); ((4, 15), (4, 8, 4, 16, "List.zip"));
         ((5, 0), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 1), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 2), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 3), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 4), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 5), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 6), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 7), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 8), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 9), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 10), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 11), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 12), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 13), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 14), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 15), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 16), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 17), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 18), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 19), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 20), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 21), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 22), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 23), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 24), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 25), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 26), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 27), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 28), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 29), (5, 7, 5, 36, "List.map (fun (b,c) -> (c,b))"));
         ((5, 30), (5, 30, 5, 35, "(c,b)")); ((5, 31), (5, 30, 5, 35, "(c,b)"));
         ((5, 32), (5, 30, 5, 35, "(c,b)")); ((5, 33), (5, 30, 5, 35, "(c,b)"));
         ((5, 34), (5, 30, 5, 35, "(c,b)")); ((5, 35), (5, 30, 5, 35, "(c,b)"));
         ((6, 0), (6, 7, 6, 17, "List.unzip")); ((6, 1), (6, 7, 6, 17, "List.unzip"));
         ((6, 2), (6, 7, 6, 17, "List.unzip")); ((6, 3), (6, 7, 6, 17, "List.unzip"));
         ((6, 4), (6, 7, 6, 17, "List.unzip")); ((6, 5), (6, 7, 6, 17, "List.unzip"));
         ((6, 6), (6, 7, 6, 17, "List.unzip")); ((6, 7), (6, 7, 6, 17, "List.unzip"));
         ((6, 8), (6, 7, 6, 17, "List.unzip")); ((6, 9), (6, 7, 6, 17, "List.unzip"));
         ((6, 10), (6, 7, 6, 17, "List.unzip")); ((6, 11), (6, 7, 6, 17, "List.unzip"));
         ((6, 12), (6, 7, 6, 17, "List.unzip")); ((6, 13), (6, 7, 6, 17, "List.unzip"));
         ((6, 14), (6, 7, 6, 17, "List.unzip")); ((6, 15), (6, 7, 6, 17, "List.unzip"));
         ((6, 16), (6, 7, 6, 17, "List.unzip"))]

    
[<Fact>]
let ``ValidateBreakpointLocation tests for pipe3`` () =
    let input =
      """
let f () =
    ([1],[2],[3]) 
    |||> List.zip3
    |> List.map (fun (a,b,c) -> (c,b,a))
    |> List.unzip3"""
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input)
    let results = getBreakpointLocations input parseResult
    printfn "%A" results
    results |> shouldEqual 
        [((3, 0), (3, 5, 3, 8, "[1]")); ((3, 1), (3, 5, 3, 8, "[1]"));
         ((3, 2), (3, 5, 3, 8, "[1]")); ((3, 3), (3, 5, 3, 8, "[1]"));
         ((3, 4), (3, 5, 3, 8, "[1]")); ((3, 5), (3, 5, 3, 8, "[1]"));
         ((3, 6), (3, 5, 3, 8, "[1]")); ((3, 7), (3, 5, 3, 8, "[1]"));
         ((3, 8), (3, 5, 3, 8, "[1]")); ((3, 9), (3, 9, 3, 12, "[2]"));
         ((3, 10), (3, 9, 3, 12, "[2]")); ((3, 11), (3, 9, 3, 12, "[2]"));
         ((3, 12), (3, 9, 3, 12, "[2]")); ((3, 13), (3, 13, 3, 16, "[3]"));
         ((3, 14), (3, 13, 3, 16, "[3]")); ((3, 15), (3, 13, 3, 16, "[3]"));
         ((3, 16), (3, 13, 3, 16, "[3]")); ((3, 17), (3, 5, 3, 8, "[1]"));
         ((4, 0), (4, 9, 4, 18, "List.zip3")); ((4, 1), (4, 9, 4, 18, "List.zip3"));
         ((4, 2), (4, 9, 4, 18, "List.zip3")); ((4, 3), (4, 9, 4, 18, "List.zip3"));
         ((4, 4), (4, 9, 4, 18, "List.zip3")); ((4, 5), (4, 9, 4, 18, "List.zip3"));
         ((4, 6), (4, 9, 4, 18, "List.zip3")); ((4, 7), (4, 9, 4, 18, "List.zip3"));
         ((4, 8), (4, 9, 4, 18, "List.zip3")); ((4, 9), (4, 9, 4, 18, "List.zip3"));
         ((4, 10), (4, 9, 4, 18, "List.zip3")); ((4, 11), (4, 9, 4, 18, "List.zip3"));
         ((4, 12), (4, 9, 4, 18, "List.zip3")); ((4, 13), (4, 9, 4, 18, "List.zip3"));
         ((4, 14), (4, 9, 4, 18, "List.zip3")); ((4, 15), (4, 9, 4, 18, "List.zip3"));
         ((4, 16), (4, 9, 4, 18, "List.zip3")); ((4, 17), (4, 9, 4, 18, "List.zip3"));
         ((5, 0), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 1), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 2), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 3), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 4), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 5), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 6), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 7), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 8), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 9), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 10), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 11), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 12), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 13), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 14), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 15), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 16), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 17), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 18), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 19), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 20), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 21), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 22), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 23), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 24), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 25), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 26), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 27), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 28), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 29), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 30), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 31), (5, 7, 5, 40, "List.map (fun (a,b,c) -> (c,b,a))"));
         ((5, 32), (5, 32, 5, 39, "(c,b,a)")); ((5, 33), (5, 32, 5, 39, "(c,b,a)"));
         ((5, 34), (5, 32, 5, 39, "(c,b,a)")); ((5, 35), (5, 32, 5, 39, "(c,b,a)"));
         ((5, 36), (5, 32, 5, 39, "(c,b,a)")); ((5, 37), (5, 32, 5, 39, "(c,b,a)"));
         ((5, 38), (5, 32, 5, 39, "(c,b,a)")); ((5, 39), (5, 32, 5, 39, "(c,b,a)"));
         ((6, 0), (6, 7, 6, 18, "List.unzip3")); ((6, 1), (6, 7, 6, 18, "List.unzip3"));
         ((6, 2), (6, 7, 6, 18, "List.unzip3")); ((6, 3), (6, 7, 6, 18, "List.unzip3"));
         ((6, 4), (6, 7, 6, 18, "List.unzip3")); ((6, 5), (6, 7, 6, 18, "List.unzip3"));
         ((6, 6), (6, 7, 6, 18, "List.unzip3")); ((6, 7), (6, 7, 6, 18, "List.unzip3"));
         ((6, 8), (6, 7, 6, 18, "List.unzip3")); ((6, 9), (6, 7, 6, 18, "List.unzip3"));
         ((6, 10), (6, 7, 6, 18, "List.unzip3"));
         ((6, 11), (6, 7, 6, 18, "List.unzip3"));
         ((6, 12), (6, 7, 6, 18, "List.unzip3"));
         ((6, 13), (6, 7, 6, 18, "List.unzip3"));
         ((6, 14), (6, 7, 6, 18, "List.unzip3"));
         ((6, 15), (6, 7, 6, 18, "List.unzip3"));
         ((6, 16), (6, 7, 6, 18, "List.unzip3"));
         ((6, 17), (6, 7, 6, 18, "List.unzip3"))]

[<Fact>]
let ``ValidateBreakpointLocation tests for lambda with pattern arg`` () =
    let input =
      """
let bodyWrapper () =
   id (fun (A(b,c)) ->
        let x = 1
        x)"""
    let file = "/home/user/Test.fsx"
    let parseResult, _typeCheckResults = parseAndCheckScript(file, input)
    let results = getBreakpointLocations input parseResult
    printfn "%A" results
    // The majority of the breakpoints here get the entire expression, except the start-of-line ones
    // on line 4 and 5, and the ones actually on the interior text of the lambda.
    //
    // This is correct
    results |> shouldEqual 
        [((3, 0), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 1), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 2), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 3), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 4), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 5), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 6), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 7), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 8), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 9), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 10), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 11), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 12), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 13), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 14), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 15), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 16), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 17), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 18), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 19), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 20), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((3, 21), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((4, 0), (4, 8, 4, 17, "let x = 1"));
         ((4, 1), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((4, 2), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((4, 3), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((4, 4), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((4, 5), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((4, 6), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((4, 7), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((4, 8), (4, 8, 4, 17, "let x = 1")); ((4, 9), (4, 8, 4, 17, "let x = 1"));
         ((4, 10), (4, 8, 4, 17, "let x = 1")); ((4, 11), (4, 8, 4, 17, "let x = 1"));
         ((4, 12), (4, 8, 4, 17, "let x = 1")); ((4, 13), (4, 8, 4, 17, "let x = 1"));
         ((4, 14), (4, 8, 4, 17, "let x = 1")); ((4, 15), (4, 8, 4, 17, "let x = 1"));
         ((4, 16), (4, 8, 4, 17, "let x = 1")); ((5, 0), (5, 8, 5, 9, "x"));
         ((5, 1), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((5, 2), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((5, 3), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((5, 4), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((5, 5), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((5, 6), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((5, 7), (3, 3, 5, 10, "id (fun (A(b,c)) ->$        let x = 1$        x)"));
         ((5, 8), (5, 8, 5, 9, "x")); ((5, 9), (5, 8, 5, 9, "x"))]

[<Fact>]
let ``ValidateBreakpointLocation tests for boolean logic`` () =
    let input =
      """
let bodyWrapper (a, b, c) = a || b && c"""
    let file = "/home/user/Test.fsx"
    let parseResult, _typeCheckResults = parseAndCheckScript(file, input)
    let results = getBreakpointLocations input parseResult
    printfn "%A" results
    // The majority of the breakpoints here get the entire expression, except the start-of-line ones
    // on line 4 and 5, and the ones actually on the interior text of the lambda.
    //
    // This is correct
    results |> shouldEqual 
        [((2, 0), (2, 28, 2, 29, "a")); ((2, 1), (2, 28, 2, 29, "a"));
         ((2, 2), (2, 28, 2, 29, "a")); ((2, 3), (2, 28, 2, 29, "a"));
         ((2, 4), (2, 28, 2, 29, "a")); ((2, 5), (2, 28, 2, 29, "a"));
         ((2, 6), (2, 28, 2, 29, "a")); ((2, 7), (2, 28, 2, 29, "a"));
         ((2, 8), (2, 28, 2, 29, "a")); ((2, 9), (2, 28, 2, 29, "a"));
         ((2, 10), (2, 28, 2, 29, "a")); ((2, 11), (2, 28, 2, 29, "a"));
         ((2, 12), (2, 28, 2, 29, "a")); ((2, 13), (2, 28, 2, 29, "a"));
         ((2, 14), (2, 28, 2, 29, "a")); ((2, 15), (2, 28, 2, 29, "a"));
         ((2, 16), (2, 28, 2, 29, "a")); ((2, 17), (2, 28, 2, 29, "a"));
         ((2, 18), (2, 28, 2, 29, "a")); ((2, 19), (2, 28, 2, 29, "a"));
         ((2, 20), (2, 28, 2, 29, "a")); ((2, 21), (2, 28, 2, 29, "a"));
         ((2, 22), (2, 28, 2, 29, "a")); ((2, 23), (2, 28, 2, 29, "a"));
         ((2, 24), (2, 28, 2, 29, "a")); ((2, 25), (2, 28, 2, 29, "a"));
         ((2, 26), (2, 28, 2, 29, "a")); ((2, 27), (2, 28, 2, 29, "a"));
         ((2, 28), (2, 28, 2, 29, "a")); ((2, 29), (2, 28, 2, 29, "a"));
         ((2, 30), (2, 33, 2, 34, "b")); ((2, 31), (2, 33, 2, 34, "b"));
         ((2, 32), (2, 33, 2, 34, "b")); ((2, 33), (2, 33, 2, 34, "b"));
         ((2, 34), (2, 33, 2, 34, "b")); ((2, 35), (2, 38, 2, 39, "c"));
         ((2, 36), (2, 38, 2, 39, "c")); ((2, 37), (2, 38, 2, 39, "c"));
         ((2, 38), (2, 38, 2, 39, "c"))]

[<Fact>]
let ``ValidateBreakpointLocation tests for side-effect expression`` () =
    let input =
      """
let print() = ()
print()
do print()
type C() =
    do print()
module M =
    print()
"""
    let file = "/home/user/Test.fsx"
    let parseResult, _typeCheckResults = parseAndCheckScript(file, input)
    let results = getBreakpointLocations input parseResult
    printfn "%A" results
    // The majority of the breakpoints here get the entire expression, except the start-of-line ones
    // on line 4 and 5, and the ones actually on the interior text of the lambda.
    //
    // This is correct
    results |> shouldEqual 
            [((2, 0), (2, 14, 2, 16, "()")); ((2, 1), (2, 14, 2, 16, "()"));
             ((2, 2), (2, 14, 2, 16, "()")); ((2, 3), (2, 14, 2, 16, "()"));
             ((2, 4), (2, 14, 2, 16, "()")); ((2, 5), (2, 14, 2, 16, "()"));
             ((2, 6), (2, 14, 2, 16, "()")); ((2, 7), (2, 14, 2, 16, "()"));
             ((2, 8), (2, 14, 2, 16, "()")); ((2, 9), (2, 14, 2, 16, "()"));
             ((2, 10), (2, 14, 2, 16, "()")); ((2, 11), (2, 14, 2, 16, "()"));
             ((2, 12), (2, 14, 2, 16, "()")); ((2, 13), (2, 14, 2, 16, "()"));
             ((2, 14), (2, 14, 2, 16, "()")); ((2, 15), (2, 14, 2, 16, "()"));
             ((3, 0), (3, 0, 3, 7, "print()")); ((3, 1), (3, 0, 3, 7, "print()"));
             ((3, 2), (3, 0, 3, 7, "print()")); ((3, 3), (3, 0, 3, 7, "print()"));
             ((3, 4), (3, 0, 3, 7, "print()")); ((3, 5), (3, 0, 3, 7, "print()"));
             ((3, 6), (3, 0, 3, 7, "print()")); ((4, 0), (4, 0, 4, 10, "do print()"));
             ((4, 1), (4, 0, 4, 10, "do print()")); ((4, 2), (4, 0, 4, 10, "do print()"));
             ((4, 3), (4, 0, 4, 10, "do print()")); ((4, 4), (4, 0, 4, 10, "do print()"));
             ((4, 5), (4, 0, 4, 10, "do print()")); ((4, 6), (4, 0, 4, 10, "do print()"));
             ((4, 7), (4, 0, 4, 10, "do print()")); ((4, 8), (4, 0, 4, 10, "do print()"));
             ((4, 9), (4, 0, 4, 10, "do print()")); ((5, 0), (5, 5, 5, 6, "C"));
             ((5, 1), (5, 5, 5, 6, "C")); ((5, 2), (5, 5, 5, 6, "C"));
             ((5, 3), (5, 5, 5, 6, "C")); ((5, 4), (5, 5, 5, 6, "C"));
             ((5, 5), (5, 5, 5, 6, "C")); ((5, 6), (5, 5, 5, 6, "C"));
             ((5, 7), (5, 5, 5, 6, "C")); ((5, 8), (5, 5, 5, 6, "C"));
             ((5, 9), (5, 5, 5, 6, "C")); ((6, 0), (6, 4, 6, 14, "do print()"));
             ((6, 1), (6, 4, 6, 14, "do print()")); ((6, 2), (6, 4, 6, 14, "do print()"));
             ((6, 3), (6, 4, 6, 14, "do print()")); ((6, 4), (6, 4, 6, 14, "do print()"));
             ((6, 5), (6, 4, 6, 14, "do print()")); ((6, 6), (6, 4, 6, 14, "do print()"));
             ((6, 7), (6, 4, 6, 14, "do print()")); ((6, 8), (6, 4, 6, 14, "do print()"));
             ((6, 9), (6, 4, 6, 14, "do print()")); ((6, 10), (6, 4, 6, 14, "do print()"));
             ((6, 11), (6, 4, 6, 14, "do print()")); ((6, 12), (6, 4, 6, 14, "do print()"));
             ((6, 13), (6, 4, 6, 14, "do print()")); ((8, 0), (8, 4, 8, 11, "print()"));
             ((8, 1), (8, 4, 8, 11, "print()")); ((8, 2), (8, 4, 8, 11, "print()"));
             ((8, 3), (8, 4, 8, 11, "print()")); ((8, 4), (8, 4, 8, 11, "print()"));
             ((8, 5), (8, 4, 8, 11, "print()")); ((8, 6), (8, 4, 8, 11, "print()"));
             ((8, 7), (8, 4, 8, 11, "print()")); ((8, 8), (8, 4, 8, 11, "print()"));
             ((8, 9), (8, 4, 8, 11, "print()")); ((8, 10), (8, 4, 8, 11, "print()"))]


[<Fact>]
let ``Partially valid namespaces should be reported`` () =
    let input =
      """
open System.Threading.Foo
open System

let _: System.Threading.Tasks.Bar = null
let _ = Threading.Buzz = null
"""

    let file = "/home/user/Test.fsx"
    let _, typeCheckResults = parseAndCheckScript(file, input)
    typeCheckResults.GetAllUsesOfAllSymbolsInFile()
    |> Array.ofSeq
    |> Array.map (fun su ->
        let r = su.Range
        su.Symbol.ToString(), (r.StartLine, r.StartColumn, r.EndLine, r.EndColumn))
    |> Array.distinct
    |> shouldEqual
        // note: these "System" and "Threading" symbol uses are not duplications because each of them corresponds to different namespaces
        [|("System", (2, 5, 2, 11))
          ("Threading", (2, 12, 2, 21))
          ("System", (3, 5, 3, 11))
          ("System", (5, 7, 5, 13))
          ("Threading", (5, 14, 5, 23))
          ("Tasks", (5, 24, 5, 29))
          ("val op_Equality", (6, 23, 6, 24))
          ("Test", (1, 0, 1, 0))|]

[<Fact>]
let ``GetDeclarationLocation should not require physical file`` () =
    let input = "let abc = 1\nlet xyz = abc"
    let file = "/home/user/Test.fsx"
    let _, typeCheckResults = parseAndCheckScript(file, input)
    let location = typeCheckResults.GetDeclarationLocation(2, 13, "let xyz = abc", ["abc"])
    match location with
    | FindDeclResult.DeclFound r -> Some (r.StartLine, r.StartColumn, r.EndLine, r.EndColumn, "<=== Found here."                             )
    | _                                -> Some (0          , 0            , 0        , 0          , "Not Found. Should not require physical file." )
    |> shouldEqual                       (Some (1          , 4            , 1        , 7          , "<=== Found here."                             ))


//-------------------------------------------------------------------------------

#if TEST_TP_PROJECTS
module internal TPProject =
    open System.IO

    let fileName1 = Path.ChangeExtension(tryCreateTemporaryFileName (), ".fs")
    let base2 = tryCreateTemporaryFileName ()
    let dllName = Path.ChangeExtension(base2, ".dll")
    let projFileName = Path.ChangeExtension(base2, ".fsproj")
    let fileSource1 = """
module M
open Samples.FSharp.RegexTypeProvider
[<Literal>]
let REGEX = "ABC"
let _ = RegexTypedStatic.IsMatch  // TEST: intellisense when typing "<"
let _ = RegexTypedStatic.IsMatch<REGEX>( ) // TEST: param info on "("
let _ = RegexTypedStatic.IsMatch<"ABC" >( ) // TEST: param info on "("
let _ = RegexTypedStatic.IsMatch<"ABC" >( (*$*) ) // TEST: meth info on ctrl-alt-space at $
let _ = RegexTypedStatic.IsMatch<"ABC" >( null (*$*) ) // TEST: param info on "," at $
let _ = RegexTypedStatic.IsMatch< > // TEST: intellisense when typing "<"
let _ = RegexTypedStatic.IsMatch< (*$*) > // TEST: param info when typing ctrl-alt-space at $
let _ = RegexTypedStatic.IsMatch<"ABC" (*$*) > // TEST: param info on Ctrl-alt-space at $
let _ = RegexTypedStatic.IsMatch<"ABC" (*$*) >(  ) // TEST: param info on Ctrl-alt-space at $
let _ = RegexTypedStatic.IsMatch<"ABC", (*$ *) >(  ) // TEST: param info on Ctrl-alt-space at $
let _ = RegexTypedStatic.IsMatch<"ABC" >(  (*$*) ) // TEST: no assert on Ctrl-space at $
    """

    FileSystem.OpenFileForWriteShim(fileName1).Write(fileSource1)
    let fileLines1 = FileSystem.OpenFileForReadShim(fileName1).AsStream().ReadLines()

    let fileNames = [fileName1]
    let args = Array.append (mkProjectCommandLineArgs (dllName, fileNames)) [| "-r:" + PathRelativeToTestAssembly(@"DummyProviderForLanguageServiceTesting.dll") |]
    let options =  checker.GetProjectOptionsFromCommandLineArgs (projFileName, args)
    let cleanFileName a = if a = fileName1 then "file1" else "??"

[<Fact>]
let ``Test TPProject all symbols`` () =

    let wholeProjectResults = checker.ParseAndCheckProject(TPProject.options) |> Async.RunImmediate
    let allSymbolUses = wholeProjectResults.GetAllUsesOfAllSymbols()
    let allSymbolUsesInfo =  [ for s in allSymbolUses -> s.Symbol.DisplayName, tups s.Range, attribsOfSymbol s.Symbol ]
    //printfn "allSymbolUsesInfo = \n----\n%A\n----" allSymbolUsesInfo

    allSymbolUsesInfo |> shouldEqual
        [("LiteralAttribute", ((4, 2), (4, 9)), ["class"]);
         ("LiteralAttribute", ((4, 2), (4, 9)), ["class"]);
         ("LiteralAttribute", ((4, 2), (4, 9)), ["member"]);
         ("REGEX", ((5, 4), (5, 9)), ["val"]);
         ("RegexTypedStatic", ((6, 8), (6, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((6, 8), (6, 32)), ["member"]);
         ("RegexTypedStatic", ((7, 8), (7, 24)), ["class"; "provided"; "erased"]);
         ("REGEX", ((7, 33), (7, 38)), ["val"]);
         ("IsMatch", ((7, 8), (7, 32)), ["member"]);
         ("RegexTypedStatic", ((8, 8), (8, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((8, 8), (8, 32)), ["member"]);
         ("RegexTypedStatic", ((9, 8), (9, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((9, 8), (9, 32)), ["member"]);
         ("RegexTypedStatic", ((10, 8), (10, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((10, 8), (10, 32)), ["member"]);
         ("RegexTypedStatic", ((11, 8), (11, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((11, 8), (11, 32)), ["member"]);
         ("RegexTypedStatic", ((12, 8), (12, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((12, 8), (12, 32)), ["member"]);
         ("RegexTypedStatic", ((13, 8), (13, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((13, 8), (13, 32)), ["member"]);
         ("RegexTypedStatic", ((14, 8), (14, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((14, 8), (14, 32)), ["member"]);
         ("RegexTypedStatic", ((15, 8), (15, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((15, 8), (15, 32)), ["member"]);
         ("RegexTypedStatic", ((16, 8), (16, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((16, 8), (16, 32)), ["member"]);
         ("M", ((2, 7), (2, 8)), ["module"])]


[<Fact>]
let ``Test TPProject errors`` () =
    let wholeProjectResults = checker.ParseAndCheckProject(TPProject.options) |> Async.RunImmediate
    let parseResult, typeCheckAnswer = checker.ParseAndCheckFileInProject(TPProject.fileName1, 0, TPProject.fileSource1, TPProject.options) |> Async.RunImmediate
    let typeCheckResults =
        match typeCheckAnswer with
        | FSharpCheckFileAnswer.Succeeded(res) -> res
        | res -> failwithf "Parsing did not finish... (%A)" res

    let errorMessages = [ for msg in typeCheckResults.Diagnostics -> msg.StartLine, msg.StartColumn, msg.EndLine, msg.EndColumn, msg.Message.Replace("\r","").Replace("\n","") ]
    //printfn "errorMessages = \n----\n%A\n----" errorMessages

    errorMessages |> shouldEqual
        [(15, 47, 15, 48, "Expected type argument or static argument");
         (6, 8, 6, 32, "This provided method requires static parameters");
         (7, 39, 7, 42, "This expression was expected to have type    'string'    but here has type    'unit'    ");
         (8, 40, 8, 43, "This expression was expected to have type    'string'    but here has type    'unit'    ");
         (9, 40, 9, 49, "This expression was expected to have type    'string'    but here has type    'unit'    ");
         (11, 8, 11, 35, "The static parameter 'pattern1' of the provided type or method 'IsMatch' requires a value. Static parameters to type providers may be optionally specified using named arguments, e.g. 'IsMatch<pattern1=...>'.");
         (12, 8, 12, 41, "The static parameter 'pattern1' of the provided type or method 'IsMatch' requires a value. Static parameters to type providers may be optionally specified using named arguments, e.g. 'IsMatch<pattern1=...>'.");
         (14, 46, 14, 50, "This expression was expected to have type    'string'    but here has type    'unit'    ");
         (15, 33, 15, 38, "No static parameter exists with name ''");
         (16, 40, 16, 50, "This expression was expected to have type    'string'    but here has type    'unit'    ")]

let internal extractToolTipText (ToolTipText(els)) =
    [ for e in els do
        match e with
        | ToolTipElement.Group txts -> for item in txts do yield item.MainDescription
        | ToolTipElement.CompositionError err -> yield err
        | ToolTipElement.None -> yield "NONE!" ]

[<Fact>]
let ``Test TPProject quick info`` () =
    let wholeProjectResults = checker.ParseAndCheckProject(TPProject.options) |> Async.RunImmediate
    let parseResult, typeCheckAnswer = checker.ParseAndCheckFileInProject(TPProject.fileName1, 0, TPProject.fileSource1, TPProject.options) |> Async.RunImmediate
    let typeCheckResults =
        match typeCheckAnswer with
        | FSharpCheckFileAnswer.Succeeded(res) -> res
        | res -> failwithf "Parsing did not finish... (%A)" res

    let toolTips  =
      [ for lineNum in 0 .. TPProject.fileLines1.Length - 1 do
         let lineText = TPProject.fileLines1.[lineNum]
         if lineText.Contains(".IsMatch") then
            let colAtEndOfNames = lineText.IndexOf(".IsMatch") + ".IsMatch".Length
            let res = typeCheckResults.GetToolTipTextAlternate(lineNum, colAtEndOfNames, lineText, ["RegexTypedStatic";"IsMatch"], FSharpTokenTag.IDENT)
            yield lineNum, extractToolTipText  res ]
    //printfn "toolTips = \n----\n%A\n----" toolTips

    toolTips |> shouldEqual
        [(5, ["RegexTypedStatic.IsMatch() : int"]);
         (6, ["RegexTypedStatic.IsMatch() : int"]);
         // NOTE: This tool tip is sub-optimal, it would be better to show RegexTypedStatic.IsMatch<"ABC">
         //       This is a little tricky to implement
         (7, ["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"]);
         (8, ["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"]);
         (9, ["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"]);
         (10, ["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"]);
         (11, ["RegexTypedStatic.IsMatch() : int"]);
         (12, ["RegexTypedStatic.IsMatch() : int"]);
         (13, ["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"]);
         (14, ["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"]);
         (15, ["RegexTypedStatic.IsMatch() : int"])]


[<Fact>]
let ``Test TPProject param info`` () =
    let wholeProjectResults = checker.ParseAndCheckProject(TPProject.options) |> Async.RunImmediate
    let parseResult, typeCheckAnswer = checker.ParseAndCheckFileInProject(TPProject.fileName1, 0, TPProject.fileSource1, TPProject.options) |> Async.RunImmediate
    let typeCheckResults =
        match typeCheckAnswer with
        | FSharpCheckFileAnswer.Succeeded(res) -> res
        | res -> failwithf "Parsing did not finish... (%A)" res

    let paramInfos =
      [ for lineNum in 0 .. TPProject.fileLines1.Length - 1 do
         let lineText = TPProject.fileLines1.[lineNum]
         if lineText.Contains(".IsMatch") then
            let colAtEndOfNames = lineText.IndexOf(".IsMatch")  + ".IsMatch".Length
            let meths = typeCheckResults.GetMethodsAlternate(lineNum, colAtEndOfNames, lineText, Some ["RegexTypedStatic";"IsMatch"])
            let elems =
                [ for meth in meths.Methods do
                   yield extractToolTipText  meth.Description, meth.HasParameters, [ for p in meth.Parameters -> p.ParameterName ], [ for p in meth.StaticParameters -> p.ParameterName ] ]
            yield lineNum, elems]
    //printfn "paramInfos = \n----\n%A\n----" paramInfos

    // This tests that properly statically-instantiated methods have the right method lists and parameter info
    paramInfos |> shouldEqual
        [(5, [(["RegexTypedStatic.IsMatch() : int"], true, [], ["pattern1"])]);
         (6, [(["RegexTypedStatic.IsMatch() : int"], true, [], ["pattern1"])]);
         // NOTE: this method description is sub-optimal, it would be better to show RegexTypedStatic.IsMatch<"ABC">
         (7,[(["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"], true,["input"], ["pattern1"])]);
         (8,[(["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"], true,["input"], ["pattern1"])]);
         (9,[(["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"], true,["input"], ["pattern1"])]);
         (10,[(["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"], true, ["input"], ["pattern1"])]);
         (11, [(["RegexTypedStatic.IsMatch() : int"], true, [], ["pattern1"])]);
         (12, [(["RegexTypedStatic.IsMatch() : int"], true, [], ["pattern1"])]);
         (13,[(["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"], true,["input"], ["pattern1"])]);
         (14,[(["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"], true,["input"], ["pattern1"])]);
         (15, [(["RegexTypedStatic.IsMatch() : int"], true, [], ["pattern1"])])]

#endif // TEST_TP_PROJECTS


[<Fact>]
let ``FSharpField.IsNameGenerated`` () =
    let checkFields source =
        let file = "/home/user/Test.fsx"
        let _, typeCheckResults = parseAndCheckScript(file, source)
        let symbols =
            typeCheckResults.GetAllUsesOfAllSymbolsInFile()
        symbols
        |> Array.ofSeq
        |> Array.choose (fun su ->
            match su.Symbol with
            | :? FSharpEntity as entity -> Some entity.FSharpFields
            | :? FSharpUnionCase as unionCase -> Some unionCase.Fields
            | _ -> None)
        |> Seq.concat
        |> Seq.map (fun (field: FSharpField) -> field.Name, field.IsNameGenerated)
        |> List.ofSeq

    ["exception E of string", ["Data0", true]
     "exception E of Data0: string", ["Data0", false]
     "exception E of Name: string", ["Name", false]
     "exception E of string * Data2: string * Data1: string * Name: string * Data4: string",
        ["Data0", true; "Data2", false; "Data1", false; "Name", false; "Data4", false]

     "type U = Case of string", ["Item", true]
     "type U = Case of Item: string", ["Item", false]
     "type U = Case of Name: string", ["Name", false]
     "type U = Case of string * Item2: string * string * Name: string",
        ["Item1", true; "Item2", false; "Item3", true; "Name", false]]
    |> List.iter (fun (source, expected) -> checkFields source |> shouldEqual expected)


[<Fact>]
let ``ValNoMutable recovery`` () =
    let _, checkResults = getParseAndCheckResults """
let x = 1
x <-
    let y = 1
    y
"""
    assertHasSymbolUsages ["y"] checkResults


[<Fact>]
let ``PropertyCannotBeSet recovery`` () =
    let _, checkResults = getParseAndCheckResults """
type T =
    static member P = 1

T.P <-
    let y = 1
    y
"""
    assertHasSymbolUsages ["y"] checkResults


[<Fact>]
let ``FieldNotMutable recovery`` () =
    let _, checkResults = getParseAndCheckResults """
type R =
    { F: int }

{ F = 1 }.F <-
    let y = 1
    y
"""
    assertHasSymbolUsages ["y"] checkResults


[<Fact>]
let ``Inherit ctor arg recovery`` () =
    let _, checkResults = getParseAndCheckResults """
    type T() as this =
        inherit System.Exception('a', 'a')

        let x = this
    """
    assertHasSymbolUsages ["x"] checkResults

[<Fact>]
let ``Missing this recovery`` () =
    let _, checkResults = getParseAndCheckResults """
    type T() =
        member M() =
            let x = 1 in ()
    """
    assertHasSymbolUsages ["x"] checkResults

[<Fact>]
let ``Brace matching smoke test`` () =
    let input =
      """
let x1 = { contents = 1 }
let x2 = {| contents = 1 |}
let x3 = [ 1 ]
let x4 = [| 1 |]
let x5 = $"abc{1}def"
"""
    let file = "/home/user/Test.fsx"
    let braces = matchBraces(file, input)

    braces
    |> Array.map (fun (r1,r2) ->
        (r1.StartLine, r1.StartColumn, r1.EndLine, r1.EndColumn),
        (r2.StartLine, r2.StartColumn, r2.EndLine, r2.EndColumn))
    |> shouldEqual
         [|((2, 9, 2, 10), (2, 24, 2, 25));
           ((3, 9, 3, 11), (3, 25, 3, 27));
           ((4, 9, 4, 10), (4, 13, 4, 14));
           ((5, 9, 5, 11), (5, 14, 5, 16));
           ((6, 14, 6, 15), (6, 16, 6, 17))|]


[<Fact>]
let ``Brace matching in interpolated strings`` () =
    let input =
      "
let x5 = $\"abc{1}def\"
let x6 = $\"abc{1}def{2}hij\"
let x7 = $\"\"\"abc{1}def{2}hij\"\"\"
let x8 = $\"\"\"abc{  {contents=1} }def{2}hij\"\"\"
"
    let file = "/home/user/Test.fsx"
    let braces = matchBraces(file, input)

    braces
    |> Array.map (fun (r1,r2) ->
        (r1.StartLine, r1.StartColumn, r1.EndLine, r1.EndColumn),
        (r2.StartLine, r2.StartColumn, r2.EndLine, r2.EndColumn))
    |> shouldEqual
        [|((2, 14, 2, 15), (2, 16, 2, 17)); ((3, 14, 3, 15), (3, 16, 3, 17));
          ((3, 20, 3, 21), (3, 22, 3, 23)); ((4, 16, 4, 17), (4, 18, 4, 19));
          ((4, 22, 4, 23), (4, 24, 4, 25)); ((5, 19, 5, 20), (5, 30, 5, 31));
          ((5, 16, 5, 17), (5, 32, 5, 33)); ((5, 36, 5, 37), (5, 38, 5, 39))|]


[<Fact>]
let ``Active pattern 01 - Named args`` () =
    let _, checkResults = getParseAndCheckResults """
do let x = 1 in ()
"""
    let su = checkResults |> findSymbolUseByName "x"
    match checkResults.GetDescription(su.Symbol, su.GenericArguments, true, su.Range) with
    | ToolTipText [ToolTipElement.Group [data]] ->
        data.MainDescription |> Array.map (fun text -> text.Text) |> String.concat "" |> shouldEqual "val x: int"
    | elements -> failwith $"Tooltip elements: {elements}"

let hasRecordField (fieldName:string) (symbolUses: FSharpSymbolUse list) =
    symbolUses
    |> List.exists (fun symbolUse ->
        match symbolUse.Symbol with
        | :? FSharpField as field -> field.DisplayName = fieldName
        | _ -> false
    )
    |> fun exists -> Assert.True(exists, $"Field {fieldName} not found.")

let hasRecordType (recordTypeName: string) (symbolUses: FSharpSymbolUse list) =
    symbolUses
    |> List.exists (fun symbolUse ->
        match symbolUse.Symbol with
        | :? FSharpEntity as recordType -> recordType.IsFSharpRecord && recordType.DisplayName = recordTypeName
        | _ -> false
    )
    |> fun exists -> Assert.True(exists, $"Record type {recordTypeName} not found.")

[<Fact>]
let ``Record fields are completed via type name usage`` () =
    let parseResults, checkResults =
        getParseAndCheckResults """
type Entry =
    {
        Idx: int
        FileName: string
        /// Own deps
        DependencyCount: int
        /// Being depended on
        DependentCount: int
        LineCount: int
    }

let x =
    {
        Entry.
    }
"""

    let declarations =
        checkResults.GetDeclarationListSymbols(
            Some parseResults,
            14,
            "        Entry.",
            {
                EndColumn = 13
                LastDotPos = Some 13
                PartialIdent = ""
                QualifyingIdents = [ "Entry" ] 
            },
            fun _ -> List.empty
        )
        |> List.concat

    hasRecordField "Idx" declarations
    hasRecordField "FileName" declarations
    hasRecordField "DependentCount" declarations
    hasRecordField "LineCount" declarations

[<Fact>]
let ``Record fields and types are completed via type name usage`` () =
    let parseResults, checkResults =
        getParseAndCheckResults """
module Module1 =
    type R1 =
        { Field1: int }

    type R2 =
        { Field2: int }

module Module2 =

    { Module1. }
"""

    let declarations =
        checkResults.GetDeclarationListSymbols(
            Some parseResults,
            11,
            "    { Module1. }",
            {
                EndColumn = 13
                LastDotPos = Some 13
                PartialIdent = ""
                QualifyingIdents = [ "Module1" ] 
            },
            fun _ -> List.empty
        )
        |> List.concat

    hasRecordField "Field1" declarations
    hasRecordField "Field2" declarations
    hasRecordType "R1" declarations
    hasRecordType "R2" declarations

[<Fact>]
let ``Record fields are completed via type name usage with open statement`` () =
    let parseResults, checkResults =
        getParseAndCheckResults """
module Module1 =
    type R1 =
        { Field1: int }

    type R2 =
        { Field2: int }

module Module2 =
    open Module1

    { R1. }
"""

    let declarations =
        checkResults.GetDeclarationListSymbols(
            Some parseResults,
            12,
            "    { R1. }",
            {
                EndColumn = 8
                LastDotPos = Some 8
                PartialIdent = ""
                QualifyingIdents = [ "R1" ] 
            },
            fun _ -> List.empty
        )
        |> List.concat

    hasRecordField "Field1" declarations

[<Fact>]
let ``Record fields are completed via type name with module usage`` () =
    let parseResults, checkResults =
        getParseAndCheckResults """
module Module1 =
    type R1 =
        { Field1: int }

    type R2 =
        { Field2: int }

module Module2 =
    { Module1.R1. }
"""

    let declarations =
        checkResults.GetDeclarationListSymbols(
            Some parseResults,
            10,
            "    { Module1.R1. }",
            {
                EndColumn = 16
                LastDotPos = Some 16
                PartialIdent = ""
                QualifyingIdents = [ "Module1"; "R1" ] 
            },
            fun _ -> List.empty
        )
        |> List.concat

    hasRecordField "Field1" declarations

[<Fact>]
let ``Record fields are completed in update record`` () =
    let parseResults, checkResults =
        getParseAndCheckResults """
module Module

type R1 =
    { Field1: int; Field2: int }

let r1 = { Field1 = 1; Field2 = 2 }

let rUpdate = { r1 with  }
"""

    let declarations =
        checkResults.GetDeclarationListSymbols(
            Some parseResults,
            9,
            "let rUpdate = { r1 with  }",
            {
                EndColumn = 24
                LastDotPos = None
                PartialIdent = ""
                QualifyingIdents = []
            },
            fun _ -> List.empty
        )
        |> List.concat

    hasRecordField "Field1" declarations
    hasRecordField "Field2" declarations

[<Fact(Skip = "Current fails to suggest any record fields")>]
let ``Record fields are completed in update record with partial field name`` () =
    let parseResults, checkResults =
        getParseAndCheckResults """
module Module

type R1 =
    { Field1: int; Field2: int }

let r1 = { Field1 = 1; Field2 = 2 }

let rUpdate = { r1 with Fi }
"""

    let declarations =
        checkResults.GetDeclarationListSymbols(
            Some parseResults,
            9,
            "let rUpdate = { r1 with Fi }",
            {
                EndColumn = 26
                LastDotPos = None
                PartialIdent = "Fi"
                QualifyingIdents = []
            },
            fun _ -> List.empty
        )
        |> List.concat

    hasRecordField "Field1" declarations
    hasRecordField "Field2" declarations
