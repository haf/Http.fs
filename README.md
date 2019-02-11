![Http.fs logo](https://raw.githubusercontent.com/relentless/Http.fs/master/docs/files/img/logo_small.png) Http.fs
=======

A gloriously functional HTTP client library for F#! NuGet name:
`Http.fs`.

.Net build (AppVeyor): [![AppVeyor Build status](https://ci.appveyor.com/api/projects/status/s3xy4lk0novh549h?svg=true)](https://ci.appveyor.com/project/haf/http-fs)
Mono build (Travis CI): [![Travis Build Status](https://travis-ci.org/haf/Http.fs.svg?branch=master)](https://travis-ci.org/haf/Http.fs)
NuGet package: [![NuGet](http://img.shields.io/nuget/v/Http.fs.svg?style=flat)](http://www.nuget.org/packages/Http.fs/)

## How do I use it? ##

In it's simplest form, this will get you a web page:

``` fsharp
open Hopac
open HttpFs.Client

let body =
  Request.createUrl Get "http://somesite.com"
  |> Request.responseAsString
  |> run

printfn "Here's the body: %s" body
```

To get into the details a bit more, there are two or three steps to getting what
you want from a web page/HTTP response.

1 - A Request (an immutable record type) is built up in a [Fluent
Builder](http://stefanoricciardi.com/2010/04/14/a-fluent-builder-in-c/) style
as follows:

``` fsharp
open System.IO
open System.Text
open Hopac
open HttpFs.Client

let pathOf relativePath =
  let here = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
  Path.Combine(here, relativePath)

let firstCt, secondCt, thirdCt, fourthCt =
    ContentType.parse "text/plain" |> Option.get,
    ContentType.parse "text/plain" |> Option.get,
    ContentType.create("application", "octet-stream"),
    ContentType.create("image", "gif")

let httpClientWithNoRedirects () =
    let handler = new HttpClientHandler(UseCookies = false)
    handler.AllowAutoRedirect <- false
    let client = new HttpClient(handler)
    client.DefaultRequestHeaders.Clear()
    client

// we can trivially extend request to add convenience functions for common operations
module Request =
    let autoFollowRedirectsDisabled h = 
        { h with httpClient = httpClientWithNoRedirects () }

let request =
    Request.createUrl Post "https://example.com"
    |> Request.queryStringItem "search" "jeebus"
    |> Request.basicAuthentication "myUsername" "myPassword" // UTF8-encoded
    |> Request.setHeader (UserAgent "Chrome or summat")
    |> Request.setHeader (Custom ("X-My-Header", "hi mum"))
    |> Request.autoFollowRedirectsDisabled
    |> Request.cookie (Cookie.create("session", "123", path="/"))
    |> Request.bodyString "This body will make heads turn"
    |> Request.bodyStringEncoded "Check out my sexy foreign body" (Encoding.UTF8)
    |> Request.body (BodyRaw [| 1uy; 2uy; 3uy |])
    |> Request.body (BodyString "this is a greeting from Santa")

    // if you submit a BodyForm, then Http.fs will also set the correct Content-Type, so you don't have to
    |> Request.body (BodyForm 
        [
            // if you only have this in your form, it will be submitted as application/x-www-form-urlencoded
            NameValue ("submit", "Hit Me!")

            // a single file form control, selecting two files from browser
            FormFile ("file", ("file1.txt", ContentType.create("text", "plain"), Plain "Hello World"))
            FormFile ("file", ("file2.txt", ContentType.create("text", "plain"), Binary [|1uy; 2uy; 3uy|]))

            // you can also use MultipartMixed for servers supporting it (this is not the browser-default)
            MultipartMixed ("files",
              [ "file1.txt", firstCt, Plain "Hello World" // => plain
                "file2.gif", secondCt, Plain "Loopy" // => plain
                "file3.gif", thirdCt, Plain "Thus" // => base64
                "cute-cat.gif", fourthCt, Binary (File.ReadAllBytes (pathOf "cat-stare.gif")) // => binary
          ])
    ])
    |> Request.responseCharacterEncoding Encoding.UTF8    
    |> Request.proxy {
          Address = "proxy.com";
          Port = 8080;
          Credentials = Credentials.Custom { username = "Tim"; password = "Password1" } }
```

(with everything after createRequest being optional)

2 - The Http response (or just the response code/body) is retrieved using one of the following:

``` fsharp
job {
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
|> List.map (createRequestSimple Get)
| > List.map (Request.responseAsString) // this takes care to dispose (req, body)
|> Job.conCollect
|> Job.map (printfn "%s")
|> start
```

If you need direct access to the response stream for some reason (for example to download a large file), you need to write yourself a function and pass it to getResponseStream like so:

``` fsharp
open Hopac
open System.IO
open HttpFs.Client

job {
  use! resp = Request.createUrl Get "http://fsharp.org/img/logo.png" |> getResponse
  use fileStream = new FileStream("c:\\bigImage.png", FileMode.Create)
  do! resp.Body.CopyToAsync fileStream
}
```

*Note* because some of the request and response headers have the same names, to prevent name clashes, the response versions have 'Response' stuck on the end, e.g.

``` fsharp
response.Headers.[ContentTypeResponse]
```

## Building

 1. Download the source code
 2. Execute the build.sh (linux & macos) or build.cmd (windows)
 
## Examples ##

Check out *HttpClient.SampleApplication*, which contains a program demonstrating
the various functions of the library being used and (to a limited extent) unit
tested.

[SamplePostApplication](https://github.com/relentless/Http.fs/blob/master/HttpClient.SamplePostApplication/README.md)
shows how you can create a post with a body containing forms.

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
  * 3.0.3 - Async -> Job, withXX -> Request.withXX

## FAQ ##

  * How does it work?

Http.fs currently uses HttpClient under the hood.

  * Does it support proxies?

Yes. By default it uses the proxy settings defined in IE, and as of 1.1.0 you
can specify basic proxy settings separately using withProxy.

  * Can I set KeepAlive?

Yes, as of version 1.2.0.  This actually sets the Connection header (to
'Keep-Alive' or 'Close').  Note that if this is set to true (which is the
default), the Connection header will only be set on the first request, not
subsequent ones.

## Why?

Simplicity for F# programmers. An abstract, immutable API that you can build better abstractions beneath (if needed).


## What other kick-ass open source libraries are involved? ##

The only thing that's used in the HttpClient module itself is
AsyncStreamReader.fs, a source file taken directly from the
[Fsharpx](https://github.com/fsharp/fsharpx) library.

However, for testing a couple of other things are used:

  * [Suave](https://suave.io) to create a web server for integration testing
  * [FsUnit](https://github.com/fsharp/FsUnit) for unit testing
  * [NancyFX](http://nancyfx.org/) to create a web server for integration testing

That's about it.
Happy requesting!

Henrik Feldt – @haf

Originally built by Grant Crofton.

## Post Scriptum

 - https://www.youtube.com/watch?v=_1rh_s1WmRA
