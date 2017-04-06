module HttpFs.Tests.RequestBody

open System
open System.Text
open Fuchu
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
      Assert.Equal((createValidRequest |> Request.bodyString """Hello mum!%2\/@$""").body,
        BodyString """Hello mum!%2\/@$""")

    testCase "withBody sets the request body binary" <| fun _ ->
      Assert.Equal((createValidRequest |> Request.body (BodyRaw [| 98uy; 111uy; 100uy; 121uy |])).body,
        BodyRaw [| 98uy; 111uy; 100uy; 121uy |])

    testCase "withBody uses default character encoding of UTF-8" <| fun _ ->
      Assert.Equal((createValidRequest |> Request.bodyString "whatever").bodyCharacterEncoding, utf8)

    testCase "withBodyEncoded sets the request body" <| fun _ ->
      Assert.Equal((createValidRequest |> Request.bodyStringEncoded """Hello mum!%2\/@$""" utf8).body,
                   BodyString """Hello mum!%2\/@$""")

    testCase "withBodyEncoded sets the body encoding" <| fun _ ->
      Assert.Equal((createValidRequest |> Request.bodyStringEncoded "Hi Mum" utf8).bodyCharacterEncoding,
                   utf8)

    testCase "with path set to url + resource" <| fun _ ->
      Assert.Equal((createValidRequest |> Request.path resource).url,
                   uriWithResource)

    testCase "with resource add to request" <| fun _ ->
      Assert.Equal((createValidRequest |> Request.resource resource).url,
                   uriWithResource)

    testCase "with method assign to 'method' property of request" <| fun _ ->
      Assert.Equal((createValidRequest |> Request.assignMethod Get).``method``,
                   Get)
  ]

[<Tests>]
let contentType =
  testCase "can convert to string" <| fun _ ->
    let subject = ContentType.create("application", "multipart", charset=Encoding.UTF8, boundary="---apa")
    Assert.Equal(subject.ToString(), "application/multipart; charset=utf-8; boundary=\"---apa\"")

