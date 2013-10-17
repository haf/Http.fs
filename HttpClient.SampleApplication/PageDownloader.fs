namespace HttpClient.SampleApplication

open HttpClient

// I mainly did this to see how the module could be mocked out for testing
type PageDownloader(getRequestBodyFunction) =

    member this.countWordInstances word url =
        let (body:string) = createRequest Get url |> getRequestBodyFunction 
        let words = body.Split(' ')
        let wordCounts = words |> Seq.countBy id
        snd (wordCounts |> Seq.find (fun item -> fst item = word))
        