Http.fs
=======

An HTTP client library for F#, which wraps [HttpWebRequest](http://msdn.microsoft.com/en-us/library/system.net.httpwebrequest.aspx)/[Response](http://msdn.microsoft.com/en-us/library/system.net.httpwebresponse.aspx) in a glorious functional jacket!

## How do I use it? ##

In it's simplest form, this will get you a web page:

      createRequest Get "http://somesite.com" |> getResponseBody  

To get into the details a bit more, there are two or three steps to getting what you want from a web page/HTTP response.

1 - A Request (an immutable record type) is built up in a [Fluent Builder](http://stefanoricciardi.com/2010/04/14/a-fluent-builder-in-c/) stylee as follows:

    let request =  
      createRequest Post "http://somesite.com"  
      |> withQueryStringItem {name="search"; value="jeebus"}  
      |> withHeader (UserAgent "Chrome or summat")  
      |> withHeader (Custom {name="X-My-Header"; value="hi mum"})  
      |> withAutoDecompression DecompressionScheme.GZip  
      |> withCookie {name="session"; value="123"}  
      |> withBody "Check out my sexy body"  
  
2 - The Http response (or just the response code/body) is retrieved using one of the following:

    request |> getResponse  
    request |> getResponseCode  
    request |> getResponseBody  

3 - If you get the full response (another record), you can get things from it like so:

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

*Note* because some of the request and response headers have the same names, you have to qualify some of the response headers with 'Resp', e.g.

    response.Headers.[Resp.ContentType]
        
If you get cryptic errors like 'This is a function and cannot be applied' when you try to set a header, it's probably this.  I'll try to think of a better way to do it soon, I promise...

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

There's really not that much to it, but if you want to know the details your best bet is the integration and unit tests.

Unit tests describe making the request:
  * createRequest makes a Request with a Method and URL, and sensible defaults
  * requests have cookies enabled by default
  * withAutoDecompression enables the specified decompression methods
  * withCookiesDisabled disables cookies
  * withHeader adds header to the request
  * withHeader Custom adds a custom header to the request
  * multiple headers of different types can be added, including custom headers with different names
  * If the same header is added multiple times, throws an exception
  * If a custom header with the same name is added multiple times, an exception is thrown
  * withBody sets the request body
  * withQueryString adds the query string item to the list
  * withCookie throws an exception if cookies are disabled
  * withCookie adds the cookie to the request
  * withAutoFollowRedirectsDisabled turns auto-follow off

Integration tests describe submitting the request and handling the response:
  * _connection keep-alive header is set automatically on the first request, but not subsequent ones
  * createRequest should set everything correctly in the HTTP request
  * getResponseCode should return the http status code for all response types
  * getResponseBody should return the entity body as a string
  * getResponseBody should return an empty string when there is no body
  * all details of the response should be available after a call to getResponse
  * getResponse should have nothing if the things don't exist
  * getResponse, given a request with an invalid url, throws an exception
  * getResponseCode, when called on a non-existant page, returns 404
  * posts to Nancy without a body don't work
  * all of the manually-set request headers should get sent to the server
  * Content-Length header is set automatically for Posts with a body
  * accept-encoding header is set automatically when decompression scheme is set
  * all of the response headers should be available after a call to getResponse

There's also a SampleApplication folder with a program which demonstrates the library being used and unit tested.

## Why on earth would you make such a thing? ##

This came out of a side project which involved working with HTTP, and I wasn't really enjoying using HttpWebRequest from F#, so I started making wrapper functions - which eventually turned into this.

The sort of things I wanted my module to do differently from HttpWebRequest include:
* usable idiomatically from F#, e.g. immutable types
* consistent handling of headers (including all the standard ones)
* easier to use, e.g. no streams
* sensible defaults
* built-in async

It isn't intended as a high-performance library, usability from F# has been the goal.  It shouldn't be much worse than HttpWebRequest, but you'd have to test it if that was important.

I've since discovered [HttpClient](http://msdn.microsoft.com/en-us/library/system.net.http.httpclient.aspx), which looks better than HttpWebRequest, but still doesn't work quite how I'd like.

## What other kick-ass open source libraries are involved? ##

The only thing that's used in the HttpClient module itself is AsyncStreamReader.fs, a source file taken directly from the [Fsharpx](https://github.com/fsharp/fsharpx) library.

However, for testing a couple of other things are used:
  * [FsUnit](https://github.com/fsharp/FsUnit) for unit testing
  * [NancyFX](http://nancyfx.org/) to create a web server for integration testing

That's about it.
Happy requesting!

Grant
@relentlessdev
