namespace HttpFs

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Runtime.CompilerServices
open System.Security.Cryptography
open Hopac
open Hopac.Infixes
open HttpFs.Logging

[<assembly: InternalsVisibleTo "HttpFs.IntegrationTests">]
[<assembly: InternalsVisibleTo "HttpFs.UnitTests">]
()

[<AutoOpen>]
module internal Prelude =
  module Option =
    let orDefault def =
      Option.fold (fun s t -> t) def

  module ASCII =
    open System.Text

    let bytes (s : string) =
      Encoding.ASCII.GetBytes s

  module ThreadSafeRandom =
    open System.Threading

    let private seed = ref Environment.TickCount
    let private rnd = new ThreadLocal<Random>(fun () -> Random (Interlocked.Increment seed))

    /// Fill buffer with random bytes
    let nextBytes (buffer : byte []) =
      rnd.Value.NextBytes buffer

    /// Generate a new random int32 value bounded to [minInclusive; maxExclusive)
    let next (minInclusive : int) (maxExclusive : int) =
      rnd.Value.Next(minInclusive, maxExclusive)

    /// Generate a new random ulong64 value
    let nextUInt64 () =
      let buffer = Array.zeroCreate<byte> sizeof<UInt64>
      rnd.Value.NextBytes buffer
      BitConverter.ToUInt64(buffer, 0)

  module Counter =

    type private T =
      { getNext : Ch<unit * IVar<unit>> }

    let create () =
      let getNext = Ch ()

      let run () =
        Job.foreverServer <| fun current ->
          getNext ^-> IVar.fill current
          <|> nack

      { getNextCh = getNext }

    let getNext (t:T) : Alt<uint64> =
      t *<=->- fun (resp, nack) -> resp, nack

type HttpFsConfig =
  { /// Gets a new random number. Calls to this function must be thread-
    /// safe. This number doesn't have to be cryptographically strong.
    getRandom : unit -> uint64

    /// Gets the next sequence number. Calls to this function must be
    /// thread-safe. This is used for generating pipelined requests
    /// and keeping track of messages sent/in flight.
    getNext   : unit -> Alt<uint64>

    /// The logger to use for logging inside the library.
    logger    : Logger }

  /// Will re-generate random CLR per-app-domain -- create your own state for
  /// deterministic boundary generation (or anything else needing random).
  static member create(?logger, ?getNext, ?getRandom) =
    let counter = lazy (Counter.create ())
    let logger = defaultArg logger (Log.create "HttpFs")
    { getRandom   = defaultArg getRandom ThreadSafeRandom.nextUInt64
      getNext     = defaultArg getNext (fun () -> Counter.getNext counter.Value)
      logger      = logger }

  member x.getRandomInRange minValue maxValue =
    let raw = x.getRandom ()
    minValue + (raw % (minValue - maxValue))

