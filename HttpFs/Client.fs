module HttpFs.Client

open System
open System.IO
open System.Net
open System.Text
open System.Web
open System.Security.Cryptography
open Microsoft.FSharp.Control
open Microsoft.FSharp.Control.CommonExtensions
open Microsoft.FSharp.Control.WebExtensions

open HttpFs.Prelude

[<Measure>] type ms

let DefaultBodyEncoding = Encoding.UTF8

type HttpMethod =
  | Options
  | Get
  | Head
  | Post
  | Put
  | Delete
  | Trace
  | Patch
  | Connect

// Same as System.Net.DecompressionMethods, but I didn't want to expose that
type DecompressionScheme = 
  | None = 0
  | GZip = 1
  | Deflate = 2

type ContentRange =
  { start  : int64
    finish : int64 }

[<CustomComparison; CustomEquality>]
type ContentType =
  { typ      : string
    subtype  : string
    charset  : Encoding option
    boundary : string option }
with
  override x.ToString() =
    String.Concat [
      yield x.typ
      yield "/"
      yield x.subtype
      match x.charset with
      | None -> ()
      | Some enc -> yield! [ ";"; " charset="; enc.WebName ]
      match x.boundary with
      | None -> ()
      | Some b -> yield! [ ";"; " boundary="; b ]
    ]

  interface IComparable with
    member x.CompareTo other =
      match other with
      | :? ContentType as ct -> (x :> IComparable<ContentType>).CompareTo ct
      | x -> failwith "invalid comparison ContentType to %s" (x.GetType().Name)

  interface IComparable<ContentType> with
    member x.CompareTo other =
      match compare x.typ other.typ with
      | 0 -> compare x.subtype x.subtype
      | x -> x

  member x.Equals(typ : string, subtype : string) =
    x.typ = typ && x.subtype = subtype

  override x.Equals o =
    match o with
    | :? ContentType as ct -> (x :> IEquatable<ContentType>).Equals ct
    | _ -> false

  interface IEquatable<ContentType> with
    member x.Equals ct =
      x.typ = ct.typ
      && x.subtype = ct.subtype
      && (x.charset |> Option.orDefault DefaultBodyEncoding) = (ct.charset |> Option.orDefault DefaultBodyEncoding)

  override x.GetHashCode () =
    397 * hash x.typ
    ^^^ hash x.subtype

  static member Create(typ : string, subtype : string, ?charset : Encoding, ?boundary : string) =
    { typ      = typ
      subtype  = subtype
      charset  = charset
      boundary = boundary }

  // TODO, use: https://github.com/freya-fs/freya/blob/master/src/Freya.Types.Http/Types.fs#L420-L426
  static member Parse (str : string) =
    match str.Split [| '/' |], str.IndexOf(';') with
    | [| typ; subtype |], -1 ->
      Some { typ = typ; subtype = subtype; charset = None; boundary = None }
    | [| typ; rest |], index ->
      Some { typ = typ; subtype = rest.Substring(0, index); charset = None; boundary = None }
    | x -> None

type ResponseHeader =
  | AccessControlAllowOrigin
  | AcceptRanges
  | Age
  | Allow
  | CacheControl
  | Connection
  | ContentEncoding
  | ContentLanguage
  | ContentLength
  | ContentLocation
  | ContentMD5Response
  | ContentDisposition
  | ContentRange
  | ContentTypeResponse
  | DateResponse
  | ETag
  | Expires
  | LastModified
  | Link
  | Location
  | P3P
  | PragmaResponse
  | ProxyAuthenticate
  | Refresh
  | RetryAfter
  | Server
  | StrictTransportSecurity
  | Trailer
  | TransferEncoding
  | Vary
  | ViaResponse
  | WarningResponse
  | WWWAuthenticate
  | NonStandard of string
with
  override x.ToString() =
    match x with
    | AccessControlAllowOrigin -> "Access-Control-Allow-Origin"
    | AcceptRanges -> "Accept-Ranges"
    | Age -> "Age"
    | Allow -> "Allow"
    | CacheControl -> "Cache-Control"
    | Connection -> "Connection"
    | ContentEncoding -> "Content-Encoding"
    | ContentLanguage -> "Content-Language"
    | ContentLength -> "Content-Length"
    | ContentLocation -> "Content-Location"
    | ContentMD5Response -> "Content-MD5-Response"
    | ContentDisposition -> "Content-Disposition"
    | ContentRange -> "Content-Range"
    | ContentTypeResponse -> "Content-Type-Response"
    | DateResponse -> "Date-Response"
    | ETag -> "ETag"
    | Expires -> "Expires"
    | LastModified -> "Last-Modified"
    | Link -> "Link"
    | Location -> "Location"
    | P3P -> "P3P"
    | PragmaResponse -> "Pragma-Response"
    | ProxyAuthenticate -> "Proxy-Authenticate"
    | Refresh -> "Refresh"
    | RetryAfter -> "Retry-After"
    | Server -> "Server"
    | StrictTransportSecurity -> "Strict-Transport-Security"
    | Trailer -> "Trailer"
    | TransferEncoding -> "Transfer-Encoding"
    | Vary -> "Vary"
    | ViaResponse -> "Via-Response"
    | WarningResponse -> "Warning-Response"
    | WWWAuthenticate -> "WWW-Authenticate"
    | NonStandard str -> str

