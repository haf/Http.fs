module HttpFs.Tests.RequestBody

open System
open System.Net.Http
open System.Text
open Expecto
open Hopac
open HttpFs.Client

let url = "https://fsharpforfunandprofit.com"
let resource = "hehe"
let uriWithResource = Uri (sprintf "%s/%s" url resource)
let ValidUri = Uri url
let createValidRequest = Request.create Get ValidUri
let utf8 = Encoding.UTF8

[<Tests>]
let apiUsage =
  testList "api usage" [
    testCase "withBody sets the request body" <| fun _ ->
      let body = (createValidRequest |> Request.bodyString """Hello mum!%2\/@$""").body
      Expect.equal body (BodyString """Hello mum!%2\/@$""") "body should be equal"

    testCase "withBody sets the request body binary" <| fun _ ->
      let body = (createValidRequest |> Request.body (BodyRaw [| 98uy; 111uy; 100uy; 121uy |])).body
      Expect.equal body (BodyRaw [| 98uy; 111uy; 100uy; 121uy |]) "body should be equal"

    testCase "withBody uses default character encoding of UTF-8" <| fun _ ->
      let encoding = (createValidRequest |> Request.bodyString "whatever").bodyCharacterEncoding
      Expect.equal encoding utf8 "encoding should be equal"

    testCase "withBodyEncoded sets the request body" <| fun _ ->
      let body = (createValidRequest |> Request.bodyStringEncoded """Hello mum!%2\/@$""" utf8).body
      Expect.equal body (BodyString """Hello mum!%2\/@$""") "body should be equal"

    testCase "withBodyEncoded sets the body encoding" <| fun _ ->
      let encoding = (createValidRequest |> Request.bodyStringEncoded "Hi Mum" utf8).bodyCharacterEncoding
      Expect.equal encoding utf8 "encoding should be equal"

    testCase "with path set to url + resource" <| fun _ ->
      let url = (createValidRequest |> Request.path resource).url
      Expect.equal url uriWithResource "url should be equal"

    testCase "with resource add to request" <| fun _ ->
      let url = (createValidRequest |> Request.resource resource).url
      Expect.equal url uriWithResource "url should be equal"

    testCase "with method set to 'method' property of request" <| fun _ ->
      let method = (createValidRequest |> Request.setMethod Get).method
      Expect.equal method Get "method should be equal"
  ]

[<Tests>]
let contentType =
  testCase "can convert to string" <| fun _ ->
    let subject = ContentType.create("application", "multipart", charset=Encoding.UTF8, boundary="---apa")
    Expect.equal (subject.ToString()) "application/multipart; charset=utf-8; boundary=\"---apa\"" "subject should be equal" 

