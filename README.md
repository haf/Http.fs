Http.fs
=======

An HTTP client library for F#, which wraps HttpWebRequest/Response in a glorious functional jacket!

Overview
--------

This is an F# module which provides functions for making and sending HTTP requests and handling the responses.  Although it does work for simple cases, there are still a few things to add before it's generally usable.

Reviewers
---------

If you've responded to my request for a review, thanks very much for your time, the F# community salutes you!  I've not been doing F# or FP long, hence the request.

I'm really just looking for you, wise in the ways of functional programming, to check out the code in HttpClient and see if it: makes sense, works in a reasonable manner, and uses appropriate idiomatic F#.  I've also put a couple of specific points I've been wondering about below.  If you also want to check out the unit and integration tests, by all means go ahead - there's not much to see in SampleApplication though.

## Background ##

This came out of a side project which involved working with HTTP, and I wasn't really enjoying using HttpWebRequest from F#, so I started making wrapper functions - which eventually turned into this.

The sort of things I wanted my module to do differently from HttpWebRequest include:
* usable from idiomatic F#, e.g. immutable types
* consistent handling of headers (including all the standard ones)
* easier to use, e.g. not having to mess about with streams
* sensible defaults (cookies enabled, no exception on a non-200-level response (?!))
* non-blocking IO

I've since discovered HttpClient, which looks better than HttpWebRequest, but still doesn't work quite how I'd like.  I've tried to keep the underlying technologies hidden anyway.

## How to use it ##

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

    request |> getResponseCode  
    request |> getResponseBody  
    request |> getResponse  

If you get the full response (another immutable record), you can get things from it like so:

    response.StatusCode  
    response.EntityBody.Value  
    response.Cookies.["cookie1"]  
    response.Headers.[ContentEncoding]  
    response.Headers.[NonStandard("X-New-Fangled-Header")]  

## Questions ##

As I said I'm happy for any comments, but a few specific things I've been wondering include:  
1. Is this OK as a module, or would it be better as a type (which, with an interface, would make it easier to mock for unit testing)  
2. Are the types in the API good choices (e.g. the NameValue used for cookies)?  
3. Is it OK to expose things as maps in the response (which means you get a less-than-ideal exception if something doesn't exist)?  
4. Have I actually made all of the IO non-blocking as I intended to?  

## Feedback ##

If you have any pointers for me, just send them over via email to grantac (at) hotmail.com, and I will be eternally grateful!

Grant
