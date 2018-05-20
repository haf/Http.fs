module HttpFs.Tests.SSE

open Expecto
open HttpFs.SSE

/// Taken from here: https://html.spec.whatwg.org/multipage/server-sent-events.html#server-sent-events-intro
module IntroductionExamples =
  let example1Lines = [
    "data: This is the first message."
    ""
    "data: This is the second message, it"
    "data: has two lines."
    ""
    "data: This is the third message."
    ""
  ]

  let example2Lines = [
    "event: add"
    "data: 73857293"
    ""
    "event: remove"
    "data: 2153"
    ""
    "event: add"
    "data: 113411"
    ""
  ]

/// Taken from here: https://html.spec.whatwg.org/multipage/server-sent-events.html#event-stream-interpretation
module InterpretationExamples =
    let example1Lines = [
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
    testList "examples" [
      testList "introduction" [
        testCase "example1" <| fun _ ->
          let example1Events =
            IntroductionExamples.example1Lines
            |> events
            |> Seq.toArray

          Expect.equal example1Events.Length 3 "should result in 3 message"

          Expect.equal example1Events.[0].data "This is the first message." "should have correct data"
          Expect.equal example1Events.[0].lastEventId "" "should have correct event id"
          Expect.equal example1Events.[0].eventType "message" "should have correct event type"

          Expect.equal example1Events.[1].data "This is the second message, it\nhas two lines." "should have correct data"
          Expect.equal example1Events.[1].lastEventId "" "should have correct event id"
          Expect.equal example1Events.[1].eventType "message" "should have correct event type"

          Expect.equal example1Events.[2].data "This is the third message." "should have correct data"
          Expect.equal example1Events.[2].lastEventId "" "should have correct event id"
          Expect.equal example1Events.[2].eventType "message" "should have correct event type"

        testCase "example2" <| fun _ ->
          let example2Events =
            IntroductionExamples.example2Lines
            |> events
            |> Seq.toArray

          Expect.equal example2Events.Length 3 "should result in 3 message"

          Expect.equal example2Events.[0].data "73857293" "should have correct data"
          Expect.equal example2Events.[0].lastEventId "" "should have correct event id"
          Expect.equal example2Events.[0].eventType "add" "should have correct event type"

          Expect.equal example2Events.[1].data "2153" "should have correct data"
          Expect.equal example2Events.[1].lastEventId "" "should have correct event id"
          Expect.equal example2Events.[1].eventType "remove" "should have correct event type"

          Expect.equal example2Events.[2].data "113411" "should have correct data"
          Expect.equal example2Events.[2].lastEventId "" "should have correct event id"
          Expect.equal example2Events.[2].eventType "add" "should have correct event type"
      ]

      testList "interpretation" [
        testCase "example1" <| fun _ ->
          let example1Events =
            InterpretationExamples.example1Lines
            |> events
            |> Seq.toArray

          Expect.equal example1Events.Length 1 "should result in 1 message"

          Expect.equal example1Events.[0].data "YHOO\n+2\n10" "should have correct data"
          Expect.equal example1Events.[0].lastEventId "" "should have correct event id"
          Expect.equal example1Events.[0].eventType "message" "should have correct event type"

        testCase "example2" <| fun _ ->
          let example2Events =
            InterpretationExamples.example2Lines
            |> events
            |> Seq.toArray

          Expect.equal example2Events.Length 3 "should result in 3 messages"

          Expect.equal example2Events.[0].data "first event" "should have correct data"
          Expect.equal example2Events.[0].lastEventId "1" "should have correct event id"
          Expect.equal example2Events.[0].eventType "message" "should have correct event type"

          Expect.equal example2Events.[1].data "second event" "should have correct data"
          Expect.equal example2Events.[1].lastEventId "" "should have correct event id"
          Expect.equal example2Events.[1].eventType "message" "should have correct event type"

          Expect.equal example2Events.[2].data " third event" "should have correct data"
          Expect.equal example2Events.[2].lastEventId "" "should have correct event id"
          Expect.equal example2Events.[2].eventType "message" "should have correct event type"

        testCase "example3" <| fun _ ->
          let example3Events =
            InterpretationExamples.example3Lines
            |> events
            |> Seq.toArray
        // This is what the example decription says
        //   Expect.equal example3Events.Length 2 "should result in 2 messages"
        //
        //   Expect.equal example3Events.[0].data "" "should have correct data"
        //   Expect.equal example3Events.[0].lastEventId "" "should have correct event id"
        //   Expect.equal example3Events.[0].eventType "message" "should have correct event type"
        //
        //   Expect.equal example3Events.[1].data "\n" "should have correct data"
        //   Expect.equal example3Events.[1].lastEventId "" "should have correct event id"
        //   Expect.equal example3Events.[1].eventType "message" "should have correct event type"
        // 
        // But the specification says that empty events are not fired at all,
        // so this is what we actually expect.

          Expect.equal example3Events.Length 1 "should result in 1 messages"

          Expect.equal example3Events.[0].data "\n" "should have correct data"
          Expect.equal example3Events.[0].lastEventId "" "should have correct event id"
          Expect.equal example3Events.[0].eventType "message" "should have correct event type"

        testCase "example4" <| fun _ ->
          let example4Events =
            InterpretationExamples.example4Lines
            |> events
            |> Seq.toArray

          Expect.equal example4Events.Length 2 "should result in 2 messages"

          Expect.equal example4Events.[0].data "test" "should have correct data"
          Expect.equal example4Events.[0].lastEventId "" "should have correct event id"
          Expect.equal example4Events.[0].eventType "message" "should have correct event type"

          Expect.equal example4Events.[1].data "test" "should have correct data"
          Expect.equal example4Events.[1].lastEventId "" "should have correct event id"
          Expect.equal example4Events.[1].eventType "message" "should have correct event type"
      ]
    ]
  ]