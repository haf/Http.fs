// Some examples of the HttpClient module in action
module HttpFs.SampleApplication.Program

open HttpFs.Client
open HttpFs.SampleApplication
open Hopac
open System.IO
open System

module Async =
  let map f value =
    async {
      let! v = value
      return f v
    }
  let bind f value =
    async {
      let! v = value
      return! f v
    }


let download = Request.create Get >> Request.responseAsBytes

// Use our PageDownloader to count the instances of a word on the bbc news site
// We pass the getResponseBody function in as a dependency to PageDownloader so
// it can be unit tested
let countWords () =
  job {
    let downloader = new PageDownloader(Request.responseAsString)
    printfn "What word would you like to count on bbc.co.uk/news?"
    let word = Console.ReadLine()

    let! count = downloader.countWordInstances word (Uri "http://www.bbc.co.uk/news/")
    printfn "'%s' was found %d times on bbc.co.uk/news" word count
    return count
  }

let private withTimer f =
  let timer = System.Diagnostics.Stopwatch.StartNew()
  let res = f ()
  timer.Stop()
  res, timer

/// Download some sites sequentially
let downloadSequentially sites =
  let res, timer = withTimer <| fun _ ->
    sites |> Async.map download |> Async.RunSynchronously
  printfn "Pages downloaded sequentially in %d ms" timer.ElapsedMilliseconds
  res

/// Download some sites in parallel
let downloadInParallel (sites : Uri list) =
  let res, timer = withTimer <| fun _ ->
    sites |> List.map download |> Job.conCollect |> Hopac.run
  printfn "Pages downloaded in parallel in %d ms" timer.ElapsedMilliseconds
  res

let returnToContinue message =
    printfn "\n%s" message
    Console.ReadLine() |> ignore

// create a more coplex request, and see the request & response
// (this should get a 302)
let complexRequest() = job {
  let request =
    Request.create Get (Uri "http://www.google.com/search")
    |> Request.queryStringItem "q" "gibbons"
    |> Request.cookie (Cookie.create("ignoreMe", "hi mum"))
    |> Request.setHeader (Accept "text/html")
    |> Request.setHeader (UserAgent "Mozilla/5.0 (Windows NT 6.2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/29.0.1547.57 Safari/537.36")

  returnToContinue "Press Return to see the request"
  printfn "%A" request

  printfn "\nRetrieving response..."
  let! response = request |> getResponse

  returnToContinue "Press Return to see the response"
  printfn "%A" response
  return response
}

let downloadImage() = job {
  let! response = Request.create Get (Uri "http://fsharp.org/img/logo.png") |> getResponse
  use ms = new IO.MemoryStream()
  response.body.CopyTo(ms)
  let bytes = ms.ToArray()

  printfn "Please enter path to save the image to, e.g. c:/temp (file will be testImage.png)"
  let filename = Console.ReadLine() + "/testImage.png"

  use file = File.Create(filename)
  file.Write(bytes, 0, bytes.Length)

  printfn "'%s' written to disk" filename
}

let downloadImagesInParallel images =
  let res, timer = withTimer <| fun _ ->
    images
    |> List.map (Request.create Get >> getResponse)
    |> List.map (Job.map Response.readBodyAsBytes)
    |> Job.conCollect
    |> Hopac.run
    |> ignore
  printfn "Images downloaded in parallel in %d ms" timer.ElapsedMilliseconds

// access the response stream and save it to a file directly
let downloadLargeFile() =
  job {
    printfn "Please enter path to save the 'large' file to, e.g. c:/temp (file will be large.bin)"
    let filename = Console.ReadLine() + "/large.bin"

    let saveToFile (sourceStream:Stream) =
      use destStream = new FileStream(filename, FileMode.Create)
      do sourceStream.CopyTo(destStream)

    use! response = Request.create Get (Uri "http://fsharp.org/img/logo.png") |> getResponse
    saveToFile response.body

    printfn "'%s' downloaded" filename
  }

[<EntryPoint>]
let Main(_) =
  job {
    let! count = countWords ()
    printfn "** Word Count **"
    return count }
  |> Hopac.run |> printfn "%d"

  printfn "\n** Downloading sites: Sequential vs Parallel **"
  [ "http://news.bbc.co.uk"
    "http://www.facebook.com"
    "http://www.wikipedia.com"
    "http://www.stackoverflow.com" ]
  |> List.map (fun u -> Uri u)
  |> downloadInParallel
  |> ignore

  printfn "\n** Creating a complex request **"
  complexRequest () |> Hopac.run |> printfn "%A"

  printfn "\n** Downloading image **"
  downloadImage () |> ignore

  printfn "\n** Downloading images: Sequential vs Parallel **"

  let images =
    [ "http://fsharp.org/img/sup/quantalea.png"
      "http://fsharp.org/img/sup/mbrace.png"
      "http://fsharp.org/img/sup/statfactory.jpg"
      "http://fsharp.org/img/sup/tsunami.png"
    ] |> List.map (fun u -> Uri u)

  //downloadImagesInParallel images

  printfn "\n** Downloading a 'large' file directly from the response stream **"
  //downloadLargeFile()

  returnToContinue "Press Return to exit"
  0