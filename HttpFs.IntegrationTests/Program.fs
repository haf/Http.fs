module HttpFs.IntegrationTests.Program

open Expecto
open HttpServer

[<EntryPoint>]
let main argv =
  use server = new SuaveTestServer()
  Tests.runTestsInAssembly defaultConfig argv