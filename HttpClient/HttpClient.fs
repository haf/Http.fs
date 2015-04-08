module HttpClient

#nowarn "25"

open System
open System.IO
open System.Net
open System.Text
open System.Web
open System.Security.Cryptography
open Microsoft.FSharp.Control
open Microsoft.FSharp.Control.CommonExtensions
open Microsoft.FSharp.Control.WebExtensions

[<Measure>] type ms

type HttpMethod = Options | Get | Head | Post | Put | Delete | Trace | Patch | Connect

// Same as System.Net.DecompressionMethods, but I didn't want to expose that
type DecompressionScheme = 
    | None = 0
    | GZip = 1
    | Deflate = 2


type NameValue = {
    name:string
    value:string
}

type ContentRange = {
    start : int64
    finish : int64
}

type ContentType = {
    typ      : string
    subtype  : string
    charset  : Encoding option
    boundary : string option
}
with
    member x.Equals(typ : string, subtype : string) =
        x.typ = typ && x.subtype = subtype

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

    static member Create(typ : string, subtype : string, ?charset : Encoding, ?boundary : string) =
        { typ = typ
          subtype = subtype
          charset = charset
          boundary = boundary
        }

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
    | Custom of NameValue

type UserDetails = {
    username : string
    password : string
}

[<RequireQualifiedAccess>]
type ProxyCredentials =
    | None
    | Default
    | Custom of UserDetails

type Proxy = { 
    Address: string
    Port: int
    Credentials: ProxyCredentials 
}

/// http://www.w3.org/Protocols/rfc2616/rfc2616-sec19.html
/// section 19.5.1 Content-Disposition, BNF.
type ContentDisposition = {
    typ      : string // "form-data" or "attachment"
    filename : string option
    /// e.g. "name=user_name"
    exts     : NameValue list
}

/// The key you have in &lt;input name="key" ... /&gt;
type FormEntryName = string

/// An optional file name
type FileName = string

type FileData =
    /// beware of newline CRLF encoding issues;
    /// see http://www.w3.org/Protocols/rfc1341/7_2_Multipart.html, starting from
    /// "NOTE: The CRLF preceding the encapsulation line",
    /// and file an issue  with samples,
    /// if it's very important for you to post text/plain as Plain; otherwise
    /// use Binary and read the file contents, to be sure they are not altered.
    | Plain of string
    | Binary of byte []

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
    | NameValue of NameValue

/// You often pass form-data to the server, e.g. curl -X POST <url> -F k=v -F file1=@file.png
type Form = FormData list

type RequestBody =
    | BodyForm of Form // * TransferEncodingHint option (7bit/8bit/binary)
    | BodyString of string
    | BodyRaw of byte []
    //| BodySocket of SocketTask // for all the nitty-gritty details, see #64

type Request = {
    Url: string
    Method: HttpMethod
    CookiesEnabled: bool
    AutoFollowRedirects: bool
    AutoDecompression: DecompressionScheme
    Headers: RequestHeader list
    Body: RequestBody
    BodyCharacterEncoding: Encoding
    QueryStringItems: NameValue list
    Cookies: NameValue list
    ResponseCharacterEncoding: Encoding option
    Proxy: Proxy option
    KeepAlive: bool
    Timeout: int<ms>
}

type Response = {
    StatusCode: int
    EntityBody: string option
    ContentLength: int64
    Cookies: Map<string, string>
    Headers: Map<ResponseHeader, string>
    /// A Uri that contains the URI of the Internet resource that responded to the request.
    /// <see cref="https://msdn.microsoft.com/en-us/library/system.net.httpwebresponse.responseuri%28v=vs.110%29.aspx"/>.
    ResponseUri : System.Uri
}
with
    override x.ToString() =
        seq {
            yield x.StatusCode.ToString()
            for h in x.Headers do
                yield h.ToString()
            yield ""
            if x.EntityBody |> Option.isSome then
                 yield x.EntityBody |> Option.get
        } |> String.concat Environment.NewLine

type HttpClientState = {
    random      : Random
    cryptRandom : RandomNumberGenerator
}

/// Will re-generate random CLR per-app-domain -- create your own state for
/// deterministic boundary generation (or anything else needing random).
let DefaultHttpClientState = {
    random      = Random()
    cryptRandom = RandomNumberGenerator.Create()
}

