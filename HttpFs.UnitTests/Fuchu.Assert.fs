module Fuchu

open global.Fuchu
open System
open System.IO

let given msg (o : 'a) (tests : (string * ('a -> unit)) list) =
  let toTestCase (name, f) =
    testCase name (fun () -> f o)
  testList msg (tests |> List.map toTestCase)

type Assert with
  static member Equal(subject : 'a, expected : 'a) =
    Assert.Equal("should equal", expected, subject)

  static member IsTrue(value) =
    Assert.Equal("should be true", true, value)

  static member IsFalse(value) =
    Assert.Equal("should be false", false, value)

  static member IsNone(value) =
    Assert.Equal("should be None", None, value)

  static member Contains (items, value) =
    match List.tryFind ((=) value) items with
    | None -> Tests.failtest "couldn't find %A in list %A" value items
    | Some x -> ()

  static member Empty (xs : 'a list) =
    match xs with
    | [] -> ()
    | xs -> Tests.failtestf "expected empty list, but got %A" xs

  static member StreamsEqual(msg, s1 : Stream, s2 : Stream) =
    let buf = Array.zeroCreate<byte> 2
    let rec compare pos =
      match s1.Read(buf, 0, 1), s2.Read(buf, 1, 1) with
      | x, y when x <> y -> Tests.failtestf "Not equal at pos %d" pos
      | 0, _ -> ()
      | _ when buf.[0] <> buf.[1] -> Tests.failtestf "Not equal at pos %d" pos
      | _ -> compare (pos + 1)
    compare 0

[<Tests>]
let asserts =
  let stream data =
    let ms = new MemoryStream()
    ms.Write(data, 0, data.Length)
    ms.Seek(0L, SeekOrigin.Begin) |> ignore
    ms
  let shouldFail f =
    try
      f ()
      raise (ApplicationException("shouldn't be reached"))
    with
    | :? ApplicationException as e -> Tests.failtest "streams should not equal"
      | _ -> ()
  testList "" [
    testCase "compare non eq streams - this test should fail" <| fun _ ->
      use ms = stream [| 1uy; 2uy; 3uy |]
      use ms2 = stream [| 1uy; 2uy; 1uy |]
      shouldFail (fun () -> Assert.StreamsEqual("nope", ms, ms2))

    testCase "non eq lengths" <| fun _ ->
      use ms = stream [| 1uy; 2uy |]
      use ms2 = stream [| 1uy; 2uy; 1uy |]
      shouldFail (fun () -> Assert.StreamsEqual("nope", ms, ms2))
  ]