// some headers can't be set with HttpWebRequest, or are set automatically, so are not included.
// others, such as transfer-encoding, just haven't been implemented.
type RequestHeader =
  | Accept of string
  | AcceptCharset of string
  | AcceptDatetime of string
  | AcceptLanguage of string
  | Authorization of string
  | Connection of string
  | ContentMD5 of string
  | ContentType of ContentType
  | Date of DateTime
  | Expect of int
  | From of string
  | IfMatch of string
  | IfModifiedSince of DateTime
  | IfNoneMatch of string
  | IfRange of string
  | MaxForwards of int
  | Origin of string
  | Pragma of string
  | ProxyAuthorization of string
  | Range of ContentRange
  | Referer of string
  | Upgrade of string
  | UserAgent of string
  | Via of string
  | Warning of string
  | Custom of string * string

  member x.KeyValue : string * string =
    match x with
    | Accept x -> "Accept", x
    | AcceptCharset x -> "Accept-Charset", x
    | AcceptDatetime x -> "Accept-Datetime", x
    | AcceptLanguage x -> "Accept-Language", x
    | Authorization x -> "Authorization", x
    | Connection x -> "Connection", x
    | ContentMD5 x -> "Content-MD5", x
    | ContentType x -> "Content-Type", x.ToString()
    | Date dt -> "Date", dt.ToString("R")
    | Expect i -> "Expect", i.ToString()
    | From x -> "From", x
    | IfMatch x -> "If-Match", x
    | IfModifiedSince dt -> "If-Modified-Since", dt.ToString("R")
    | IfNoneMatch x -> "If-None-Match", x
    | IfRange x -> "If-Range", x
    | MaxForwards i -> "Max-Forwards", string i
    | Origin x -> "Origin", x
    | Pragma x -> "Pragma", x
    | ProxyAuthorization x -> "Proxy-Authorization", x
    | Range { start = s; finish = f } when f <= 0L -> "Range", "bytes=" + string s
    | Range { start = s; finish = f } -> "Range", sprintf "bytes=%d-%d" s f
    | Referer x -> "Referer", x
    | Upgrade x -> "Upgrade", x
    | UserAgent x -> "User-Agent", x
    | Via x -> "Via", x
    | Warning x -> "Warning", x
    | Custom (n, v) -> n, v

  member x.Key = x.KeyValue |> fst

type UserDetails =
  { username : string
    password : string }

[<RequireQualifiedAccess>]
type Credentials =
  | None
  | Default
  | Custom of UserDetails

type Proxy =
  { Address: string
    Port: int
    Credentials: Credentials }

/// The key you have in &lt;input name="key" ... /&gt;
/// This string value is not yet encoded.
type FormEntryName = string

/// The string value is not yet encoded.
type FormValue = string

/// The key of a query string key-value pair.
/// The string value is not yet encoded.
type QueryStringName = string

/// The string value is not yet encoded.
type QueryStringValue = string

/// http://www.w3.org/Protocols/rfc2616/rfc2616-sec19.html
/// section 19.5.1 Content-Disposition, BNF.
type ContentDisposition =
  { typ      : string // "form-data" or "attachment"
    filename : string option
    /// e.g. "name=user_name"
    exts     : (FormEntryName * FormValue) list }

/// An optional file name
type FileName = string

type FileData =
  | Plain of string
  | Binary of byte []
  | StreamData of Stream

/// A file is a file name, a content-type (application/octet-stream if unknown) and the data.
type File = FileName * ContentType * FileData

/// http://www.w3.org/TR/html401/interact/forms.html
type FormData =
  /// Use when you post a single file
  /// Will use: multipart/form-data
  | FormFile of name:FormEntryName * File
  /// Use when you post multiple files as a multi-file-browse control
  /// Will use: multipart/mixed inside a multipart/form-data.
  | MultipartMixed of name:FormEntryName * files:File list
  /// Use when you simply post form data
  | NameValue of FormEntryName * FormValue

/// You often pass form-data to the server, e.g. curl -X POST <url> -F k=v -F file1=@file.png
type Form = FormData list

