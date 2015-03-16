module HttpClient_ManualIntegrationTests

open System
open NUnit.Framework
open FsUnit
open HttpClient


[<Test>]
[<Category("ManualIntegrationTests")>]
let ``Create request without Timeout and check default after run`` () =
    let createdRequest = createRequest Get "http://www.google.com"

    let resp = createdRequest |> getResponse
    resp.StatusCode |> should equal 200
    createdRequest.Timeout |> should equal 100000

[<Test>]
[<Category("ManualIntegrationTests")>]
let ``Create request with Timeout and check after request`` () =
    let createdRequest = 
        createRequest Get "http://www.google.com"
        |> withTimeout 150000

    let resp = createdRequest |> getResponse
    resp.StatusCode |> should equal 200
    createdRequest.Timeout |> should equal 150000