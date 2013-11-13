Http.fs
=======

An HTTP client library for F#, which wraps HttpWebRequest/Response in a glorious functional jacket!

## How do I use it? ##

In it's simplest form, this will get you a web page:

      createRequest Get "http://somesite.com" |> getResponseBody  

A Request (an immutable record type) is built up in a Fluent Builder stylee as follows:

    let request =  
      createRequest Post "http://somesite.com"  
      |> withQueryStringItem {name="search"; value="jeebus"}  
      |> withHeader (UserAgent "Chrome or summat")  
      |> withHeader (Custom {name="X-My-Header"; value="hi mum"})  
      |> withAutoDecompression DecompressionScheme.GZip  
      |> withCookie {name="session"; value="123"}  
      |> withBody "Check out my sexy body"  
  
The Http response (or just the response code/body) is retrieved using one of the following:

    request |> getResponse  
    request |> getResponseCode  
    request |> getResponseBody  

If you get the full response (another record), you can get things from it like so:

    response.StatusCode  
    response.EntityBody.Value  
    response.ContentLength  
    response.Cookies.["cookie1"]  
    response.Headers.[ContentEncoding]  
    response.Headers.[NonStandard("X-New-Fangled-Header")] 
    
If you like to do things asynchronously, you're in luck, we have functions for that:

    request |> getResponseAsync  
    request |> getResponseCodeAsync  
    request |> getResponseBodyAsync  
    
So you can do the old download-multiple-sites-in-parallel thing:

    ["http://news.bbc.co.uk"
     "http://www.wikipedia.com"
     "http://www.stackoverflow.com"]
    |> List.map (fun url -> createRequest Get url |> getResponseBodyAsync)
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Array.iter (printfn "%s")

## Cool!  So how do I get it in my code? ##

Everything you need's in the Release folder.  You can either reference the DLL, or include the two source files directly.

So to use it from a script, it would be this:

    #r "HttpClient.dll"

    open HttpClient  

    printfn "%s" (createRequest Get "http://www.google.com" |> getResponseBody)

or this:

    #load "AsyncStreamReader.fs"
    #load "HttpClient.fs"

    open HttpClient

    printfn "%s" (createRequest Get "http://www.google.com" |> getResponseBody)

## Where can I find out more? ##

There's really not that much to it, but if you want to know the details your best bet is the integration and unit tests.  The test names describe the functionality pretty well.

There's also a Sample Application which demonstrates the library being used and unit tested.

## Why on earth would you make such a thing? ##

This came out of a side project which involved working with HTTP, and I wasn't really enjoying using HttpWebRequest from F#, so I started making wrapper functions - which eventually turned into this.

The sort of things I wanted my module to do differently from HttpWebRequest include:
* usable idiomatically from F#, e.g. immutable types
* consistent handling of headers (including all the standard ones)
* easier to use, e.g. no streams
* sensible defaults
* built-in async

It isn't intended as a high-performance library, usability from F# has been the goal.  It shouldn't be much worse than HttpWebRequest, but you'd have to test it if that was important.

I've since discovered HttpClient, which looks better than HttpWebRequest, but still doesn't work quite how I'd like.

## What other kick-ass open source libraries are involved? ##

The only thing that's used in the HttpClient module itself is AsyncStreamReader.fs, a source file taken directly from the Fsharpx library.

However, for testing a couple of other things are used:
  * FsUnit for unit testing
  * NancyFX to create a web server for integration testing

That's about it.
Happy requesting!

Grant
@relentlessdev
