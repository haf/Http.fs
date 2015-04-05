module Fuchu

open global.Fuchu
open System

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