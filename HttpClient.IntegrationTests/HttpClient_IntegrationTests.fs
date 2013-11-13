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
open System.Threading

// ? operator to get values from a Nancy DynamicDictionary
let (?) (parameters:obj) param =
    (parameters :?> Nancy.DynamicDictionary).[param]
 
let recordedRequest = ref (null:Request)

type FakeServer() as self = 
    inherit NancyModule()
    do
        self.Post.["RecordRequest"] <- 
            fun _ -> 
                recordedRequest := self.Request
                200 :> obj

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

        self.Get.["AllHeaders"] <- 
            fun _ -> 
                let response = "Some JSON or whatever" |> Nancy.Response.op_Implicit 
                response.ContentType <- "text/html; charset=utf-8"
                response.Headers.Add("Access-Control-Allow-Origin", "*")
                response.Headers.Add("Accept-Ranges", "bytes")
                response.Headers.Add("Age", "12")
                response.Headers.Add("Allow", "GET, HEAD")
                response.Headers.Add("Cache-Control", "max-age=3600")
                response.Headers.Add("Connection", "close")
                response.Headers.Add("Content-Encoding", "gzip")
                response.Headers.Add("Content-Language", "EN-gb")
                response.Headers.Add("Content-Location", "/index.htm")
                response.Headers.Add("Content-MD5", "Q2hlY2sgSW50ZWdyaXR5IQ==")
                response.Headers.Add("Content-Disposition", "attachment; filename=\"fname.ext\"")
                response.Headers.Add("Content-Range", "bytes 21010-47021/47022")
                response.Headers.Add("Content-Type", "text/html; charset=utf-8")
                //response.Headers.Add("Date", "") // will be current date
                response.Headers.Add("ETag", "737060cd8c284d8af7ad3082f209582d")
                response.Headers.Add("Expires", "Thu, 01 Dec 1994 16:00:00 GMT")
                response.Headers.Add("Last-Modified", "Tue, 15 Nov 1994 12:45:26 +0000")
                response.Headers.Add("Link", "</feed>; rel=\"alternate\"")
                response.Headers.Add("Location", "http://www.w3.org/pub/WWW/People.html")
                response.Headers.Add("P3P", "CP=\"your_compact_policy\"")
                response.Headers.Add("Pragma", "no-cache")
                response.Headers.Add("Proxy-Authenticate", "Basic")
                response.Headers.Add("Refresh", "5; url=http://www.w3.org/pub/WWW/People.html")
                response.Headers.Add("Retry-After", "120")
                //response.Headers.Add("Server", "") // will be 'Microsoft-HTTPAPI/2.0'
                response.Headers.Add("Strict-Transport-Security", "max-age=16070400; includeSubDomains")
                response.Headers.Add("Trailer", "Max-Forwards")
                response.Headers.Add("Transfer-Encoding", "chunked")
                response.Headers.Add("Vary", "*")
                response.Headers.Add("Via", "1.0 fred, 1.1 example.com (Apache/1.1)")
                response.Headers.Add("Warning", "199 Miscellaneous warning")
                response.Headers.Add("WWW-Authenticate", "Basic")
                response.Headers.Add("X-New-Fangled-Header", "some value")
                response :> obj

        self.Get.["SlowCall"] <- 
            fun _ -> 
                Thread.Sleep(100)
                200 :> obj

        self.Get.["OneSecondCall"] <- 
            fun _ -> 
                Thread.Sleep(1000)
                200 :> obj

let nancyHost = new NancyHost(new Uri("http://localhost:1234/TestServer/"))