[<Tests>]
let bodyFormatting =
  let testSeed = 1234567765

  let contentToBytes (content: HttpContent) =
    content.ReadAsByteArrayAsync()
    |> Async.AwaitTask
    |> Async.RunSynchronously

  testList "formatting different sorts of body" [
    testCase "can format raw" <| fun _ ->
      let clientState = { HttpFsState.empty with random = Random testSeed }
      let newCt, httpContent = Impl.createHttpContent clientState (None, utf8, BodyRaw [|1uy; 2uy; 3uy|])
      let bytes = httpContent |> contentToBytes

      Expect.equal bytes [|1uy; 2uy; 3uy|] "body should be sequence of stream writers"
      Expect.equal newCt None "no new content type for byte body"

    testCase "ordinary multipart/form-data" <| fun _ ->
      /// can't lift outside, because test cases may run in parallel
      let clientState = { HttpFsState.empty with random = Random testSeed }

      let fileCt, fileContents = ContentType.parse "text/plain" |> Option.get, "Hello World"

      let form =
        // example from http://www.w3.org/TR/html401/interact/forms.html
        [   NameValue ("submit-name", "Larry")
            FormFile ("files", ("file1.txt", fileCt, Plain fileContents)) ]

      let newCt, subject =
        Impl.createHttpContent clientState (None, utf8, BodyForm form)
        |> fun (newCt, httpContent) ->
          let bytes = httpContent |> contentToBytes
          newCt, bytes |> utf8.GetString

      let expectedBoundary = "BgOE:fCUQGnYfKwGMnxoyfwVMbRzZF"

      let expected = [ sprintf "--%s" expectedBoundary
                       "Content-Type: text/plain; charset=utf-8"
                       "Content-Disposition: form-data; name=\"submit-name\""
                       ""
                       "Larry"
                       sprintf "--%s" expectedBoundary
                       "Content-Type: text/plain"
                       "Content-Disposition: form-data; name=\"files\"; filename=\"file1.txt\""
                       ""
                       "Hello World"
                       sprintf "--%s--" expectedBoundary
                       "" ]
                     |> String.concat "\r\n"

      Expect.equal subject expected "should have correct body"
      Expect.equal (newCt |> Option.get) (ContentType.create("multipart", "form-data", boundary=expectedBoundary)) "should have new ct"

    testCase "multipart/form-data with multipart/mixed" <| fun _ ->
      /// can't lift outside, because test cases may run in parallel
      let clientState = { HttpFsState.empty with random = Random testSeed }

      let firstCt, secondCt, thirdCt, fourthCt, fifthCt, fileContents =
        ContentType.parse "text/plain" |> Option.get,
        ContentType.parse "text/plain" |> Option.get,
        ContentType.parse "application/json" |> Option.get,
        ContentType.parse "application/atom+xml" |> Option.get,
        ContentType.parse "application/x-doom" |> Option.get,
        "Hello World"

      let form =
        // example from http://www.w3.org/TR/html401/interact/forms.html
        [
          NameValue ("submit-name", "Larry")
          MultipartMixed
            ("files",
              [
                "file1.txt", firstCt, Plain fileContents
                "file2.gif", secondCt, Plain "...contents of file2.gif..."
                "file3.json", thirdCt, Plain fileContents
                "file4.rss", fourthCt, Plain fileContents
                "file5.wad", fifthCt, Plain fileContents
              ])
        ]

      let newCt, subject =
        Impl.createHttpContent clientState (None, utf8, BodyForm form)
        |> fun (newCt, httpContent) ->
          let bytes = httpContent |> contentToBytes
          newCt, bytes |> utf8.GetString

      let expectedBoundary1 = "BgOE:fCUQGnYfKwGMnxoyfwVMbRzZF"
      let expectedBoundary2 = "TYHqj_uqWKBHGwtogjeH-_oyB_JTR/"

      let expected =
          [ sprintf "--%s" expectedBoundary1
            "Content-Type: text/plain; charset=utf-8"
            "Content-Disposition: form-data; name=\"submit-name\""
            ""
            "Larry"
            sprintf "--%s" expectedBoundary1
            sprintf "Content-Type: multipart/mixed; boundary=\"%s\"" expectedBoundary2
            "Content-Disposition: form-data; name=\"files\""
            ""
            sprintf "--%s" expectedBoundary2
            "Content-Type: text/plain"
            "Content-Disposition: file; filename=\"file1.txt\""
            ""
            "Hello World"
            sprintf "--%s" expectedBoundary2
            "Content-Type: text/plain"
            "Content-Disposition: file; filename=\"file2.gif\""
            ""
            "...contents of file2.gif..."
            sprintf "--%s" expectedBoundary2
            "Content-Type: application/json"
            "Content-Disposition: file; filename=\"file3.json\""
            ""
            "Hello World"
            sprintf "--%s" expectedBoundary2
            "Content-Type: application/atom+xml"
            "Content-Disposition: file; filename=\"file4.rss\""
            ""
            "Hello World"
            sprintf "--%s" expectedBoundary2
            "Content-Type: application/x-doom"
            "Content-Transfer-Encoding: base64"
            "Content-Disposition: file; filename=\"file5.wad\""
            ""
            "SGVsbG8gV29ybGQ="
            sprintf "--%s--" expectedBoundary2
            ""
            sprintf "--%s--" expectedBoundary1
            "" ]
          |> String.concat "\r\n"
      
      Expect.equal subject expected "should have correct body"

    testCase "can format urlencoded data" <| fun _ ->
      // http://www.url-encode-decode.com/
      // https://unspecified.wordpress.com/2008/07/08/browser-uri-encoding-the-best-we-can-do/
      // http://stackoverflow.com/questions/912811/what-is-the-proper-way-to-url-encode-unicode-characters
      // https://www.ietf.org/rfc/rfc1738.txt
      // http://www.w3.org/TR/html401/interact/forms.html
      // http://stackoverflow.com/questions/4007969/application-x-www-form-urlencoded-or-multipart-form-data
      let encoded =
        [
          ("user_name", "Åsa den Röde")
          ("user_pass", "Bović")
        ]
        |> Impl.uriEncode
      
      Expect.equal encoded "user_name=%C3%85sa+den+R%C3%B6de&user_pass=Bovi%C4%87" "Should encode Swedish properly"

    testCase "can format urlencoded form" <| fun _ ->
        let clientState = { HttpFsState.empty with random = Random testSeed }
        // example from http://www.w3.org/TR/html401/interact/forms.html
        [   NameValue ("submit", "Join Now!")
            NameValue ("user_name", "Åsa den Röde")
            NameValue ("user_pass", "Bović")
        ]
        |> fun form -> Impl.createHttpContent clientState (None, utf8, BodyForm form)
        |> fun (newCt, message) ->
          let bodyToString = message.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
          Expect.equal bodyToString "submit=Join+Now%21&user_name=%C3%85sa+den+R%C3%B6de&user_pass=Bovi%C4%87" "body should be equal"
          Expect.equal newCt (ContentType.parse "application/x-www-form-urlencoded") "should have new ct"
  ]

[<Tests>]
let internals =
  testCase "http web request url" <| fun _ ->
    let hfsReq = Request.create Get (Uri "http://localhost/") |> Request.queryStringItem "a" "1"
    let reqMessage = DotNetWrapper.toHttpRequestMessage HttpFsState.empty hfsReq
    Expect.equal (string reqMessage.RequestUri) "http://localhost/?a=1" "uri should be equal"

[<Tests>]
let textEncodingTest =
  let pathOf relativePath =
    let here = IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
    IO.Path.Combine(here, relativePath)

  testCase "character encoding when files are attached to request" <| fun _ ->
    let requestBody =
      Request.create Post (Uri "http://localhost/")
      |> Request.body (
        BodyForm [
          NameValue("Special letters", "åäö")
          NameValue("More Special letters", "©®™")
          FormFile("file", ("pix.gif", ContentType.create("image", "gif"), Binary (System.IO.File.ReadAllBytes(pathOf "pix.gif"))))
      ])
    let rawBodyString = DotNetWrapper.getRawRequestBodyString HttpFsState.empty requestBody

    Expect.stringContains rawBodyString "åäö" "should contain chars"
    Expect.stringContains rawBodyString "©®™" "should contain symbols"
