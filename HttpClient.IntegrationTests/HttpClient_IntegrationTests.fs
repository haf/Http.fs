module HttpClient_IntegrationTests

open System
open System.IO
open System.Net
open System.Text
open NUnit.Framework
open FsUnit
open HttpClient
open Nancy
open Nancy.Hosting.Self

// ? operator to get values from a Nancy DynamicDictionary
let (?) (parameters:obj) param =
    (parameters :?> Nancy.DynamicDictionary).[param]
 
let recordedRequest = ref (null:Request)
let recordedPostRequest = ref (null:Request)

type FakeServer() as self = 
    inherit NancyModule()
    do
        self.Get.["RecordRequest"] <- 
            fun _ -> 
                recordedRequest := self.Request
                200 :> obj

        self.Get.["GoodStatusCode"] <- 
            fun _ -> 
                200 :> obj

        self.Get.["BadStatusCode"] <- 
            fun _ -> 
                401 :> obj

        self.Get.["GotBody"] <- 
            fun _ -> 
                "Check out my sexy body" :> obj

        self.Get.["AllTheThings"] <- 
            fun _ -> 
                let response = "Some JSON or whatever" |> Nancy.Response.op_Implicit 
                response.StatusCode <- HttpStatusCode.ImATeapot
                response.AddCookie("cookie1", "chocolate chip") |> ignore
                response.AddCookie("cookie2", "smarties") |> ignore
                response.Headers.Add("Content-Encoding", "gzip")
                response.Headers.Add("X-New-Fangled-Header", "some value")
                response :> obj

        self.Post.["RecordPost"] <- 
            fun _ -> 
                recordedPostRequest := self.Request
                200 :> obj

let nancyHost = new NancyHost(new Uri("http://localhost:1234/TestServer/"))

[<TestFixture>] 
type ``Integration tests`` ()=

    [<TestFixtureSetUp>]
    member x.fixtureSetup() =
        nancyHost.Start()

    [<TestFixtureTearDown>]
    member x.fixtureTearDown() =
        nancyHost.Stop()

    [<Test>] 
    member x.``getResponse should set everything correctly in the request`` ()=
        createRequest Get "http://localhost:1234/TestServer/RecordRequest" 
        |> withQueryStringItem {name="search"; value="jeebus"}
        |> withHeader (Accept "application/xml,text/html;q=0.3")
        |> getResponseCode |> ignore
        recordedRequest |> should not' (equal null)
        recordedRequest.Value.Query?search.ToString() |> should equal "jeebus"
        recordedRequest.Value.Headers.Accept |> should contain ("application/xml", 1m)
        recordedRequest.Value.Headers.Accept |> should contain ("text/html", 0.3m)

    [<Test>]
    member x.``getResponseCode should return the http status code for all response types`` () =
        createRequest Get "http://localhost:1234/TestServer/GoodStatusCode" |> getResponseCode |> should equal 200
        createRequest Get "http://localhost:1234/TestServer/BadStatusCode" |> getResponseCode |> should equal 401

    [<Test>]
    member x.``getResponseBody should return the entity body as a string`` () =
        createRequest Get "http://localhost:1234/TestServer/GotBody" |> getResponseBody |> should equal "Check out my sexy body"

    [<Test>]
    member x.``getResponseBody should return an empty string when there is no body`` () =
        createRequest Get "http://localhost:1234/TestServer/GoodStatusCode" |> getResponseBody |> should equal ""

    [<Test>]
    member x.``getResponse should return all the things`` () =
        let response = createRequest Get "http://localhost:1234/TestServer/AllTheThings" |> getResponse
        response.StatusCode |> should equal 418
        response.EntityBody.Value |> should equal "Some JSON or whatever"
        response.Cookies.["cookie1"] |> should equal "chocolate+chip" // cookies get encoded
        response.Cookies.["cookie2"] |> should equal "smarties"
        response.Headers.[ContentEncoding] |> should equal "gzip"
        response.Headers.[NonStandard("X-New-Fangled-Header")] |> should equal "some value"
        // TODO: add the rest

    [<Test>]
    member x.``getResponse should have nothing if the things don't exist`` () =
        let response = createRequest Get "http://localhost:1234/TestServer/GoodStatusCode" |> getResponse
        response.StatusCode |> should equal 200
        response.EntityBody.IsSome |> should equal false
        response.Cookies.IsEmpty |> should equal true
        // TODO: Add the rest

    [<Test>]
    member x.``getResponse, given a request with an invalid url, throws an exception`` () =
        (fun() -> createRequest Get "www.google.com" |> getResponse |> ignore) |> should throw typeof<UriFormatException>

    [<Test>]
    member x.``Content-Length is set correctly for Posts with a body`` () =
        createRequest Post "http://localhost:1234/TestServer/RecordPost"
        |> withBody "Hi Mum"
        |> getResponseCode |> ignore
        recordedPostRequest |> should not' (equal null)
        recordedPostRequest.Value.Headers.ContentLength |> should equal 6
