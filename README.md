Http.fs
=======

An HTTP client library for F#, which wraps HttpWebRequest/Response in a glorious functional jacket!

Overview
--------

This is an F# module which provides functions for making and sending HTTP requests and handling the responses.

## How to use it ##

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
  
The Http response (or a specific part thereof) is retrieved using one of the following:

    request |> getResponse  
    request |> getResponseCode  
    request |> getResponseBody  
    
Or, if you like to do things asynchronously:

    request |> getResponseAsync  
    request |> getResponseCodeAsync  
    request |> getResponseBodyAsync  

If you get the full response (another immutable record), you can get things from it like so:

    response.StatusCode  
    response.EntityBody.Value  
    response.ContentLength  
    response.Cookies.["cookie1"]  
    response.Headers.[ContentEncoding]  
    response.Headers.[NonStandard("X-New-Fangled-Header")] 

## Background ##

This came out of a side project which involved working with HTTP, and I wasn't really enjoying using HttpWebRequest from F#, so I started making wrapper functions - which eventually turned into this.

The sort of things I wanted my module to do differently from HttpWebRequest include:
* usable idiomatically from F#, e.g. immutable types
* consistent handling of headers (including all the standard ones)
* easier to use, e.g. no streams
* sensible defaults
* built-in async

It isn't intended as a high-performance library, usability from F# has been the goal.  It shouldn't be much worse than HttpWebRequest, but you'd have to test it if that was important.

I've since discovered HttpClient, which looks better than HttpWebRequest, but still doesn't work quite how I'd like.

Grant
