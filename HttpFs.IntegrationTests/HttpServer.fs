module HttpFs.IntegrationTests.HttpServer

open System
open System.Net
open System.Threading
open System.Text
open Suave
open Suave.Operators

let mutable recordedRequest = None

let app =
  choose [
    Filters.GET >=> choose [
      Filters.path "/RecordRequest" >=> request (fun r ->
        recordedRequest <- Some r
        Successful.OK "")

      Filters.path "/GoodStatusCode" >=> Successful.OK ""

      Filters.path "/BadStatusCode" >=> RequestErrors.UNAUTHORIZED ""

      Filters.path "/GotBody" >=> Successful.OK "Check out my sexy body"

      Filters.path "/NoPage" >=> RequestErrors.NOT_FOUND "Not here!"

      Filters.path "/AllTheThings"
        >=> Writers.setHeader "Content-Encoding" "gzip"
        >=> Writers.setHeader "X-New-Fangled-Header" "some value"
        >=> Cookie.setCookie (HttpCookie.createKV "cookie1" "chocolate chip")
        >=> Cookie.setCookie (HttpCookie.createKV "cookie2" "smarties")
        >=> Successful.ACCEPTED "Some JSON or whatever"

      Filters.path "/MoonLanguageCorrectEncoding"
        >=> Writers.setHeader "Content-Type" "text/plain; charset=windows-1251"
        >=> warbler (fun _ ->
            Encoding.GetEncoding("windows-1251").GetBytes("яЏ§§їДЙ")
            |> Successful.ok)

      Filters.path "/MoonLanguageTextPlainNoEncoding"
        >=> Writers.setHeader "Content-Type" "text/plain"
        >=> warbler (fun _ ->
            Encoding.GetEncoding("windows-1251").GetBytes("яЏ§§їДЙ")
            |> Successful.ok)

      Filters.path "/MoonLanguageApplicationXmlNoEncoding"
        >=> Writers.setHeader "Content-Type" "application/xml"
        >=> warbler (fun _ ->
            Encoding.GetEncoding("windows-1251").GetBytes("яЏ§§їДЙ")
            |> Successful.ok)

      Filters.path "/MoonLanguageInvalidEncoding"
        >=> Writers.setHeader "Content-Type" "text/plain; charset=Ninky-Nonk"
        >=> warbler (fun _ ->
          Encoding.GetEncoding("windows-1251").GetBytes("яЏ§§їДЙ")
          |> Successful.ok)

      Filters.path "/utf8"
        >=> Writers.setHeader "Content-Type" "text/plain; charset=utf8"
        >=> warbler (fun _ ->
          Encoding.GetEncoding("utf-8").GetBytes("'Why do you hate me so much, Windows?!' - utf8")
          |> Successful.ok)

      Filters.path "/utf16"
        >=> Writers.setHeader "Content-type" "text/plain; charset=utf16"
        >=> warbler (fun _ ->
          Encoding.GetEncoding("utf-16").GetBytes("'Why are you so picky, Windows?!' - utf16")
          |> Successful.ok)

      Filters.path "/AllHeaders"
        >=> Writers.setHeader "Content-Type" "text/html; charset=utf-8"
        >=> Writers.setHeader "Access-Control-Allow-Origin" "*"
        >=> Writers.setHeader "Accept-Ranges" "bytes"
        >=> Writers.setHeader "Age" "12"
        >=> Writers.setHeader "Allow" "GET, HEAD"
        >=> Writers.setHeader "Cache-Control" "max-age=3600"
        >=> Writers.setHeader "Connection" "close"
        >=> Writers.setHeader "Content-Encoding" "gzip"
        >=> Writers.setHeader "Content-Language" "EN-gb"
        >=> Writers.setHeader "Content-Location" "/index.htm"
        >=> Writers.setHeader "Content-MD5" "Q2hlY2sgSW50ZWdyaXR5IQ=="
        >=> Writers.setHeader "Content-Disposition" "attachment; filename=\"fname.ext\""
        >=> Writers.setHeader "Content-Range" "bytes 21010-47021/47022"
        >=> Writers.setHeader "Set-Cookie" "test1=123;test2=456"
        >=> Writers.setHeader "ETag" "737060cd8c284d8af7ad3082f209582d"
        >=> Writers.setHeader "Expires" "Thu 01 Dec 1994 16:00:00 GMT"
        >=> Writers.setHeader "Last-Modified" "Tue 15 Nov 1994 12:45:26 +0000"
        >=> Writers.setHeader "Link" "</feed>; rel=\"alternate\""
        >=> Writers.setHeader "Location" "http://www.w3.org/pub/WWW/People.html"
        >=> Writers.setHeader "P3P" "CP=\"your_compact_policy\""
        >=> Writers.setHeader "Pragma" "no-cache"
        >=> Writers.setHeader "Proxy-Authenticate" "Basic"
        >=> Writers.setHeader "Refresh" "5; url=http://www.w3.org/pub/WWW/People.html"
        >=> Writers.setHeader "Retry-After" "120"
        >=> Writers.setHeader "Strict-Transport-Security" "max-age=16070400; includeSubDomains"
        >=> Writers.setHeader "Trailer" "Max-Forwards"
        >=> Writers.setHeader "Transfer-Encoding" "identity"
        >=> Writers.setHeader "Vary" "*"
        >=> Writers.setHeader "Via" "1.0 fred 1.1 example.com (Apache/1.1)"
        >=> Writers.setHeader "Warning" "199 Miscellaneous warning"
        >=> Writers.setHeader "WWW-Authenticate" "Basic"
        >=> Writers.setHeader "X-New-Fangled-Header" "some value"
        >=> Successful.OK ""

      Filters.path "/CookieRedirect"
        >=> Cookie.setCookie (HttpCookie.createKV "cookie1" "baboon")
        >=> Writers.setHeader "Location" "http://localhost:1234/NoCookies"
        >=> Writers.setStatus HTTP_307

      Filters.path "/NoCookies" >=> Successful.OK "body"

      Filters.path "/Raw" >=> Successful.OK "body"

      Filters.path "/SlowResponse" >=> (fun ctx -> async {
        do! Async.Sleep(10000)
        return! Successful.OK "" ctx })

      Filters.path "/Get" >=> Successful.OK ""
    ]

    Filters.POST >=> choose [
        Filters.path "/RecordRequest" >=> request (fun r ->
            recordedRequest <- Some r
            Successful.OK "")

        Filters.path "/Post" >=> Successful.OK ""

        Filters.path "/Redirect"
          >=> Writers.setHeader "Location" "http://localhost:1234/GoodStatusCode"
          >=> Writers.setStatus HTTP_303

        Filters.path "/filenames" >=> request (fun r ->
          r.files
          |> List.map (fun f -> f.fileName)
          |> String.concat "\n"
          |> Successful.OK)
    ]

    Filters.HEAD >=> Filters.path "/Head" >=> Successful.OK ""

    Filters.OPTIONS >=> Filters.path "/Options" >=> Successful.OK ""

    Filters.DELETE >=> Filters.path "/Delete" >=> Successful.OK ""

    Filters.PUT >=> Filters.path "/Put" >=> Successful.OK ""

    Filters.PATCH >=> Filters.path "/Patch" >=> Successful.OK ""
  ]

type SuaveTestServer() =
  let cts = new CancellationTokenSource()

  do
    let config =
      { defaultConfig with
          bindings = [ HttpBinding.create HTTP (IPAddress.Parse "0.0.0.0") 1234us ]
          cancellationToken = cts.Token }
    let listening, server = startWebServerAsync config app
    Async.Start(server, cts.Token) |> ignore
    Async.RunSynchronously listening |> ignore
    ()

  interface IDisposable with
    member x.Dispose() =
      cts.Cancel true
      cts.Dispose()