module internal String =
    let toLowerInvariant (s : string) =
        s.ToLowerInvariant()

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
    let uriEncode byteEncoding =
        List.map (fun kv ->
            String.Concat [
                HttpUtility.UrlEncode (kv.name, byteEncoding)
                "="
                HttpUtility.UrlEncode (kv.value, byteEncoding)
            ])
        >> String.concat "&"

    let getQueryString byteEncoding request =
        match request.QueryStringItems with
        | [] -> ""
        | items -> String.Concat [ "?"; uriEncode byteEncoding items ]

    // Adds an element to a list which may be none
    let append item = function
        | [] -> [ item ]
        | existingList -> existingList @ [ item ]

    /// Checks if a header already exists in a list
    /// (standard headers just checks type, custom headers also checks 'name' field).
    let headerExists header =
        List.exists (fun existingHeader -> 
            match existingHeader, header with
            | Custom { name = existingName; value = existingValue },
              Custom { name = newName; value = newValue } ->
                existingName = newName
            | _ ->
                existingHeader.GetType() = header.GetType())

    let putHeader predicate header headers =
         // warning is wrong; by lemma excluded middle (predicate h xor not predicate h)
        let rec replaceHeaderInner = function
            | [] ->
                header :: headers
            | h :: hs when predicate h ->
                header :: hs
            | h :: hs when not (predicate h) ->
                h :: replaceHeaderInner hs
        replaceHeaderInner headers

    // Adds a header to the collection as long as it isn't already in it
    let appendHeaderNoRepeat newHeader headerList =
        match headerList with
        | [] -> [newHeader]
        | existingList -> 
            if existingList |> headerExists newHeader then
                raise (DuplicateHeader newHeader)
            existingList @ [newHeader]

    let basicAuthorz username password =
        String.Concat [ username; ":"; password ]
        // TODO: consider https://github.com/relentless/Http.fs/issues/73
        |> ISOLatin1.GetBytes
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

    let generateFileData (encoding : Encoding) contentType contents = seq {
        match contentType, contents with
        | { typ = "text"; subtype = "plain" }, Plain text ->
            yield ""
            yield text
        | _, Plain text ->
            yield "Content-Transfer-Encoding: base64"
            yield ""
            yield text |> encoding.GetBytes |> Convert.ToBase64String
        | _, Binary bytes ->
            yield "Content-Transfer-Encoding: base64"
            yield ""
            yield bytes |> Convert.ToBase64String
    }

    let generateContentDispos value (kvs : (string * string) list) =
        let formatKv = function
            | (k, v) -> k + "=" + "\"" + escapeQuotes v + "\""
        String.concat "; " [ yield "Content-Disposition: " + value
                             yield! (kvs |> List.map formatKv)
                           ]

    let generateFormData state (encoding : Encoding) boundary formData =
        let rec generateFormDataInner boundary values isMultiFile = seq {
            match values with
            | [] ->
                yield "--" + boundary + "--"
                if not isMultiFile then yield CRLF
            | h :: rest ->
                yield "--" + boundary
                match h with
                | FormFile (name, (fileName, contentType, contents)) ->
                    let dispos = if isMultiFile then "file" else "form-data"
                    yield generateContentDispos dispos [
                        if not isMultiFile then yield "name", name
                        yield "filename", fileName
                    ]
                    yield sprintf "Content-Type: %O" contentType
                    yield! generateFileData encoding contentType contents

                | MultipartMixed (name, files) ->
                    let boundary' = generateBoundary state
                    yield "Content-Type: multipart/mixed; boundary=" + boundary'
                    yield generateContentDispos "form-data" [
                        "name", name
                    ]
                    yield ""
                    // remap the multi-files to single files and recursively call myself
                    let files' = files |> List.map (fun f -> FormFile (name, f))
                    yield! generateFormDataInner boundary' files' true

                | NameValue { name = name; value = value } ->
                    yield "Content-Disposition: form-data; name=\"" + escapeQuotes name + "\""
                    yield ""
                    yield value
                yield! generateFormDataInner boundary rest isMultiFile
        }
        generateFormDataInner boundary formData false

    let private formatBodyUrlencoded bodyEncoding formData =
        formData
        |> List.map (function
            | NameValue kv -> kv
            | x -> failwith "programming error: expected all formData to be NameValue as per 'formatBody'.")
        |> uriEncode bodyEncoding
        // after URI encoding, we represent all bytes in ASCII (subset of Latin1)
        // and none-the-less; they will map 1-1 with the UTF8 set if the server
        // interpret Content-Type: ...; charset=utf8 as 'raw bytes' of the body.
        |> ISOLatin1.GetBytes

    let private formatBodyFormData clientState encoding formData boundary =
        generateFormData clientState encoding boundary formData
        |> String.concat CRLF
        |> encoding.GetBytes

    let formatBody (clientState : HttpClientState)
                   // we may actually change the content type if it's wrong
                   : ContentType option * Encoding * RequestBody -> ContentType option * byte [] =
        function
        | userCt, _, BodyRaw raw ->
            userCt, raw

        | userCt, encoding, BodyString str ->
            userCt, encoding.GetBytes str

        | userCt, _, BodyForm [] ->
            userCt, [||]

        | userCt, encoding, BodyForm formData ->
            let onlyNameValues =
                formData |> List.forall (function | NameValue _ -> true | _ -> false)

            if onlyNameValues then
                ContentType.Parse "application/x-www-form-urlencoded",
                formatBodyUrlencoded encoding formData
            else
                let boundary = generateBoundary clientState
                ContentType.Create("multipart", "form-data", boundary=boundary) |> Some,
                formatBodyFormData clientState encoding formData boundary

