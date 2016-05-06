module HttpFs.Tests.Api

open System
open System.Text
open Fuchu
open HttpFs.Client
open HttpFs.Client.Request

let ValidUri = Uri "http://www"
let validRequest = create Get ValidUri

[<Tests>]
let api =
    testList "api" [
        // an example of creating a DSL that still gives nice output when a test fails!
        // doable because we're using values and not 'programming language'
        given "a default request with Method and Url" (Request.createUrl Get "http://www.google.com") [
            "has same url", fun r -> Assert.Equal(r.url.ToString(), "http://www.google.com/")
            "has get method", fun r -> Assert.Equal(r.``method``, Get)
            "has no decompression scheme", fun r -> Assert.Equal(r.autoDecompression, DecompressionScheme.None)
            "should follow redirects", fun r -> Assert.IsTrue r.autoFollowRedirects
            "has empty body", fun r -> Assert.Equal (r.body, BodyRaw [||])
            "has UTF8 body character encoding", fun r -> Assert.Equal (r.bodyCharacterEncoding, Encoding.UTF8)
            "has no cookies", fun r -> Assert.Empty r.cookies
            "has cookies enabled", fun r -> Assert.IsTrue r.cookiesEnabled
            "has no special headers", fun r -> Assert.Empty r.headers
            "has no query string", fun r -> Assert.Empty r.queryStringItems
            "has no response char encoding", fun r -> Assert.IsNone r.responseCharacterEncoding
            "has no proxy configured", fun r -> Assert.IsNone r.proxy
            "uses keep-alive", fun r -> Assert.IsTrue r.keepAlive
        ]

        testCase "withAutoDecompression enables the specified decompression methods" <| fun _ ->
            let createdRequest =
              validRequest
              |> autoDecompression (DecompressionScheme.Deflate ||| DecompressionScheme.GZip)
            Assert.Equal("deflate", DecompressionScheme.Deflate, createdRequest.autoDecompression &&& DecompressionScheme.Deflate)
            Assert.Equal("gzip", DecompressionScheme.GZip, createdRequest.autoDecompression &&& DecompressionScheme.GZip)

        testCase "withCookiesDisabled disables cookies" <| fun _ ->
            Assert.IsFalse((validRequest |> Request.cookiesDisabled).cookiesEnabled)

        testCase "withHeader adds header to the request" <| fun _ ->
          let expected = UserAgent "Mozilla/5.0"
          let header = (validRequest |> setHeader expected).headers |> Map.find expected.Key
          Assert.Equal(header, expected)

        testCase "withHeader Custom adds a custom header to the request" <| fun _ ->
          let expected = Custom ("X-Hello-Mum", "Happy Birthday!")
          let header = (validRequest |> Request.setHeader expected).headers |> Map.find expected.Key
          Assert.Equal(header, expected)

        given "multiple headers of different types can be added, including custom headers with different names" 
          (validRequest
          |> setHeader (UserAgent "ua")
          |> setHeader (Referer "ref")
          |> setHeader (Custom ("c1", "v1"))
          |> setHeader (Custom ("c2", "v2"))
          |> fun x -> x.headers)
          [   "has four items", fun hs -> Assert.Equal(hs.Count, 4)
              "contains 'ua' UserAgent", fun hs -> Assert.Contains (hs, UserAgent "ua")
              "contains a referrer", fun hs -> Assert.Contains (hs, Referer "ref")
              "contains custom 1", fun hs -> Assert.Contains (hs, Custom ("c1", "v1"))
              "contains custom 2", fun hs -> Assert.Contains (hs, Custom ("c2", "v2"))
          ]

        testCase "withBasicAuthentication sets the Authorization header with the username and password base-64 encoded" <| fun _ ->
          let createdRequest =
            validRequest
            |> basicAuthentication "myUsername" "myPassword"
          Assert.Contains(createdRequest.headers, Authorization "Basic bXlVc2VybmFtZTpteVBhc3N3b3Jk")

        testCase "withBasicAuthentication encodes the username and password with UTF-8 before converting to base64" <| fun _ ->
          let createdRequest =
            validRequest
            |> basicAuthentication "Ãµ¶" "汉语" // UTF-8 characters not present in ASCII
          Assert.Contains(createdRequest.headers, Authorization "Basic w4PCtcK2OuaxieivrQ==")

        testCase "uses latest added header when eq name" <| fun _ ->
          let req =
            validRequest
            |> setHeader (Custom ("c1", "v1"))
            |> setHeader (Custom ("c1", "v2"))
          req.headers |> Map.find "c1" |> function | (Custom (c1key, c1value)) -> Assert.Equal(c1value, "v2")
                                                   | _ -> Tests.failtest "errrrrorrrrr"

        given "withQueryString adds the query string item to the list"
          (validRequest
          |> queryStringItem "f1" "v1"
          |> queryStringItem "f2" "v2"
          |> fun r -> r.queryStringItems)
          [ "has two items", fun qs -> Assert.Equal(2, qs.Count)
            "contains first item", fun qs -> Assert.Contains(qs, "v1")
            "contains second item", fun qs -> Assert.Contains(qs, "v2")
          ]

        testCase "withCookie throws an exception if cookies are disabled" <| fun _ ->
            Assert.Raise("there is no cake", typeof<Exception>, fun() ->
                validRequest 
                |> cookiesDisabled 
                |> cookie (Cookie.create("message", "hi mum"))
                |> ignore)

        given "a request with two cookies"
            (createUrl Get "http://www.google.com/"
            |> cookie (Cookie.create("c1", "v1"))
            |> cookie (Cookie.create("c2", "v2"))
            |> fun x -> x.cookies)
            [   "should have two cookies", fun cs -> Assert.Equal(2, cs.Count)
                "should have first cookie", fun cs -> Assert.Contains(cs, Cookie.create("c1", "v1"))
                "should have second cookie", fun cs -> Assert.Contains(cs, Cookie.create("c2", "v2"))
            ]

        testCase "withAutoFollowRedirectsDisabled turns auto-follow off" <| fun _ ->
            Assert.IsFalse((validRequest |> autoFollowRedirectsDisabled).autoFollowRedirects)

        testCase "withResponseCharacterEncoding sets the response character encoding" <| fun _ ->
            let createdRequest =
                createUrl Get "http://www.google.com/"
                |> responseCharacterEncoding Encoding.UTF8
            Assert.Equal(createdRequest.responseCharacterEncoding.Value, Encoding.UTF8)

        given "a request withProxy"
            (validRequest 
            |> proxy { Address = "proxy.com"; Port = 8080; Credentials = Credentials.None }
            |> fun x -> x.proxy.Value)
            [   "sets address", fun p -> Assert.Equal(p.Address, "proxy.com")
                "sets port", fun p -> Assert.Equal(p.Port, 8080)
            ]

        testCase "withProxy can set proxy with custom credentials" <| fun _ ->
            let request = 
                validRequest 
                |> proxy { 
                    Address = "proxy.com"; 
                    Port = 8080; 
                    Credentials = Credentials.Custom { username = "Tim"; password = "Password1" } }
            
            Assert.Equal(request.proxy.Value.Credentials, (Credentials.Custom { username = "Tim"; password = "Password1" }))

        testCase "withProxy can set proxy with default credentials" <| fun _ ->
            let request = 
                validRequest 
                |> proxy { Address = ""; Port = 0; Credentials = Credentials.Default }
            
            Assert.Equal(request.proxy.Value.Credentials, Credentials.Default)

        testCase "withProxy can set proxy with no credentials" <| fun _ ->
            let request = 
                validRequest 
                |> proxy { Address = ""; Port = 0; Credentials = Credentials.None }
            
            Assert.Equal(request.proxy.Value.Credentials, Credentials.None)

        testCase "withKeepAlive sets KeepAlive" <| fun _ ->
            Assert.IsFalse((validRequest |> keepAlive false).keepAlive)
    ]