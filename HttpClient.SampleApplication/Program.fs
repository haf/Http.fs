module Program

open HttpClient.SampleApplication
open System

[<EntryPoint>]
     let Main(_) = 

         let downloader = new PageDownloader( HttpClient.getResponseBody )
         printfn "What word would you like to count on bbc.co.uk/news?"
         let word = Console.ReadLine();
         let count = downloader.countWordInstances word "http://www.bbc.co.uk/news/"
         printfn "'%s' was found %d times" word count
         printfn "Press Return to exit"
         Console.ReadLine() |> ignore
         // main entry point return
         0