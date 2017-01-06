module HttpFs.SamplePostApplication

open HttpFs.Client
open Hopac
open System
open System.Web // TODO: remember to add reference to project ->
open System.Text

type ApiHttpResponse =
    | Ok of body:string
    | Error of statusCode:int
    | Exception of e:exn

let sampleGetBody urlStr bodyStr : Async<ApiHttpResponse> =
    let resp =
        Request.create Post (Uri urlStr)
        |> Request.bodyString bodyStr
        |> getResponse

    resp |> Alt.afterJob (fun resp ->
        // if we don't cancel the request, let's read the body in full
        match resp.statusCode with
        | x when x < 300 ->
            resp
            |> Response.readBodyAsString
            |> Job.map Ok
        | x ->
            Error resp.statusCode
            |> Job.result
    )
    |> Alt.toAsync

[<EntryPoint>]
let main argv = 
    // Set up form for small pizza with bacon and onions.
    let form =
        [
            "custname", "John Doe"
            "custtel", "12345678"
            "email", "john@example.com"
            "size", "small"
            "topping", "bacon"
            "topping", "cheese"
        ]
        |> List.map (fun (k, v) -> NameValue (k, v))

    // List of common html headers.
    // http://en.wikipedia.org/wiki/List_of_HTTP_header_fields
    let request =
        Request.createUrl Post "http://httpbin.org/post"
        |> Request.body (BodyForm form)
        |> Request.setHeader (Referer "https://github.com/haf/Http.fs")
        |> Request.setHeader (UserAgent "Mozilla/5.0 (Windows NT 6.3; WOW64; rv:36.0) Gecko/20100101 Firefox/36.0")
        |> Request.setHeader (Accept "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")
        |> Request.setHeader (AcceptLanguage "Accept-Language: en-US")
        |> Request.setHeader (ContentType (ContentType.create("application", "x-www-form-urlencoded")))
        |> Request.setHeader (Custom ("DNT", "1")) // Custom header. Do Not Track Enabled 
        |> Request.autoDecompression (DecompressionScheme.GZip ||| DecompressionScheme.Deflate) // Accept both.
        |> Request.responseCharacterEncoding Encoding.UTF8
        |> Request.keepAlive true

    job {
        use! resp = request |> HttpFs.Client.getResponse
        printfn "StatusCode: %d" resp.statusCode
        return if resp.statusCode = 200 then 0 else resp.statusCode
    }
    |> run // only use a single run per app