open Impl

/// <summary>Creates the Request record which can be used to make an HTTP request</summary>
/// <param name="httpMethod">The type of request to be made (Get, Post, etc.)</param>
/// <param name="url">The URL of the resource including protocol, e.g. 'http://www.relentlessdevelopment.net'</param>
/// <returns>The Request record</returns>
let createRequest httpMethod url = {
    Url                       = url
    Method                    = httpMethod
    CookiesEnabled            = true
    AutoFollowRedirects       = true
    AutoDecompression         = DecompressionScheme.None
    Headers                   = []
    Body                      = BodyRaw [||]
    BodyCharacterEncoding     = Encoding.UTF8
    QueryStringItems          = []
    Cookies                   = []
    ResponseCharacterEncoding = None
    Proxy = None
    KeepAlive = true
    /// The default value is 100,000 milliseconds (100 seconds).
    /// <see cref="https://msdn.microsoft.com/en-us/library/system.net.httpwebrequest.timeout%28v=vs.110%29.aspx"/>.
    Timeout = 100000<ms>
}

/// Disables cookies, which are enabled by default
let withCookiesDisabled request = 
    { request with CookiesEnabled = false }

/// Disables automatic following of redirects, which is enabled by default
let withAutoFollowRedirectsDisabled request = 
    { request with AutoFollowRedirects = false }

/// Adds a header, defined as a RequestHeader
let withHeader header (request:Request) =
    { request with Headers = request.Headers |> appendHeaderNoRepeat header }

/// Adds an HTTP Basic Authentication header, which includes the username and password encoded as a base-64 string
let withBasicAuthentication username password (request:Request) =
    let header = basicAuthorz username password
    { request with Headers = request.Headers |> appendHeaderNoRepeat header }

/// Sets the accept-encoding request header to accept the decompression methods selected,
/// and automatically decompresses the responses.
///
/// Multiple schemes can be OR'd together, e.g. (DecompressionScheme.Deflate ||| DecompressionScheme.GZip)
let withAutoDecompression decompressionSchemes request =
    { request with AutoDecompression = decompressionSchemes}

/// Lets you set your own body - use the RequestBody type to build it up.
let withBody body request =
    { request with Body = body }

/// Sets the the request body, using UTF-8 character encoding.
///
/// Only certain request types should have a body, e.g. Posts.
let withBodyString body request =
    { request with Body = BodyString body }

/// Sets the request body, using the provided character encoding.
let withBodyStringEncoded body characterEncoding request =
    { request with Body = BodyString body; BodyCharacterEncoding = characterEncoding }

/// Adds the provided QueryString record onto the request URL.
/// Multiple items can be appended.
let withQueryStringItem item request =
    { request with QueryStringItems = request.QueryStringItems |> append item}

/// Adds a cookie to the request
/// The domain will be taken from the URL, and the path set to '/'.
///
/// If your cookie appears not to be getting set, it could be because the response is a redirect,
/// which (by default) will be followed automatically, but cookies will not be re-sent.
let withCookie cookie request =
    if not request.CookiesEnabled then failwithf "Cannot add cookie %A - cookies disabled" cookie.name
    { request with Cookies = request.Cookies |> append cookie }

