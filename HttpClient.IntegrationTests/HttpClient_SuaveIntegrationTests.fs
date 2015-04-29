module HttpClient_SuaveIntegrationTests

open NUnit.Framework
open System
open System.Threading

open Suave
open Suave.Types
open Suave.Http
open Suave.Http.Applicatives
open Suave.Http.Successful
open Suave.Http.RequestErrors
open Suave.Web

open HttpClient

let app =
  choose
    [   POST
        >>= choose [
            path "/filecount" >>= warbler (fun ctx ->
                OK (string ctx.request.files.Length))

            path "/filenames"
                >>= Writers.setMimeType "application/json"
                >>= warbler (fun ctx ->
                    printfn "inside suave"
                    ctx.request.files
                    |> List.map (fun f -> "\"" + f.fileName + "\"")
                    |> String.concat ","
                    |> fun files -> "[" + files + "]"
                    |> OK)
            
            NOT_FOUND "Nope."
        ]
    ]

[<TestFixture; Ignore "pending: https://github.com/SuaveIO/suave/issues/228">]
type ``Suave Integration Tests`` ()=
    let cts = new CancellationTokenSource()

    [<TestFixtureSetUp>]
    member x.fixtureSetup() =
        let config =
            { defaultConfig with
                cancellationToken = cts.Token
                logger = Logging.Loggers.ConsoleWindowLogger(Logging.Verbose) }
        let listening, server = startWebServerAsync config app
        Async.Start(server, cts.Token) |> ignore

    [<TestFixtureTearDown>]
    member x.fixtureTearDown() =
        printfn "cancelling"
        cts.Cancel true |> ignore

    [<Test>]
    member x.``server receives valid filenames``() =
        let firstCt, secondCt =
            ContentType.Parse "text/plain" |> Option.get,
            ContentType.Parse "text/plain" |> Option.get

        let req =
            createRequest Post (Uri "http://localhost:8083/filenames")
            |> withBody
                ([ FormFile ("file", ("file1.txt", firstCt, Plain "Hello World")) ]|> BodyForm)
            (*(
                // example from http://www.w3.org/TR/html401/interact/forms.html
                [   NameValue { name = "submit-name"; value = "Larry" }
                    MultiFile ("files",
                               [ "file1.txt", firstCt, Plain "Hello World"
                                 "file2.gif", secondCt, Plain "...contents of file2.gif..."
                               ])
                ]
                |> BodyForm)*)

        printfn "get response body"
        let response =
            req |> getResponseBody

        printfn "asserting"
        for fileName in [ "file1.txt"; "file2.gif" ] do
            Assert.That(response, Is.StringContaining(fileName))