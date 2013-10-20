namespace HttpClient.SampleApplication

open HttpClient

// I mainly did this to see how the module could be mocked out for testing
type PageDownloader(getResponseBodyFunction) =

    let countWord word (text:string) =
        let words = text.Split(' ')
        let wordCounts = words |> Seq.countBy id
        snd (wordCounts |> Seq.find (fun item -> fst item = word))

    member this.countWordInstances word url =
        let (body:string) = createRequest Get url |> getResponseBodyFunction 
        body |> countWord word
        
    member this.countWordInstances2 word urls =
        urls
        |> List.map (fun url -> createRequest Get url |> getResponseBodyAsync)
        |> Async.Parallel
        |> Async.RunSynchronously
        |> Seq.sumBy (fun body -> body |> countWord word)