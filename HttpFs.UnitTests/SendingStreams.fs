module HttpFs.UnitTests.SendingStreams

open System
open System.IO
open System.Reflection
open Expecto
open Hopac
open HttpFs
open HttpFs.Client
open Suave
open Suave.Logging
open Suave.Filters
open Suave.RequestErrors
open Suave.Testing
open Suave.Operators


let app =
  choose
    [ choose [POST;PUT;PATCH]
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
  let runWithConfig = runWith defaultConfig
  let uriFor (res : string) = Uri (sprintf "http://localhost:8080/%s" (res.TrimStart('/')))
  let request method res = Request.create ``method`` (uriFor res)

  testCase "can send/receive" <| fun _ ->
    job {
      let ctx = runWithConfig app
      try
        for method in [Post;Put;Patch] do
          use fs = File.OpenRead (pathOf "pix.gif")
          let file = "pix.gif", ContentType.create("image", "gif"), StreamData fs
          
          use ms = new MemoryStream()
          //printfn "--- get response"
          use! resp =
            request method "gifs/echo"
            |> Request.body (BodyForm [ FormFile ("img", file) ])
            |> Request.setHeader (Custom ("Access-Code", "Super-Secret"))
            |> getResponse

          do! Job.awaitUnitTask (resp.body.CopyToAsync ms)

          fs.Seek(0L, SeekOrigin.Begin) |> ignore
          ms.Seek(0L, SeekOrigin.Begin) |> ignore

          Expect.streamsEqual fs ms <| sprintf "the input should eq the echoed %A data" method
      finally
        disposeContext ctx
        ()
    } |> Hopac.run