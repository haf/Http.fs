module HttpClient_IntegrationTests

open System
open System.IO
open System.Net
open System.Net.Cache
open System.Text
open NUnit.Framework
open FsUnit
open HttpClient
open Nancy
open Nancy.Hosting.Self
open HttpServer

let hostConfig = new HostConfiguration()
hostConfig.AllowChunkedEncoding <- false
// Enabling chunked encoding breaks HEAD requests if you're self-hosting.
// It also seems to mean the Content-Length isn't set in some cases.
hostConfig.UrlReservations<-UrlReservations(CreateAutomatically=true)

let nancyHost = 
    new NancyHost(
        hostConfig, 
        new Uri("http://localhost:1234/TestServer/"))

let utf8 = Encoding.UTF8

[<TestFixture>]
type ``Integration tests`` ()=

    [<TestFixtureSetUp>]
    member x.fixtureSetup() =
        // disable caching in HttpWebRequest/Response in case it interferes with the tests
        try HttpWebRequest.DefaultCachePolicy <- HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore)
        with _ -> ()
        nancyHost.Start()

    [<TestFixtureTearDown>]
    member x.fixtureTearDown() =
        nancyHost.Stop()

    [<SetUp>]
    member x.setUp() =
        HttpServer.recordedRequest := null

    [<Test>]
    // This needs to be run first, as the keep-alive is only set on the first call.  They seem to be run alphabetically.
    member x.``_if KeepAlive is true, Connection set to 'Keep-Alive' on the first request, but not subsequent ones`` () =

        createRequest Get "http://localhost:1234/TestServer/RecordRequest"
        |> getResponseCode |> ignore
        HttpServer.recordedRequest.Value |> should not' (equal null)
        HttpServer.recordedRequest.Value.Headers.Connection.ToLowerInvariant() |> should equal "keep-alive"

        HttpServer.recordedRequest := null
        createRequest Get "http://localhost:1234/TestServer/RecordRequest"
        |> getResponseCode |> ignore
        HttpServer.recordedRequest.Value |> should not' (equal null)
        HttpServer.recordedRequest.Value.Headers.Connection |> should equal ""

    [<Test>]
    member x.``if KeepAlive is false, Connection set to 'Close' on every request`` () =

        createRequest Get "http://localhost:1234/TestServer/RecordRequest"
        |> withKeepAlive false
        |> getResponseCode |> ignore
        HttpServer.recordedRequest.Value |> should not' (equal null)
        HttpServer.recordedRequest.Value.Headers.Connection.ToLowerInvariant() |> should equal "close"

        HttpServer.recordedRequest := null
        createRequest Get "http://localhost:1234/TestServer/RecordRequest"
        |> withKeepAlive false
        |> getResponseCode |> ignore
        HttpServer.recordedRequest.Value |> should not' (equal null)
        HttpServer.recordedRequest.Value.Headers.Connection.ToLowerInvariant() |> should equal "close"

    [<Test>] 
    member x.``createRequest should set everything correctly in the HTTP request`` ()=
        createRequest Post "http://localhost:1234/TestServer/RecordRequest" 
        |> withQueryStringItem {name="search"; value="jeebus"}
        |> withQueryStringItem {name="qs2"; value="hi mum"}
        |> withHeader (Accept "application/xml")
        |> withCookie {name="SESSIONID"; value="1234"}
        |> withBodyString "some XML or whatever"
        |> getResponseCode |> ignore
        HttpServer.recordedRequest.Value |> should not' (equal null)
        HttpServer.recordedRequest.Value.Query?search.ToString() |> should equal "jeebus"
        HttpServer.recordedRequest.Value.Query?qs2.ToString() |> should equal "hi mum"
        HttpServer.recordedRequest.Value.Headers.Accept |> should contain ("application/xml", 1m)
        HttpServer.recordedRequest.Value.Cookies.["SESSIONID"] |> should contain "1234"
        use bodyStream = new StreamReader(HttpServer.recordedRequest.Value.Body,Encoding.GetEncoding(1252))
        bodyStream.ReadToEnd() |> should equal "some XML or whatever"

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
    member x.``all details of the response should be available after a call to getResponse`` () =
        let response = createRequest Get "http://localhost:1234/TestServer/AllTheThings" |> getResponse
        response.StatusCode |> should equal 418
        response.EntityBody.Value |> should equal "Some JSON or whatever"
        response.ContentLength |> should equal 21
        response.Cookies.["cookie1"] |> should equal "chocolate+chip" // cookies get encoded
        response.Cookies.["cookie2"] |> should equal "smarties"
        response.Headers.[ContentEncoding] |> should equal "gzip"
        response.Headers.[NonStandard("X-New-Fangled-Header")] |> should equal "some value"

    [<Test>]
    member x.``getResponse should have nothing if the things don't exist`` () =
        let response = createRequest Get "http://localhost:1234/TestServer/GoodStatusCode" |> getResponse
        response.StatusCode |> should equal 200
        response.EntityBody.IsSome |> should equal false
        response.Cookies.IsEmpty |> should equal true
        // There will always be headers

    [<Test>]
    member x.``getResponse, given a request with an invalid url, throws an exception`` () =
        (fun() -> createRequest Get "www.google.com" |> getResponse |> ignore) |> should throw typeof<UriFormatException>

    [<Test>]
    member x.``getResponseCode, when called on a non-existant page, returns 404`` () =
        createRequest Get "http://localhost:1234/TestServer/NoPage" 
        |> getResponseCode
        |> should equal 404

    [<Test>] 
    member x.``posts to Nancy without a body don't work`` ()=
        // Not sure if this is just a Nancy thing, but the handler doesn't get called if there isn't a body
        createRequest Post "http://localhost:1234/TestServer/RecordRequest" 
        |> getResponseCode |> ignore
        HttpServer.recordedRequest.Value |> should equal null

    [<Test>] 
    member x.``all of the manually-set request headers get sent to the server`` ()=
        createRequest Get "http://localhost:1234/TestServer/RecordRequest" 
        |> withHeader (Accept "application/xml,text/html;q=0.3")
        |> withHeader (AcceptCharset "utf-8, utf-16;q=0.5" )
        |> withHeader (AcceptDatetime "Thu, 31 May 2007 20:35:00 GMT" )
        |> withHeader (AcceptLanguage "en-GB, en-US;q=0.1" )
        |> withHeader (Authorization  "QWxhZGRpbjpvcGVuIHNlc2FtZQ==" )
        |> withHeader (Connection "conn1" )
        |> withHeader (ContentMD5 "Q2hlY2sgSW50ZWdyaXR5IQ==" )
        |> withHeader (ContentType { typ = "application"; subtype = "json"; charset = None})
        |> withHeader (Date (new DateTime(1999, 12, 31, 11, 59, 59, DateTimeKind.Utc)))
        |> withHeader (From "user@example.com" )
        |> withHeader (IfMatch "737060cd8c284d8af7ad3082f209582d" )
        |> withHeader (IfModifiedSince (new DateTime(2000, 12, 31, 11, 59, 59, DateTimeKind.Utc)))
        |> withHeader (IfNoneMatch "737060cd8c284d8af7ad3082f209582d" )
        |> withHeader (IfRange "737060cd8c284d8af7ad3082f209582d" )
        |> withHeader (MaxForwards 5 )
        |> withHeader (Origin "http://www.mybot.com" )
        |> withHeader (RequestHeader.Pragma "no-cache" )
        |> withHeader (ProxyAuthorization "QWxhZGRpbjpvcGVuIHNlc2FtZQ==" )
        |> withHeader (Range {start=0L; finish=500L} )
        |> withHeader (Referer "http://en.wikipedia.org/" )
        |> withHeader (Upgrade "HTTP/2.0, SHTTP/1.3" )
        |> withHeader (UserAgent "(X11; Linux x86_64; rv:12.0) Gecko/20100101 Firefox/21.0" )
        |> withHeader (Via "1.0 fred, 1.1 example.com (Apache/1.1)" )
        |> withHeader (Warning "199 Miscellaneous warning" )
        |> withHeader (Custom {name="X-Greeting"; value="Happy Birthday"})
        |> getResponseCode |> ignore

        HttpServer.recordedRequest.Value |> should not' (equal null)
        HttpServer.recordedRequest.Value.Headers.Accept |> should contain ("application/xml", 1m)
        HttpServer.recordedRequest.Value.Headers.Accept |> should contain ("text/html", 0.3m)
        HttpServer.recordedRequest.Value.Headers.AcceptCharset |> should contain ("utf-8", 1m)
        HttpServer.recordedRequest.Value.Headers.AcceptCharset |> should contain ("utf-16", 0.5m)
        HttpServer.recordedRequest.Value.Headers.["Accept-Datetime"] |> should equal ["Thu, 31 May 2007 20:35:00 GMT"]
        HttpServer.recordedRequest.Value.Headers.AcceptLanguage |> should contain ("en-GB", 1m)
        HttpServer.recordedRequest.Value.Headers.AcceptLanguage |> should contain ("en-US", 0.1m)
        HttpServer.recordedRequest.Value.Headers.Authorization |> should equal "QWxhZGRpbjpvcGVuIHNlc2FtZQ=="
        HttpServer.recordedRequest.Value.Headers.Connection |> should equal "conn1"
        HttpServer.recordedRequest.Value.Headers.["Content-MD5"] |> should equal ["Q2hlY2sgSW50ZWdyaXR5IQ=="]
        HttpServer.recordedRequest.Value.Headers.ContentType |> should equal "application/json"
        HttpServer.recordedRequest.Value.Headers.Date.Value |> should equal (new DateTime(1999, 12, 31, 11, 59, 59, DateTimeKind.Utc))
        HttpServer.recordedRequest.Value.Headers.["From"] |> should equal ["user@example.com"]
        HttpServer.recordedRequest.Value.Headers.IfMatch |> should equal ["737060cd8c284d8af7ad3082f209582d"]
        HttpServer.recordedRequest.Value.Headers.IfModifiedSince |> should equal (new DateTime(2000, 12, 31, 11, 59, 59, DateTimeKind.Utc))
        HttpServer.recordedRequest.Value.Headers.IfNoneMatch |> should equal ["737060cd8c284d8af7ad3082f209582d"]
        HttpServer.recordedRequest.Value.Headers.IfRange |> should equal "737060cd8c284d8af7ad3082f209582d"
        HttpServer.recordedRequest.Value.Headers.MaxForwards |> should equal 5
        HttpServer.recordedRequest.Value.Headers.["Origin"] |> should equal ["http://www.mybot.com"]
        HttpServer.recordedRequest.Value.Headers.["Pragma"] |> should equal ["no-cache"]
        HttpServer.recordedRequest.Value.Headers.["Proxy-Authorization"] |> should equal ["QWxhZGRpbjpvcGVuIHNlc2FtZQ=="]
        HttpServer.recordedRequest.Value.Headers.["Range"] |> should equal ["bytes=0-500"]
        HttpServer.recordedRequest.Value.Headers.["Referer"] |> should equal ["http://en.wikipedia.org/"]
        HttpServer.recordedRequest.Value.Headers.["Upgrade"] |> should contain "HTTP/2.0"
        HttpServer.recordedRequest.Value.Headers.["Upgrade"] |> should contain "SHTTP/1.3" 
        HttpServer.recordedRequest.Value.Headers.UserAgent |> should equal "(X11; Linux x86_64; rv:12.0) Gecko/20100101 Firefox/21.0"
        HttpServer.recordedRequest.Value.Headers.["Via"] |> should contain ("1.0 fred")
        HttpServer.recordedRequest.Value.Headers.["Via"] |> should contain ("1.1 example.com (Apache/1.1)")
        HttpServer.recordedRequest.Value.Headers.["Warning"] |> should equal ["199 Miscellaneous warning"]
        HttpServer.recordedRequest.Value.Headers.["X-Greeting"] |> should equal ["Happy Birthday"]

    [<Test>]
    member x.``Content-Length header is set automatically for Posts with a body`` () =
        createRequest Post "http://localhost:1234/TestServer/RecordRequest"
        |> withBodyString "Hi Mum"
        |> getResponseCode |> ignore
        HttpServer.recordedRequest.Value |> should not' (equal null)
        HttpServer.recordedRequest.Value.Headers.ContentLength |> should equal 6

    [<Test>]
    member x.``accept-encoding header is set automatically when decompression scheme is set`` () =
        createRequest Get "http://localhost:1234/TestServer/RecordRequest"
        |> withAutoDecompression (DecompressionScheme.Deflate ||| DecompressionScheme.GZip)
        |> getResponseCode |> ignore
        HttpServer.recordedRequest.Value |> should not' (equal null)
        HttpServer.recordedRequest.Value.Headers.AcceptEncoding |> should contain "gzip"
        HttpServer.recordedRequest.Value.Headers.AcceptEncoding |> should contain "deflate"

        // TODO: Separate tests for the headers which get set automatically:
        // Cache-Control
        // Host
        // IfUnmodifiedSince

    [<Test>]
    member x.``all of the response headers are available after a call to getResponse`` () =
        let response = createRequest Get "http://localhost:1234/TestServer/AllHeaders" |> getResponse
        response.Headers.[AccessControlAllowOrigin] |> should equal "*"
        response.Headers.[AcceptRanges] |> should equal "bytes"
        response.Headers.[Age] |> should equal "12"
        response.Headers.[Allow] |> should equal "GET, HEAD"
        response.Headers.[CacheControl] |> should equal "max-age=3600"
        //response.Headers.[Connection] |> should equal "close" // don't seem to get connection header from nancy
        response.Headers.[ContentEncoding] |> should equal "gzip"
        response.Headers.[ContentLanguage] |> should equal "EN-gb"
        response.Headers.[ContentLocation] |> should equal "/index.htm"
        response.Headers.[ContentMD5Response] |> should equal "Q2hlY2sgSW50ZWdyaXR5IQ=="
        response.Headers.[ContentDisposition] |> should equal "attachment; filename=\"fname.ext\""
        response.Headers.[ContentRange] |> should equal "bytes 21010-47021/47022"
        response.Headers.[ContentTypeResponse] |> should equal "text/html; charset=utf-8"
        let (parsedOK,_) = System.DateTime.TryParse(response.Headers.[DateResponse])
        parsedOK |> should equal true
        response.Headers.[ETag] |> should equal "737060cd8c284d8af7ad3082f209582d"
        response.Headers.[Expires] |> should equal "Thu, 01 Dec 1994 16:00:00 GMT"
        response.Headers.[LastModified] |> should equal "Tue, 15 Nov 1994 12:45:26 +0000"
        response.Headers.[Link] |> should equal "</feed>; rel=\"alternate\""
        response.Headers.[Location] |> should equal "http://www.w3.org/pub/WWW/People.html"
        response.Headers.[P3P] |> should equal "CP=\"your_compact_policy\""
        response.Headers.[PragmaResponse] |> should equal "no-cache"
        response.Headers.[ProxyAuthenticate] |> should equal "Basic"
        response.Headers.[Refresh] |> should equal "5; url=http://www.w3.org/pub/WWW/People.html"
        response.Headers.[RetryAfter] |> should equal "120"
        response.Headers.[Server] |> should contain "HTTPAPI/"
        response.Headers.[StrictTransportSecurity] |> should equal "max-age=16070400; includeSubDomains"
        response.Headers.[Trailer] |> should equal "Max-Forwards"
        response.Headers.[TransferEncoding] |> should equal "identity"
        response.Headers.[Vary] |> should equal "*"
        response.Headers.[ViaResponse] |> should equal "1.0 fred, 1.1 example.com (Apache/1.1)"
        response.Headers.[WarningResponse] |> should equal "199 Miscellaneous warning"
        response.Headers.[WWWAuthenticate] |> should equal "Basic"
        response.Headers.[NonStandard("X-New-Fangled-Header")] |> should equal "some value"

    [<Test>]
    member x.``if body character encoding is specified, encodes the request body with it`` () =
        let response = 
            createRequest Post "http://localhost:1234/TestServer/RecordRequest" 
            |> withBodyStringEncoded "¥§±Æ" utf8 // random UTF-8 characters
            |> getResponseCode |> ignore
        use bodyStream = new StreamReader(HttpServer.recordedRequest.Value.Body,Encoding.GetEncoding("UTF-8"))
        bodyStream.ReadToEnd() |> should equal "¥§±Æ"

    [<Test>]
    member x.``if a response character encoding is specified, that encoding is used regardless of what the response content-type specifies`` () =
        let response = 
            createRequest Get "http://localhost:1234/TestServer/MoonLanguageCorrectEncoding" 
            |> withResponseCharacterEncoding (Encoding.GetEncoding "utf-16")
            |> getResponse
        response.EntityBody.Value |> should equal "迿ꞧ쒿" // "яЏ§§їДЙ" (as encoded with windows-1251) decoded with utf-16

    [<Test>]
    member x.``if a response character encoding is NOT specified, the body is read using the character encoding specified in the response's content-type header`` () =
        let response = createRequest Get "http://localhost:1234/TestServer/MoonLanguageCorrectEncoding" |> getResponse
        response.EntityBody.Value |> should equal "яЏ§§їДЙ"

    [<Test>]
    member x.``if a response character encoding is NOT specified, and character encoding is NOT specified in the response's content-type header, the body is read using ISO Latin 1 character encoding`` () =
        let expected = "ÿ§§¿ÄÉ" // "яЏ§§їДЙ" (as encoded with windows-1251) decoded with ISO-8859-1 (Latin 1)

        let response = createRequest Get "http://localhost:1234/TestServer/MoonLanguageTextPlainNoEncoding" |> getResponse
        response.EntityBody.Value |> should equal expected

        let response = createRequest Get "http://localhost:1234/TestServer/MoonLanguageApplicationXmlNoEncoding" |> getResponse
        response.EntityBody.Value |> should equal expected

    [<Test>]
    member x.``if a response character encoding is NOT specified, and the character encoding specified in the response's content-type header is invalid, an exception is thrown`` () =
        (fun() -> createRequest Get "http://localhost:1234/TestServer/MoonLanguageInvalidEncoding" |> getResponse |> ignore) 
            |> should throw typeof<ArgumentException>

    // .Net encoder doesn't like utf8, seems to need utf-8
    [<Test>]
    member x.``if the response character encoding is specified as 'utf8', uses 'utf-8' instead`` () =
        let response = createRequest Get "http://localhost:1234/TestServer/utf8" |> getResponse
        response.EntityBody.Value |> should equal "'Why do you hate me so much, Windows?!' - utf8"

    [<Test>]
    member x.``if the response character encoding is specified as 'utf16', uses 'utf-16' instead`` () =
        let response = createRequest Get "http://localhost:1234/TestServer/utf16" |> getResponse
        response.EntityBody.Value |> should equal "'Why are you so picky, Windows?!' - utf16"

    [<Test>]
    member x.``cookies are not kept during an automatic redirect`` () =
        let response =
            createRequest Get "http://localhost:1234/TestServer/CookieRedirect"
            |> getResponse
        
        response.StatusCode |> should equal 200
        response.Cookies.ContainsKey "cookie1" |> should equal false

    [<Test>]
    member x.``reading the body as bytes works properly`` () =
        let response = createRequest Get "http://localhost:1234/TestServer/Raw" |> getResponseBytes

        let bodyBytes = Array.create 4 (new Byte())
        bodyBytes.[0] <- byte(98)
        bodyBytes.[1] <- byte(111)
        bodyBytes.[2] <- byte(100)
        bodyBytes.[3] <- byte(121)

        response |> should equal bodyBytes

    [<Test>]
    member x.``when there is no body, reading it as bytes gives an empty array`` () =
        let response = createRequest Get "http://localhost:1234/TestServer/GoodStatusCode" |> getResponseBytes

        let noBytes = Array.create 0 (new Byte())
        response |> should equal noBytes

    [<Test>]
    member x.``getResponseStream can access the response stream by passing a function`` () =

        let asString (sourceStream:Stream) =
            use reader = new StreamReader(sourceStream)
            reader.ReadToEnd()

        let responseFromStream = createRequest Get "http://localhost:1234/TestServer/Raw" |> getResponseStream asString

        responseFromStream |> should equal "body"

    [<Test>]
    member x.``Closing the response stream retrieved from getResponseStream does not cause an exception`` () =

        let responseStream = createRequest Get "http://localhost:1234/TestServer/Raw" |> getResponseStream id
        responseStream.Close |> ignore

    [<Test; Ignore "different behaviour Mono vs Windows">]
    member x.``Trying to access the response stream after getResponseStream causes an ArgumentException`` () =

        (fun() -> new StreamReader(createRequest Get "http://localhost:1234/TestServer/Raw" |> getResponseStream id) |> ignore) 
            |> should throw typeof<ArgumentException>

    [<Test; Ignore "exception not thrown on Mono - investigate">]
    /// Timeout follows .Net behaviour and throws WebException exception when reached.
    /// https://msdn.microsoft.com/en-us/library/system.net.httpwebrequest.timeout%28v=vs.110%29.aspx
    member x.``if the resource is not returned within Timeout, throw WebException`` () =
        (fun() ->
            createRequest Post "http://localhost:1234/TestServer/SlowResponse" 
            |> withTimeout 1000<ms>
            |> withBodyString "hi mum"
            |> getResponseCode |> ignore)
            |> should throw typeof<WebException>

    [<Test>]
    member x.``Get method works`` () =
        createRequest Get "http://localhost:1234/TestServer/Get" |> getResponseCode |> should equal 200

    [<Test>]
    member x.``Options method works`` () =
        createRequest Options "http://localhost:1234/TestServer/Options" |> getResponseCode |> should equal 200

    [<Test>]
    member x.``Post method works`` () =
        createRequest Post "http://localhost:1234/TestServer/Post" 
        |> withBodyString "hi mum" // posts need a body in Nancy
        |> getResponseCode 
        |> should equal 200

    [<Test>]
    member x.``Patch method works`` () =
        createRequest Patch "http://localhost:1234/TestServer/Patch" |> getResponseCode |> should equal 200

    [<Test>]
    member x.``Head method works`` () =
        createRequest Head "http://localhost:1234/TestServer/Get" |> getResponseCode |> should equal 200
        // Head method automatically handled for Get methods in Nancy

    [<Test>]
    member x.``Put method works`` () =
        createRequest Put "http://localhost:1234/TestServer/Put" 
        |> withBodyString "hi mum" // puts need a body in Nancy
        |> getResponseCode 
        |> should equal 200

    [<Test>]
    member x.``Delete method works`` () =
        createRequest Delete "http://localhost:1234/TestServer/Delete" |> getResponseCode |> should equal 200

    [<Test>]
    member x.``geResponse.ResponseUri should contain URI that responded to the request`` () =
        // Is going to redirect to another route and return GET 200.
        let request = 
            createRequest Post "http://localhost:1234/TestServer/Redirect" 
            |> withBodyString "hi mum" // posts need a body in Nancy
        
        let resp = request |> getResponse 
        resp.StatusCode |> should equal 200
        resp.ResponseUri.ToString() |> should equal "http://localhost:1234/TestServer/GoodStatusCode"

    // Nancy doesn't support Trace or Connect HTTP methods, so we can't test them easily

    // TODO: test proxy - approach below doesn't seem to work, even without port specified in proxy (which appends it to the end of the URL)
    // There's a script called 'test proxy' which can be used to test it manually.

//    [<Test>]
//    member x.``requests with a proxy set use the proxy details`` () =
//        createRequest Get "http://localhost:1234/TestServer/NoPage"
//        |> withProxy { Address = "localhost:1234/TestServer/RecordRequest"; Port = 1234; Credentials = ProxyCredentials.Default }
//        |> getResponseCode |> ignore
//        HttpServer.recordedRequest.Value |> should not' (equal null)