/// Decodes the response using the specified encoding, regardless of what the response specifies.
///
/// If this is not set, response character encoding will be:
///  - taken from the response content-encoding header, if provided, otherwise
///  - ISO Latin 1
///
/// Many web pages define the character encoding in the HTML. This will not be used.
let withResponseCharacterEncoding encoding request : Request = 
    { request with ResponseCharacterEncoding = Some encoding }

/// Sends the request via the provided proxy.
///
/// If this is no set, the proxy settings from IE will be used, if available.
let withProxy proxy request =
    {request with Proxy = Some proxy}

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
        headers
        |> List.iter (fun header ->
            match header with
            | Accept(value) -> webRequest.Accept <- value
            | AcceptCharset(value) -> webRequest.Headers.Add("Accept-Charset", value)
            | AcceptDatetime(value) -> webRequest.Headers.Add("Accept-Datetime", value)
            | AcceptLanguage(value) -> webRequest.Headers.Add("Accept-Language", value)
            | Authorization(value) -> webRequest.Headers.Add("Authorization", value)
            | RequestHeader.Connection(value) -> webRequest.Connection <- value
            | RequestHeader.ContentMD5(value) -> webRequest.Headers.Add("Content-MD5", value)
            | RequestHeader.ContentType(value) -> webRequest.ContentType <- value.ToString()
            | RequestHeader.Date(value) -> webRequest.Date <- value
            | Expect(value) -> webRequest.Expect <- value.ToString()
            | From(value) -> webRequest.Headers.Add("From", value)
            | IfMatch(value) -> webRequest.Headers.Add("If-Match", value)
            | IfModifiedSince(value) -> webRequest.IfModifiedSince <- value
            | IfNoneMatch(value) -> webRequest.Headers.Add("If-None-Match", value)
            | IfRange(value) -> webRequest.Headers.Add("If-Range", value)
            | MaxForwards(value) -> webRequest.Headers.Add("Max-Forwards", value.ToString())
            | Origin(value) -> webRequest.Headers.Add("Origin", value)
            | RequestHeader.Pragma(value) -> webRequest.Headers.Add("Pragma", value)
            | ProxyAuthorization(value) -> webRequest.Headers.Add("Proxy-Authorization", value)
            | Range(value) -> webRequest.AddRange(value.start, value.finish)
            | Referer(value) -> webRequest.Referer <- value
            | Upgrade(value) -> webRequest.Headers.Add("Upgrade", value)
            | UserAgent(value) -> webRequest.UserAgent <- value
            | RequestHeader.Via(value) -> webRequest.Headers.Add("Via", value)
            | RequestHeader.Warning(value) -> webRequest.Headers.Add("Warning", value)
            | Custom( {name=customName; value=customValue}) -> webRequest.Headers.Add(customName, customValue))

    /// Sets cookies on HttpWebRequest.
    /// Mutates HttpWebRequest.
    let setCookies (cookies:NameValue list) url (webRequest:HttpWebRequest) =
        let domain = Uri(url).Host
        cookies
        |> List.iter (fun cookie ->
            webRequest.CookieContainer.Add(new System.Net.Cookie(cookie.name, cookie.value, Path="/", Domain=domain)))

    /// Sets proxy on HttpWebRequest.
    /// Mutates HttpWebRequest.
    let setProxy proxy (webRequest : HttpWebRequest) =
        proxy |> Option.iter (fun proxy ->
            let webProxy = WebProxy(proxy.Address, proxy.Port)

            match proxy.Credentials with
            | ProxyCredentials.Custom { username = name; password = pwd} -> 
                webProxy.Credentials <- NetworkCredential(name, pwd)
            | ProxyCredentials.Default -> webProxy.UseDefaultCredentials <- true
            | ProxyCredentials.None -> webProxy.Credentials <- null

            webRequest.Proxy <- webProxy)

    /// Sets body on HttpWebRequest.
    /// Mutates HttpWebRequest.
    let setBody state body (webRequest : HttpWebRequest) =
        match body with
        | [||] ->
            ()
        | bodyBytes ->
            // Getting the request stream seems to be actually connecting to the internet in some way
            use requestStream = webRequest.GetRequestStream()
            // TODO: expose async body
            requestStream.AsyncWrite(bodyBytes, 0, bodyBytes.Length) |> Async.RunSynchronously

    let matchCtHeader = function
        | RequestHeader.ContentType _ -> true
        | _ -> false

    /// The nasty business of turning a Request into an HttpWebRequest
    let toHttpWebRequest state (request : Request) =
        let contentType =
            request.Headers
            |> List.tryFind matchCtHeader
            |> function | Some (ContentType value) -> Some value | _ -> None

        let contentEncoding =
            // default the ContentType charset encoding, otherwise, use BodyCharacterEncoding.
            contentType
            |> function
            | Some { charset = Some enc } -> Some enc
            | _ -> None
            |> Option.fold (fun s t -> t) request.BodyCharacterEncoding

        let url =
            request.Url + (request |> getQueryString contentEncoding)

        let webRequest =
            HttpWebRequest.Create(url) :?> HttpWebRequest

        let newContentType, body =
            formatBody state (contentType, contentEncoding, request.Body)

        let request =
            // if we have a new content type, from using BodyForm, then this
            // updates the request value with that header
            newContentType
            |> Option.fold (fun (req : Request) newCt ->
                let header = RequestHeader.ContentType newCt
                { req with Headers = req.Headers |> putHeader matchCtHeader header })
                request

        webRequest.Method <- (request |> getMethodAsString)
        webRequest.ProtocolVersion <- HttpVersion.Version11

        if request.CookiesEnabled then
            webRequest.CookieContainer <- CookieContainer()

        webRequest.AllowAutoRedirect <- request.AutoFollowRedirects

        // this relies on the DecompressionScheme enum values being the same as those in System.Net.DecompressionMethods
        webRequest.AutomaticDecompression <- enum<DecompressionMethods> <| int request.AutoDecompression

        webRequest |> setHeaders request.Headers
        webRequest |> setCookies request.Cookies request.Url
        webRequest |> setProxy request.Proxy

        webRequest |> setBody state body

        webRequest.KeepAlive <- request.KeepAlive
        webRequest.Timeout <- (int)request.Timeout

        webRequest

    /// Uses the HttpWebRequest to get the response.
    /// HttpWebRequest throws an exception on anything but a 200-level response,
    /// so we handle such exceptions and return the response.
    let getResponseNoException (request:HttpWebRequest) = async {
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

    /// Get the header as a ResponseHeader option.  Is an option because there are some headers we don't want to set.
    let getResponseHeader headerName =
        match headerName with
        | null -> None
        | "Access-Control-Allow-Origin" -> Some(AccessControlAllowOrigin)
        | "Accept-Ranges" -> Some(AcceptRanges)
        | "Age" -> Some(Age)
        | "Allow" -> Some(Allow)
        | "Cache-Control" -> Some(CacheControl)
        | "Connection" -> Some(ResponseHeader.Connection)
        | "Content-Encoding" -> Some(ContentEncoding)
        | "Content-Language" -> Some(ContentLanguage)
        | "Content-Length" -> None
        | "Content-Location" -> Some(ContentLocation)
        | "Content-MD5" -> Some(ResponseHeader.ContentMD5Response)
        | "Content-Disposition" -> Some(ContentDisposition)
        | "Content-Range" -> Some(ContentRange)
        | "Content-Type" -> Some(ResponseHeader.ContentTypeResponse)
        | "Date" -> Some(ResponseHeader.DateResponse)
        | "ETag" -> Some(ETag)
        | "Expires" -> Some(Expires)
        | "Last-Modified" -> Some(LastModified)
        | "Link" -> Some(Link)
        | "Location" -> Some(Location)
        | "P3P" -> Some(P3P)
        | "Pragma" -> Some(ResponseHeader.PragmaResponse)
        | "Proxy-Authenticate" -> Some(ProxyAuthenticate)
        | "Refresh" -> Some(Refresh)
        | "Retry-After" -> Some(RetryAfter)
        | "Server" -> Some(Server)
        | "Set-Cookie" -> None
        | "Strict-Transport-Security" -> Some(StrictTransportSecurity)
        | "Trailer" -> Some(Trailer)
        | "Transfer-Encoding" -> Some(TransferEncoding)
        | "Vary" -> Some(Vary)
        | "Via" -> Some(ResponseHeader.ViaResponse)
        | "Warning" -> Some(ResponseHeader.WarningResponse)
        | "WWW-Authenticate" -> Some(WWWAuthenticate)
        | _ -> Some(NonStandard headerName)

    /// Gets the headers from the passed response as a map of ResponseHeader and string.
    let getHeadersAsMap (response:HttpWebResponse) =
        // TODO: Find a better way of dong this
        let headerArray = Array.zeroCreate response.Headers.Count
        for index = 0 to response.Headers.Count-1 do
            headerArray.[index] <- 
                match getResponseHeader response.Headers.Keys.[index] with
                | Some(headerKey) -> Some((headerKey, response.Headers.Item(response.Headers.Keys.[index])))
                | None -> None
        headerArray
        |> Array.filter (fun item -> item <> None)
        |> Array.map Option.get
        |> Map.ofArray

    let mapEncoding = String.toLowerInvariant >> function
        | "utf8" -> "utf-8"
        | "utf16" -> "utf-16"
        | other -> other

    let readBody encoding (response:HttpWebResponse) = async {
        let charset = 
            match encoding with
            | None ->
                match response.CharacterSet with
                | null | "" -> ISOLatin1
                | responseCharset -> Encoding.GetEncoding(responseCharset |> mapEncoding)
            | Some enc -> enc
        use responseStream = new AsyncStreamReader(response.GetResponseStream(),charset)
        let! body = responseStream.ReadToEnd()
        return body
    }

    let readAsRaw (response:HttpWebResponse) = async {
        use ms = new MemoryStream()
        do! response.GetResponseStream().CopyToAsync(ms) |> Async.AwaitIAsyncResult |> Async.Ignore
        return ms.ToArray()
    }

open DotNetWrapper

/// Sends the HTTP request and returns the response code as an integer, asynchronously.
let getResponseCodeAsync request = async {
    use! response = request |> toHttpWebRequest DefaultHttpClientState |> getResponseNoException
    return response.StatusCode |> int
}

/// Sends the HTTP request and returns the response code as an integer.
let getResponseCode request =
    getResponseCodeAsync request |> Async.RunSynchronously

/// Sends the HTTP request and returns the response body as a string, asynchronously.
///
/// Gives an empty string if there's no response body.
let getResponseBodyAsync request = async {
    use! response = request |> toHttpWebRequest DefaultHttpClientState |> getResponseNoException
    let! body = response |> readBody request.ResponseCharacterEncoding
    return body
}

/// Sends the HTTP request and returns the response body as raw bytes, asynchronously.
///
/// Gives an empty array if there's no response body.
let getResponseBytesAsync request = async {
    use! response = request |> toHttpWebRequest DefaultHttpClientState |> getResponseNoException
    let! raw = response |> readAsRaw
    return raw
}

/// Sends the HTTP request and returns the response body as raw bytes.
///
/// Gives an empty array if there's no response body.
let getResponseBytes request = 
    getResponseBytesAsync request |> Async.RunSynchronously

/// Sends the HTTP request and returns the response body as a string.
///
/// Gives an empty string if there's no response body.
let getResponseBody request =
    getResponseBodyAsync request |> Async.RunSynchronously

/// Sends the HTTP request and returns the full response as a Response record, asynchronously.
let getResponseAsync request = async {
    use! response = request |> toHttpWebRequest DefaultHttpClientState |> getResponseNoException

    let code = response.StatusCode |> int
    let! body = response |> readBody request.ResponseCharacterEncoding

    let cookies = response |> getCookiesAsMap
    let headers = response |> getHeadersAsMap

    let entityBody = 
        match body.Length > 0 with
        | true -> Some(body)
        | false -> None

    return {
        StatusCode = code
        EntityBody = entityBody
        ContentLength = response.ContentLength
        Cookies = cookies
        Headers = headers
        ResponseUri = response.ResponseUri
    }
}

/// Sends the HTTP request and returns the full response as a Response record.
let getResponse request =
    getResponseAsync request |> Async.RunSynchronously

/// Passes the response stream to the passed consumer function.
/// Useful if accessing a large file, as won't copy to memory.
///
/// The response stream will be closed automatically, do not access it outside the function scope.
let getResponseStream streamConsumer request =
    use response = request |> toHttpWebRequest DefaultHttpClientState |> getResponseNoException |> Async.RunSynchronously
    use responseStream = response.GetResponseStream()
    streamConsumer responseStream