type RequestBody =
  | BodyForm of Form // * TransferEncodingHint option (7bit/8bit/binary)
  | BodyString of string
  | BodyRaw of byte []
    //| BodySocket of SocketTask // for all the nitty-gritty details, see #64

/// The name (key) of a cookie.
/// The string value is unencoded.
type CookieName = string

type Cookie =
  { name     : CookieName
    value    : string
    expires  : DateTimeOffset option
    path     : string option
    domain   : string option
    secure   : bool
    httpOnly : bool }

  static member Create(name : CookieName, value : string, ?expires, ?path, ?domain, ?secure, ?httpOnly) =
    { name     = name
      value    = value
      expires  = expires
      path     = path
      domain   = domain
      secure   = defaultArg secure false
      httpOnly = defaultArg httpOnly false }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Cookie =
  let toSystem x =
    let sc = System.Net.Cookie(x.name, x.value, Option.orDefault "/" x.path, Option.orDefault "" x.domain)
    x.expires |> Option.iter (fun e -> sc.Expires <- e.DateTime)
    sc.HttpOnly <- x.httpOnly
    sc.Secure <- x.secure
    sc

type Request =
  { Url                       : Uri
    Method                    : HttpMethod
    CookiesEnabled            : bool
    AutoFollowRedirects       : bool
    AutoDecompression         : DecompressionScheme
    Headers                   : Map<string, RequestHeader>
    Body                      : RequestBody
    BodyCharacterEncoding     : Encoding
    QueryStringItems          : Map<QueryStringName, QueryStringValue>
    Cookies                   : Map<CookieName, Cookie>
    ResponseCharacterEncoding : Encoding option
    Proxy                     : Proxy option
    KeepAlive                 : bool
    Timeout                   : int<ms> 
    NetworkCredentials        : Credentials option}

type CharacterSet = string

type Response =
  { StatusCode       : int
    ContentLength    : int64
    CharacterSet     : CharacterSet
    Cookies          : Map<string, string>
    Headers          : Map<ResponseHeader, string>
    /// A Uri that contains the URI of the Internet resource that responded to the request.
    /// <see cref="https://msdn.microsoft.com/en-us/library/system.net.httpwebresponse.responseuri%28v=vs.110%29.aspx"/>.
    ExpectedEncoding : Encoding option
    ResponseUri      : System.Uri
    Body             : Stream
    Luggage          : IDisposable option
  }
with
  interface IDisposable with
    member x.Dispose() =
      x.Body.Dispose()
      x.Luggage |> Option.iter (fun x -> x.Dispose())

  override x.ToString() =
    seq {
      yield x.StatusCode.ToString()
      for h in x.Headers do
        yield h.ToString()
      yield ""
      //if x.EntityBody |> Option.isSome then
      //     yield x.EntityBody |> Option.get
    } |> String.concat Environment.NewLine

type HttpFsState =
  { random      : Random
    cryptRandom : RandomNumberGenerator
    logger      : Logging.Logger }

/// Will re-generate random CLR per-app-domain -- create your own state for
/// deterministic boundary generation (or anything else needing random).
let DefaultHttpFsState =
  { random      = Random()
    cryptRandom = RandomNumberGenerator.Create()
    logger      = Logging.NoopLogger }

/// The header you tried to add was already there, see issue #64.
exception DuplicateHeader of RequestHeader

