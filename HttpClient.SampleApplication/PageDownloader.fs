namespace HttpClient.SampleApplication

open HttpClient

// I mainly did this to see how the module could be mocked out for testing
type PageDownloader(getResponseBodyFunction) =

    let countWord word (text:string) =
        let words = text.Split(' ')
        let wordCounts = words |> Seq.countBy id
        if wordCounts |> Seq.exists (fun item -> fst item = word) then
            snd (wordCounts |> Seq.find (fun item -> fst item = word))
        else
            0

    member this.countWordInstances word url = async {
        let! (body:string) = createRequest Get url |> getResponseBodyFunction
        return body |> countWord word
      }