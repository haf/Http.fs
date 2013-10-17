module HttpClient_Tests

open System
open NUnit.Framework
open FsUnit
open HttpClient

let VALID_URL = "http://www"

let createValidRequest = createRequest Get VALID_URL

[<Test>]
let ``createRequest makes a Request with a Method and URL, and sensible defaults`` () =
    let createdRequest = createRequest Get "http://www.google.com"
    createdRequest.Url |> should equal "http://www.google.com"
    createdRequest.Method |> should equal Get
    createdRequest.AutoDecompression |> should equal DecompressionScheme.None
    createdRequest.AutoFollowRedirects |> should equal true
    createdRequest.Body.IsNone |> should equal true
    createdRequest.Cookies.IsNone |> should equal true
    createdRequest.CookiesEnabled |> should equal true
    createdRequest.Headers.IsNone |> should equal true
    createdRequest.QueryStringItems.IsNone |> should equal true

[<Test>]
let ``requests have cookies enabled by default`` () =
    createValidRequest.CookiesEnabled |> should equal true

[<Test>]
let ``withAutoDecompression enables the specified decompression methods`` () =
    let createdRequest = 
        createValidRequest 
        |> withAutoDecompression (DecompressionScheme.Deflate ||| DecompressionScheme.GZip)
    (createdRequest.AutoDecompression &&& DecompressionScheme.Deflate) |> should equal DecompressionScheme.Deflate
    (createdRequest.AutoDecompression &&& DecompressionScheme.GZip) |> should equal DecompressionScheme.GZip

[<Test>]
let ``withCookiesDisabled disables cookies`` () =
    (createValidRequest |> withCookiesDisabled).CookiesEnabled |> should equal false

[<Test>]
let ``withHeader adds header to the request`` () =
    (createValidRequest
    |> withHeader (UserAgent "Mozilla/5.0 (Windows NT 6.2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/29.0.1547.57 Safari/537.36")).Headers.Value.[0]
    |> should equal <| UserAgent "Mozilla/5.0 (Windows NT 6.2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/29.0.1547.57 Safari/537.36"

[<Test>]
let ``withHeader Custom adds a custom header to the request`` () =
    (createValidRequest
    |> withHeader (Custom { name="X-Hello-Mum"; value="Happy Birthday!"})).Headers.Value.[0]
    |> should equal <| Custom { name="X-Hello-Mum"; value="Happy Birthday!"}

[<Test>]
let ``multiple headers of different types can be added, including custom headers with different names`` () =
    let createdRequest =
        createValidRequest
        |> withHeader (UserAgent "ua")
        |> withHeader (Referer "ref")
        |> withHeader (Custom { name="c1"; value="v1"})
        |> withHeader (Custom { name="c2"; value="v2"})
    createdRequest.Headers.Value |> List.length |> should equal 4
    createdRequest.Headers.Value |> List.exists (fun header -> header = UserAgent "ua") |> should equal true
    createdRequest.Headers.Value |> List.exists (fun header -> header = Referer "ref") |> should equal true
    createdRequest.Headers.Value |> List.exists (fun header -> header = Custom { name="c1"; value="v1"}) |> should equal true
    createdRequest.Headers.Value |> List.exists (fun header -> header = Custom { name="c2"; value="v2"}) |> should equal true

[<Test>]
let ``If the same header is added multiple times, throws an exception`` () =
    (fun () ->
        createValidRequest
        |> withHeader (UserAgent "ua1")
        |> withHeader (UserAgent "ua2")
        |> ignore )
    |> should throw typeof<Exception>

[<Test>]
let ``If a custom header with the same name is added multiple times, an exception is thrown`` () =
    (fun () ->
        createValidRequest
        |> withHeader (Custom { name="c1"; value="v1"})
        |> withHeader (Custom { name="c1"; value="v2"})
        |> ignore )
    |> should throw typeof<Exception>

// TODO: check content-length is set in integration tests

[<Test>]
let ``withBody sets the request body`` () =
    (createValidRequest |> withBody """Hello mum!%2\/@$""").Body.Value |> should equal """Hello mum!%2\/@$"""

[<Test>]
let ``withQueryString adds the query string item to the list`` () =
    let createdRequest = 
        createValidRequest
        |> withQueryStringItem {name="f1"; value="v1"}
        |> withQueryStringItem {name="f2"; value="v2"}
    createdRequest.QueryStringItems.Value.Length |> should equal 2
    createdRequest.QueryStringItems.Value |> List.exists (fun qs -> qs = {name="f1"; value="v1"}) |> should equal true
    createdRequest.QueryStringItems.Value |> List.exists (fun qs -> qs = {name="f2"; value="v2"}) |> should equal true

[<Test>]
let ``withCookie throws an exception if cookies are disabled`` () =
    (fun() -> 
        createValidRequest 
        |> withCookiesDisabled 
        |> withCookie { name = "message"; value = "hi mum" }|> ignore) 
    |> should throw typeof<Exception>

[<Test>]
let ``withCookie adds the cookie to the request`` () =
    let createdRequest =
        createRequest Get "http://www.google.com/"
        |> withCookie { name = "c1"; value = "v1" }
        |> withCookie { name = "c2"; value = "v2" }
    createdRequest.Cookies.Value.Length |> should equal 2
    createdRequest.Cookies.Value |> List.exists (fun cookie -> cookie = { name = "c1"; value = "v1" }) |> should equal true
    createdRequest.Cookies.Value |> List.exists (fun cookie -> cookie = { name = "c2"; value = "v2" }) |> should equal true

[<Test>]
let ``withAutoFollowRedirects turns auto-follow on`` () =
    (createValidRequest |> withAutoFollowRedirectsDisabled).AutoFollowRedirects |> should equal false

// TODO: test all HTTP methods in integration tests
