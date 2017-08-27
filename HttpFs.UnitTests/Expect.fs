module Expect

open Expecto

let canPick (map: Map<'a, 'b>) (element: 'b) (message: string) : unit =
    map
    |> Map.tryPick (fun k v -> if v = element then Some v else None)
    |> function
    | None -> Tests.failtestf "couldn't find value %A in map %A" element map
    | Some x -> ()