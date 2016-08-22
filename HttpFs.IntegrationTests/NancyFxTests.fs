module HttpFs.IntegrationTests.NancyFxTests

open Hopac
open System
open System.IO
open System.Net
open System.Net.Cache
open System.Text
open NUnit.Framework
open FsUnit
open HttpFs.Client
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

let uriFor path =
  (Uri ("http://localhost:1234/TestServer" + path))

let runIgnore =
  getResponse
  >> Hopac.run
  >> (fun (r : HttpFs.Client.Response) -> (r :> IDisposable).Dispose())

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
    Request.create Get (uriFor "/RecordRequest") |> runIgnore
    HttpServer.recordedRequest.Value |> should not' (equal null)
    HttpServer.recordedRequest.Value.Headers.Connection.ToLowerInvariant() |> should equal "keep-alive"

    HttpServer.recordedRequest := null
    Request.create Get (uriFor "/RecordRequest") |> runIgnore
    HttpServer.recordedRequest.Value |> should not' (equal null)
    HttpServer.recordedRequest.Value.Headers.Connection |> should equal ""

  [<Test>]
  member x.``if KeepAlive is false, Connection set to 'Close' on every request`` () =
    Request.create Get (uriFor "/RecordRequest") |> Request.keepAlive false |> runIgnore
    HttpServer.recordedRequest.Value |> should not' (equal null)
    HttpServer.recordedRequest.Value.Headers.Connection.ToLowerInvariant() |> should equal "close"

    HttpServer.recordedRequest := null
    Request.create Get (uriFor "/RecordRequest") |> Request.keepAlive false |> runIgnore
    HttpServer.recordedRequest.Value |> should not' (equal null)
    HttpServer.recordedRequest.Value.Headers.Connection.ToLowerInvariant() |> should equal "close"

  [<Test>] 
  member x.``createRequest should set everything correctly in the HTTP request`` ()=
    Request.create Post (uriFor "/RecordRequest")
    |> Request.queryStringItem "search" "jeebus"
    |> Request.queryStringItem "qs2" "hi mum"
    |> Request.setHeader (Accept "application/xml")
    |> Request.cookie (Cookie.create("SESSIONID", "1234"))
    |> Request.bodyString "some XML or whatever"
    |> runIgnore
    HttpServer.recordedRequest.Value |> should not' (equal null)
    HttpServer.recordedRequest.Value.Query?search.ToString() |> should equal "jeebus"
    HttpServer.recordedRequest.Value.Query?qs2.ToString() |> should equal "hi mum"
    HttpServer.recordedRequest.Value.Headers.Accept |> should contain ("application/xml", 1m)
    HttpServer.recordedRequest.Value.Cookies.["SESSIONID"] |> should contain "1234"
    use bodyStream = new StreamReader(HttpServer.recordedRequest.Value.Body,Encoding.GetEncoding(1252))
    bodyStream.ReadToEnd() |> should equal "some XML or whatever"

  [<Test>]
  member x.``readResponseBodyAsString should return the entity body as a string`` () =
    Request.create Get (uriFor "/GotBody")
    |> Request.responseAsString
    |> run
    |> should equal "Check out my sexy body"

  [<Test>]
  member x.``readResponseBodyAsString should return an empty string when there is no body`` () =
    Request.create Get (uriFor "/GoodStatusCode")
    |> Request.responseAsString
    |> run
    |> should equal ""

  [<Test>]
  member x.``all details of the response should be available after a call to getResponse`` () =
    let request = Request.create Get (uriFor "/AllTheThings")
    use response = request |> getResponse |> run
    response.statusCode |> should equal 418
    let body = Response.readBodyAsString response |> run
    body |> should equal "Some JSON or whatever"
    response.contentLength |> should equal 21
    response.cookies.["cookie1"] |> should equal "chocolate+chip" // cookies get encoded
    response.cookies.["cookie2"] |> should equal "smarties"
    response.headers.[ContentEncoding] |> should equal "gzip"
    response.headers.[NonStandard("X-New-Fangled-Header")] |> should equal "some value"

  [<Test>]
  member x.``simplest possible response`` () =
    let request = Request.create Get (uriFor "/NoCookies")
    use response = request |> getResponse |> run
    response.statusCode |> should equal 200
    use ms = new MemoryStream()
    response.body.CopyTo ms // Windows workaround "this stream does not support seek"
    ms.Length |> should equal 4
    response.cookies.IsEmpty |> should equal true

  [<Test>]
  member x.``getResponseAsync, given a request with an invalid url, throws an exception`` () =
    (fun() ->
      Request.create Get (Uri "www.google.com")
      |> getResponse
      |> ignore)
    |> should throw typeof<UriFormatException>

  [<Test>]
  member x.``when called on a non-existant page, returns 404`` () =
    use response = Request.create Get (uriFor "/NoPage") |> getResponse |> run
    response.statusCode |> should equal 404

  [<Test>] 
  member x.``all of the manually-set request headers get sent to the server`` ()=
    Request.create Get (uriFor "/RecordRequest")
    |> Request.setHeader (Accept "application/xml,text/html;q=0.3")
    |> Request.setHeader (AcceptCharset "utf-8, utf-16;q=0.5" )
    |> Request.setHeader (AcceptDatetime "Thu, 31 May 2007 20:35:00 GMT" )
    |> Request.setHeader (AcceptLanguage "en-GB, en-US;q=0.1" )
    |> Request.setHeader (Authorization  "QWxhZGRpbjpvcGVuIHNlc2FtZQ==" )
    |> Request.setHeader (Connection "conn1" )
    |> Request.setHeader (ContentMD5 "Q2hlY2sgSW50ZWdyaXR5IQ==" )
    |> Request.setHeader (ContentType (ContentType.create("application", "json")))
    |> Request.setHeader (Date (new DateTime(1999, 12, 31, 11, 59, 59, DateTimeKind.Utc)))
    |> Request.setHeader (From "user@example.com" )
    |> Request.setHeader (IfMatch "737060cd8c284d8af7ad3082f209582d" )
    |> Request.setHeader (IfModifiedSince (new DateTime(2000, 12, 31, 11, 59, 59, DateTimeKind.Utc)))
    |> Request.setHeader (IfNoneMatch "737060cd8c284d8af7ad3082f209582d" )
    |> Request.setHeader (IfRange "737060cd8c284d8af7ad3082f209582d" )
    |> Request.setHeader (MaxForwards 5 )
    |> Request.setHeader (Origin "http://www.mybot.com" )
    |> Request.setHeader (RequestHeader.Pragma "no-cache" )
    |> Request.setHeader (ProxyAuthorization "QWxhZGRpbjpvcGVuIHNlc2FtZQ==" )
    |> Request.setHeader (Range {start=0L; finish=500L} )
    |> Request.setHeader (Referer "http://en.wikipedia.org/" )
    |> Request.setHeader (Upgrade "HTTP/2.0, SHTTP/1.3" )
    |> Request.setHeader (UserAgent "(X11; Linux x86_64; rv:12.0) Gecko/20100101 Firefox/21.0" )
    |> Request.setHeader (Via "1.0 fred, 1.1 example.com (Apache/1.1)" )
    |> Request.setHeader (Warning "199 Miscellaneous warning" )
    |> Request.setHeader (Custom ("X-Greeting", "Happy Birthday"))
    |> runIgnore

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
    Request.create Post (uriFor "/RecordRequest")
    |> Request.bodyString "Hi Mum"
    |> runIgnore
    HttpServer.recordedRequest.Value |> should not' (equal null)
    HttpServer.recordedRequest.Value.Headers.ContentLength |> should equal 6

  [<Test>]
  member x.``accept-encoding header is set automatically when decompression scheme is set`` () =
    Request.create Get (uriFor "/RecordRequest")
    |> Request.autoDecompression (DecompressionScheme.Deflate ||| DecompressionScheme.GZip)
    |> runIgnore
    HttpServer.recordedRequest.Value |> should not' (equal null)
    HttpServer.recordedRequest.Value.Headers.AcceptEncoding |> should contain "gzip"
    HttpServer.recordedRequest.Value.Headers.AcceptEncoding |> should contain "deflate"

    // TODO: Separate tests for the headers which get set automatically:
    // Cache-Control
    // Host
    // IfUnmodifiedSince

  [<Test>]
  member x.``all of the response headers are available after a call to getResponse`` () =
    use response = Request.create Get (uriFor "/AllHeaders") |> getResponse |> run
    response.headers.[AccessControlAllowOrigin] |> should equal "*"
    response.headers.[AcceptRanges] |> should equal "bytes"
    response.headers.[Age] |> should equal "12"
    response.headers.[Allow] |> should equal "GET, HEAD"
    response.headers.[CacheControl] |> should equal "max-age=3600"
    //response.headers.[Connection] |> should equal "close" // don't seem to get connection header from nancy
    response.headers.[ContentEncoding] |> should equal "gzip"
    response.headers.[ContentLanguage] |> should equal "EN-gb"
    response.headers.[ContentLocation] |> should equal "/index.htm"
    response.headers.[ContentMD5Response] |> should equal "Q2hlY2sgSW50ZWdyaXR5IQ=="
    response.headers.[ContentDisposition] |> should equal "attachment; filename=\"fname.ext\""
    response.headers.[ContentRange] |> should equal "bytes 21010-47021/47022"
    response.headers.[ContentTypeResponse] |> should equal "text/html; charset=utf-8"
    let (parsedOK,_) = System.DateTime.TryParse(response.headers.[DateResponse])
    parsedOK |> should equal true
    response.headers.[ETag] |> should equal "737060cd8c284d8af7ad3082f209582d"
    response.headers.[Expires] |> should equal "Thu, 01 Dec 1994 16:00:00 GMT"
    response.headers.[LastModified] |> should equal "Tue, 15 Nov 1994 12:45:26 +0000"
    response.headers.[Link] |> should equal "</feed>; rel=\"alternate\""
    response.headers.[Location] |> should equal "http://www.w3.org/pub/WWW/People.html"
    response.headers.[P3P] |> should equal "CP=\"your_compact_policy\""
    response.headers.[PragmaResponse] |> should equal "no-cache"
    response.headers.[ProxyAuthenticate] |> should equal "Basic"
    response.headers.[Refresh] |> should equal "5; url=http://www.w3.org/pub/WWW/People.html"
    response.headers.[RetryAfter] |> should equal "120"
    response.headers.[Server] |> should contain "HTTPAPI/"
    response.headers.[StrictTransportSecurity] |> should equal "max-age=16070400; includeSubDomains"
    response.headers.[Trailer] |> should equal "Max-Forwards"
    response.headers.[TransferEncoding] |> should equal "identity"
    response.headers.[Vary] |> should equal "*"
    response.headers.[ViaResponse] |> should equal "1.0 fred, 1.1 example.com (Apache/1.1)"
    response.headers.[WarningResponse] |> should equal "199 Miscellaneous warning"
    response.headers.[WWWAuthenticate] |> should equal "Basic"
    response.headers.[NonStandard("X-New-Fangled-Header")] |> should equal "some value"

  [<Test>]
  member x.``if body character encoding is specified, encodes the request body with it`` () =
    Request.create Post (uriFor "/RecordRequest")
    |> Request.bodyStringEncoded "¥§±Æ" utf8 // random UTF-8 characters
    |> runIgnore

    use bodyStream = new StreamReader(HttpServer.recordedRequest.Value.Body,Encoding.GetEncoding("UTF-8"))
    bodyStream.ReadToEnd() |> should equal "¥§±Æ"

  [<Test>]
  member x.``response charset SPECIFIED, is used regardless of Content-Type header`` () =
    let responseBodyString =
      Request.create Get (uriFor "/MoonLanguageCorrectEncoding")
      |> Request.responseCharacterEncoding (Encoding.GetEncoding "utf-16")
      |> Request.responseAsString
      |> run

    responseBodyString |> should equal "迿ꞧ쒿" // "яЏ§§їДЙ" (as encoded with windows-1251) decoded with utf-16

  [<Test>]
  member x.``response charset IS NOT SPECIFIED, Content-Type header is used`` () =
    let responseBodyString =
      Request.create Get (uriFor "/MoonLanguageCorrectEncoding")
      |> Request.responseAsString
      |> run

    responseBodyString|> should equal "яЏ§§їДЙ"

  [<Test>]
  member x.``response charset IS NOT SPECIFIED, NO Content-Type header, body read by default as Latin 1`` () =
    let expected = "ÿ§§¿ÄÉ" // "яЏ§§їДЙ" (as encoded with windows-1251) decoded with ISO-8859-1 (Latin 1)

    let response =
      Request.create Get (uriFor "/MoonLanguageTextPlainNoEncoding")
      |> Request.responseAsString
      |> run
    response |> should equal expected

    let response = Request.create Get (uriFor "/MoonLanguageApplicationXmlNoEncoding") |> Request.responseAsString |> run
    response |> should equal expected

  [<Test>]
  member x.``assumes utf8 encoding for invalid Content-Type charset when reading string`` () =
    try
      Request.create Get (uriFor "/MoonLanguageInvalidEncoding")
      |> Request.responseAsString
      |> run
      |> ignore
    with :? ArgumentException as e ->
      Assert.Fail "should default to utf8"

  // .Net encoder doesn't like utf8, seems to need utf-8
  [<Test>]
  member x.``if the response character encoding is specified as 'utf8', uses 'utf-8' instead`` () =
    let str =
      Request.create Get (uriFor "/utf8")
      |> Request.responseAsString
      |> run
    str |> should equal "'Why do you hate me so much, Windows?!' - utf8"

  [<Test>]
  member x.``if the response character encoding is specified as 'utf16', uses 'utf-16' instead`` () =
    let str = Request.create Get (uriFor "/utf16") |> Request.responseAsString |> run
    str |> should equal "'Why are you so picky, Windows?!' - utf16"

  [<Test>]
  member x.``cookies are not kept during an automatic redirect`` () =
    use response =
      Request.create Get (uriFor "/CookieRedirect")
      |> getResponse
      |> run

    response.statusCode |> should equal 200
    response.cookies.ContainsKey "cookie1" |> should equal false

  [<Test>]
  member x.``reading the body as bytes works properly`` () =
    use response =
      Request.create Get (uriFor "/Raw")
      |> getResponse
      |> run
    let expected =
      [| 98uy
         111uy
         100uy
         121uy |]
    let actual = Response.readBodyAsBytes response |> run
    actual |> should equal expected

  [<Test>]
  member x.``when there is no body, reading it as bytes gives an empty array`` () =
    use response = Request.create Get (uriFor "/GoodStatusCode") |> getResponse |> run
    use ms = new MemoryStream()
    response.body.CopyTo ms // Windows workaround "this stream does not support seek"
    Assert.That(ms.Length, Is.EqualTo(0), "Should be zero length")

  [<Test>]
  member x.``readResponseBodyAsString can read the response body`` () =
    Request.create Get (uriFor "/Raw")
    |> Request.responseAsString
    |> run
    |> should equal "body"

  [<Test>]
  member x.``Closing the response body stream retrieved from getResponseAsync does not cause an exception`` () =
    use response =
      Request.create Get (uriFor "/Raw")
      |> getResponse
      |> run
    response.body.Close ()

  [<Test; Ignore "exception not thrown on Mono - investigate">]
  /// Timeout follows .Net behaviour and throws WebException exception when reached.
  /// https://msdn.microsoft.com/en-us/library/system.net.httpwebrequest.timeout%28v=vs.110%29.aspx
  member x.``if the resource is not returned within Timeout, throw WebException`` () =
    (fun() ->
      Request.create Post (uriFor "/SlowResponse")
      |> Request.timeout 1000<ms>
      |> Request.bodyString "hi mum"
      |> Request.responseAsString
      |> run
      |> ignore)
    |> should throw typeof<WebException>

  [<Test>]
  member x.``Get method works`` () =
    use resp =
      Request.create Get (uriFor "/Get")
      |> getResponse
      |> run

    resp.statusCode |> should equal 200

  [<Test>]
  member x.``Options method works`` () =
    use resp =
      Request.create Options (uriFor "/Options")
      |> getResponse
      |> run

    resp.statusCode |> should equal 200

  [<Test>]
  member x.``Post method works`` () =
    use resp =
      Request.create Post (uriFor "/Post") 
      |> Request.bodyString "hi mum" // posts need a body in Nancy
      |> getResponse
      |> run

    resp.statusCode |> should equal 200

  [<Test>]
  member x.``Patch method works`` () =
    Request.create Patch (uriFor "/Patch")
    |> getResponse
    |> run
    |> fun r ->
      r.statusCode |> should equal 200

  [<Test>]
  member x.``Head method works`` () =
    use resp =
      Request.create Head (uriFor "/Get")
      |> getResponse
      |> run

    resp.statusCode |> should equal 200
    // Head method automatically handled for Get methods in Nancy

  [<Test>]
  member x.``Put method works`` () =
    use resp =
      Request.create Put (uriFor "/Put")
      |> Request.bodyString "hi mum" // puts need a body in Nancy
      |> getResponse
      |> run

    resp.statusCode |> should equal 200

  [<Test>]
  member x.``Delete method works`` () =
    use resp =
      Request.create Delete (uriFor "/Delete")
      |> getResponse
      |> run

    resp.statusCode |> should equal 200

  [<Test>]
  member x.``getResponse.ResponseUri should contain URI that responded to the request`` () =
    // Is going to redirect to another route and return GET 200.
    let request =
      Request.create Post (uriFor "/Redirect")
      |> Request.bodyString "hi mum" // posts need a body in Nancy

    use resp = request |> getResponse |> run
    resp.statusCode |> should equal 200
    resp.responseUri.ToString() |> should equal "http://localhost:1234/TestServer/GoodStatusCode"

  // Nancy doesn't support Trace or Connect HTTP methods, so we can't test them easily

  // TODO: test proxy - approach below doesn't seem to work, even without port specified in proxy (which appends it to the end of the URL)
  // There's a script called 'test proxy' which can be used to test it manually.

