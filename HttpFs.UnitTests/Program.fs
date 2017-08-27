module Program

open Expecto
open System.Text

[<EntryPoint>]
let main argv =
  Tests.runTestsInAssembly defaultConfig argv