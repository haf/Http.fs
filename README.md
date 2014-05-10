Http.fs
=======

A gloriously functional HTTP client library for F#!

## How do I use it? ##

In it's simplest form, this will get you a web page:

      createRequest Get "http://somesite.com" |> getResponseBody  

To get into the details a bit more, there are two or three steps to getting what you want from a web page/HTTP response.

1 - A Request (an immutable record type) is built up in a [Fluent Builder](http://stefanoricciardi.com/2010/04/14/a-fluent-builder-in-c/) stylee as follows:

    let request =  
      createRequest Post "http://somesite.com"  
      |> withQueryStringItem {name="search"; value="jeebus"}  
      |> withBasicAuthentication "myUsername" "myPassword"
      |> withHeader (UserAgent "Chrome or summat")  
      |> withHeader (Custom {name="X-My-Header"; value="hi mum"})  
      |> withAutoDecompression DecompressionScheme.GZip  
      |> withAutoFollowRedirectsDisabled  
      |> withCookie {name="session"; value="123"}  
      |> withBody "Check out my sexy body"  
      |> withBodyEncoded "Check out my sexy foreign body" "ISO-8859-5"
      |> withResponseCharacterEncoding "utf-8"
      |> withProxy { 
            Address = "proxy.com"; 
            Port = 8080; 
            Credentials = ProxyCredentials.Custom { username = "Tim"; password = "Password1" } }
  
(with everything after createRequest being optional)
  
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

*Note* because some of the request and response headers have the same names, to prevent name clashes, the response versions have 'Response' stuck on the end, e.g.

    response.Headers.[ContentTypeResponse]
        
## Cool!  So how do I get it in my code? ##

The easiest way, if you have a full-on project, is to us [the NuGet package](https://www.nuget.org/packages/Http.fs/):

    PM> install-package Http.fs
    
Then just open the module and use as required:

    open HttpClient  

    printfn "%s" (createRequest Get "http://www.google.com" |> getResponseBody)

If you can't use NuGet (perhaps you're writing a script), or want to use the source files, everything you need's in the Release folder.  You can either reference the DLL, or include the two source files directly.

So to use it from a script, it would be this:

    #r "HttpClient.dll"

    open HttpClient  

    printfn "%s" (createRequest Get "http://www.google.com" |> getResponseBody)

or this:

    #load "AsyncStreamReader.fs"
    #load "HttpClient.fs"

    open HttpClient

    printfn "%s" (createRequest Get "http://www.google.com" |> getResponseBody)

## Version History ##

Http.fs attempts to follow [Semantic Versioning](http://semver.org/), which defines what the different parts of the version number mean and how they relate to backwards compatability of the API.  In a nutshell, as long as the major version doesn't change, everything should still work.

* 0.X.X - Various.  Thanks for code and suggestions from [Sergeeeek](https://github.com/Sergeeeek), [rodrigodival](https://github.com/rodrigovidal), [ovatsus](https://github.com/ovatsus) and more
* 1.0.0 - First stable API release.  Changed how 'duplicated' DUs were named between request/response.
* 1.1.0 - Added withProxy, thanks to [vasily-kirichenko](https://github.com/vasily-kirichenko)
* 1.1.1 - Handles response encoding secified as 'utf8' (.net encoder only likes 'utf-8')

## FAQ ##

  * How does it work?

Http.fs currently uses [HttpWebRequest](http://msdn.microsoft.com/en-us/library/system.net.httpwebrequest.aspx)/[Response](http://msdn.microsoft.com/en-us/library/system.net.httpwebresponse.aspx) under the hood.

  * Why are my cookies not getting set?

Perhaps the response is a redirect (a 302 or similar) - unfortunately, although HttpWebRequest handles redirects automatically by default, it doesn't maintain the cookies set during the redirect. (See [this CodeProject article about it](http://www.codeproject.com/Articles/49243/Handling-Cookies-with-Redirects-and-HttpWebRequest)).

The solution is to set 'withAutoFollowRedirectsDisabled' on your request - although this does mean you'll have to handle the redirection yourself.

## I need details! ##

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
  * withBody uses default character encoding of ISO-8859-1
  * withBodyEncoded sets the request body
  * withBodyEncoded sets the body encoding
  * withQueryString adds the query string item to the list
  * withCookie throws an exception if cookies are disabled
  * withCookie adds the cookie to the request
  * withAutoFollowRedirectsDisabled turns auto-follow off
  * withBasicAuthentication sets the Authorization header with the username and password base-64 encoded
  * withBasicAuthentication encodes the username and password with ISO-8859-1 before converting to base-64
  * withResponseCharacterEncoding sets the response character encoding
  * withProxy sets proxy address and port
  * withProxy can set proxy with custom credentials
  * withProxy can set proxy with default credentials
  * withProxy can set proxy with no credentials

Integration tests describe submitting the request and handling the response:
  * connection keep-alive header is set automatically on the first request, but not subsequent ones
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
  * if body character encoding is specified, encodes the request body with it
  * the body is read using the character encoding specified in the content-type header
  * if a response character encoding is specified, that encoding is used regardless of what the response's content-type specifies
  * if an invalid response character encoding is specified, an exception is thrown
  * if a response character encoding is NOT specified, the body is read using the character encoding specified in the response's content-type header
  * if a response character encoding is NOT specified, and character encoding is NOT specified in the response's content-type header, the body is read using ISO Latin 1 character encoding
  * if a response character encoding is NOT specified, and the character encoding specified in the response's content-type header is invalid, an exception is thrown
  * if the response character encoding is specified as 'utf8', uses 'utf-8' instead
  * cookies are not kept during an automatic redirect

You can also check out the *SampleApplication* folder, which contains a program demonstrating the library being used and unit tested.

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

If you want to read a bit more about why using HttpWebRequest sucks, check out [my blog entry introducing Http.fs](http://www.relentlessdevelopment.net/2013/11/15/web-requests-in-f-now-easy-introducing-http-fs/).

## What other kick-ass open source libraries are involved? ##

The only thing that's used in the HttpClient module itself is AsyncStreamReader.fs, a source file taken directly from the [Fsharpx](https://github.com/fsharp/fsharpx) library.

However, for testing a couple of other things are used:
  * [FsUnit](https://github.com/fsharp/FsUnit) for unit testing
  * [NancyFX](http://nancyfx.org/) to create a web server for integration testing

And for building, there's also:
  * [FAKE](http://fsharp.github.io/FAKE/), the F# MAKE tool

That's about it.
Happy requesting!

Grant Crofton  
@relentlessdev
