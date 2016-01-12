module HttpFs.Tests.Api

open System
open System.Text
open Fuchu
open HttpFs.Client

let VALID_URL = Uri "http://www"

let createValidRequest = createRequest Get VALID_URL

[<Tests>]
let api =
    testList "api" [
        // an example of creating a DSL that still gives nice output when a test fails!
        // doable because we're using values and not 'programming language'
        given "a default request with Method and Url" (createRequest Get (Uri "http://www.google.com")) [
            "has same url", fun r -> Assert.Equal(r.Url.ToString(), "http://www.google.com/")
            "has get method", fun r -> Assert.Equal(r.Method, Get)
            "has no decompression scheme", fun r -> Assert.Equal(r.AutoDecompression, DecompressionScheme.None)
            "should follow redirects", fun r -> Assert.IsTrue r.AutoFollowRedirects
            "has empty body", fun r -> Assert.Equal (r.Body, BodyRaw [||])
            "has UTF8 body character encoding", fun r -> Assert.Equal (r.BodyCharacterEncoding, Encoding.UTF8)
            "has no cookies", fun r -> Assert.Empty r.Cookies
            "has cookies enabled", fun r -> Assert.IsTrue r.CookiesEnabled
            "has no special headers", fun r -> Assert.Empty r.Headers
            "has no query string", fun r -> Assert.Empty r.QueryStringItems
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
          let expected = UserAgent "Mozilla/5.0"
          let header = (createValidRequest |> withHeader expected).Headers |> Map.find expected.Key
          Assert.Equal(header, expected)

        testCase "withHeader Custom adds a custom header to the request" <| fun _ ->
          let expected = Custom ("X-Hello-Mum", "Happy Birthday!")
          let header = (createValidRequest |> withHeader expected).Headers |> Map.find expected.Key
          Assert.Equal(header, expected)

        given "multiple headers of different types can be added, including custom headers with different names" 
            (createValidRequest
            |> withHeader (UserAgent "ua")
            |> withHeader (Referer "ref")
            |> withHeader (Custom ("c1", "v1"))
            |> withHeader (Custom ("c2", "v2"))
            |> fun x -> x.Headers)
            [   "has four items", fun hs -> Assert.Equal(hs.Count, 4)
                "contains 'ua' UserAgent", fun hs -> Assert.Contains (hs, UserAgent "ua")
                "contains a referrer", fun hs -> Assert.Contains (hs, Referer "ref")
                "contains custom 1", fun hs -> Assert.Contains (hs, Custom ("c1", "v1"))
                "contains custom 2", fun hs -> Assert.Contains (hs, Custom ("c2", "v2"))
            ]

        testCase "withBasicAuthentication sets the Authorization header with the username and password base-64 encoded" <| fun _ ->
            let createdRequest =
                createValidRequest
                |> withBasicAuthentication "myUsername" "myPassword"
            Assert.Contains(createdRequest.Headers, Authorization "Basic bXlVc2VybmFtZTpteVBhc3N3b3Jk")

        testCase "withBasicAuthentication encodes the username and password with UTF-8 before converting to base64" <| fun _ ->
            let createdRequest =
                createValidRequest
                |> withBasicAuthentication "Ãµ¶" "汉语" // UTF-8 characters not present in ASCII
            Assert.Contains(createdRequest.Headers, Authorization "Basic w4PCtcK2OuaxieivrQ==")

        testCase "uses latest added header when eq name" <| fun _ ->
          let req =
            createValidRequest
            |> withHeader (Custom ("c1", "v1"))
            |> withHeader (Custom ("c1", "v2"))
          req.Headers |> Map.find "c1" |> function | (Custom (c1key, c1value)) -> Assert.Equal(c1value, "v2")
                                                   | _ -> Tests.failtest "errrrrorrrrr"

        given "withQueryString adds the query string item to the list"
            (createValidRequest
            |> withQueryStringItem "f1" "v1"
            |> withQueryStringItem "f2" "v2"
            |> fun r -> r.QueryStringItems)
            [   "has two items", fun qs -> Assert.Equal(2, qs.Count)
                "contains first item", fun qs -> Assert.Contains(qs, "v1")
                "contains second item", fun qs -> Assert.Contains(qs, "v2")
            ]

        testCase "withCookie throws an exception if cookies are disabled" <| fun _ ->
            Assert.Raise("there is no cake", typeof<Exception>, fun() ->
                createValidRequest 
                |> withCookiesDisabled 
                |> withCookie (Cookie.Create("message", "hi mum"))
                |> ignore)

        given "a request with two cookies"
            (createRequest Get (Uri "http://www.google.com/")
            |> withCookie (Cookie.Create("c1", "v1"))
            |> withCookie (Cookie.Create("c2", "v2"))
            |> fun x -> x.Cookies)
            [   "should have two cookies", fun cs -> Assert.Equal(2, cs.Count)
                "should have first cookie", fun cs -> Assert.Contains(cs, Cookie.Create("c1", "v1"))
                "should have second cookie", fun cs -> Assert.Contains(cs, Cookie.Create("c2", "v2"))
            ]

        testCase "withAutoFollowRedirectsDisabled turns auto-follow off" <| fun _ ->
            Assert.IsFalse((createValidRequest |> withAutoFollowRedirectsDisabled).AutoFollowRedirects)

        testCase "withResponseCharacterEncoding sets the response character encoding" <| fun _ ->
            let createdRequest =
                createRequest Get (Uri "http://www.google.com/")
                |> withResponseCharacterEncoding Encoding.UTF8
            Assert.Equal(createdRequest.ResponseCharacterEncoding.Value, Encoding.UTF8)

        given "a request withProxy"
            (createValidRequest 
            |> withProxy { Address = "proxy.com"; Port = 8080; Credentials = Credentials.None }
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
                    Credentials = Credentials.Custom { username = "Tim"; password = "Password1" } }
            
            Assert.Equal(request.Proxy.Value.Credentials, (Credentials.Custom { username = "Tim"; password = "Password1" }))

        testCase "withProxy can set proxy with default credentials" <| fun _ ->
            let request = 
                createValidRequest 
                |> withProxy { Address = ""; Port = 0; Credentials = Credentials.Default }
            
            Assert.Equal(request.Proxy.Value.Credentials, Credentials.Default)

        testCase "withProxy can set proxy with no credentials" <| fun _ ->
            let request = 
                createValidRequest 
                |> withProxy { Address = ""; Port = 0; Credentials = Credentials.None }
            
            Assert.Equal(request.Proxy.Value.Credentials, Credentials.None)

        testCase "withKeepAlive sets KeepAlive" <| fun _ ->
            Assert.IsFalse((createValidRequest |> withKeepAlive false).KeepAlive)
    ]