module HttpFs.SampleApplication.PageDownloaderTests

open System
open NUnit.Framework
open FsUnit
open HttpFs
open HttpFs.SampleApplication

[<Test>]
let ``countWordInstances counts the number of times a word is repeated at a given URL`` () =
    let fakeGetResponseBodyFunction request =
      async {
        return "hi world hi hi hello hi ciao hi"
      }

    let downloader = new PageDownloader( fakeGetResponseBodyFunction )
    downloader.countWordInstances "hi" (Uri "some url")
    |> should equal 5