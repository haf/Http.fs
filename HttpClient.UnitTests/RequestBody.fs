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