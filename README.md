![Http.fs logo](https://raw.githubusercontent.com/relentless/Http.fs/master/docs/files/img/logo_small.png) Http.fs
=======

A gloriously functional HTTP client library for F#!

**NOTE:** These instructions are for the forthcoming version 2.0.  For the current version on NuGet (1.5.1), see:  
[Readme for Version 1.5.1](https://github.com/relentless/Http.fs/blob/d456af90164586ebd41a1c0601548c8dbf19c9e7/README.md)

.Net build (AppVeyor): [![AppVeyor Build status](https://ci.appveyor.com/api/projects/status/vcqrxl5d03xxyoa3/branch/master)](https://ci.appveyor.com/project/GrantCrofton/http-fs/branch/master)
Mono build (Travis CI): [![Travis Build status](https://travis-ci.org/relentless/Http.fs.svg?branch=master)](https://travis-ci.org/relentless/Http.fs)
NuGet package: [![NuGet](http://img.shields.io/badge/NuGet-1.5.1-blue.svg?style=flat)](http://www.nuget.org/packages/Http.fs/)

## How do I use it? ##

In it's simplest form, this will get you a web page:

``` fsharp
createRequest Get "http://somesite.com" |> getResponseBody
```

To get into the details a bit more, there are two or three steps to getting what
you want from a web page/HTTP response.

1 - A Request (an immutable record type) is built up in a [Fluent
Builder](http://stefanoricciardi.com/2010/04/14/a-fluent-builder-in-c/) style
as follows:

``` fsharp
open HttpFs.Client
open System
open System.Text

let request =
    createRequest Post <| Uri("https://example.com")
    |> withQueryStringItem "search" "jeebus"
    |> withBasicAuthentication "myUsername" "myPassword" // UTF8-encoded
    |> withHeader (UserAgent "Chrome or summat")
    |> withHeader (Custom ("X-My-Header", "hi mum"))
    |> withAutoDecompression DecompressionScheme.GZip 
    |> withAutoFollowRedirectsDisabled
    |> withCookie (Cookie.Create("session", "123", path="/"))
    |> withBodyString "This body will make heads turn"
    |> withBodyStringEncoded "Check out my sexy foreign body" (Encoding.UTF8)
    |> withBody (BodyRaw [| 1uy; 2uy; 3uy |])
    |> withBody (BodyString "this is a greeting from Santa")

    // if you submit a BodyForm, then Http.fs will also set the correct Content-Type, so you don't have to
    |> withBody (BodyForm [
        // if you only have this in your form, it will be submitted as application/x-www-form-urlencoded
        NameValue ("submit", "Hit Me!")

        // a single file form control, selecting two files from browser
        FormFile ("file", ("file1.txt", ContentType.Create("text", "plain"), Plain "Hello World"))
        FormFile ("file", ("file2.txt", ContentType.Create("text", "plain"), Binary [|1uy; 2uy; 3uy|]))

        // you can also use MultipartMixed for servers supporting it (this is not the browser-default)
        MultipartMixed ("files",
          [ "file1.txt", firstCt, Plain "Hello World" // => plain
            "file2.gif", secondCt, Plain "Loopy" // => plain
            "file3.gif", thirdCt, Plain "Thus" // => base64
            "cute-cat.gif", fourthCt, Binary (File.ReadAllBytes (pathOf "cat-stare.gif")) // => binary
          ])
    ])
    |> withResponseCharacterEncoding (Encoding.UTF8)
    |> withKeepAlive false
    |> withProxy {
          Address = "proxy.com";
          Port = 8080;
          Credentials = ProxyCredentials.Custom { username = "Tim"; password = "Password1" } }
```

(with everything after createRequest being optional)
  
2 - The Http response (or just the response code/body) is retrieved using one of the following:

``` fsharp
async {
  use! response = getResponse request // disposed at the end of async, don't
                                      // fetch outside async body
  // the above doesn't download the response, so you'll have to do that:
  let! bodyStr = Response.readBodyAsString response
  // OR:
  //let! bodyBs = Response.readBodyAsBytes

  // remember HttpFs doesn't buffer the stream (how would we know if we're
  // downloading 3GiB?), so once you use one of the above methods, you can't do it
  // again, but have to buffer/stash it yourself somewhere.
  return bodyStr
}
```

3 - If you get the full response (another record), you can get things from it like so:

``` fsharp
response.StatusCode
response.Body // but prefer the above helper functions
response.ContentLength
response.Cookies.["cookie1"]
response.Headers.[ContentEncoding]
response.Headers.[NonStandard("X-New-Fangled-Header")]
```

So you can do the old download-multiple-sites-in-parallel thing:

``` fsharp
[ "http://news.bbc.co.uk"
  "http://www.wikipedia.com"
  "http://www.stackoverflow.com"]
|> List.map (fun u -> Uri u)
|> List.map (createRequest Get)
|> List.map (Request.responseAsString) // this takes care to dispose (req, body)
|> Async.Parallel
|> Async.RunSynchronously
|> Array.iter (printfn "%s")
```

If you need direct access to the response stream for some reason (for example to download a large file), you need to write yourself a function and pass it to getResponseStream like so:

``` fsharp
open System.IO

async {
  use! resp = createRequest Get "http://fsharp.org/img/logo.png"
  use fileStream = new FileStream("c:\\bigImage.png", FileMode.Create)
  do! resp.Body.CopyToAsync fileStream
}
```

*Note* because some of the request and response headers have the same names, to prevent name clashes, the response versions have 'Response' stuck on the end, e.g.

``` fsharp
response.Headers.[ContentTypeResponse]
```

## Examples ##

Check out *HttpClient.SampleApplication*, which contains a program demonstrating
the various functions of the library being used and (to a limited extent) unit
tested.

[SamplePostApplication](https://github.com/relentless/Http.fs/blob/master/HttpClient.SamplePostApplication/README.md) shows how you can create a post with a body containing forms.

## Cool!  So how do I get it in my code? ##

The easiest way, if you have a full-on project, is to us [the NuGet package](https://www.nuget.org/packages/Http.fs/):

``` shell
PM> install-package Http.fs
```
    
Then just open the module and use as required:

``` fsharp
open HttpClient  

printfn "%s" (createRequest Get "http://www.google.com" |> getResponseBody)
```

If you can't use NuGet (perhaps you're writing a script), check out the [Releases](https://github.com/relentless/Http.fs/releases), where you should be able to find the latest version.

To use it from a script, it would be this:

``` fsharp
#r "HttpClient.dll"

open HttpClient  

printfn "%s" (createRequest Get "http://www.google.com" |> getResponseBody)
```

## Version History ##

Http.fs attempts to follow [Semantic Versioning](http://semver.org/), which defines what the different parts of the version number mean and how they relate to backwards compatability of the API.  In a nutshell, as long as the major version doesn't change, everything should still work.

  * 0.X.X - Various.  Thanks for code and suggestions from
    [Sergeeeek](https://github.com/Sergeeeek),
  [rodrigodival](https://github.com/rodrigovidal),
  [ovatsus](https://github.com/ovatsus) and more
  * 1.0.0 - First stable API release.  Changed how 'duplicated' DUs were named
    between request/response.
  * 1.1.0 - Added withProxy, thanks to
    [vasily-kirichenko](https://github.com/vasily-kirichenko)
  * 1.1.1 - Handles response encoding secified as 'utf8' (.net encoder only likes
    'utf-8')
  * 1.1.2 - Added utf16 to response encoding map
  * 1.1.3 - Added XML comments to public functions, made a couple of things
    private which should always have been (technically a breaking change, but I
  doubt anybody was using them)
  * 1.2.0 - Added withKeepAlive
  * 1.3.0 - Added getResponseBytes, thanks to
    [Sergeeeek](https://github.com/Sergeeeek)
  * 1.3.1 - Added project logo, thanks to
    [sergey-tihon](https://github.com/sergey-tihon)
  * 1.4.0 - Added getResponseStream, with thanks to
    [xkrt](https://github.com/xkrt)
  * 1.5.0 - Added support for Patch method with help from
    [haf](https://github.com/haf), and [xkrt](https://github.com/xkrt) fixed an
    issue with an empty response.CharacterSet
  * 1.5.1 - Corrected the assembly version
  * 2.0.0 - Production hardened, major release, major improvements

## FAQ ##

  * How does it work?

Http.fs currently uses
[HttpWebRequest](http://msdn.microsoft.com/en-us/library/system.net.httpwebrequest.aspx)/[Response](http://msdn.microsoft.com/en-us/library/system.net.httpwebresponse.aspx)
under the hood.

  * Why are my cookies not getting set?

Perhaps the response is a redirect (a 302 or similar) - unfortunately, although
HttpWebRequest handles redirects automatically by default, it doesn't maintain
the cookies set during the redirect. (See [this CodeProject article about
it](http://www.codeproject.com/Articles/49243/Handling-Cookies-with-Redirects-and-HttpWebRequest)).

The solution is to set 'withAutoFollowRedirectsDisabled' on your request -
although this does mean you'll have to handle the redirection yourself.

  * Does it support proxies?

Yes. By default it uses the proxy settings defined in IE, and as of 1.1.0 you
can specify basic proxy settings separately using withProxy.

  * Can I set KeepAlive?

Yes, as of version 1.2.0.  This actually sets the Connection header (to
'Keep-Alive' or 'Close').  Note that if this is set to true (which is the
default), the Connection header will only be set on the first request, not
subsequent ones.

## Why on earth would you make such a thing? ##

This came out of a side project which involved working with HTTP, and I wasn't
really enjoying using HttpWebRequest from F#, so I started making wrapper
functions - which eventually turned into this.

The sort of things I wanted my module to do differently from HttpWebRequest
include:

  * usable idiomatically from F#, e.g. immutable types
  * consistent handling of headers (including all the standard ones)
  * easier to use, e.g. no streams
  * sensible defaults
  * built-in async

It isn't intended as a high-performance library, usability from F# has been the
goal. It shouldn't be much worse than HttpWebRequest, but you'd have to test it
if that was important.

If you want to read a bit more about why using HttpWebRequest sucks, check out
[my blog entry introducing
Http.fs](http://www.relentlessdevelopment.net/2013/11/15/web-requests-in-f-now-easy-introducing-http-fs/).

## What other kick-ass open source libraries are involved? ##

The only thing that's used in the HttpClient module itself is
AsyncStreamReader.fs, a source file taken directly from the
[Fsharpx](https://github.com/fsharp/fsharpx) library.

However, for testing a couple of other things are used:

  * [FsUnit](https://github.com/fsharp/FsUnit) for unit testing
  * [NancyFX](http://nancyfx.org/) to create a web server for integration testing
  * [Suave](http://suave.io) to create a web server for integration testing

And for building, there's also:

  * [FAKE](http://fsharp.github.io/FAKE/), the F# MAKE tool
  * [Albacore](https://github.com/albacore/albacore)

That's about it.
Happy requesting!

Grant Crofton
@relentlessdev
