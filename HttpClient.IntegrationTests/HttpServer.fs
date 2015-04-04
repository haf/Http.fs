module HttpServer

open Nancy
open Nancy.Extensions
open Nancy.Hosting.Self
open System
open System.Threading
open System.Text

// A Nancy Response overridden to allow different encoding on the body
type EncodedResponse(body:string, encoding:string) =
    inherit Nancy.Response()

    let writeBody (stream:IO.Stream) = 
        let bytes = Encoding.GetEncoding(encoding).GetBytes(body)
        stream.Write(bytes, 0, bytes.Length)

    do base.Contents <- Action<IO.Stream> writeBody

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
                response.WithCookie("cookie1", "chocolate chip") |> ignore
                response.WithCookie("cookie2", "smarties") |> ignore
                response.Headers.Add("Content-Encoding", "gzip")
                response.Headers.Add("X-New-Fangled-Header", "some value")
                response :> obj

        self.Get.["MoonLanguageCorrectEncoding"] <- 
            fun _ -> 
                let response = new EncodedResponse("яЏ§§їДЙ", "windows-1251")
                response.ContentType <- "text/plain; charset=windows-1251"
                response :> obj

        self.Get.["MoonLanguageTextPlainNoEncoding"] <- 
            fun _ -> 
                let response = new EncodedResponse("яЏ§§їДЙ", "windows-1251")
                response.ContentType <- "text/plain"
                response :> obj

        self.Get.["MoonLanguageApplicationXmlNoEncoding"] <- 
            fun _ -> 
                let response = new EncodedResponse("яЏ§§їДЙ", "windows-1251")
                response.ContentType <- "application/xml"
                response :> obj

        self.Get.["MoonLanguageInvalidEncoding"] <- 
            fun _ -> 
                let response = new EncodedResponse("яЏ§§їДЙ", "windows-1251")
                response.ContentType <- "text/plain; charset=Ninky-Nonk"
                response :> obj

        self.Get.["utf8"] <- 
            fun _ -> 
                let response = new EncodedResponse("'Why do you hate me so much, Windows?!' - utf8", "utf-8")
                response.ContentType <- "text/plain; charset=utf8"
                response :> obj

        self.Get.["utf16"] <- 
            fun _ -> 
                let response = new EncodedResponse("'Why are you so picky, Windows?!' - utf16", "utf-16")
                response.ContentType <- "text/plain; charset=utf16"
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
                response.Headers.Add("Transfer-Encoding", "identity")
                response.Headers.Add("Vary", "*")
                response.Headers.Add("Via", "1.0 fred, 1.1 example.com (Apache/1.1)")
                response.Headers.Add("Warning", "199 Miscellaneous warning")
                response.Headers.Add("WWW-Authenticate", "Basic")
                response.Headers.Add("X-New-Fangled-Header", "some value")
                response :> obj

        self.Get.["CookieRedirect"] <- 
            fun _ -> 
                let response = "body" |> Nancy.Response.op_Implicit
                response.WithCookie("cookie1", "baboon") |> ignore
                response.Headers.Add("Location", "http://localhost:1234/TestServer/NoCookies")
                response.StatusCode <- HttpStatusCode.TemporaryRedirect
                response :> obj

        self.Get.["NoCookies"] <- 
            fun _ -> 
                let response = "body" |> Nancy.Response.op_Implicit
                response.StatusCode <- HttpStatusCode.OK
                response :> obj

        self.Get.["Raw"] <-
            fun _ ->
                let response = "body" |> Nancy.Response.op_Implicit
                response.StatusCode <- HttpStatusCode.OK
                response :> obj

        self.Post.["SlowResponse"] <- 
            fun _ -> 
                async { 
                    do! Async.Sleep(10000)
                } |> Async.RunSynchronously
                200 :> obj

        /// The response to the request can be found under another URI using a GET method. 
        /// When received in response to a POST (or PUT/DELETE), it should be assumed that the server has 
        /// received the data and the redirect should be issued with a separate GET message.
        /// However, some Web applications and frameworks use the 302 status code as if it were the 303.
        /// See http://en.wikipedia.org/wiki/List_of_HTTP_status_codes.
        self.Post.["Redirect"] <- 
            fun _ -> 
                //let response = "body" |> Nancy.Response.op_Implicit
                let r = new Responses.RedirectResponse(
                            // Use existing route with code 200.
                            "http://localhost:1234/TestServer/GoodStatusCode",
                            // Redirect this request using HTTP GET (303), no 302 code.
                            Nancy.Responses.RedirectResponse.RedirectType.SeeOther) 
                r :> obj

        self.Get.["Get"] <- fun _ -> 200 :> obj
        // Head method automatically handled for Get methods in Nancy
        self.Post.["Post"] <- fun _ -> 200 :> obj
        self.Options.["Options"] <- fun _ -> 200 :> obj
        self.Put.["Put"] <- fun _ -> 200 :> obj
        self.Delete.["Delete"] <- fun _ -> 200 :> obj
        self.Patch.["Patch"] <- fun _ -> 200 :> obj