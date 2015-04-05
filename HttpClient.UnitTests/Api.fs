module HttpClient.Tests.Api

open System
open Fuchu
open HttpClient

let VALID_URL = "http://www"

let createValidRequest = createRequest Get VALID_URL

[<Tests>]
let api =
    testList "api" [
        // an example of creating a DSL that still gives nice output when a test fails!
        // doable because we're using values and not 'programming language'
        given "a default request with Method and Url" (createRequest Get "http://www.google.com") [
            "has same url", fun r -> Assert.Equal(r.Url, "http://www.google.com")
            "has get method", fun r -> Assert.Equal(r.Method, Get)
            "has no decompression scheme", fun r -> Assert.Equal(r.AutoDecompression, DecompressionScheme.None)
            "should follow redirects", fun r -> Assert.IsTrue r.AutoFollowRedirects
            "has no body", fun r -> Assert.IsNone r.Body
            "has no body character encoding", fun r -> Assert.IsNone r.BodyCharacterEncoding
            "has no cookies", fun r -> Assert.IsNone r.Cookies
            "has cookies enabled", fun r -> Assert.IsTrue r.CookiesEnabled
            "has no special headers", fun r -> Assert.IsNone r.Headers
            "has no query string", fun r -> Assert.IsNone r.QueryStringItems
            "has no response char encoding", fun r -> Assert.IsNone r.ResponseCharacterEncoding
            "has no proxy configured", fun r -> Assert.IsNone r.Proxy
            "uses keep-alive", fun r -> Assert.IsTrue r.KeepAlive
        ]

        testCase "withAutoDecompression enables the specified decompression methods" <| fun _ ->
            let createdRequest =
                createValidRequest
                |> withAutoDecompression (DecompressionScheme.Deflate ||| DecompressionScheme.GZip)
            Assert.Equal("deflate", DecompressionScheme.Deflate, createdRequest.AutoDecompression &&& DecompressionScheme.Deflate)
            Assert.Equal("gzip", DecompressionScheme.GZip, createdRequest.AutoDecompression &&& DecompressionScheme.GZip)

        testCase "withCookiesDisabled disables cookies" <| fun _ ->
            Assert.IsFalse((createValidRequest |> withCookiesDisabled).CookiesEnabled)

        testCase "withHeader adds header to the request" <| fun _ ->
            let header =
                (createValidRequest
                |> withHeader (UserAgent "Mozilla/5.0 (Windows NT 6.2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/29.0.1547.57 Safari/537.36"))
                  .Headers.Value
            Assert.Equal(header, [ UserAgent "Mozilla/5.0 (Windows NT 6.2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/29.0.1547.57 Safari/537.36" ])

        testCase "withHeader Custom adds a custom header to the request" <| fun _ ->
            let header =
                (createValidRequest
                |> withHeader (Custom { name="X-Hello-Mum"; value="Happy Birthday!"}))
                  .Headers.Value
            Assert.Equal(header, [ Custom { name="X-Hello-Mum"; value="Happy Birthday!"} ])
        
        given "multiple headers of different types can be added, including custom headers with different names" 
            (createValidRequest
            |> withHeader (UserAgent "ua")
            |> withHeader (Referer "ref")
            |> withHeader (Custom { name="c1"; value="v1"})
            |> withHeader (Custom { name="c2"; value="v2"})
            |> fun x -> x.Headers.Value)
            [   "has four items", fun hs -> Assert.Equal(hs.Length, 4)
                "contains 'ua' UserAgent", fun hs -> Assert.Contains (hs, UserAgent "ua")
                "contains a referrer", fun hs -> Assert.Contains (hs, Referer "ref")
                "contains custom 1", fun hs -> Assert.Contains (hs, Custom { name = "c1"; value = "v1" })
                "contains custom 2", fun hs -> Assert.Contains (hs, Custom { name = "c2"; value = "v2"})
            ]

        testCase "withBasicAuthentication sets the Authorization header with the username and password base-64 encoded" <| fun _ ->
            let createdRequest =
                createValidRequest
                |> withBasicAuthentication "myUsername" "myPassword"
            Assert.Contains(createdRequest.Headers.Value, Authorization "Basic bXlVc2VybmFtZTpteVBhc3N3b3Jk")

        testCase "withBasicAuthentication encodes the username and password with ISO-8859-1 before converting to base-64" <| fun _ ->
            let createdRequest =
                createValidRequest
                |> withBasicAuthentication "Ãµ¶" "ÖØ" // ISO-8859-1 characters not present in ASCII
            Assert.Contains(createdRequest.Headers.Value, Authorization "Basic w7W2OtbY")

        testCase "If the same header is added multiple times, throws an exception" <| fun _ ->
            Assert.Raise("header added twice", typeof<Exception>, (fun () ->
                createValidRequest
                |> withHeader (UserAgent "ua1")
                |> withHeader (UserAgent "ua2")
                |> ignore))
          
        testCase "If a custom header with the same name is added multiple times, an exception is thrown" <| fun _ ->
            Assert.Raise("header added twice", typeof<Exception>, (fun () ->
                createValidRequest
                |> withHeader (Custom { name="c1"; value="v1"})
                |> withHeader (Custom { name="c1"; value="v2"})
                |> ignore))

        given "withQueryString adds the query string item to the list"
            (createValidRequest
            |> withQueryStringItem {name="f1"; value="v1"}
            |> withQueryStringItem {name="f2"; value="v2"}
            |> fun r -> r.QueryStringItems.Value)
            [   "has two items", fun qs -> Assert.Equal(2, qs.Length)
                "contains first item", fun qs -> Assert.Contains(qs, {name="f1"; value="v1"})
                "contains second item", fun qs -> Assert.Contains(qs, {name="f2"; value="v2"})
            ]

        testCase "withCookie throws an exception if cookies are disabled" <| fun _ ->
            Assert.Raise("there is no cake", typeof<Exception>, fun() ->
                createValidRequest 
                |> withCookiesDisabled 
                |> withCookie { name = "message"; value = "hi mum" }|> ignore)

        given "a request with two cookies"
            (createRequest Get "http://www.google.com/"
            |> withCookie { name = "c1"; value = "v1" }
            |> withCookie { name = "c2"; value = "v2" }
            |> fun x -> x.Cookies.Value)
            [   "should have two cookies", fun cs -> Assert.Equal(2, cs.Length)
                "should have first cookie", fun cs -> Assert.Contains(cs, { name = "c1"; value = "v1" })
                "should have second cookie", fun cs -> Assert.Contains(cs, { name = "c2"; value = "v2" })
            ]

        testCase "withAutoFollowRedirectsDisabled turns auto-follow off" <| fun _ ->
            Assert.IsFalse((createValidRequest |> withAutoFollowRedirectsDisabled).AutoFollowRedirects)

        testCase "withResponseCharacterEncoding sets the response character encoding" <| fun _ ->
            let createdRequest =
                createRequest Get "http://www.google.com/"
                |> withResponseCharacterEncoding "utf-8"
            Assert.Equal(createdRequest.ResponseCharacterEncoding.Value, "utf-8")

        given "a request withProxy"
            (createValidRequest 
            |> withProxy { Address = "proxy.com"; Port = 8080; Credentials = ProxyCredentials.None }
            |> fun x -> x.Proxy.Value)
            [   "sets address", fun p -> Assert.Equal(p.Address, "proxy.com")
                "sets port", fun p -> Assert.Equal(p.Port, 8080)
            ]

        testCase "withProxy can set proxy with custom credentials" <| fun _ ->
            let request = 
                createValidRequest 
                |> withProxy { 
                    Address = "proxy.com"; 
                    Port = 8080; 
                    Credentials = ProxyCredentials.Custom { username = "Tim"; password = "Password1" } }
            
            Assert.Equal(request.Proxy.Value.Credentials, (ProxyCredentials.Custom { username = "Tim"; password = "Password1" }))

        testCase "withProxy can set proxy with default credentials" <| fun _ ->
            let request = 
                createValidRequest 
                |> withProxy { Address = ""; Port = 0; Credentials = ProxyCredentials.Default }
            
            Assert.Equal(request.Proxy.Value.Credentials, ProxyCredentials.Default)

        testCase "withProxy can set proxy with no credentials" <| fun _ ->
            let request = 
                createValidRequest 
                |> withProxy { Address = ""; Port = 0; Credentials = ProxyCredentials.None }
            
            Assert.Equal(request.Proxy.Value.Credentials, ProxyCredentials.None)

        testCase "withKeepAlive sets KeepAlive" <| fun _ ->
            Assert.IsFalse((createValidRequest |> withKeepAlive false).KeepAlive)
    ]