[<Tests>]
let bodyFormatting =
    let testSeed = 1234567765

    let bodyToBytes body =
      use stream = new IO.MemoryStream()
      for writer in body do
        do writer stream |> Hopac.run
      stream.Seek(0L, IO.SeekOrigin.Begin) |> ignore
      stream.ToArray()

    testList "formatting different sorts of body" [
        testCase "can format raw" <| fun _ ->
            let clientState = { HttpFsState.empty with random = Random testSeed }
            let newCt, body = Impl.formatBody clientState (None, utf8, BodyRaw [|1uy; 2uy; 3uy|])
            let bytes = bodyToBytes body
            Assert.Equal("body should be sequence of stream writers", [|1uy; 2uy; 3uy|], bytes)
            Assert.Equal("no new content type for byte body", None, newCt)

        testCase "ordinary multipart/form-data" <| fun _ ->
            /// can't lift outside, because test cases may run in parallel
            let clientState = { HttpFsState.empty with random = Random testSeed }

            let fileCt, fileContents =
                ContentType.parse "text/plain" |> Option.get,
                "Hello World"

            let form =
                // example from http://www.w3.org/TR/html401/interact/forms.html
                [   NameValue ("submit-name", "Larry")
                    FormFile ("files", ("file1.txt", fileCt, Plain fileContents)) ]

            let newCt, subject =
                Impl.formatBody clientState (None, utf8, BodyForm form)
                |> fun (newCt, body) ->
                  let bytes = bodyToBytes body
                  newCt, bytes |> utf8.GetString

            let expectedBoundary = "nLWsTCFurKCiU_PjC/cCmmU-tnJHHa"

            let expected = [ sprintf "--%s" expectedBoundary
                             "Content-Disposition: form-data; name=\"submit-name\""
                             ""
                             "Larry"
                             sprintf "--%s" expectedBoundary
                             "Content-Disposition: form-data; name=\"files\"; filename=\"file1.txt\""
                             "Content-Type: text/plain"
                             ""
                             "Hello World"
                             sprintf "--%s--" expectedBoundary
                             ""
                             "" ]
                           |> String.concat "\r\n"

            Assert.Equal("should have correct body", expected, subject)
            Assert.Equal("should have new ct",
                         ContentType.create("multipart", "form-data", boundary=expectedBoundary),
                         newCt |> Option.get)

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
                [   NameValue ("submit-name", "Larry")
                    MultipartMixed ("files",
                               [ "file1.txt", firstCt, Plain fileContents
                                 "file2.gif", secondCt, Plain "...contents of file2.gif..."
                                 "file3.json", thirdCt, Plain fileContents
                                 "file4.rss", fourthCt, Plain fileContents
                                 "file5.wad", fifthCt, Plain fileContents
                               ])
                ]

            let newCt, subject =
                Impl.formatBody clientState (None, utf8, BodyForm form)
                |> fun (newCt, body) ->
                  let bytes = bodyToBytes body
                  newCt, bytes |> utf8.GetString

            let expectedBoundary1 = "nLWsTCFurKCiU_PjC/cCmmU-tnJHHa"
            let expectedBoundary2 = "BgOE:fCUQGnYfKwGMnxoyfwVMbRzZF"

            let expected =
                [ sprintf "--%s" expectedBoundary1
                  "Content-Disposition: form-data; name=\"submit-name\""
                  ""
                  "Larry"
                  sprintf "--%s" expectedBoundary1
                  sprintf "Content-Type: multipart/mixed; boundary=\"%s\"" expectedBoundary2
                  "Content-Disposition: form-data; name=\"files\""
                  ""
                  sprintf "--%s" expectedBoundary2
                  "Content-Disposition: file; filename=\"file1.txt\""
                  "Content-Type: text/plain"
                  ""
                  "Hello World"
                  sprintf "--%s" expectedBoundary2
                  "Content-Disposition: file; filename=\"file2.gif\""
                  "Content-Type: text/plain"
                  ""
                  "...contents of file2.gif..."
                  sprintf "--%s" expectedBoundary2
                  "Content-Disposition: file; filename=\"file3.json\""
                  "Content-Type: application/json"
                  ""
                  "Hello World"
                  sprintf "--%s" expectedBoundary2
                  "Content-Disposition: file; filename=\"file4.rss\""
                  "Content-Type: application/atom+xml"
                  ""
                  "Hello World"
                  sprintf "--%s" expectedBoundary2
                  "Content-Disposition: file; filename=\"file5.wad\""
                  "Content-Type: application/x-doom"
                  "Content-Transfer-Encoding: base64"
                  ""
                  "SGVsbG8gV29ybGQ="
                  sprintf "--%s--" expectedBoundary2
                  sprintf "--%s--" expectedBoundary1
                  ""
                  "" ]
                |> String.concat "\r\n"
            Assert.Equal("should have correct body", expected, subject)

        testCase "can format urlencoded data" <| fun _ ->
            // http://www.url-encode-decode.com/
            // https://unspecified.wordpress.com/2008/07/08/browser-uri-encoding-the-best-we-can-do/
            // http://stackoverflow.com/questions/912811/what-is-the-proper-way-to-url-encode-unicode-characters
            // https://www.ietf.org/rfc/rfc1738.txt
            // http://www.w3.org/TR/html401/interact/forms.html
            // http://stackoverflow.com/questions/4007969/application-x-www-form-urlencoded-or-multipart-form-data
            Assert.Equal("Should encode Swedish properly",
                         "user_name=%c3%85sa+den+R%c3%b6de&user_pass=Bovi%c4%87",
                         Impl.uriEncode utf8 [
                            ("user_name", "Åsa den Röde")
                            ("user_pass", "Bović")
                         ])

        testCase "can format urlencoded form" <| fun _ ->
            let clientState = { HttpFsState.empty with random = Random testSeed }
            // example from http://www.w3.org/TR/html401/interact/forms.html
            [   NameValue ("submit", "Join Now!")
                NameValue ("user_name", "Åsa den Röde")
                NameValue ("user_pass", "Bović")
            ]
            |> fun form -> Impl.formatBody clientState (None, utf8, BodyForm form)
            |> fun (newCt, body) ->
              let bodyToString = body |> bodyToBytes |> utf8.GetString
              Assert.Equal(bodyToString, "submit=Join+Now!&user_name=%c3%85sa+den+R%c3%b6de&user_pass=Bovi%c4%87")
              Assert.Equal("should have new ct",
                           ContentType.parse "application/x-www-form-urlencoded",
                           newCt)
    ]

[<Tests>]
let internals =
    testCase "http web request url" <| fun _ ->
        let hfsReq = Request.create Get (Uri "http://localhost/") |> Request.queryStringItem "a" "1"
        let netReq, _ = DotNetWrapper.toHttpWebRequest HttpFsState.empty hfsReq
        Assert.Equal(string netReq.RequestUri, "http://localhost/?a=1")

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
    Assert.StringContains("", "åäö", rawBodyString)
    Assert.StringContains("", "©®™", rawBodyString)