module internal Impl =
  [<Literal>]
  let CRLF = "\r\n"

  let ISOLatin1 = Encoding.GetEncoding "ISO-8859-1"

  let getMethodAsString request =
    match request.Method with
    | Options -> "OPTIONS"
    | Get -> "GET"
    | Head -> "HEAD"
    | Post -> "POST"
    | Put -> "PUT"
    | Delete -> "DELETE"
    | Trace -> "TRACE"
    | Connect -> "CONNECT"
    | Patch   -> "PATCH"

  /// URI encoding: for each byte in the byte-representation of the string,
  /// as seen after encoding with a given `byteEncoding`, print the %xx character
  /// as an ASCII character, for transfer.
  ///
  /// Pass the byteEncoding -- this is equivalent of the
  /// `accept-charset` attribute on the form-element in HTML. If you don't
  /// know what to do: pass UTF8 and it will 'just work'.
  let uriEncode byteEncoding : _ -> string =
    List.map (fun kv ->
      String.Concat [
        HttpUtility.UrlEncode (fst kv, byteEncoding)
        "="
        HttpUtility.UrlEncode (snd kv, byteEncoding)
      ])
    >> String.concat "&"

  let getQueryString byteEncoding request =
    if Map.isEmpty request.QueryStringItems then ""
    else
      let items = Map.toList request.QueryStringItems
      String.Concat [ "?"; uriEncode byteEncoding items ]

  let basicAuthorz username password =
    String.Concat [ username; ":"; password ]
    |> DefaultBodyEncoding.GetBytes
    |> Convert.ToBase64String
    |> fun base64 -> "Basic " + base64
    |> fun headerValue -> Authorization headerValue

  let generateBoundary =
    let boundaryChars = "abcdefghijklmnopqrstuvwxyz_-/':ABCDEFGHIJKLMNOPQRSTUVWXYZ"
    let boundaryLen = 30
    fun clientState ->
        let rnd = clientState.random
        let sb = new StringBuilder(boundaryLen)
        for i in 0 .. boundaryLen - 1 do
            sb.Append (boundaryChars.[rnd.Next(boundaryChars.Length)]) |> ignore
        sb.ToString()

  let escapeQuotes (s : string) =
      // https://github.com/rack/rack/issues/323#issuecomment-3609743
      s.Replace("\"", "\\\"")

  module StreamWriters =
    let writeBytes bs (output : Stream) =
      Async.AwaitTask (output.WriteAsync(bs, 0, bs.Length))

    let writeBytesLine bs (output : Stream) =
      async {
        do! writeBytes bs output
        do! output.WriteAsync (ASCII.bytes CRLF, 0, 2)
      }

    /// Writes a string and CRLF as ASCII
    let writeLineAscii string : Stream -> Async<unit> =
      String.Concat [ string; CRLF ] |> ASCII.bytes |> writeBytes

    /// Writes a string as ASCII
    let writeAscii : string -> Stream -> Async<unit> =
      ASCII.bytes >> writeBytes

    /// Writes a string and CRLF as UTF8
    let writeLineUtf8 string =
      String.Concat [ string; CRLF ] |> ASCII.bytes |> writeBytes

    /// Writes a string as UTF8
    let writeUtf8 : string -> Stream -> Async<unit> =
      UTF8.bytes >> writeBytes
    
    let writeStream (input : Stream) (output : Stream) =
      Async.AwaitTask (input.CopyToAsync output)

    let writeStreamLine input output =
      async {
        do! writeStream input output
        do! output.WriteAsync (ASCII.bytes CRLF, 0, 2)
      }

  open StreamWriters

  let generateFileData (encoding : Encoding) contentType contents = seq {
    match contentType, contents with
    | { typ = "text"; subtype = "plain" }, Plain text ->
      yield writeLineAscii ""
      yield writeLineUtf8 text

    | _, Plain text ->
      yield writeLineAscii "Content-Transfer-Encoding: base64"
      yield writeLineAscii ""
      yield writeLineAscii (text |> encoding.GetBytes |> Convert.ToBase64String)

    | _, Binary bytes ->
      yield writeLineAscii "Content-Transfer-Encoding: binary"
      yield writeLineAscii ""
      yield writeBytesLine bytes

    | _, StreamData stream ->
      yield writeLineAscii "Content-Transfer-Encoding: binary"
      yield writeLineAscii ""
      yield writeStreamLine stream
  }

  let generateContentDispos value (kvs : (string * string) list) =
      let formatKv = function
          | k, v -> (sprintf "%s=\"%s\"" k (escapeQuotes v))
      String.concat "; " [ yield sprintf "Content-Disposition: %s" value
                           yield! (kvs |> List.map formatKv) ]

  let generateFormData state (encoding : Encoding) boundary formData =
    let rec generateFormDataInner boundary values isMultiFile = [
      match values with
      | [] ->
        yield writeLineAscii (sprintf "--%s--" boundary)
        if not isMultiFile then yield writeLineAscii ""
      | h :: rest ->
        yield writeLineAscii (sprintf "--%s" boundary)
        match h with
        | FormFile (name, (fileName, contentType, contents)) ->
          let dispos = if isMultiFile then "file" else "form-data"
          yield writeLineUtf8 (generateContentDispos dispos
                                [ if not isMultiFile then yield "name", name
                                  yield "filename", fileName ])
          yield writeLineUtf8 (sprintf "Content-Type: %O" contentType)
          yield! generateFileData encoding contentType contents

        | MultipartMixed (name, files) ->
          let boundary' = generateBoundary state
          yield writeLineAscii (sprintf "Content-Type: multipart/mixed; boundary=%s" boundary')
          yield writeLineUtf8 (generateContentDispos "form-data" [ "name", name ])
          yield writeLineUtf8 ""
          // remap the multi-files to single files and recursively call myself
          let files' = files |> List.map (fun f -> FormFile (name, f))
          yield! generateFormDataInner boundary' files' true

        | NameValue (name, value) ->
          yield writeLineAscii (sprintf "Content-Disposition: form-data; name=\"%s\"" (escapeQuotes name))
          yield writeLineAscii ""
          yield writeLineUtf8 value
        yield! generateFormDataInner boundary rest isMultiFile
    ]
    generateFormDataInner boundary formData false

  let private formatBodyUrlencoded bodyEncoding formData =
    [ formData
      |> List.map (function
          | NameValue (k, v) -> k, v
          | x -> failwith "programming error: expected all formData to be NameValue as per 'formatBody'.")
      |> uriEncode bodyEncoding
      // after URI encoding, we represent all bytes in ASCII (subset of Latin1)
      // and none-the-less; they will map 1-1 with the UTF8 set if the server
      // interpret Content-Type: ...; charset=utf8 as 'raw bytes' of the body.
      |> ISOLatin1.GetBytes
      |> writeBytes
    ]

  let formatBody (clientState : HttpFsState) =
               // we may actually change the content type if it's wrong
               //: ContentType option * Encoding * RequestBody -> ContentType option * byte [] =
    function
    | userCt, _, BodyRaw raw ->
      userCt, [ writeBytes raw ]

    | userCt, _, BodyString str ->
      userCt, [ writeUtf8 str ]

    | userCt, _, BodyForm [] ->
      userCt, [ writeBytes [||] ]

    | userCt, encoding, BodyForm formData ->
      let onlyNameValues =
        formData |> List.forall (function | NameValue _ -> true | _ -> false)

      if onlyNameValues then
        ContentType.Parse "application/x-www-form-urlencoded",
        formatBodyUrlencoded encoding formData
      else
        let boundary = generateBoundary clientState
        ContentType.Create("multipart", "form-data", boundary=boundary) |> Some,
        generateFormData clientState encoding boundary formData

open Impl

/// <summary>Creates the Request record which can be used to make an HTTP request</summary>
/// <param name="httpMethod">The type of request to be made (Get, Post, etc.)</param>
/// <param name="url">The URL of the resource including protocol, e.g. 'http://www.relentlessdevelopment.net'</param>
/// <returns>The Request record</returns>
let createRequest httpMethod (url : Uri) =
  { Url                       = url
    Method                    = httpMethod
    CookiesEnabled            = true
    AutoFollowRedirects       = true
    AutoDecompression         = DecompressionScheme.None
    Headers                   = Map.empty
    Body                      = BodyRaw [||]
    BodyCharacterEncoding     = DefaultBodyEncoding
    QueryStringItems          = Map.empty
    Cookies                   = Map.empty
    ResponseCharacterEncoding = None
    Proxy                     = None
    KeepAlive                 = true
    /// The default value is 100,000 milliseconds (100 seconds).
    /// <see cref="https://msdn.microsoft.com/en-us/library/system.net.httpwebrequest.timeout%28v=vs.110%29.aspx"/>.
    Timeout                   = 100000<ms> 
    NetworkCredentials        = None }

/// Disables cookies, which are enabled by default
let withCookiesDisabled request = 
  { request with CookiesEnabled = false }

/// Disables automatic following of redirects, which is enabled by default
let withAutoFollowRedirectsDisabled request = 
  { request with AutoFollowRedirects = false }

/// Adds a header, defined as a RequestHeader
/// The current implementation doesn't allow you to add a single header multiple
/// times. File an issue if this is a limitation for you.
let withHeader (header : RequestHeader) (request : Request) =
  { request with Headers = request.Headers |> Map.put header.Key header }

/// Adds an HTTP Basic Authentication header, which includes the username and password encoded as a base-64 string
let withBasicAuthentication username password =
  withHeader (basicAuthorz username password)

/// Adds a credential cache to support NTLM authentication
let withNTLMAuthentication username password (request : Request) =
  {request with NetworkCredentials = Some (Credentials.Custom { username = username; password = password}) }

/// Sets the accept-encoding request header to accept the decompression methods selected,
/// and automatically decompresses the responses.
///
/// Multiple schemes can be OR'd together, e.g. (DecompressionScheme.Deflate ||| DecompressionScheme.GZip)
let withAutoDecompression decompressionSchemes request =
  { request with AutoDecompression = decompressionSchemes}

/// Lets you set your own body - use the RequestBody type to build it up.
let withBody body (request : Request) =
  { request with Body = body }

/// Sets the the request body, using UTF-8 character encoding.
///
/// Only certain request types should have a body, e.g. Posts.
let withBodyString body (request : Request) =
  { request with Body = BodyString body }

/// Sets the request body, using the provided character encoding.
let withBodyStringEncoded body characterEncoding request =
  { request with Body = BodyString body; BodyCharacterEncoding = characterEncoding }

/// Adds the provided QueryString record onto the request URL.
/// Multiple items can be appended, but only the last appended key/value with
/// the same key as a previous key/value will be used.
let withQueryStringItem (name : QueryStringName) (value : QueryStringValue) request =
  { request with QueryStringItems = request.QueryStringItems |> Map.put name value }

/// Adds a cookie to the request
/// The domain will be taken from the URL, and the path set to '/'.
///
/// If your cookie appears not to be getting set, it could be because the response is a redirect,
/// which (by default) will be followed automatically, but cookies will not be re-sent.
let withCookie cookie request =
  if not request.CookiesEnabled then failwithf "Cannot add cookie %A - cookies disabled" cookie.name
  { request with Cookies = request.Cookies |> Map.put cookie.name cookie }

/// Decodes the response using the specified encoding, regardless of what the response specifies.
///
/// If this is not set, response character encoding will be:
///  - taken from the response content-encoding header, if provided, otherwise
///  UTF8
///
/// Many web pages define the character encoding in the HTML. This will not be used.
let withResponseCharacterEncoding encoding request : Request = 
  { request with ResponseCharacterEncoding = Some encoding }

/// Sends the request via the provided proxy.
///
/// If this is no set, the proxy settings from IE will be used, if available.
let withProxy proxy request =
  {request with Proxy = Some proxy }

/// Sets the keep-alive header.  Defaults to true.
///
/// If true, Connection header also set to 'Keep-Alive'
/// If false, Connection header also set to 'Close'
///
/// NOTE: If true, headers only sent on first request.
let withKeepAlive value request =
  { request with KeepAlive = value }

let withTimeout timeout request =
  { request with Timeout = timeout }

module internal DotNetWrapper =
  /// Sets headers on HttpWebRequest.
  /// Mutates HttpWebRequest.
  let setHeaders (headers : RequestHeader list) (webRequest : HttpWebRequest) =
    let add (k : string) v = webRequest.Headers.Add (k, v)
    List.iter (function
              | Accept value                     -> webRequest.Accept <- value
              | AcceptCharset value              -> add "Accept-Charset" value
              | AcceptDatetime value             -> add "Accept-Datetime" value
              | AcceptLanguage value             -> add "Accept-Language" value
              | Authorization value              -> add "Authorization" value
              | RequestHeader.Connection value   -> webRequest.Connection <- value
              | RequestHeader.ContentMD5 value   -> add "Content-MD5" value
              | RequestHeader.ContentType value  -> webRequest.ContentType <- value.ToString()
              | RequestHeader.Date value         -> webRequest.Date <- value
              | Expect value                     -> webRequest.Expect <- value.ToString()
              | From value                       -> add "From" value
              | IfMatch value                    -> add "If-Match" value
              | IfModifiedSince value            -> webRequest.IfModifiedSince <- value
              | IfNoneMatch value                -> add "If-None-Match" value
              | IfRange value                    -> add "If-Range" value
              | MaxForwards value                -> add "Max-Forwards" (value.ToString())
              | Origin value                     -> add "Origin" value
              | RequestHeader.Pragma value       -> add "Pragma" value
              | ProxyAuthorization value         -> add "Proxy-Authorization" value
              | Range value                      -> webRequest.AddRange(value.start, value.finish)
              | Referer value                    -> webRequest.Referer <- value
              | Upgrade value                    -> add "Upgrade" value
              | UserAgent value                  -> webRequest.UserAgent <- value
              | RequestHeader.Via value          -> add "Via" value
              | RequestHeader.Warning value      -> add "Warning" value
              | Custom (customName, customValue) -> add customName customValue)
              headers

  /// Sets cookies on HttpWebRequest.
  /// Mutates HttpWebRequest.
  let setCookies (cookies : Cookie list) (url : Uri) (webRequest : HttpWebRequest) =
    let mapDomain c = { c with domain = Some url.Host }
    cookies
    |> List.map (mapDomain >> Cookie.toSystem)
    |> List.iter (webRequest.CookieContainer.Add)

  /// Sets proxy on HttpWebRequest.
  /// Mutates HttpWebRequest.
  let setProxy proxy (webRequest : HttpWebRequest) =
    proxy |> Option.iter (fun proxy ->
      let webProxy = WebProxy(proxy.Address, proxy.Port)

      match proxy.Credentials with
      | Credentials.Custom { username = name; password = pwd} ->
          webProxy.Credentials <- NetworkCredential(name, pwd)
      | Credentials.Default -> webProxy.UseDefaultCredentials <- true
      | Credentials.None -> webProxy.Credentials <- null

      webRequest.Proxy <- webProxy)

  /// Sets NetworkCredentials on HttpWebRequest.
  /// Mutates HttpWebRequest.
  let setNetworkCredentials credentials (webRequest : HttpWebRequest) =
    credentials |> Option.iter (fun credentials ->

      match credentials with
      | Credentials.Custom { username = name; password = pwd} ->
          let last n xs = Array.toSeq xs |> Seq.skip (xs.Length - n) |> Seq.toArray
          match (last 2 (name.Split [|'\\'|])) with
          | [| domain ; user |] -> webRequest.Credentials <- NetworkCredential(user, pwd, domain)
          | _ -> raise (System.Exception("User name is not in form domain\\user"))
          
      | Credentials.Default -> webRequest.UseDefaultCredentials <- true
      | Credentials.None -> webRequest.Credentials <- null)

  /// Sets body on HttpWebRequest.
  /// Mutates HttpWebRequest.
  let tryWriteBody (writers : seq<Stream -> Async<unit>>) (webRequest : HttpWebRequest) = 
    if webRequest.Method = "POST" || webRequest.Method = "PUT" then
      async {
        // Getting the request stream seems to be actually connecting 
        use reqStream = webRequest.GetRequestStream()
        for writer in writers do
          do! writer reqStream
      }
    else async.Return ()

  let matchCtHeader k = function
    | RequestHeader.ContentType ct -> Some ct
    | _ -> None

  let ensureNo100Continue () =
    if ServicePointManager.Expect100Continue then
      ServicePointManager.Expect100Continue <- false

  /// The nasty business of turning a Request into an HttpWebRequest
  let toHttpWebRequest state (request : Request) =
    ensureNo100Continue ()

    let contentType = request.Headers |> Map.tryPick matchCtHeader

    let contentEncoding =
      // default the ContentType charset encoding, otherwise, use BodyCharacterEncoding.
      contentType
      |> function
      | Some { charset = Some enc } -> Some enc
      | _ -> None
      |> Option.fold (fun s t -> t) request.BodyCharacterEncoding

    let url =
      let b = UriBuilder (request.Url)
      match b.Query with
      | "" | null -> b.Query <- getQueryString contentEncoding request
      | _ -> ()
      b.Uri

    let webRequest =
      HttpWebRequest.Create(url) :?> HttpWebRequest

    let newContentType, body =
      formatBody state (contentType, contentEncoding, request.Body)

    let request =
      // if we have a new content type, from using BodyForm, then this
      // updates the request value with that header
      newContentType
      |> Option.map RequestHeader.ContentType
      |> Option.fold (flip withHeader) request

    webRequest.Method <- getMethodAsString request
    webRequest.ProtocolVersion <- HttpVersion.Version11

    if request.CookiesEnabled then
      webRequest.CookieContainer <- CookieContainer()

    webRequest.AllowAutoRedirect <- request.AutoFollowRedirects

    // this relies on the DecompressionScheme enum values being the same as those in System.Net.DecompressionMethods
    webRequest.AutomaticDecompression <- enum<DecompressionMethods> <| int request.AutoDecompression

    webRequest |> setHeaders (request.Headers |> Map.toList |> List.map snd)
    webRequest |> setCookies (request.Cookies |> Map.toList |> List.map snd) request.Url
    webRequest |> setProxy request.Proxy
    webRequest |> setNetworkCredentials request.NetworkCredentials

    webRequest.KeepAlive <- request.KeepAlive
    webRequest.Timeout <- (int)request.Timeout

    webRequest, webRequest |> tryWriteBody body

  /// Uses the HttpWebRequest to get the response.
  /// HttpWebRequest throws an exception on anything but a 200-level response,
  /// so we handle such exceptions and return the response.
  let getResponseNoException (request : HttpWebRequest) = async {
    try
      let! response = request.AsyncGetResponse()
      return response :?> HttpWebResponse
    with
    | :? WebException as wex ->
      if wex.Response <> null then
        return wex.Response :?> HttpWebResponse
      else
        return raise wex
  }

  let getCookiesAsMap (response:HttpWebResponse) =
      let cookieArray = Array.zeroCreate response.Cookies.Count
      response.Cookies.CopyTo(cookieArray, 0)
      cookieArray |> Array.fold (fun map cookie -> map |> Map.add cookie.Name cookie.Value) Map.empty

  /// Get the header as a ResponseHeader option. Is an option because there are some headers we don't want to set.
  let getResponseHeader = function
    | null -> None
    | "Access-Control-Allow-Origin" -> Some(AccessControlAllowOrigin)
    | "Accept-Ranges"               -> Some(AcceptRanges)
    | "Age"                         -> Some(Age)
    | "Allow"                       -> Some(Allow)
    | "Cache-Control"               -> Some(CacheControl)
    | "Connection"                  -> Some(ResponseHeader.Connection)
    | "Content-Encoding"            -> Some(ContentEncoding)
    | "Content-Language"            -> Some(ContentLanguage)
    | "Content-Length"              -> None
    | "Content-Location"            -> Some(ContentLocation)
    | "Content-MD5"                 -> Some(ResponseHeader.ContentMD5Response)
    | "Content-Disposition"         -> Some(ContentDisposition)
    | "Content-Range"               -> Some(ContentRange)
    | "Content-Type"                -> Some(ResponseHeader.ContentTypeResponse)
    | "Date"                        -> Some(ResponseHeader.DateResponse)
    | "ETag"                        -> Some(ETag)
    | "Expires"                     -> Some(Expires)
    | "Last-Modified"               -> Some(LastModified)
    | "Link"                        -> Some(Link)
    | "Location"                    -> Some(Location)
    | "P3P"                         -> Some(P3P)
    | "Pragma"                      -> Some(ResponseHeader.PragmaResponse)
    | "Proxy-Authenticate"          -> Some(ProxyAuthenticate)
    | "Refresh"                     -> Some(Refresh)
    | "Retry-After"                 -> Some(RetryAfter)
    | "Server"                      -> Some(Server)
    | "Set-Cookie"                  -> None
    | "Strict-Transport-Security"   -> Some(StrictTransportSecurity)
    | "Trailer"                     -> Some(Trailer)
    | "Transfer-Encoding"           -> Some(TransferEncoding)
    | "Vary"                        -> Some(Vary)
    | "Via"                         -> Some(ResponseHeader.ViaResponse)
    | "Warning"                     -> Some(ResponseHeader.WarningResponse)
    | "WWW-Authenticate"            -> Some(WWWAuthenticate)
    | _ as name                     -> Some(NonStandard name)

  /// Gets the headers from the passed response as a map of ResponseHeader and string.
  let getHeadersAsMap (response:HttpWebResponse) =
    response.Headers.Keys
    |> Seq.cast<string>
    |> List.ofSeq
    |> List.map (fun wcKey -> wcKey, getResponseHeader wcKey)
    |> List.map (fun (wcKey, httpfsKey) -> httpfsKey, response.Headers.Item(wcKey))
    |> List.filter (fst >> Option.isSome)
    |> List.map (fun (k, v) -> Option.get k, v)
    |> Map.ofList

  let mapEncoding = String.toLowerInvariant >> function
    | "utf8" -> "utf-8"
    | "utf16" -> "utf-16"
    | other -> other

open DotNetWrapper

type Response with
  static member internal FromHttpResponse (response : HttpWebResponse) =
    { StatusCode       = int (response.StatusCode)
      CharacterSet     = response.CharacterSet
      ContentLength    = response.ContentLength
      Cookies          = getCookiesAsMap response
      Headers          = getHeadersAsMap response
      ResponseUri      = response.ResponseUri
      ExpectedEncoding = None
      Body             = response.GetResponseStream()
      Luggage          = Some (upcast response) }

/// Sends the HTTP request and returns the full response as a Response record, asynchronously.
let getResponse request = async {
  let webRequest, exec = toHttpWebRequest DefaultHttpFsState request
  do! exec
  let! resp = getResponseNoException webRequest
  let wrapped =
    { Response.FromHttpResponse resp with
        ExpectedEncoding = request.ResponseCharacterEncoding }
  return wrapped 
}

[<Obsolete "Use 'getResponse' instead, everything is async by default">]
let getResponseAsync = getResponse

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Response =
  let readBodyAsString (response : Response) : Async<string> =
    async {
      let charset =
        match response.ExpectedEncoding with
        | None ->
          match response.CharacterSet with
          | null | "" ->
            ISOLatin1 // TODO: change to UTF-8
          | responseCharset ->
            Encoding.GetEncoding(mapEncoding responseCharset)

        | Some enc ->
          enc

      use rdr = new AsyncStreamReader(response.Body, charset, true, 0x2000, false)
      return! rdr.ReadToEnd()
    }

  let readBodyAsBytes (response : Response) : Async<byte []> =
    async {
      use ms = new MemoryStream()
      do! response.Body.CopyToAsync ms
      return ms.ToArray()
    }

/// For those of you who can't be bothered to use getResponse |> Response.readBodyAsString.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Request =

  /// Note: this sends the request, reads the response, disposes it and its stream
  let responseAsString req = async {
    use! resp = getResponse req 
    return! Response.readBodyAsString resp
  }

  /// Note: this sends the request, reads the response, disposes it and its stream
  let responseAsBytes req = async {
    use! resp = getResponse req
    return! Response.readBodyAsBytes resp
  }