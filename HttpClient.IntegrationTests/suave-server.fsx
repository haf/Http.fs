#!/usr/bin/env fsharpi
#I "bin/Debug"
#r "Suave.dll"

open System
open System.Net

open Suave
open Suave.Types
open Suave.Http
open Suave.Http.Applicatives
open Suave.Http.Successful
open Suave.Http.RequestErrors
open Suave.Web

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

let config =
  { defaultConfig with
      bindings =
        [ HttpBinding.mk HTTP (IPAddress.Parse "0.0.0.0") 1234us
        ]
      logger = Logging.Loggers.ConsoleWindowLogger(Logging.Verbose) }
startWebServer config app
