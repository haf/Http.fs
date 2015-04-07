#!/usr/bin/env fsharpi

#I "bin/Debug"
#r "HttpClient.dll"

open HttpClient

let firstCt, secondCt, thirdCt =
    ContentType.Create("text", "plain"),
    ContentType.Create("text", "plain"),
    ContentType.Create("image", "gif")

let req =
    createRequest Post "http://localhost:1234/filenames"
    |> withBody
        //([ SingleFile ("file", ("file1.txt", firstCt, Plain "Hello World")) ]|> BodyForm)

                        // example from http://www.w3.org/TR/html401/interact/forms.html
        ([   NameValue { name = "submit-name"; value = "Larry" }
             FormFile ("files", ("file1.txt", firstCt, Plain "Hello World"))
             FormFile ("files", ("file2.gif", secondCt, Plain "...contents of file2.gif..."))
             FormFile ("files", ("tracker.gif", thirdCt, Binary [|1uy; 2uy; 3uy |]))
        ]
        |> BodyForm)

let response = req |> getResponse

printfn "%O" response