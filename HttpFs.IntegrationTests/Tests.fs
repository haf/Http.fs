module HttpFs.IntegrationTests.Tests

open System
open System.IO
open System.Net
open System.Net.Cache
open System.Text
open Expecto
open Hopac
open HttpFs.Client
open HttpServer

let runIgnore req = async {
  use! asyncRec =
    req
    |> getResponse
    |> Alt.toAsync

  ()
}

let fstChoiceOf2 =
  function
  | Choice1Of2 x -> x
  | x -> Tests.failtestf "%A was not a %A" x Choice1Of2

let getQueryParam name (httpRequest: Suave.Http.HttpRequest) =
  httpRequest.queryParam name |> fstChoiceOf2

let getHeader name (httpRequest: Suave.Http.HttpRequest) =
  httpRequest.header name |> fstChoiceOf2

[<Tests>]
let recorded =
  testSequenced <| testList "integration recorded" [
    // HttpClient sets Keep-Alive on every request unless explicitly set to "Close"
    // testCase "connection set to 'Keep-Alive' on the first request, but not subsequent ones" <| fun _ ->
    //   Request.create Get (uriFor "/RecordRequest") |> runIgnore
    //   let req = HttpServer.recordedRequest
    //   Expect.isSome req "request should not be none"
    //   Expect.equal ((req.Value |> getHeader "connection").ToLowerInvariant()) "keep-alive" "header should be keep-alive"

    //   HttpServer.recordedRequest <- None
    //   Request.create Get (uriFor "/RecordRequest") |> runIgnore
    //   let req = HttpServer.recordedRequest
    //   Expect.isSome req "request should not be none"
    //   Expect.equal (req.Value |> getHeader "connection") "" "header should be empty"

    testCaseAsync "Connection set to 'Close' on every request" <| async {
      do! Request.create Get (uriFor "/RecordRequest") |> Request.setHeader (Connection "Close") |> runIgnore
      let req = HttpServer.recordedRequest
      Expect.isSome req "request should not be none"
      Expect.equal ((req.Value |> getHeader "connection").ToLowerInvariant()) "close" "connection should be set to close"

      HttpServer.recordedRequest <- None
      do! Request.create Get (uriFor "/RecordRequest") |> Request.setHeader (Connection "Close") |> runIgnore
      let req = HttpServer.recordedRequest
      Expect.isSome req "request should not be none"
      Expect.equal ((req.Value |> getHeader "connection").ToLowerInvariant()) "close" "connection should be set to close"
    }

    testCaseAsync "createRequest should set everything correctly in the HTTP request" <| async {
      do!
        Request.create Post (uriFor "/RecordRequest")
        |> Request.queryStringItem "search" "jeebus"
        |> Request.queryStringItem "qs2" "hi mum"
        |> Request.setHeader (Accept "application/xml")
        |> Request.cookie (Cookie.create("SESSIONID", "1234"))
        |> Request.bodyString "some XML or whatever"
        |> runIgnore

      let req = HttpServer.recordedRequest
      Expect.isSome req "request should be some"
      Expect.equal (req.Value |> getQueryParam "search") "jeebus" "should be equal"
      Expect.equal (req.Value |> getQueryParam "qs2") "hi mum" "should be equal"
      Expect.stringContains (req.Value |> getHeader "accept") "application/xml" "should contain accept"
      Expect.stringContains (req.Value |> getHeader "cookie") "SESSIONID=1234" "should contain cookie"
      
      let body = Encoding.UTF8.GetString(req.Value.rawForm)
      Expect.equal body "some XML or whatever" "body should be equal"
    }

    testCaseAsync "all of the manually-set request headers get sent to the server" <| async {
      do!
        Request.create Post (uriFor "/RecordRequest")
        |> Request.setHeader (Accept "application/xml,text/html;q=0.3")
        |> Request.setHeader (AcceptCharset "utf-8, utf-16;q=0.5")
        |> Request.setHeader (AcceptDatetime "Thu, 31 May 2007 20:35:00 GMT")
        |> Request.setHeader (AcceptLanguage "en-GB, en-US;q=0.1")
        |> Request.setHeader (Authorization  "Basic QWxhZGRpbjpvcGVuIHNlc2FtZQ==")
        // HttpClient discards any connection header value that is not Keep-Alive/Close
        // and overrides it with "Keep-Alive"
        |> Request.setHeader (Connection "close")
        |> Request.setHeader (ContentMD5 "Q2hlY2sgSW50ZWdyaXR5IQ==")
        |> Request.setHeader (ContentType (ContentType.create("application", "json")))
        |> Request.setHeader (Date (DateTime(1999, 12, 31, 11, 59, 59, DateTimeKind.Utc)))
        |> Request.setHeader (From "user@example.com" )
        |> Request.setHeader (IfMatch "\"737060cd8c284d8af7ad3082f209582d\"")
        |> Request.setHeader (IfModifiedSince (DateTime(2000, 12, 31, 11, 59, 59, DateTimeKind.Utc)))
        |> Request.setHeader (IfNoneMatch "\"737060cd8c284d8af7ad3082f209582d\"")
        |> Request.setHeader (IfRange "\"737060cd8c284d8af7ad3082f209582d\"")
        |> Request.setHeader (MaxForwards 5)
        |> Request.setHeader (Origin "http://www.mybot.com")
        |> Request.setHeader (RequestHeader.Pragma "no-cache" )
        |> Request.setHeader (ProxyAuthorization "Basic QWxhZGRpbjpvcGVuIHNlc2FtZQ==" )
        |> Request.setHeader (Range {start= Some 0L; finish= Some 500L} )
        |> Request.setHeader (Referer "http://en.wikipedia.org/" )
        |> Request.setHeader (Upgrade "HTTP/2.0, SHTTP/1.3" )
        |> Request.setHeader (UserAgent "(X11; Linux x86_64; rv:12.0) Gecko/20100101 Firefox/21.0")
        |> Request.setHeader (Via "1.0 fred, 1.1 example.com (Apache/1.1)")
        |> Request.setHeader (Warning "199 - \"Miscellaneous warning\"")
        |> Request.setHeader (Custom ("X-Greeting", "Happy Birthday"))
        |> runIgnore

      let req = HttpServer.recordedRequest
      Expect.isSome req "request should be some"
      Expect.stringContains (req.Value |> getHeader "accept") "application/xml" "accept should be set"
      Expect.stringContains (req.Value |> getHeader "accept") "text/html" "accept should be set"
      Expect.stringContains (req.Value |> getHeader "accept-charset") "utf-8" "accept-charset should be set"
      Expect.stringContains (req.Value |> getHeader "accept-charset") "utf-16" "accept-charset should be set"
      Expect.equal (req.Value |> getHeader "accept-datetime") "Thu, 31 May 2007 20:35:00 GMT" "accept-datetime should be equal"
      Expect.stringContains (req.Value |> getHeader "accept-language") "en-GB" "accept-language should be set"
      Expect.stringContains (req.Value |> getHeader "accept-language") "en-US" "accept-language should be set"
      Expect.equal (req.Value |> getHeader "authorization") "Basic QWxhZGRpbjpvcGVuIHNlc2FtZQ==" "authorization should be equal"
      Expect.stringContains ((req.Value |> getHeader "connection").ToLowerInvariant()) "close" "connection should be set"
      Expect.equal (req.Value |> getHeader "content-md5") "Q2hlY2sgSW50ZWdyaXR5IQ==" "content-md5 should be equal"
      Expect.equal (req.Value |> getHeader "content-type") "application/json" "content-type should be equal"
      Expect.equal (req.Value |> getHeader "date") "Fri, 31 Dec 1999 11:59:59 GMT" "date should be equal"
      Expect.equal (req.Value |> getHeader "from") "user@example.com" "from should be equal"
      Expect.equal (req.Value |> getHeader "if-match") "\"737060cd8c284d8af7ad3082f209582d\"" "if-match should be equal"
      Expect.equal (req.Value |> getHeader "if-modified-since") "Sun, 31 Dec 2000 11:59:59 GMT" "if-modified-since should be equal"
      Expect.equal (req.Value |> getHeader "if-none-match") "\"737060cd8c284d8af7ad3082f209582d\"" "if-none-match should be equal"
      Expect.equal (req.Value |> getHeader "if-range") "\"737060cd8c284d8af7ad3082f209582d\"""if-range should match"
      Expect.equal (req.Value |> getHeader "max-forwards") "5" "max-forwards should be equal"
      Expect.equal (req.Value |> getHeader "origin") "http://www.mybot.com" "origin should be equal"
      Expect.equal (req.Value |> getHeader "pragma") "no-cache" "pragma should be equal"
      Expect.equal (req.Value |> getHeader "proxy-authorization") "Basic QWxhZGRpbjpvcGVuIHNlc2FtZQ==" "proxy-authorization should be equal"
      Expect.equal (req.Value |> getHeader "range") "bytes=0-500" "range should be equal"
      Expect.equal (req.Value |> getHeader "referer") "http://en.wikipedia.org/" "referer should be equal"
      Expect.stringContains (req.Value |> getHeader "upgrade") "HTTP/2.0" "upgrade should be set"
      Expect.stringContains (req.Value |> getHeader "upgrade") "SHTTP/1.3" "upgrade should be set"
      Expect.equal (req.Value |> getHeader "user-agent") "(X11; Linux x86_64; rv:12.0) Gecko/20100101 Firefox/21.0" "user-agent should be equal"
      Expect.stringContains (req.Value |> getHeader "via") "1.0 fred" "via should be set"
      Expect.stringContains (req.Value |> getHeader "via") "1.1 example.com (Apache/1.1)" "via should be set"
      Expect.equal (req.Value |> getHeader "warning") "199 - \"Miscellaneous warning\"" "warning should be equal"
      Expect.equal (req.Value |> getHeader "x-greeting") "Happy Birthday" "x-greeting should be equal"
    }

    testCaseAsync "Content-Length header is set automatically for Posts with a body" <| async {
      do!
        Request.create Post (uriFor "/RecordRequest")
        |> Request.bodyString "Hi Mum"
        |> runIgnore

      let req = HttpServer.recordedRequest
      Expect.isSome req "request should be some"
      Expect.equal (req.Value |> getHeader "content-length") "6" "content-length should be equal"
    }

    // automatic decompression needs to be set at the HttpMessageHandler level
    // testCase "accept-encoding header is set automatically when decompression scheme is set" <| fun _ ->
    //   Request.create Get (uriFor "/RecordRequest")
    //   |> Request.autoDecompression (DecompressionScheme.Deflate ||| DecompressionScheme.GZip)
    //   |> runIgnore

    //   let req = HttpServer.recordedRequest
    //   Expect.isSome req "request should be some"
    //   Expect.stringContains (req.Value |> getHeader "accept-encoding") "gzip" "accept-encoding should be set"
    //   Expect.stringContains (req.Value |> getHeader "accept-encoding") "deflate" "accept-encoding should be set"

    testCaseAsync "if body character encoding is specified, encodes the request body with it" <| async {
      do!
        Request.create Post (uriFor "/RecordRequest")
        |> Request.bodyStringEncoded "¥§±Æ" Encoding.UTF8
        |> runIgnore

      Expect.equal (Encoding.UTF8.GetString(HttpServer.recordedRequest.Value.rawForm)) "¥§±Æ" "body should be equal"
    }
  ]