//    [<Test>]
//    member x.``requests with a proxy set use the proxy details`` () =
//        createRequest Get "http://localhost:1234/TestServer/NoPage"
//        |> withProxy { Address = "localhost:1234/TestServer/RecordRequest"; Port = 1234; Credentials = ProxyCredentials.Default }
//        |> getResponseCode |> ignore
//        HttpServer.recordedRequest.Value |> should not' (equal null)

  [<Test>]
  member x.``returns the uploaded file names`` () =
    let firstCt, secondCt =
      ContentType.parse "text/plain" |> Option.get,
      ContentType.parse "text/plain" |> Option.get

    let req =
      Request.create Post (uriFor "/filenames")
      |> Request.body
          //([ SingleFile ("file", ("file1.txt", firstCt, Plain "Hello World")) ]|> BodyForm)
                          // example from http://www.w3.org/TR/html401/interact/forms.html
          ([ NameValue ("submit-name", "Larry")
             FormFile ("files", ("file1.txt", firstCt, Plain "Hello World"))
             FormFile ("files", ("file2.gif", secondCt, Plain "...contents of file2.gif..."))
          ]
          |> BodyForm)

    let response = req |> Request.responseAsString |> run

    for fileName in [ "file1.txt"; "file2.gif" ] do
      Assert.That(response, Is.StringContaining(fileName))