[<TestFixture>] 
type ``Integration tests`` ()=

    [<TestFixtureSetUp>]
    member x.fixtureSetup() =
        HttpWebRequest.DefaultCachePolicy <- HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore)
        nancyHost.Start()

    [<TestFixtureTearDown>]
    member x.fixtureTearDown() =
        nancyHost.Stop()

    [<SetUp>]
    member x.setUp() =
        recordedRequest := null

    [<Test>]
    // This needs to be run first, as the keep-aliver is only set on the first call.  They seem to be run alphabetically.
    member x.``_connection keep-alive header is set automatically on the first request, but not subsequent ones`` () =

        createRequest Get "http://localhost:1234/TestServer/RecordRequest"
        |> getResponseCode |> ignore
        recordedRequest.Value |> should not' (equal null)
        recordedRequest.Value.Headers.Connection |> should equal "Keep-Alive"

        recordedRequest := null
        createRequest Get "http://localhost:1234/TestServer/RecordRequest"
        |> getResponseCode |> ignore
        recordedRequest.Value |> should not' (equal null)
        recordedRequest.Value.Headers.Connection |> should equal ""

    [<Test>] 
    member x.``getResponse should set everything correctly in the request`` ()=
        createRequest Post "http://localhost:1234/TestServer/RecordRequest" 
        |> withQueryStringItem {name="search"; value="jeebus"}
        |> withQueryStringItem {name="qs2"; value="hi mum"}
        |> withHeader (Accept "application/xml")
        |> withCookie {name="SESSIONID"; value="1234"}
        |> withBody "some JSON or whatever"
        |> getResponseCode |> ignore
        recordedRequest.Value |> should not' (equal null)
        recordedRequest.Value.Query?search.ToString() |> should equal "jeebus"
        recordedRequest.Value.Query?qs2.ToString() |> should equal "hi mum"
        recordedRequest.Value.Headers.Accept |> should contain ("application/xml", 1m)
        recordedRequest.Value.Cookies.["SESSIONID"] |> should contain "1234"
        use bodyStream = new StreamReader(recordedRequest.Value.Body,Encoding.GetEncoding(1252))
        bodyStream.ReadToEnd() |> should equal "some JSON or whatever"

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
        response.ContentLength |> should equal -1 // nancy isn't setting the content-length for some reason
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
        recordedRequest.Value |> should equal null

    [<Test>] 
    member x.``all of the manually-set request headers should get sent to the server`` ()=
        createRequest Get "http://localhost:1234/TestServer/RecordRequest" 
        |> withHeader (Accept "application/xml,text/html;q=0.3")
        |> withHeader (AcceptCharset "utf-8, utf-16;q=0.5" )
        |> withHeader (AcceptDatetime "Thu, 31 May 2007 20:35:00 GMT" )
        |> withHeader (AcceptLanguage "en-GB, en-US;q=0.1" )
        |> withHeader (Authorization  "QWxhZGRpbjpvcGVuIHNlc2FtZQ==" )
        |> withHeader (RequestHeader.Connection "conn1" )
        |> withHeader (RequestHeader.ContentMD5 "Q2hlY2sgSW50ZWdyaXR5IQ==" )
        |> withHeader (RequestHeader.ContentType "application/json" )
        |> withHeader (RequestHeader.Date (new DateTime(1999, 12, 31, 11, 59, 59)))
        |> withHeader (Expect 100 )
        |> withHeader (From "user@example.com" )
        |> withHeader (IfMatch "737060cd8c284d8af7ad3082f209582d" )
        |> withHeader (IfModifiedSince (new DateTime(2000, 12, 31, 11, 59, 59)))
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
        |> withHeader (RequestHeader.Via "1.0 fred, 1.1 example.com (Apache/1.1)" )
        |> withHeader (RequestHeader.Warning "199 Miscellaneous warning" )
        |> withHeader (Custom {name="X-Greeting"; value="Happy Birthday"})
        |> getResponseCode |> ignore

        recordedRequest.Value |> should not' (equal null)
        recordedRequest.Value.Headers.Accept |> should contain ("application/xml", 1m)
        recordedRequest.Value.Headers.Accept |> should contain ("text/html", 0.3m)
        recordedRequest.Value.Headers.AcceptCharset |> should contain ("utf-8", 1m)
        recordedRequest.Value.Headers.AcceptCharset |> should contain ("utf-16", 0.5m)
        recordedRequest.Value.Headers.["Accept-Datetime"] |> should equal ["Thu, 31 May 2007 20:35:00 GMT"]
        recordedRequest.Value.Headers.AcceptLanguage |> should contain ("en-GB", 1m)
        recordedRequest.Value.Headers.AcceptLanguage |> should contain ("en-US", 0.1m)
        recordedRequest.Value.Headers.Authorization |> should equal "QWxhZGRpbjpvcGVuIHNlc2FtZQ=="
        recordedRequest.Value.Headers.Connection |> should equal "conn1"
        recordedRequest.Value.Headers.["Content-MD5"] |> should equal ["Q2hlY2sgSW50ZWdyaXR5IQ=="]
        recordedRequest.Value.Headers.ContentType |> should equal "application/json"
        recordedRequest.Value.Headers.Date.Value |> should equal (new DateTime(1999, 12, 31, 11, 59, 59))
        recordedRequest.Value.Headers.["Expect"] |> should equal ["100"]
        recordedRequest.Value.Headers.["From"] |> should equal ["user@example.com"]
        recordedRequest.Value.Headers.IfMatch |> should equal ["737060cd8c284d8af7ad3082f209582d"]
        recordedRequest.Value.Headers.IfModifiedSince |> should equal (new DateTime(2000, 12, 31, 11, 59, 59))
        recordedRequest.Value.Headers.IfNoneMatch |> should equal ["737060cd8c284d8af7ad3082f209582d"]
        recordedRequest.Value.Headers.IfRange |> should equal "737060cd8c284d8af7ad3082f209582d"
        recordedRequest.Value.Headers.MaxForwards |> should equal 5
        recordedRequest.Value.Headers.["Origin"] |> should equal ["http://www.mybot.com"]
        recordedRequest.Value.Headers.["Pragma"] |> should equal ["no-cache"]
        recordedRequest.Value.Headers.["Proxy-Authorization"] |> should equal ["QWxhZGRpbjpvcGVuIHNlc2FtZQ=="]
        recordedRequest.Value.Headers.["Range"] |> should equal ["bytes=0-500"]
        recordedRequest.Value.Headers.["Referer"] |> should equal ["http://en.wikipedia.org/"]
        recordedRequest.Value.Headers.["Upgrade"] |> should contain "HTTP/2.0"
        recordedRequest.Value.Headers.["Upgrade"] |> should contain "SHTTP/1.3" 
        recordedRequest.Value.Headers.UserAgent |> should equal "(X11; Linux x86_64; rv:12.0) Gecko/20100101 Firefox/21.0"
        recordedRequest.Value.Headers.["Via"] |> should contain ("1.0 fred")
        recordedRequest.Value.Headers.["Via"] |> should contain ("1.1 example.com (Apache/1.1)")
        recordedRequest.Value.Headers.["Warning"] |> should equal ["199 Miscellaneous warning"]
        recordedRequest.Value.Headers.["X-Greeting"] |> should equal ["Happy Birthday"]

    [<Test>]
    member x.``Content-Length header is set automatically for Posts with a body`` () =
        createRequest Post "http://localhost:1234/TestServer/RecordRequest"
        |> withBody "Hi Mum"
        |> getResponseCode |> ignore
        recordedRequest.Value |> should not' (equal null)
        recordedRequest.Value.Headers.ContentLength |> should equal 6

    [<Test>]
    member x.``accept-encoding header is set automatically when decompression scheme is set`` () =
        createRequest Get "http://localhost:1234/TestServer/RecordRequest"
        |> withAutoDecompression (DecompressionScheme.Deflate ||| DecompressionScheme.GZip)
        |> getResponseCode |> ignore
        recordedRequest.Value |> should not' (equal null)
        recordedRequest.Value.Headers.AcceptEncoding |> should contain "gzip"
        recordedRequest.Value.Headers.AcceptEncoding |> should contain "deflate"

        // TODO: Seperate tests for the headers which get set automatically:
        // Cache-Control
        // Host
        // IfUnmodifiedSince

    [<Test>]
    member x.``all of the response headers should be available after a call to getResponse`` () =
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
        response.Headers.[ContentMD5] |> should equal "Q2hlY2sgSW50ZWdyaXR5IQ=="
        response.Headers.[ContentDisposition] |> should equal "attachment; filename=\"fname.ext\""
        response.Headers.[ContentRange] |> should equal "bytes 21010-47021/47022"
        response.Headers.[ContentType] |> should equal "text/html; charset=utf-8"
        let (parsedOK,_) = System.DateTime.TryParse(response.Headers.[Date])
        parsedOK |> should equal true
        response.Headers.[ETag] |> should equal "737060cd8c284d8af7ad3082f209582d"
        response.Headers.[Expires] |> should equal "Thu, 01 Dec 1994 16:00:00 GMT"
        response.Headers.[LastModified] |> should equal "Tue, 15 Nov 1994 12:45:26 +0000"
        response.Headers.[Link] |> should equal "</feed>; rel=\"alternate\""
        response.Headers.[Location] |> should equal "http://www.w3.org/pub/WWW/People.html"
        response.Headers.[P3P] |> should equal "CP=\"your_compact_policy\""
        response.Headers.[Pragma] |> should equal "no-cache"
        response.Headers.[ProxyAuthenticate] |> should equal "Basic"
        response.Headers.[Refresh] |> should equal "5; url=http://www.w3.org/pub/WWW/People.html"
        response.Headers.[RetryAfter] |> should equal "120"
        response.Headers.[Server] |> should equal "Microsoft-HTTPAPI/2.0"
        response.Headers.[StrictTransportSecurity] |> should equal "max-age=16070400; includeSubDomains"
        response.Headers.[Trailer] |> should equal "Max-Forwards"
        response.Headers.[TransferEncoding] |> should equal "chunked"
        response.Headers.[Vary] |> should equal "*"
        response.Headers.[Via] |> should equal "1.0 fred, 1.1 example.com (Apache/1.1)"
        response.Headers.[Warning] |> should equal "199 Miscellaneous warning"
        response.Headers.[WWWAuthenticate] |> should equal "Basic"
        response.Headers.[NonStandard("X-New-Fangled-Header")] |> should equal "some value"