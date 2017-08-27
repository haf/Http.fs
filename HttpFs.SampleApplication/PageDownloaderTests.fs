module HttpFs.SampleApplication.PageDownloaderTests

open System
open Expecto
open Hopac
open HttpFs
open HttpFs.SampleApplication

[<Tests>]
let tests =
  testList "page downloader" [
    testCase "countWordInstances counts the number of times a word is repeated at a given URL" <| fun _ ->
      let fakeGetResponseBodyFunction request =
        Job.result "hi world hi hi hello hi ciao hi"

      let downloader = new PageDownloader( fakeGetResponseBodyFunction )

      let count =
        downloader.countWordInstances "hi" (Uri "https://www")
        |> Hopac.run
      
      Expect.equal count 5 "count should be equal"
  ]