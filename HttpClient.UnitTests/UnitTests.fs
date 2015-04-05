module HttpClient.UnitTests

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
        given "a request with Method and Url" (createRequest Get "http://www.google.com") [
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

        testCase "requests have cookies enabled by default" <| fun _ ->
            createValidRequest.CookiesEnabled |> should equal true

        testCase "withAutoDecompression enables the specified decompression methods" <| fun _ ->
            let createdRequest = 
                createValidRequest 
                |> withAutoDecompression (DecompressionScheme.Deflate ||| DecompressionScheme.GZip)
            (createdRequest.AutoDecompression &&& DecompressionScheme.Deflate) |> should equal DecompressionScheme.Deflate
            (createdRequest.AutoDecompression &&& DecompressionScheme.GZip) |> should equal DecompressionScheme.GZip

        testCase "withCookiesDisabled disables cookies" <| fun _ ->
            (createValidRequest |> withCookiesDisabled).CookiesEnabled |> should equal false

        testCase "withHeader adds header to the request" <| fun _ ->
            (createValidRequest
            |> withHeader (UserAgent "Mozilla/5.0 (Windows NT 6.2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/29.0.1547.57 Safari/537.36")).Headers.Value
            |> should equal [ UserAgent "Mozilla/5.0 (Windows NT 6.2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/29.0.1547.57 Safari/537.36" ]

        testCase "withHeader Custom adds a custom header to the request" <| fun _ ->
            (createValidRequest
            |> withHeader (Custom { name="X-Hello-Mum"; value="Happy Birthday!"})).Headers.Value
            |> should equal [ Custom { name="X-Hello-Mum"; value="Happy Birthday!"} ]
        
        testCase "multiple headers of different types can be added, including custom headers with different names" <| fun _ ->
            let createdRequest =
                createValidRequest
                |> withHeader (UserAgent "ua")
                |> withHeader (Referer "ref")
                |> withHeader (Custom { name="c1"; value="v1"})
                |> withHeader (Custom { name="c2"; value="v2"})
            createdRequest.Headers.Value |> should haveLength 4
            createdRequest.Headers.Value |> should contain (UserAgent "ua")
            createdRequest.Headers.Value |> should contain (Referer "ref")
            createdRequest.Headers.Value |> should contain (Custom { name = "c1"; value = "v1" })
            createdRequest.Headers.Value |> should contain (Custom { name = "c2"; value = "v2"})

        testCase "withBasicAuthentication sets the Authorization header with the username and password base-64 encoded" <| fun _ ->
            let createdRequest =
                createValidRequest
                |> withBasicAuthentication "myUsername" "myPassword"
            createdRequest.Headers |> Option.isSome |> should equal true
            createdRequest.Headers.Value |> should contain (Authorization "Basic bXlVc2VybmFtZTpteVBhc3N3b3Jk")

        testCase "withBasicAuthentication encodes the username and password with ISO-8859-1 before converting to base-64" <| fun _ ->
            let createdRequest =
                createValidRequest
                |> withBasicAuthentication "Ãµ¶" "ÖØ" // ISO-8859-1 characters not present in ASCII
            createdRequest.Headers.Value |> should contain (Authorization "Basic w7W2OtbY")

        testCase "If the same header is added multiple times, throws an exception" <| fun _ ->
            (fun () ->
                createValidRequest
                |> withHeader (UserAgent "ua1")
                |> withHeader (UserAgent "ua2")
                |> ignore )
            |> should throw typeof<Exception>
          
        testCase "If a custom header with the same name is added multiple times, an exception is thrown" <| fun _ ->
            (fun () ->
                createValidRequest
                |> withHeader (Custom { name="c1"; value="v1"})
                |> withHeader (Custom { name="c1"; value="v2"})
                |> ignore )
            |> should throw typeof<Exception>

        testCase "withBody sets the request body" <| fun _ ->
            (createValidRequest |> withBody """Hello mum!%2\/@$""").Body.Value |> should equal """Hello mum!%2\/@$"""

        testCase "withBody uses default character encoding of ISO-8859-1" <| fun _ ->
            (createValidRequest |> withBody "whatever").BodyCharacterEncoding.Value |> should equal "ISO-8859-1"

        testCase "withBodyEncoded sets the request body" <| fun _ ->
            (createValidRequest |> withBodyEncoded """Hello mum!%2\/@$""" "UTF-8").Body.Value |> should equal """Hello mum!%2\/@$"""

        testCase "withBodyEncoded sets the body encoding" <| fun _ ->
            (createValidRequest |> withBodyEncoded "Hi Mum" "UTF-8").BodyCharacterEncoding.Value |> should equal "UTF-8"

        testCase "if a body character encoding is somehow not specified, throws an exception" <| fun _ ->
            let request = 
                createRequest Post "http://localhost:1234/TestServer/RecordRequest" 
                |> withBodyEncoded "¥§±Æ" "UTF-8" // random UTF-8 characters
                    
            let dodgyRequest = {request with BodyCharacterEncoding = None }

            (fun () -> dodgyRequest |> getResponseCode |> ignore)
                |> should throw typeof<Exception>

        testCase "withQueryString adds the query string item to the list" <| fun _ ->
            let createdRequest = 
                createValidRequest
                |> withQueryStringItem {name="f1"; value="v1"}
                |> withQueryStringItem {name="f2"; value="v2"}
            createdRequest.QueryStringItems.Value |> should haveLength 2
            createdRequest.QueryStringItems.Value |> should contain {name="f1"; value="v1"}
            createdRequest.QueryStringItems.Value |> should contain {name="f2"; value="v2"}

        testCase "withCookie throws an exception if cookies are disabled" <| fun _ ->
            (fun() -> 
                createValidRequest 
                |> withCookiesDisabled 
                |> withCookie { name = "message"; value = "hi mum" }|> ignore) 
            |> should throw typeof<Exception>


        testCase "withCookie adds the cookie to the request" <| fun _ ->
            let createdRequest =
                createRequest Get "http://www.google.com/"
                |> withCookie { name = "c1"; value = "v1" }
                |> withCookie { name = "c2"; value = "v2" }
            createdRequest.Cookies.Value |> should haveLength 2
            createdRequest.Cookies.Value |> should contain { name = "c1"; value = "v1" }
            createdRequest.Cookies.Value |> should contain { name = "c2"; value = "v2" }

        testCase "withAutoFollowRedirectsDisabled turns auto-follow off" <| fun _ ->
            (createValidRequest |> withAutoFollowRedirectsDisabled).AutoFollowRedirects |> should equal false

        testCase "withResponseCharacterEncoding sets the response character encoding" <| fun _ ->
            let createdRequest =
                createRequest Get "http://www.google.com/"
                |> withResponseCharacterEncoding "utf-8"
            createdRequest.ResponseCharacterEncoding.Value |> should equal "utf-8"

        testCase "withProxy sets proxy address and port" <| fun _ ->
            let request = 
                createValidRequest 
                |> withProxy { Address = "proxy.com"; Port = 8080; Credentials = ProxyCredentials.None }
            
            request.Proxy.IsSome |> should equal true
            request.Proxy.Value.Address |> should equal "proxy.com"
            request.Proxy.Value.Port |> should equal 8080

        testCase "withProxy can set proxy with custom credentials" <| fun _ ->
            let request = 
                createValidRequest 
                |> withProxy { 
                    Address = "proxy.com"; 
                    Port = 8080; 
                    Credentials = ProxyCredentials.Custom { username = "Tim"; password = "Password1" } }
            
            request.Proxy.Value.Credentials |> should equal (ProxyCredentials.Custom { username = "Tim"; password = "Password1" })

        testCase "withProxy can set proxy with default credentials" <| fun _ ->
            let request = 
                createValidRequest 
                |> withProxy { Address = ""; Port = 0; Credentials = ProxyCredentials.Default }
            
            request.Proxy.Value.Credentials |> should equal ProxyCredentials.Default

        testCase "withProxy can set proxy with no credentials" <| fun _ ->
            let request = 
                createValidRequest 
                |> withProxy { Address = ""; Port = 0; Credentials = ProxyCredentials.None }
            
            request.Proxy.Value.Credentials |> should equal ProxyCredentials.None

        testCase "withKeepAlive sets KeepAlive" <| fun _ ->
            (createValidRequest |> withKeepAlive false).KeepAlive |> should equal false
    ]