type Client(config : HttpFsConfig) =
  member x.hi = "there"

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Client =

  open System
  open System.IO
  open System.Net
  open System.Text
  open System.Web
  open Microsoft.FSharp.Control
  open Microsoft.FSharp.Control.CommonExtensions
  open Microsoft.FSharp.Control.WebExtensions

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
        | Some b -> yield! [ ";"; " boundary="; sprintf "\"%s\"" b ]
      ]

    interface IComparable with
      member x.CompareTo other =
        match other with
        | :? ContentType as ct -> (x :> IComparable<ContentType>).CompareTo ct
        | x -> failwithf "invalid comparison ContentType to %s" (x.GetType().Name)

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

    static member create(typ : string, subtype : string, ?charset : Encoding, ?boundary : string) =
      { typ      = typ
        subtype  = subtype
        charset  = charset
        boundary = boundary }

    // TODO, use: https://github.com/freya-fs/freya/blob/master/src/Freya.Types.Http/Types.fs#L420-L426
    static member parse (str : string) =
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

    static member create(name : CookieName, value : string, ?expires, ?path, ?domain, ?secure, ?httpOnly) =
      { name     = name
        value    = value
        expires  = expires
        path     = path
        domain   = domain
        secure   = defaultArg secure false
        httpOnly = defaultArg httpOnly false }

  [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
  module Cookie =
    let internal toSystem x =
      let sc = System.Net.Cookie(x.name, x.value, Option.orDefault "/" x.path, Option.orDefault "" x.domain)
      x.expires |> Option.iter (fun e -> sc.Expires <- e.DateTime)
      sc.HttpOnly <- x.httpOnly
      sc.Secure <- x.secure
      sc

  type Request =
    { url                       : Uri
      ``method``                : HttpMethod
      cookiesEnabled            : bool
      autoFollowRedirects       : bool
      autoDecompression         : DecompressionScheme
      headers                   : Map<string, RequestHeader>
      body                      : RequestBody
      bodyCharacterEncoding     : Encoding
      queryStringItems          : Map<QueryStringName, QueryStringValue>
      cookies                   : Map<CookieName, Cookie>
      responseCharacterEncoding : Encoding option
      proxy                     : Proxy option
      keepAlive                 : bool
      timeout                   : int<ms>
      networkCredentials        : Credentials option }

  type CharacterSet = string

  type TcpException =
    /// WebExceptionStatus.ConnectFailure
    | ConnectFailure
    /// WebExceptionStatus.ConnectionClosed
    /// WebExceptionStatus.ReceiveFailure
    /// WebExceptionStatus.SendFailure
    | SocketClosed
    /// WebExceptionStatus.RequestProhibitedByProxy
    | SocketClosedProxy
    /// WebExceptionStatus.NameResolutionFailure
    | NameResolutionFailure
    /// WebExceptionStatus.ProxyNameResolutionFailure
    | NameResolutionFailureProxy
    /// Unhandled Tcp exception
    | SocketException of System.Net.Sockets.SocketException
    | IOException of System.IO.IOException

  type HttpException =
    /// WebExceptionStatus.KeepAliveFailure
    | KeepAliveClosed
    /// WebExceptionStatus.MessageLengthLimitExceeded
    /// https://code.logos.com/blog/2012/01/webexception-the-message-limit-length-was-exceeded.html
    | TooLongResponseHeaders
    /// WebExceptionStatus.ServerProtocolViolation
    /// The server did not reply with a valid HTTP response.
    | InvalidServerResponse

  type TlsException =
    /// A server certificate could not be validated.
    /// WebExceptionStatus.TrustFailure
    | BrokenTrust
    /// E.g. the data was corrupted and a message-authenticating protocol like
    /// AES-GCM was used. Or there was a TLS ALERT message on the channel. This
    /// means that you're experiencing meddelsome man-in-the-middle, like
    /// a bad forward proxy, an over-eager ISP or an active man-in-the-middle
    /// attack.
    /// WebExceptionStatus.SecureChannelFailure
    | SecureChannelFailure

  type Error =
    | TlsException of TlsException
    | TcpException of TcpException
    | HttpException of HttpException
    | Cancelled

  type Response =
    { statusCode       : int
      contentLength    : int64
      characterSet     : CharacterSet
      cookies          : Map<string, string>
      headers          : Map<ResponseHeader, string>
      /// A Uri that contains the URI of the Internet resource that responded to the request.
      /// <see cref="https://msdn.microsoft.com/en-us/library/system.net.httpwebresponse.responseuri%28v=vs.110%29.aspx"/>.
      expectedEncoding : Encoding option
      responseUri      : System.Uri
      body             : Stream
      luggage          : IDisposable option }

    interface IDisposable with
      member x.Dispose() =
        x.body.Dispose()
        x.luggage |> Option.iter (fun x -> x.Dispose())

    override x.ToString() =
      seq {
        yield x.statusCode.ToString()
        for h in x.headers do
          yield h.ToString()
        yield ""
        //if x.EntityBody |> Option.isSome then
        //     yield x.EntityBody |> Option.get
      } |> String.concat Environment.NewLine

  /// The header you tried to add was already there, see issue #64.
  exception DuplicateHeader of RequestHeader

  module internal Impl =
    [<Literal>]
    let CRLF = "\r\n"

    let ISOLatin1 = Encoding.GetEncoding "ISO-8859-1"

    let getMethodAsString request =
      match request.``method`` with
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
      if Map.isEmpty request.queryStringItems then ""
      else
        let items = Map.toList request.queryStringItems
        String.Concat [ uriEncode byteEncoding items ]

    let basicAuthorz username password =
      String.Concat [ username; ":"; password ]
      |> DefaultBodyEncoding.GetBytes
      |> Convert.ToBase64String
      |> fun base64 -> "Basic " + base64
      |> fun headerValue -> Authorization headerValue

    let generateBoundary =
      let boundaryChars = "abcdefghijklmnopqrstuvwxyz_-/':ABCDEFGHIJKLMNOPQRSTUVWXYZ"
      let boundaryLen = 30
      fun (config : HttpFsConfig) ->
        let sb = new StringBuilder(boundaryLen)
        for i in 0 .. boundaryLen - 1 do
          let rndIndex =
            int (config.getRandomInRange 0UL (uint64 boundaryChars.Length))
          sb.Append (boundaryChars.[rndIndex]) |> ignore
        sb.ToString()

    let escapeQuotes (s : string) =
      // https://github.com/rack/rack/issues/323#issuecomment-3609743
      s.Replace("\"", "\\\"")

    module StreamWriters =
      let writeBytes bs (output : Stream) =
        Job.awaitUnitTask (output.WriteAsync(bs, 0, bs.Length))

      let writeBytesLine bs (output : Stream) =
        job {
          do! writeBytes bs output
          do! Job.awaitUnitTask (output.WriteAsync (ASCII.bytes CRLF, 0, 2))
        }

      /// Writes a string and CRLF as ASCII
      let writeLineAscii string : Stream -> Job<unit> =
        String.Concat [ string; CRLF ] |> ASCII.bytes |> writeBytes

      /// Writes a string as ASCII
      let writeAscii : string -> Stream -> Job<unit> =
        ASCII.bytes >> writeBytes

      /// Writes a string and CRLF as UTF8
      let writeLineUtf8 string =
        String.Concat [ string; CRLF ] |> UTF8.bytes |> writeBytes

      /// Writes a string as UTF8
      let writeUtf8 : string -> Stream -> Job<unit> =
        UTF8.bytes >> writeBytes

      let writeStream (input : Stream) (output : Stream) =
        Job.awaitUnitTask (input.CopyToAsync output)

      let writeStreamLine input output =
        job {
          do! writeStream input output
          do! Job.awaitUnitTask (output.WriteAsync (ASCII.bytes CRLF, 0, 2))
        }

    open StreamWriters

    let generateFileData (encoding : Encoding) contentType contents = seq {
      match contentType, contents with
      | { typ = "text"; subtype = _ }, Plain text ->
        yield writeLineAscii ""
        yield writeLineUtf8 text

      | { typ = "application"; subtype = subtype }, Plain text 
        when List.exists ((=) (subtype.Split('+') |> Seq.last)) ["json"; "xml"] ->
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
            yield writeLineAscii (sprintf "Content-Type: multipart/mixed; boundary=\"%s\"" boundary')
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

    let formatBody (config : HttpFsConfig) =
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
          ContentType.parse "application/x-www-form-urlencoded",
          formatBodyUrlencoded encoding formData
        else
          let boundary = generateBoundary config
          ContentType.create("multipart", "form-data", boundary=boundary) |> Some,
          generateFormData config encoding boundary formData

  open Impl

  module internal DotNetWrapper =

    open HttpFs.Logging.Message

    let private setLocalName lastBit =
      setName [| "HttpFs"; "Client"; "DotNetWrapper"; lastBit |]

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

    let mapFailure (wex : WebException) : Error =
      match wex.Status with
      | WebExceptionStatus.Success ->
        failwith "The Success status should not be used when exceptions are thrown"

      | WebExceptionStatus.NameResolutionFailure ->
        TcpException NameResolutionFailure

      | WebExceptionStatus.ConnectFailure ->
        TcpException ConnectFailure

      | WebExceptionStatus.ConnectionClosed
      | WebExceptionStatus.ReceiveFailure
      | WebExceptionStatus.SendFailure ->
        TcpException SocketClosed

      | WebExceptionStatus.PipelineFailure ->
        failwith "HttpFs has not been tested with HTTP pipelining; got WebExceptionStatus.PipelineFailure"

      | WebExceptionStatus.RequestCanceled ->
        Cancelled

      | WebExceptionStatus.ProtocolError ->
        failwith "HttpFs handles HTTP status codes, not as exceptions, but as normal responses"

      | WebExceptionStatus.TrustFailure ->
        TlsException BrokenTrust

      | WebExceptionStatus.SecureChannelFailure ->
        TlsException SecureChannelFailure

      | WebExceptionStatus.ServerProtocolViolation ->
        HttpException InvalidServerResponse

      | WebExceptionStatus.KeepAliveFailure ->
        HttpException KeepAliveClosed

      | WebExceptionStatus.Pending ->
        failwith "HttpFs should not call operations out of order on the underlying HTTP libraries"

      | WebExceptionStatus.Timeout ->
        failwith "HttpFs handles timeout by not committing to the returned Alt and using Hopac.Global.timeOut"

      | WebExceptionStatus.ProxyNameResolutionFailure ->
        TcpException NameResolutionFailureProxy

      | WebExceptionStatus.UnknownError ->
        match wex.InnerException with
        | null ->
          failwithf "CLR returned 'Unknown Error' but there's no inner exception, here's the inner message '%s'"
                    wex.Message

        | :? System.Net.Sockets.SocketException as se ->
          TcpException (SocketException se)

        | :? System.IO.IOException as ioe ->
          TcpException (IOException ioe)

        | e ->
          raise <| Exception(sprintf "Unknown exception from the CLR - %s" wex.Message, e)

      | WebExceptionStatus.MessageLengthLimitExceeded ->
        HttpException TooLongResponseHeaders

      | WebExceptionStatus.RequestProhibitedByCachePolicy ->
        failwith "HttpFs doesn't support cache policies"

      | WebExceptionStatus.RequestProhibitedByProxy ->
        TcpException NameResolutionFailureProxy

      | WebExceptionStatus.CacheEntryNotFound ->
        failwith "using CacheEntry with HttpFs not supported"

      | other ->
        failwith "There are only 21 options in this enum, something the F# compiler should be aware of"

    let tryCommunicate (config : HttpFsConfig)
                       (request : WebRequest)
                       (xJ : Job<_>)
                       fWebExnResp
                       : Alt<Choice<'a, Error>> =

      Alt.withNackJob <| fun nack ->
        let abort () =
          // https://msdn.microsoft.com/en-us/library/system.net.httpwebrequest.abort.aspx
          config.logger.debug (eventX "Aborting request" >> setLocalName "tryCommunicate")
          request.Abort()

        // Result holder
        let rI = IVar ()

        // Non-throwing job
        let wrJ =
          Job.tryIn xJ (IVar.fill rI) (IVar.fill rI << function
          | :? WebException as wex ->
            if wex.Response <> null then
              Choice.create (fWebExnResp (wex.Response :?> HttpWebResponse))
            else
              Choice.createSnd (mapFailure wex)

          | :? IOException as ioex ->
            Choice.createSnd (TcpException (IOException ioex))

          | :? System.Net.Sockets.SocketException as sex ->
            Choice.createSnd (TcpException (SocketException sex))
            
          | otherwise ->
            // We should only be getting IOEceptions, TcpExceptions and WebExceptions.
            // So it's OK to crash the runtime fatally if this happens; then
            // afterwards we can patch this library.
            failwithf "Unknown exception type from network API: %O" otherwise)

        // Send the request!
        start wrJ

        // Enable abort when nack is signalled
        Job.start (nack ^-> abort) >>-.

        // Return the actual result
        rI

    /// Sets body on HttpWebRequest.
    /// Mutates HttpWebRequest.
    let tryWriteBody (config : HttpFsConfig)
                     (writers : seq<Stream -> Job<unit>>)
                     (webRequest : HttpWebRequest)
                     : Alt<Choice<unit, Error>> =

      if webRequest.Method = "POST" || webRequest.Method = "PUT" then
        // We'll be writing *a lot* of small pieces of data, so let's buffer that.
        webRequest.AllowWriteStreamBuffering <- true
        let writer =
          job {
            // Getting the request stream seems to be actually connecting to the server
            use! reqStream = Job.fromBeginEnd webRequest.BeginGetRequestStream webRequest.EndGetRequestStream

            config.logger.verbose (
              eventX "Starting to write body for {method}"
              >> setField "method" webRequest.Method
              >> setLocalName "tryWriteBody")
            for writer in writers do
              do! writer reqStream

            config.logger.verbose (
              eventX "Flushing"
              >> setLocalName "tryWriteBody")

            do! Job.awaitUnitTask (reqStream.FlushAsync())
            return Choice.create ()
          }

        tryCommunicate config webRequest writer ignore

      else
        config.logger.verbose (
          eventX "No body to write for {method}"
          >> setField "method" webRequest.Method
          >> setLocalName "tryWriteBody")
        Alt.always (Choice.create ())

    let matchCtHeader k = function
      | RequestHeader.ContentType ct ->
        Some ct
      | _ ->
        None

    let ensureNo100Continue () =
      if ServicePointManager.Expect100Continue then
        ServicePointManager.Expect100Continue <- false

    let setHeader (request : Request) (header : RequestHeader) =
      { request with headers = request.headers |> Map.add header.Key header }

    /// The nasty business of turning a Request into an HttpWebRequest
    let toHttpWebRequest state (request : Request) =
      ensureNo100Continue ()

      let contentType = request.headers |> Map.tryPick matchCtHeader

      let contentEncoding =
        // default the ContentType charset encoding, otherwise, use BodyCharacterEncoding.
        contentType
        |> Option.bind (function { charset = Some enc } -> Some enc | _ -> None)
        |> Option.fold (fun s t -> t) request.bodyCharacterEncoding

      let url =
        let b = UriBuilder request.url
        match b.Query with
        | ""
        | null ->
          b.Query <- getQueryString contentEncoding request
        | _ ->
          ()

        b.Uri

      let webRequest =
        HttpWebRequest.Create(url) :?> HttpWebRequest

      let newContentType, body =
        formatBody state (contentType, contentEncoding, request.body)

      let request =
        // if we have a new content type, from using BodyForm, then this
        // updates the request value with that header
        newContentType
        |> Option.map RequestHeader.ContentType
        |> Option.fold setHeader request

      webRequest.Method <- getMethodAsString request
      webRequest.ProtocolVersion <- HttpVersion.Version11

      if request.cookiesEnabled then
        webRequest.CookieContainer <- CookieContainer()

      webRequest.AllowAutoRedirect <- request.autoFollowRedirects

      // this relies on the DecompressionScheme enum values being the same as those in System.Net.DecompressionMethods
      webRequest.AutomaticDecompression <- enum<DecompressionMethods> <| int request.autoDecompression

      webRequest |> setHeaders (request.headers |> Map.toList |> List.map snd)
      webRequest |> setCookies (request.cookies |> Map.toList |> List.map snd) request.url
      webRequest |> setProxy request.proxy
      webRequest |> setNetworkCredentials request.networkCredentials

      webRequest.KeepAlive <- request.keepAlive
      webRequest.Timeout <- (int)request.timeout

      webRequest, webRequest |> tryWriteBody state body

    /// For debugging purposes only
    /// Converts the Request body to a format suitable for HttpWebRequest and returns this raw body as a string.
    let getRawRequestBodyString (config : HttpFsConfig) (request : Request) =
      let contentType = request.headers |> Map.tryPick matchCtHeader
      let contentEncoding =
        // default the ContentType charset encoding, otherwise, use BodyCharacterEncoding.
        contentType
        |> function
        | Some { charset = Some enc } -> Some enc
        | _ -> None
        |> Option.fold (fun s t -> t) request.bodyCharacterEncoding

      let newContentType, body =
        formatBody config (contentType, contentEncoding, request.body)

      use dataStream = new IO.MemoryStream()
      job {
          for writer in body do
            do! writer dataStream
        } |> Hopac.run

      dataStream.Position <- 0L // Reset stream position before reading
      use reader = new IO.StreamReader(dataStream)
      reader.ReadToEnd()

    /// Uses the HttpWebRequest to get the response.
    /// HttpWebRequest throws an exception on anything but a 200-level response,
    /// so we handle such exceptions and return the response.
    let getResponseNoException state (request : HttpWebRequest)
                               : Alt<Choice<HttpWebResponse, Error>> = 
      let requestJob =
        Job.fromBeginEnd request.BeginGetResponse request.EndGetResponse
        >>- (fun resp -> resp :?> HttpWebResponse)
        >>- Choice.create

      tryCommunicate state request requestJob id

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
    static member internal ofHttpResponse (response : HttpWebResponse) =
      { statusCode       = int (response.StatusCode)
        characterSet     = response.CharacterSet
        contentLength    = response.ContentLength
        cookies          = getCookiesAsMap response
        headers          = getHeadersAsMap response
        responseUri      = response.ResponseUri
        expectedEncoding = None
        body             = response.GetResponseStream()
        luggage          = Some (upcast response) }

  /// Sends the HTTP request and returns the full response as a Response record, asynchronously.
  let getResponse state request : Alt<Choice<Response, Error>> =
    let webRequest, writeRequest =
      toHttpWebRequest state request

    let wrapResponse = function
      | Choice1Of2 res ->
        { Response.ofHttpResponse res with
            expectedEncoding = request.responseCharacterEncoding }
        |> Choice.create

      | Choice2Of2 err ->
        Choice.createSnd err

    let bindReqResp : Choice<unit, Error> -> Alt<_> = function
      | Choice1Of2 ()  ->
        printfn "[getResponse] bindReqResp"
        getResponseNoException state webRequest

      | Choice2Of2 err ->
        Alt.always (Choice.createSnd err)

    (writeRequest ^=> bindReqResp) ^-> wrapResponse

  [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
  module Response =
    let readBodyAsString (response : Response) : Job<string> =
      job {
        let charset =
          match response.expectedEncoding with
          | None ->
            match response.characterSet with
            | null | "" ->
              ISOLatin1 // TODO: change to UTF-8
            | responseCharset ->
              try Encoding.GetEncoding(mapEncoding responseCharset)
              with _ -> Encoding.UTF8

          | Some enc ->
            enc
        
        use sr = new StreamReader(response.body, charset)
        return! sr.ReadToEndAsync()
      }

    let readBodyAsBytes (response : Response) : Job<byte []> =
      job {
        use ms = new MemoryStream()
        do! Job.awaitUnitTask (response.body.CopyToAsync ms)
        return ms.ToArray()
      }

  /// For those of you who can't be bothered to use getResponse |> Response.readBodyAsString.
  [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
  module Request =

    /// <summary>Creates the Request record which can be used to make an HTTP request</summary>
    /// <param name="httpMethod">The type of request to be made (Get, Post, etc.)</param>
    /// <param name="url">The URL of the resource including protocol, e.g. 'http://www.relentlessdevelopment.net'</param>
    /// <returns>The Request record</returns>
    let create httpMethod (url : Uri) =
      { url                       = url
        ``method``                = httpMethod
        cookiesEnabled            = true
        autoFollowRedirects       = true
        autoDecompression         = DecompressionScheme.None
        headers                   = Map.empty
        body                      = BodyRaw [||]
        bodyCharacterEncoding     = DefaultBodyEncoding
        queryStringItems          = Map.empty
        cookies                   = Map.empty
        responseCharacterEncoding = None
        proxy                     = None
        keepAlive                 = true
        /// The default value is 100,000 milliseconds (100 seconds).
        /// <see cref="https://msdn.microsoft.com/en-us/library/system.net.httpwebrequest.timeout%28v=vs.110%29.aspx"/>.
        timeout                   = 100000<ms>
        networkCredentials        = None }

    let createUrl httpMethod url =
      create httpMethod (Uri url)

    /// Disables cookies, which are enabled by default
    let cookiesDisabled request =
      { request with cookiesEnabled = false }

    /// Disables automatic following of redirects, which is enabled by default
    let autoFollowRedirectsDisabled request =
      { request with autoFollowRedirects = false }

    /// Adds a header, defined as a RequestHeader
    /// The current implementation doesn't allow you to add a single header multiple
    /// times. File an issue if this is a limitation for you.
    let setHeader (header : RequestHeader) (request : Request) =
      { request with headers = request.headers |> Map.put header.Key header }

    /// Adds an HTTP Basic Authentication header, which includes the username and password encoded as a base-64 string
    let basicAuthentication username password =
      setHeader (basicAuthorz username password)

    /// Adds a credential cache to support NTLM authentication
    let withNTLMAuthentication username password (request : Request) =
      {request with networkCredentials = Some (Credentials.Custom { username = username; password = password}) }

    /// Sets the accept-encoding request header to accept the decompression methods selected,
    /// and automatically decompresses the responses.
    ///
    /// Multiple schemes can be OR'd together, e.g. (DecompressionScheme.Deflate ||| DecompressionScheme.GZip)
    let autoDecompression decompressionSchemes request =
      { request with autoDecompression = decompressionSchemes}

    /// Lets you set your own body - use the RequestBody type to build it up.
    let body body (request : Request) =
      { request with body = body }

    /// Sets the the request body, using UTF-8 character encoding.
    ///
    /// Only certain request types should have a body, e.g. Posts.
    let bodyString body (request : Request) =
      { request with body = BodyString body }

    /// Sets the request body, using the provided character encoding.
    let bodyStringEncoded body characterEncoding request =
      { request with body = BodyString body; bodyCharacterEncoding = characterEncoding }

    /// Adds the provided QueryString record onto the request URL.
    /// Multiple items can be appended, but only the last appended key/value with
    /// the same key as a previous key/value will be used.
    let queryStringItem (name : QueryStringName) (value : QueryStringValue) request =
      { request with queryStringItems = request.queryStringItems |> Map.put name value }

    /// Adds a cookie to the request
    /// The domain will be taken from the URL, and the path set to '/'.
    ///
    /// If your cookie appears not to be getting set, it could be because the response is a redirect,
    /// which (by default) will be followed automatically, but cookies will not be re-sent.
    let cookie cookie request =
      if not request.cookiesEnabled then failwithf "Cannot add cookie %A - cookies disabled" cookie.name
      { request with cookies = request.cookies |> Map.put cookie.name cookie }

    /// Decodes the response using the specified encoding, regardless of what the response specifies.
    ///
    /// If this is not set, response character encoding will be:
    ///  - taken from the response content-encoding header, if provided, otherwise
    ///  UTF8
    ///
    /// Many web pages define the character encoding in the HTML. This will not be used.
    let responseCharacterEncoding encoding request : Request =
      { request with responseCharacterEncoding = Some encoding }

    /// Sends the request via the provided proxy.
    ///
    /// If this is no set, the proxy settings from IE will be used, if available.
    let proxy proxy request =
      {request with proxy = Some proxy }

    /// Sets the keep-alive header.  Defaults to true.
    ///
    /// If true, Connection header also set to 'Keep-Alive'
    /// If false, Connection header also set to 'Close'
    ///
    /// NOTE: If true, headers only sent on first request.
    let keepAlive value request =
      { request with keepAlive = value }

    let timeout timeout request =
      { request with timeout = timeout }

    /// Note: this sends the request, reads the response, disposes it and its stream
    let responseAsString state req : Alt<Choice<string, Error>> =
      // Note: it won't be possible to cancel the reading of the potentially large response body
      getResponse state req ^=> function
      | Choice1Of2 resp ->
        job {
          use resp = resp 
          let! str = Response.readBodyAsString resp
          return Choice.create str
        }
      | Choice2Of2 err ->
        Job.result (Choice.createSnd err)

    /// Note: this sends the request, reads the response, disposes it and its stream
    let responseAsBytes state req =
      getResponse state req ^=> function
      | Choice1Of2 resp ->
        job {
          use resp = resp
          let! bytes = Response.readBodyAsBytes resp
          return Choice.create bytes
        }
      | Choice2Of2 err ->
        Job.result (Choice.createSnd err)

module Composition =

  open Client
  open System.Diagnostics

  type JobFunc<'a, 'b> = 'a -> Job<'b>

  module JobFunc =
    let map (f : 'b -> 'c) (func : JobFunc<'a, 'b>) : JobFunc<'a, 'c> =
      func >> Job.map f

    let mapLeft (f : 'a -> 'c) (func : JobFunc<'c, 'b>) : JobFunc<'a, 'b> =
      f >> func

  type JobFilter<'a, 'b, 'c, 'd> = JobFunc<'a, 'b> -> JobFunc<'c, 'd>
  type JobFilter<'a, 'b> = JobFilter<'a, 'b, 'a, 'b>
  type JobSink<'a> = JobFunc<'a, unit>

  let identify clientName : JobFilter<Request, Response> =
    fun service ->
      Request.setHeader (RequestHeader.UserAgent clientName)
      >> service

  let timerFilter (config : HttpFsConfig) : JobFilter<Request, Response> =
    fun func req -> job {
      let sw = Stopwatch.StartNew()
      let! res = func req
      sw.Stop()
      Message.gauge sw.ElapsedMilliseconds "ms" |> config.logger.logSimple
      return res
    }

  let codecFilter (enc, dec) : JobFilter<'i, 'o, Request, Response> =
    JobFunc.mapLeft enc
    >> JobFunc.map dec