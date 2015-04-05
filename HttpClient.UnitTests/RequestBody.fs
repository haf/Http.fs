module HttpClient.Tests.RequestBody

open Fuchu
open HttpClient

let VALID_URL = "http://www"
let createValidRequest = createRequest Get VALID_URL

[<Tests>]
let apiUsage =
    testList "api usage" [
        testCase "withBody sets the request body" <| fun _ ->
            Assert.Equal((createValidRequest |> withBody """Hello mum!%2\/@$""").Body.Value, """Hello mum!%2\/@$""")

        testCase "withBody uses default character encoding of ISO-8859-1" <| fun _ ->
            Assert.Equal((createValidRequest |> withBody "whatever").BodyCharacterEncoding.Value, "ISO-8859-1")

        testCase "withBodyEncoded sets the request body" <| fun _ ->
            Assert.Equal((createValidRequest |> withBodyEncoded """Hello mum!%2\/@$""" "UTF-8").Body.Value, """Hello mum!%2\/@$""")

        testCase "withBodyEncoded sets the body encoding" <| fun _ ->
            Assert.Equal((createValidRequest |> withBodyEncoded "Hi Mum" "UTF-8").BodyCharacterEncoding.Value, "UTF-8")

        testCase "if a body character encoding is somehow not specified, throws an exception" <| fun _ ->
            let request =
                createRequest Post "http://localhost:1234/TestServer/RecordRequest" 
                |> withBodyEncoded "¥§±Æ" "UTF-8" // random UTF-8 characters
                    
            let dodgyRequest = { request with BodyCharacterEncoding = None }

            Assert.Raise("when no body char encoding", typeof<Exception>,
                         fun () -> dodgyRequest |> getResponseCode |> ignore)
    ]


let useCase = testCase

(* Use cases for RequestBody.

   It should be possible to send data using all formattings that are common
   to use - raw, urlencoded and formdata.

   A good reference is the implementation of the MailGun API since it really
   uses HTTP as an application protocol.

   https://documentation.mailgun.com/api-sending.html
*)

[<Tests>]
let useCases =
    testList "use cases" [
        testList "sending forms" [
            useCase "sending key-value pairs (urlencoded style)" <| fun _ ->
                ()

            useCase "sending key-value paris (formdata style)" <| fun _ ->
                ()
        ]

        testList "sending files" [
            useCase "regular files" <| fun _ ->
                ()

            useCase "sending UTF8 file names" <| fun _ ->
                ()
        ]

        testCase "sending raw bytes" <| fun _ ->
            ()

        testCase "sending using socket" <| fun _ ->
            ()
    ]