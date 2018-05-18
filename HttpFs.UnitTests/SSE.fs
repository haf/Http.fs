module HttpFs.Tests.SSE

open Expecto
open HttpFs.SSE

let stockLines = [
  "data: YHOO"
  "data: +2"
  "data: 10"
  ""
]

let example2Lines = [
  ": test stream"
  ""
  "data: first event"
  "id: 1"
  ""
  "data:second event"
  "id"
  ""
  "data:  third event"
  ""
]

let example3Lines = [
  "data"
  ""
  "data"
  "data"
  ""
  "data:"
]

let example4Lines = [
  "data:test"
  ""
  "data: test"
  ""
]

[<Tests>]
let interpretation =
  testList "interpretation" [
    testCase "stock" <| fun _ ->
      let stockEvents =
        stockLines
        |> events
        |> Seq.toArray
      Expect.equal stockEvents.Length 1 "should result in 1 message"
      Expect.equal stockEvents.[0].data "YHOO\n+2\n10" "should have correct data"
      Expect.equal stockEvents.[0].lastEventId "" "should have correct event id"
      Expect.equal stockEvents.[0].eventType "message" "should have correct event type"

    testCase "example2" <| fun _ ->
      let example2Events =
        example2Lines
        |> events
        |> Seq.toArray
      Expect.equal example2Events.Length 3 "should result in 3 messages"
      Expect.equal example2Events.[0].data "first event" "should have correct data"
      Expect.equal example2Events.[0].lastEventId "1" "should have correct event id"
      Expect.equal example2Events.[0].eventType "message" "should have correct event type"
      Expect.equal example2Events.[1].data "second event" "should have correct data"
      Expect.equal example2Events.[1].lastEventId "" "should have correct event id"
      Expect.equal example2Events.[0].eventType "message" "should have correct event type"
      Expect.equal example2Events.[2].data " third event" "should have correct data"
      Expect.equal example2Events.[2].lastEventId "" "should have correct event id"
      Expect.equal example2Events.[0].eventType "message" "should have correct event type"

    testCase "example3" <| fun _ ->
      let example3Events =
        example3Lines
        |> events
        |> Seq.toArray
    // This is what the example decription says
    //   Expect.equal example3Events.Length 2 "should result in 2 messages"
    //   Expect.equal example3Events.[0].data "" "should have correct data"
    //   Expect.equal example3Events.[0].lastEventId "" "should have correct event id"
    //   Expect.equal example3Events.[0].eventType "message" "should have correct event type"
    //   Expect.equal example3Events.[1].data "\n" "should have correct data"
    //   Expect.equal example3Events.[1].lastEventId "" "should have correct event id"
    //   Expect.equal example3Events.[0].eventType "message" "should have correct event type"
    // But the specification says that empty events are not fired at all,
    // so this is what we actually expect.
      Expect.equal example3Events.Length 1 "should result in 1 messages"
      Expect.equal example3Events.[0].data "\n" "should have correct data"
      Expect.equal example3Events.[0].lastEventId "" "should have correct event id"
      Expect.equal example3Events.[0].eventType "message" "should have correct event type"

    testCase "example4" <| fun _ ->
      let example4Events =
        example4Lines
        |> events
        |> Seq.toArray
      Expect.equal example4Events.Length 2 "should result in 2 messages"
      Expect.equal example4Events.[0].data "test" "should have correct data"
      Expect.equal example4Events.[0].lastEventId "" "should have correct event id"
      Expect.equal example4Events.[0].eventType "message" "should have correct event type"
      Expect.equal example4Events.[1].data "test" "should have correct data"
      Expect.equal example4Events.[1].lastEventId "" "should have correct event id"
      Expect.equal example4Events.[0].eventType "message" "should have correct event type"
  ]