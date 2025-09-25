module DiagnosticSample

// Intentional issues for LSP diagnostics:
//  - unused value
//  - type mismatch (+) between int and string

let unused = 42
let f = 1 + "" // type error expected
