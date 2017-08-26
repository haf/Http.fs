module HttpFs.IntegrationTests.Program

open Expecto
open HttpServer
open System.Text

[<EntryPoint>]
let main argv =
  Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)

  use server = new SuaveTestServer()
  Tests.runTestsInAssembly defaultConfig argv