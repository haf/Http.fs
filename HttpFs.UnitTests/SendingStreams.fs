module HttpFs.UnitTests.SendingStreams

open HttpFs
open HttpFs.Client
open Fuchu
open System
open System.IO
open System.Reflection
open Hopac
open Suave
open Suave.Logging
open Suave.Filters
open Suave.RequestErrors
open Suave.Testing
open Suave.Operators


let app =
  choose
    [ POST
      >=> choose [
          path "/gifs/echo"
              >=> Writers.setMimeType "image/gif"
              >=> warbler (fun ctx ->
                  let file = ctx.request.files.Head
                  //printfn "||| in suave, handing over to sendFile, file %s len %d"
                  //        file.tempFilePath (FileInfo(file.tempFilePath).Length)
                  Files.sendFile file.tempFilePath false)
          NOT_FOUND "Nope."
      ]
    ]

let pathOf relativePath =
  let here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
  Path.Combine(here, relativePath)

[<Tests>]
let tests =
  let config = { defaultConfig with logger = Loggers.saneDefaultsFor LogLevel.Warn }
  let runWithConfig = runWith config
  let uriFor (res : string) = Uri (sprintf "http://localhost:8083/%s" (res.TrimStart('/')))
  let postTo res = Request.create Post (uriFor res) |> Request.keepAlive false
  let successful = function
    | Choice1Of2 resp -> resp
    | Choice2Of2 err -> Tests.failtestf "Error from request %A" err

  testCase "can send/receive" <| fun _ ->
    job {
      let ctx = runWithConfig app
      try
        use fs = File.OpenRead (pathOf "pix.gif")
        let file = "pix.gif", ContentType.create("image", "gif"), StreamData fs

        use ms = new MemoryStream()
        //printfn "--- get response"
        use! resp =
          postTo "gifs/echo"
          |> Request.body (BodyForm [ FormFile ("img", file) ])
          |> Request.setHeader (Custom ("Access-Code", "Super-Secret"))
          |> getResponse
          |> Job.map successful

        printfn "--- reading response body stream"
        do! Job.awaitUnitTask (resp.body.CopyToAsync ms)

        fs.Seek(0L, SeekOrigin.Begin) |> ignore
        ms.Seek(0L, SeekOrigin.Begin) |> ignore
        printfn "--- asserting"
        Assert.StreamsEqual("the input should eq the echoed data", ms, fs)

      finally
        disposeContext ctx
        ()
    } |> run
