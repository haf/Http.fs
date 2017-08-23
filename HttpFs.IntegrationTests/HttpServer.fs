module HttpFs.IntegrationTests.HttpServer

open System
open System.Net
open System.Threading
open System.Text
open Suave
open Suave.Operators

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
let mutable recordedRequest = None

let app =
  choose [
    Filters.GET >=> choose [
      Filters.path "/RecordRequest" >=> request (fun r ->
        recordedRequest <- Some r
        Successful.OK "")

      Filters.path "/GoodStatusCode" >=> request (fun r ->
        recordedRequest <- Some r
        Successful.OK "")

      Filters.path "/BadStatusCode" >=> request (fun r ->
        recordedRequest <- Some r
        RequestErrors.UNAUTHORIZED "")

      Filters.path "/GotBody" >=> request (fun r ->
        recordedRequest <- Some r
        Successful.OK "Check out my sexy body")

      Filters.path "/NoPage" >=> request (fun r ->
        recordedRequest <- Some r
        RequestErrors.NOT_FOUND "Not here!")

      Filters.path "/AllTheThings"
        >=> Writers.setHeader "Content-Encoding" "gzip"
        >=> Writers.setHeader "X-New-Fangled-Header" "some value"
        >=> Cookie.setCookie (HttpCookie.createKV "cookie1" "chocolate chip")
        >=> Cookie.setCookie (HttpCookie.createKV "cookie2" "smarties")
        >=> request (fun r ->
            recordedRequest <- Some r
            Successful.ACCEPTED "Some JSON or whatever")

      Filters.path "/MoonLanguageCorrectEncoding"
        >=> Writers.setHeader "Content-Type" "text/plain; charset=windows-1251"
        >=> request (fun r ->
            recordedRequest <- Some r
            Encoding.GetEncoding("windows-1251").GetBytes("яЏ§§їДЙ")
            |> Successful.ok)

      Filters.path "/MoonLanguageTextPlainNoEncoding"
        >=> Writers.setHeader "Content-Type" "text/plain"
        >=> request (fun r ->
            recordedRequest <- Some r
            Encoding.GetEncoding("windows-1251").GetBytes("яЏ§§їДЙ")
            |> Successful.ok)

      Filters.path "/MoonLanguageApplicationXmlNoEncoding"
        >=> Writers.setHeader "Content-Type" "application/xml"
        >=> request (fun r ->
            recordedRequest <- Some r
            Encoding.GetEncoding("windows-1251").GetBytes("яЏ§§їДЙ")
            |> Successful.ok)

      Filters.path "/MoonLanguageInvalidEncoding"
        >=> Writers.setHeader "Content-Type" "text/plain; charset=Ninky-Nonk"
        >=> request (fun r ->
          recordedRequest <- Some r
          Encoding.GetEncoding("windows-1251").GetBytes("яЏ§§їДЙ")
          |> Successful.ok)

      Filters.path "/utf8"
        >=> Writers.setHeader "Content-Type" "text/plain; charset=utf8"
        >=> request (fun r ->
          recordedRequest <- Some r
          Encoding.GetEncoding("utf-8").GetBytes("'Why do you hate me so much, Windows?!' - utf8")
          |> Successful.ok)

      Filters.path "/utf16"
        >=> Writers.setHeader "Content-type" "text/plain; charset=utf16"
        >=> request (fun r ->
          recordedRequest <- Some r
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
        >=> request (fun r ->
          recordedRequest <- Some r
          Successful.OK "")

      Filters.path "/CookieRedirect"
        >=> Cookie.setCookie (HttpCookie.createKV "cookie1" "baboon")
        >=> Writers.setHeader "Location" "http://localhost:1234/NoCookies"
        >=> (fun ctx -> async {
          recordedRequest <- Some ctx.request
          return! ctx |> succeed >>= Writers.setStatus HTTP_307 })

      Filters.path "/NoCookies" >=> request (fun r ->
        recordedRequest <- Some r
        Successful.OK "body")

      Filters.path "/Raw" >=> request (fun r ->
        recordedRequest <- Some r
        Successful.OK "body")

      Filters.path "/SlowResponse" >=> (fun ctx -> async {
        recordedRequest <- Some ctx.request
        do! Async.Sleep(10000)
        return! Successful.OK "" ctx })

      Filters.path "/Get" >=> request (fun r ->
        recordedRequest <- Some r
        Successful.OK "")
    ]

    Filters.POST >=> choose [
        Filters.path "/RecordRequest" >=> request (fun r ->
            recordedRequest <- Some r
            Successful.OK "")

        Filters.path "/Post" >=> request (fun r ->
          recordedRequest <- Some r
          Successful.OK "")

        Filters.path "/Redirect"
          >=> Writers.setHeader "Location" "http://localhost:1234/GoodStatusCode"
          >=> (fun ctx -> async {
            recordedRequest <- Some ctx.request
            return! ctx |> succeed >>= Writers.setStatus HTTP_303 })

        Filters.path "/filenames" >=> request (fun r ->
          r.files
          |> List.map (fun f -> f.fileName)
          |> String.concat "\n"
          |> Successful.OK)
    ]

    Filters.HEAD >=> Filters.path "/Head" >=> request (fun r ->
      recordedRequest <- Some r
      Successful.OK "")

    Filters.OPTIONS >=> Filters.path "/Options" >=> request (fun r ->
      recordedRequest <- Some r
      Successful.OK "")

    Filters.DELETE >=> Filters.path "/Delete" >=> request (fun r ->
      recordedRequest <- Some r
      Successful.OK "")

    Filters.PUT >=> Filters.path "/Put" >=> request (fun r ->
      recordedRequest <- Some r
      Successful.OK "")

    Filters.PATCH >=> Filters.path "/Patch" >=> request (fun r ->
      recordedRequest <- Some r
      Successful.OK "")
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
// type FakeServer() as self = 
//     inherit NancyModule()
//     do
//         self.Post.["RecordRequest"] <- 
//             fun _ -> 
//                 recordedRequest := self.Request
//                 box 200

//         self.Get.["RecordRequest"] <- 
//             fun _ -> 
//                 recordedRequest := self.Request
//                 box 200

//         self.Get.["GoodStatusCode"] <- 
//             fun _ -> 
//                 box 200

//         self.Get.["BadStatusCode"] <- 
//             fun _ -> 
//                 box 401

//         self.Get.["GotBody"] <- 
//             fun _ -> 
//                 box "Check out my sexy body"

//         self.Get.["AllTheThings"] <- 
//             fun _ -> 
//                 let response = "Some JSON or whatever" |> Nancy.Response.op_Implicit 
//                 response.StatusCode <- HttpStatusCode.ImATeapot
//                 response.WithCookie("cookie1", "chocolate chip") |> ignore
//                 response.WithCookie("cookie2", "smarties") |> ignore
//                 response.Headers.Add("Content-Encoding", "gzip")
//                 response.Headers.Add("X-New-Fangled-Header", "some value")
//                 box response

//         self.Get.["MoonLanguageCorrectEncoding"] <- 
//             fun _ -> 
//                 let response = new EncodedResponse("яЏ§§їДЙ", "windows-1251")
//                 response.ContentType <- "text/plain; charset=windows-1251"
//                 box response

//         self.Get.["MoonLanguageTextPlainNoEncoding"] <- 
//             fun _ -> 
//                 let response = new EncodedResponse("яЏ§§їДЙ", "windows-1251")
//                 response.ContentType <- "text/plain"
//                 response :> obj

//         self.Get.["MoonLanguageApplicationXmlNoEncoding"] <- 
//             fun _ -> 
//                 let response = new EncodedResponse("яЏ§§їДЙ", "windows-1251")
//                 response.ContentType <- "application/xml"
//                 response :> obj

//         self.Get.["MoonLanguageInvalidEncoding"] <- 
//             fun _ -> 
//                 let response = new EncodedResponse("яЏ§§їДЙ", "windows-1251")
//                 response.ContentType <- "text/plain; charset=Ninky-Nonk"
//                 response :> obj

//         self.Get.["utf8"] <- 
//             fun _ -> 
//                 let response = new EncodedResponse("'Why do you hate me so much, Windows?!' - utf8", "utf-8")
//                 response.ContentType <- "text/plain; charset=utf8"
//                 response :> obj

//         self.Get.["utf16"] <- 
//             fun _ -> 
//                 let response = new EncodedResponse("'Why are you so picky, Windows?!' - utf16", "utf-16")
//                 response.ContentType <- "text/plain; charset=utf16"
//                 response :> obj

//         self.Get.["AllHeaders"] <- 
//             fun _ -> 
//                 let response = "Some JSON or whatever" |> Nancy.Response.op_Implicit 
//                 response.ContentType <- "text/html; charset=utf-8"
//                 response.Headers.Add("Access-Control-Allow-Origin", "*")
//                 response.Headers.Add("Accept-Ranges", "bytes")
//                 response.Headers.Add("Age", "12")
//                 response.Headers.Add("Allow", "GET, HEAD")
//                 response.Headers.Add("Cache-Control", "max-age=3600")
//                 response.Headers.Add("Connection", "close")
//                 response.Headers.Add("Content-Encoding", "gzip")
//                 response.Headers.Add("Content-Language", "EN-gb")
//                 response.Headers.Add("Content-Location", "/index.htm")
//                 response.Headers.Add("Content-MD5", "Q2hlY2sgSW50ZWdyaXR5IQ==")
//                 response.Headers.Add("Content-Disposition", "attachment; filename=\"fname.ext\"")
//                 response.Headers.Add("Content-Range", "bytes 21010-47021/47022")
//                 response.Headers.Add("Set-Cookie", "test1=123;test2=456")
//                 //response.Headers.Add("Date", "") // will be current date
//                 response.Headers.Add("ETag", "737060cd8c284d8af7ad3082f209582d")
//                 response.Headers.Add("Expires", "Thu, 01 Dec 1994 16:00:00 GMT")
//                 response.Headers.Add("Last-Modified", "Tue, 15 Nov 1994 12:45:26 +0000")
//                 response.Headers.Add("Link", "</feed>; rel=\"alternate\"")
//                 response.Headers.Add("Location", "http://www.w3.org/pub/WWW/People.html")
//                 response.Headers.Add("P3P", "CP=\"your_compact_policy\"")
//                 response.Headers.Add("Pragma", "no-cache")
//                 response.Headers.Add("Proxy-Authenticate", "Basic")
//                 response.Headers.Add("Refresh", "5; url=http://www.w3.org/pub/WWW/People.html")
//                 response.Headers.Add("Retry-After", "120")
//                 //response.Headers.Add("Server", "") // will be 'Microsoft-HTTPAPI/2.0'
//                 response.Headers.Add("Strict-Transport-Security", "max-age=16070400; includeSubDomains")
//                 response.Headers.Add("Trailer", "Max-Forwards")
//                 response.Headers.Add("Transfer-Encoding", "identity")
//                 response.Headers.Add("Vary", "*")
//                 response.Headers.Add("Via", "1.0 fred, 1.1 example.com (Apache/1.1)")
//                 response.Headers.Add("Warning", "199 Miscellaneous warning")
//                 response.Headers.Add("WWW-Authenticate", "Basic")
//                 response.Headers.Add("X-New-Fangled-Header", "some value")
//                 response :> obj

//         self.Get.["CookieRedirect"] <- 
//             fun _ -> 
//                 let response = "body" |> Nancy.Response.op_Implicit
//                 response.WithCookie("cookie1", "baboon") |> ignore
//                 response.Headers.Add("Location", "http://localhost:1234/TestServer/NoCookies")
//                 response.StatusCode <- HttpStatusCode.TemporaryRedirect
//                 response :> obj

//         self.Get.["NoCookies"] <- 
//             fun _ -> 
//                 let response = "body" |> Nancy.Response.op_Implicit
//                 response.StatusCode <- HttpStatusCode.OK
//                 response :> obj

//         self.Get.["Raw"] <-
//             fun _ ->
//                 let response = "body" |> Nancy.Response.op_Implicit
//                 response.StatusCode <- HttpStatusCode.OK
//                 response :> obj

//         self.Post.["SlowResponse"] <- 
//             fun _ -> 
//                 async { 
//                     do! Async.Sleep(10000)
//                 } |> Async.RunSynchronously
//                 200 :> obj

//         /// The response to the request can be found under another URI using a GET method. 
//         /// When received in response to a POST (or PUT/DELETE), it should be assumed that the server has 
//         /// received the data and the redirect should be issued with a separate GET message.
//         /// However, some Web applications and frameworks use the 302 status code as if it were the 303.
//         /// See http://en.wikipedia.org/wiki/List_of_HTTP_status_codes.
//         self.Post.["Redirect"] <- 
//             fun _ -> 
//                 //let response = "body" |> Nancy.Response.op_Implicit
//                 let r = new Responses.RedirectResponse(
//                             // Use existing route with code 200.
//                             "http://localhost:1234/TestServer/GoodStatusCode",
//                             // Redirect this request using HTTP GET (303), no 302 code.
//                             Nancy.Responses.RedirectResponse.RedirectType.SeeOther) 
//                 r :> obj

//         self.Get.["Get"] <- fun _ -> 200 :> obj
//         // Head method automatically handled for Get methods in Nancy
//         self.Post.["Post"] <- fun _ -> 200 :> obj
//         self.Options.["Options"] <- fun _ -> 200 :> obj
//         self.Put.["Put"] <- fun _ -> 200 :> obj
//         self.Delete.["Delete"] <- fun _ -> 200 :> obj
//         self.Patch.["Patch"] <- fun _ -> 200 :> obj

//         self.Post.["filenames"] <-
//             fun _ ->
//                 self.Request.Files
//                 |> Seq.map (fun file -> file.Name)
//                 |> String.concat "\n"
//                 |> box