[<Tests>]
let tests =
  testList "integration" [
    testCaseAsync "when called on a non-existant page, returns 404" <| async {
      use! response = Request.create Get (uriFor "/NoPage") |> getResponse |> Alt.toAsync
      Expect.equal response.statusCode 404 "statusCode should be equal"
    }

    testCaseAsync "readResponseBodyAsString should return the entity body as a string" <| async {
      let! body =
        Request.create Get (uriFor "/GotBody")
        |> Request.responseAsString
        |> Job.toAsync
      
      Expect.equal body "Check out my sexy body" "body should be equal"
    }

    testCaseAsync "readResponseBodyAsString should return an empty string when there is no body" <| async {
      let! body =
        Request.create Get (uriFor "/GoodStatusCode")
        |> Request.responseAsString
        |> Job.toAsync

      Expect.equal body "" "body should be equal"
    }

    testCaseAsync "all details of the response should be available after a call to getResponse" <| async {
      let request = Request.create Get (uriFor "/AllTheThings")
      use! response = request |> getResponse |> Alt.toAsync
      Expect.equal response.statusCode 202 "statusCode should be equal"
      let! body = Response.readBodyAsString response |> Job.toAsync
      Expect.equal body "Some JSON or whatever" "body should be equal"
      Expect.equal response.contentLength (Some 21L) "contentLength should be equal"
      Expect.equal response.cookies.["cookie1"] "chocolate chip" "cookie should be equal"
      Expect.equal response.cookies.["cookie2"] "smarties" "cookie should be equal"
      Expect.equal response.headers.[ContentEncoding] "gzip" "contentEncoding should be equal"
      Expect.equal response.headers.[NonStandard("X-New-Fangled-Header")] "some value" "non standard header should be equal"
    }

    testCaseAsync "simplest possible response" <| async {
      let request = Request.create Get (uriFor "/NoCookies")
      use! response = request |> getResponse |> Alt.toAsync
      Expect.equal response.statusCode 200 "statusCode should be equal"

      use ms = new MemoryStream()
      response.body.CopyTo ms // Windows workaround "this stream does not support seek"
      Expect.equal ms.Length 4L "stream length should be equal"
      Expect.isEmpty response.cookies "cookies should be empty"
    }

    testCase "getResponseAsync, given a request with an invalid url, throws an exception" <| fun _ ->
      let doReq = fun () ->
        Request.create Get (Uri "www.google.com")
        |> getResponse
        |> ignore

      Expect.throwsT<UriFormatException> doReq "should throw"

    testCaseAsync "all of the response headers are available after a call to getResponse" <| async {
      use! resp = Request.create Get (uriFor "/AllHeaders") |> getResponse |> Alt.toAsync
      Expect.equal resp.headers.[AccessControlAllowOrigin] "*" "should be equal"
      Expect.equal resp.headers.[AcceptRanges] "bytes" "should be equal"
      Expect.equal resp.headers.[Age] "12" "should be equal"
      Expect.equal resp.headers.[Allow] "GET, HEAD" "should be equal"
      Expect.equal resp.headers.[CacheControl] "max-age=3600" "should be equal"
      Expect.equal resp.headers.[ResponseHeader.Connection] "close" "should be equal"
      Expect.equal resp.headers.[ContentEncoding] "gzip" "should be equal"
      Expect.equal resp.headers.[ContentLanguage] "EN-gb" "should be equal"
      Expect.equal resp.headers.[ContentLocation] "/index.htm" "should be equal"
      Expect.equal resp.headers.[ContentMD5Response] "Q2hlY2sgSW50ZWdyaXR5IQ==" "should be equal"
      Expect.equal resp.headers.[ContentDisposition] "attachment; filename=\"fname.ext\"" "should be equal"
      Expect.equal resp.headers.[ContentRange] "bytes 21010-47021/47022" "should be equal"
      Expect.equal resp.headers.[ContentTypeResponse] "text/html; charset=utf-8" "should be equal"
      let (parsedOK,_) = System.DateTime.TryParse(resp.headers.[DateResponse])
      Expect.equal parsedOK true "should be equal"
      Expect.equal resp.headers.[ETag] "737060cd8c284d8af7ad3082f209582d" "should be equal"
      Expect.equal resp.headers.[Expires] "Thu 01 Dec 1994 16:00:00 GMT" "should be equal"
      Expect.equal resp.headers.[LastModified] "Tue 15 Nov 1994 12:45:26 +0000" "should be equal"
      Expect.equal resp.headers.[Link] "</feed>; rel=\"alternate\"" "should be equal"
      Expect.equal resp.headers.[Location] "http://www.w3.org/pub/WWW/People.html" "should be equal"
      Expect.equal resp.headers.[P3P] "CP=\"your_compact_policy\"" "should be equal"
      Expect.equal resp.headers.[PragmaResponse] "no-cache" "should be equal"
      Expect.equal resp.headers.[ProxyAuthenticate] "Basic" "should be equal"
      Expect.equal resp.headers.[Refresh] "5; url=http://www.w3.org/pub/WWW/People.html" "should be equal"
      Expect.equal resp.headers.[RetryAfter] "120" "should be equal"
      Expect.stringContains resp.headers.[Server] "(https://suave.io)" "should be set"
      Expect.stringContains resp.headers.[SetCookie] "test1=123;test2=456" "should be set"
      Expect.equal resp.headers.[StrictTransportSecurity] "max-age=16070400; includeSubDomains" "should be equal"
      Expect.equal resp.headers.[Trailer] "Max-Forwards" "should be equal"
      Expect.equal resp.headers.[TransferEncoding] "identity" "should be equal"
      Expect.equal resp.headers.[Vary] "*" "should be equal"
      Expect.equal resp.headers.[ViaResponse] "1.0 fred 1.1 example.com (Apache/1.1)" "should be equal"
      Expect.equal resp.headers.[WarningResponse] "199 Miscellaneous warning" "should be equal"
      Expect.equal resp.headers.[WWWAuthenticate] "Basic" "should be equal"
      Expect.equal resp.headers.[NonStandard("X-New-Fangled-Header")] "some value" "should be equal"
    }

    testCaseAsync "response charset SPECIFIED, is used regardless of Content-Type header" <| async {
      let! responseBodyString =
        Request.create Get (uriFor "/MoonLanguageCorrectEncoding")
        |> Request.responseCharacterEncoding (Encoding.GetEncoding "utf-16")
        |> Request.responseAsString
        |> Job.toAsync

      Expect.equal responseBodyString "迿ꞧ쒿" "body should be equal" // "яЏ§§їДЙ" (as encoded with windows-1251) decoded with utf-16
    }

    testCaseAsync "response charset IS NOT SPECIFIED, Content-Type header is used" <| async {
      let! responseBodyString =
        Request.create Get (uriFor "/MoonLanguageCorrectEncoding")
        |> Request.responseAsString
        |> Job.toAsync

      Expect.equal responseBodyString "яЏ§§їДЙ" "body should be equal"
    }

    testCaseAsync "response charset IS NOT SPECIFIED, NO Content-Type header, body read by default as UTF8" <| async {
      let expected = "яЏ§§їДЙ"

      let! response =
        Request.create Get (uriFor "/MoonLanguageTextPlainNoEncoding")
        |> Request.responseAsString
        |> Job.toAsync

      Expect.equal response expected "body should be equal"

      let! response = Request.create Get (uriFor "/MoonLanguageApplicationXmlNoEncoding") |> Request.responseAsString |> Job.toAsync
      Expect.equal response expected "body should be equal"
    }

    testCaseAsync "assumes utf8 encoding for invalid Content-Type charset when reading string" <| async {
      let req = Request.create Get (uriFor "/MoonLanguageInvalidEncoding")
      try
        let! _ =
          req
          |> Request.responseAsString
          |> Job.toAsync

        ()
      with :? ArgumentException as e ->
        Tests.failtest "should default to utf8"
    }

    // .Net encoder doesn't like utf8, seems to need utf-8
    testCaseAsync "if the response character encoding is specified as 'utf8', uses 'utf-8' instead" <| async {
      let! str =
        Request.create Get (uriFor "/utf8")
        |> Request.responseAsString
        |> Job.toAsync

      Expect.equal str "'Why do you hate me so much, Windows?!' - utf8" "body should be equal"
    }

    testCaseAsync "if the response character encoding is specified as 'utf16', uses 'utf-16' instead" <| async {
      let! str = Request.create Get (uriFor "/utf16") |> Request.responseAsString |> Job.toAsync

      Expect.equal str "'Why are you so picky, Windows?!' - utf16" "body should be equal"
    }

    testCaseAsync "cookies are not kept during an automatic redirect" <| async {
      use! response =
        Request.create Get (uriFor "/CookieRedirect")
        |> getResponse
        |> Alt.toAsync

      Expect.equal response.statusCode 200 "statusCode should be equal"
      Expect.equal (response.cookies.ContainsKey "cookie1") false "cookies should not contain key"
    }

    testCaseAsync "cookies with invalid path" <| async {
      use! response =
        Request.create Get (uriFor "/CookieInvalidPath")
        |> getResponse
        |> Alt.toAsync

      Expect.equal response.statusCode 200 "statusCode should be equal"
    }

    testCaseAsync "reading the body as bytes works properly" <| async {
      use! response =
        Request.create Get (uriFor "/Raw")
        |> getResponse
        |> Alt.toAsync
      let expected =
        [| 98uy
           111uy
           100uy
           121uy |]
      let! actual = Response.readBodyAsBytes response |> Job.toAsync

      Expect.equal actual expected "bytes should be equal"
    }

    testCaseAsync "when there is no content-type, charset should be none" <| async {
      use! response =
        Request.create Get (uriFor "/NoContentType")
        |> getResponse
        |> Alt.toAsync
      
      Expect.isNone response.characterSet "characterSet should be none"
    }

    testCaseAsync "when there is no body, reading it as bytes gives an empty array" <| async {
      use! response = Request.create Get (uriFor "/GoodStatusCode") |> getResponse |> Alt.toAsync
      use ms = new MemoryStream()
      response.body.CopyTo ms // Windows workaround "this stream does not support seek"

      Expect.equal ms.Length 0L "stream length should be 0"
    }

    testCaseAsync "readResponseBodyAsString can read the response body" <| async {
      let! body =
        Request.create Get (uriFor "/Raw")
        |> Request.responseAsString
        |> Job.toAsync

      Expect.equal body "body" "body should be equal"
    }

    testCaseAsync "Closing the response body stream retrieved from getResponseAsync does not cause an exception" <| async {
      use! response =
        Request.create Get (uriFor "/Raw")
        |> getResponse
        |> Alt.toAsync

      response.body.Close ()
    }

    testCaseAsync "Get method works" <| async {
      use! resp =
        Request.create Get (uriFor "/Get")
        |> getResponse
        |> Alt.toAsync

      Expect.equal resp.statusCode 200 "statusCode should be equal"
    }

    testCaseAsync "Options method works" <| async {
      use! resp =
        Request.create Options (uriFor "/Options")
        |> getResponse
        |> Alt.toAsync

      Expect.equal resp.statusCode 200 "statusCode should be equal"
    }

    testCaseAsync "Post method works" <| async {
      use! resp =
        Request.create Post (uriFor "/Post") 
        |> Request.bodyString "hi mum" // posts need a body in Nancy
        |> getResponse
        |> Alt.toAsync

      Expect.equal resp.statusCode 200 "statusCode should be equal"
    }

    testCaseAsync "Patch method works" <| async {
      use! resp =
        Request.create Patch (uriFor "/Patch")
          |> getResponse
          |> Alt.toAsync

      Expect.equal resp.statusCode 200 "statusCode should be equal"
    }

    testCaseAsync "Head method works" <| async {
      use! resp =
        Request.create Head (uriFor "/Head")
        |> getResponse
        |> Alt.toAsync

      Expect.equal resp.statusCode 200 "statusCode should be equal"
    }

    testCaseAsync "Delete method works" <| async {
      use! resp =
        Request.create Delete (uriFor "/Delete")
        |> getResponse
        |> Alt.toAsync

      Expect.equal resp.statusCode 200 "statusCode should be equal"
    }

    testCaseAsync "Other method works" <| async {
        use! resp = 
          Request.create (HttpMethod.Other "OTHER") (uriFor "/Other")
          |> getResponse
          |> Alt.toAsync
    
        Expect.equal resp.statusCode 200 "status code should be equal"
    }

    testCaseAsync "getResponse.ResponseUri should contain URI that responded to the request" <| async {
      // Is going to redirect to another route and return GET 200.
      let request =
        Request.create Post (uriFor "/Redirect")
        |> Request.bodyString "hi mum"

      use! resp = request |> getResponse |> Alt.toAsync
      Expect.equal resp.statusCode 200 "statusCode should be equal"
      Expect.equal (resp.responseUri.ToString()) (uriStringFor "/GoodStatusCode") "responseUri should be equal"
    }

    testCaseAsync "returns the uploaded file names" <| async {
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

      let! response = req |> Request.responseAsString |> Job.toAsync

      for fileName in [ "file1.txt"; "file2.gif" ] do
        Expect.stringContains response fileName "response should contain filename"
    }

    testCaseAsync "multipart/mixed returns form values" <| async {
      let firstCt, secondCt, thirdCt, fourthCt, fifthCt, fileContents =
        ContentType.parse "text/plain" |> Option.get,
        ContentType.parse "text/plain" |> Option.get,
        ContentType.parse "application/json" |> Option.get,
        ContentType.parse "application/atom+xml" |> Option.get,
        ContentType.parse "application/x-doom" |> Option.get,
        "Hello World"

      let req =
        Request.create Post (uriFor "/multipart")
        |> Request.body (BodyForm
          [ NameValue ("submit-name", "Larry")
            MultipartMixed
              ("files",
                [
                  "file1.txt", firstCt, Plain fileContents
                  "file2.gif", secondCt, Plain fileContents
                  "file3.json", thirdCt, Plain fileContents
                  "file4.rss", fourthCt, Plain fileContents
                  "file5.wad", fifthCt, Plain fileContents
                ])
          ])

      let! response = req |> Request.responseAsString |> Job.toAsync

      let expected =
        [ "submit-name: Larry"
          "file1.txt"
          "file2.gif"
          "file3.json"
          "file4.rss"
          "file5.wad" ]
        |> String.concat "\n"

      Expect.equal response expected "Response fields and files should match"
    }
  ]