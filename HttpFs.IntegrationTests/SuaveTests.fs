module HttpFs.IntegrationTests.SuaveTests

open NUnit.Framework
open System
open System.Reflection
open System.IO
open System.Threading

open Suave
open Suave.Types
open Suave.Http
open Suave.Http.Applicatives
open Suave.Http.Successful
open Suave.Http.RequestErrors
open Suave.Web

open HttpFs.Client

type Assert with
  static member StreamsEqual(msg, s1 : Stream, s2 : Stream) =
    let bufLen = 0x10
    let s1buf, s2buf = Array.zeroCreate<byte> bufLen, Array.zeroCreate<byte> bufLen
    let mutable read = 0
    let mutable pos = 0
    let mutable eq = true
    while read > 0 && eq do
      read <- s1.Read(s1buf, pos, bufLen)
      let s2read = s2.Read(s2buf, pos, bufLen)
      eq <- eq && read = s2read
      eq <- eq && not (Array.exists2 (fun s1 s2 -> s1 <> s2) s1buf.[0..read] s2buf.[0..read])
      pos <- pos + read
    if not eq then Assert.Fail(sprintf "The streams to not equal at position %d" pos)

let app =
  choose
    [ POST
      >>= choose [
          path "/filecount" >>= warbler (fun ctx ->
            OK (string ctx.request.files.Length))

          path "/filenames"
              >>= Writers.setMimeType "application/json"
              >>= warbler (fun ctx ->
                  //printfn "+++++++++ inside suave +++++++++++++"
                  ctx.request.files
                  |> List.map (fun f -> "\"" + f.fileName + "\"")
                  |> String.concat ","
                  |> fun files -> "[" + files + "]"
                  |> OK)
          
          path "/gifs/echo"
              >>= Writers.setMimeType "image/gif"
              >>= warbler (fun ctx ->
                  let file = ctx.request.files.Head
                  Files.sendFile file.tempFilePath false)

          NOT_FOUND "Nope."
      ]
    ]

let pathOf relativePath =
  let here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
  Path.Combine(here, relativePath)

[<TestFixture>]
type ``Suave Integration Tests`` () =
  let cts = new CancellationTokenSource()
  let uriFor (res : string) = Uri (sprintf "http://localhost:8083/%s" (res.TrimStart('/')))
  let postTo res =
    createRequest Post (uriFor res)
    |> withKeepAlive false

  [<TestFixtureSetUp>]
  member x.fixtureSetup() =
    let config =
      { defaultConfig with
          cancellationToken = cts.Token
          logger = Logging.Loggers.ConsoleWindowLogger(Logging.Warn) }
    let listening, server = startWebServerAsync config app
    Async.Start(server, cts.Token) |> ignore
    ()

  [<TestFixtureTearDown>]
  member x.fixtureTearDown() =
    cts.Cancel true |> ignore

  [<Test>]
  member x.``server receives valid filenames``() =
    let firstCt, secondCt, thirdCt, fourthCt =
      ContentType.Parse "text/plain" |> Option.get,
      ContentType.Parse "text/plain" |> Option.get,
      ContentType.Create("application", "octet-stream"),
      ContentType.Create("image", "gif")

    let req =
      postTo "filenames"
      |> withBody
          // example from http://www.w3.org/TR/html401/interact/forms.html
          (BodyForm
            [  NameValue { name = "submit-name"; value = "Larry" }
               MultipartMixed ("files",
                 [ "file1.txt", firstCt, Plain "Hello World" // => plain
                   "file2.gif", secondCt, Plain "Loopy" // => plain
                   "file3.gif", thirdCt, Plain "Thus" // => base64
                   "cute-cat.gif", fourthCt, Binary (File.ReadAllBytes (pathOf "cat-stare.gif")) // => binary
                 ])
            ])
    System.Net.ServicePointManager.Expect100Continue <- false
    let response = Request.responseAsString req |> Async.RunSynchronously

    for fileName in [ "file1.txt"; "file2.gif"; "file3.gif"; "cute-cat.gif" ] do
      Assert.That(response, Is.StringContaining(fileName))

  [<Test>]
  member x.``server can echo gif image sent binary`` () =
    use fs = File.OpenRead (pathOf "pix.gif")
    let file = "pix.gif", ContentType.Create("image", "gif"), StreamData fs

    use resp =
      postTo "gifs/echo"
      |> withBody (BodyForm [ FormFile ("img", file) ])
      |> getResponse
      |> Async.RunSynchronously

    use ms = new MemoryStream()
    resp.Body.CopyTo ms
    fs.Seek(0L, SeekOrigin.Begin) |> ignore

    Assert.StreamsEqual("the input should eq the echoed data", ms, fs)
