module HttpFs.IntegrationTests.Program

open Expecto
open HttpServer
open System.Text

[<EntryPoint>]
let main argv =
  Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)

  Tests.runTestsInAssembly defaultConfig argv