module HttpClient

open System
open System.IO
open System.Net
open System.Text
open System.Web
open Microsoft.FSharp.Control
open Microsoft.FSharp.Control.CommonExtensions
open Microsoft.FSharp.Control.WebExtensions

let private ISO_Latin_1 = "ISO-8859-1"

type HttpMethod = Options | Get | Head | Post | Put | Delete | Trace | Patch | Connect

// Same as System.Net.DecompressionMethods, but I didn't want to expose that
type DecompressionScheme = 
    | None = 0
    | GZip = 1
    | Deflate = 2

// Defines mappings between encodings which might be specified to the names
// which work with the .net encoder
let private responseEncodingMappings =
    Map.empty
        .Add("utf8", "utf-8")
        .Add("utf16", "utf-16")

type NameValue = { name:string; value:string }
type ContentRange = {start:int64; finish:int64 }

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
    | ContentType of string
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

type UserDetails = { username:string; password:string }

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

type Request = {
    Url: string
    Method: HttpMethod
    CookiesEnabled: bool
    AutoFollowRedirects: bool
    AutoDecompression: DecompressionScheme
    Headers: RequestHeader list option
    Body: string option
    BodyCharacterEncoding: string option
    QueryStringItems: NameValue list option
    Cookies: NameValue list option
    ResponseCharacterEncoding: string option
    Proxy: Proxy option
    KeepAlive: bool
}

type Response = {
    StatusCode: int
    EntityBody: string option
    ContentLength: int64
    Cookies: Map<string,string>
    Headers: Map<ResponseHeader,string>
}

/// <summary>Creates the Request record which can be used to make an HTTP request</summary>
/// <param name="httpMethod">The type of request to be made (Get, Post, etc.)</param>
/// <param name="url">The URL of the resource including protocol, e.g. 'http://www.relentlessdevelopment.net'</param>
/// <returns>The Request record</returns>
let createRequest httpMethod url = {
    Url = url; 
    Method = httpMethod;
    CookiesEnabled = true;
    AutoFollowRedirects = true;
    AutoDecompression = DecompressionScheme.None;
    Headers = None; 
    Body = None;
    BodyCharacterEncoding = None;
    QueryStringItems = None;
    Cookies = None;
    ResponseCharacterEncoding = None;
    Proxy = None;
    KeepAlive = true;
}

// Adds an element to a list which may be none
let private append item listOption =
    match listOption with
    | None -> Some([item])
    | Some(existingList) -> Some(existingList@[item])

// Checks if a header already exists in a list
// (standard headers just checks type, custom headers also checks 'name' field).
let private headerExists header headerList =
    headerList
    |> List.exists (
            fun existingHeader -> 
                match existingHeader, header with
                | Custom {name = existingName; value = existingValue },
                  Custom {name = newName; value = newValue } -> existingName = newName
                | _ -> existingHeader.GetType() = header.GetType())

// Adds a header to the collection as long as it isn't already in it
let private appendHeaderNoRepeat newHeader headerList =
    match headerList with
    | None -> Some([newHeader])
    | Some(existingList) -> 
        if existingList |> headerExists newHeader then
            failwithf "Header %A already exists" newHeader
        Some(existingList@[newHeader])

/// Disables cookies, which are enabled by default
let withCookiesDisabled request = 
    {request with CookiesEnabled = false }

/// Disables automatic following of redirects, which is enabled by default
let withAutoFollowRedirectsDisabled request = 
    {request with AutoFollowRedirects = false }

/// Adds a header, defined as a RequestHeader
let withHeader header (request:Request) =
    {request with Headers = request.Headers |> appendHeaderNoRepeat header}

/// Adds an HTTP Basic Authentication header, which includes the username and password encoded as a base-64 string
let withBasicAuthentication username password (request:Request) =
    let authHeader = Authorization ("Basic " + Convert.ToBase64String(Encoding.GetEncoding(ISO_Latin_1).GetBytes(username + ":" + password)))
    {request with Headers = request.Headers |> appendHeaderNoRepeat authHeader}

/// Sets the accept-encoding request header to accept the decompression methods selected,
/// and automatically decompresses the responses.
///
/// Multiple schemes can be OR'd together, e.g. (DecompressionScheme.Deflate ||| DecompressionScheme.GZip)
let withAutoDecompression decompressionSchemes request =
    {request with AutoDecompression = decompressionSchemes}

/// Sets the the request body, using ISO Latin 1 character encoding.
///
/// Only certain request types should have a body, e.g. Posts.
let withBody body request =
    {request with Body = Some(body); BodyCharacterEncoding = Some(ISO_Latin_1)}

/// Sets the request body, using the provided character encoding.
let withBodyEncoded body characterEncoding request =
    {request with Body = Some(body); BodyCharacterEncoding = Some(characterEncoding)}

/// Adds the provided QueryString record onto the request URL.
/// Multiple items can be appended.
let withQueryStringItem item request =
    {request with QueryStringItems = request.QueryStringItems |> append item}

/// Adds a cookie to the request
/// The domain will be taken from the URL, and the path set to '/'.
///
/// If your cookie appears not to be getting set, it could be because the response is a redirect,
/// which (by default) will be followed automatically, but cookies will not be re-sent.
let withCookie cookie request =
    if not request.CookiesEnabled then failwithf "Cannot add cookie %A - cookies disabled" cookie.name
    {request with Cookies = request.Cookies |> append cookie}

/// Decodes the response using the specified encoding, regardless of what the response specifies.
///
/// If this is not set, response character encoding will be:
///  - taken from the response content-encoding header, if provided, otherwise
///  - ISO Latin 1
///
/// Many web pages define the character encoding in the HTML. This will not be used.
let withResponseCharacterEncoding encoding request:Request = 
    {request with ResponseCharacterEncoding = Some(encoding)}
    
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
    {request with KeepAlive = value}

let private getMethodAsString request =
    match request.Method with
        | Options -> "Options"
        | Get -> "Get"
        | Head -> "HEAD"
        | Post -> "POST"
        | Put -> "PUT"
        | Delete -> "DELETE"
        | Trace -> "TRACE"
        | Connect -> "CONNECT"

let private getQueryString request = 
    match request.QueryStringItems.IsSome with
    | true -> request.QueryStringItems.Value 
                |> List.fold (
                    fun currentQueryString queryStringItem -> 
                        (if currentQueryString = "?" then currentQueryString else currentQueryString + "&" ) 
                        + HttpUtility.UrlEncode(queryStringItem.name)
                        + "=" 
                        + HttpUtility.UrlEncode(queryStringItem.value)) 
                    "?"
    | false -> ""

// Sets headers on HttpWebRequest.
// Mutates HttpWebRequest.
let private setHeaders (headers:RequestHeader list option) (webRequest:HttpWebRequest) =
    if headers.IsSome then
        headers.Value
        |> List.iter (fun header ->
            match header with
            | Accept(value) -> webRequest.Accept <- value
            | AcceptCharset(value) -> webRequest.Headers.Add("Accept-Charset", value)
            | AcceptDatetime(value) -> webRequest.Headers.Add("Accept-Datetime", value)
            | AcceptLanguage(value) -> webRequest.Headers.Add("Accept-Language", value)
            | Authorization(value) -> webRequest.Headers.Add("Authorization", value)
            | RequestHeader.Connection(value) -> webRequest.Connection <- value
            | RequestHeader.ContentMD5(value) -> webRequest.Headers.Add("Content-MD5", value)
            | RequestHeader.ContentType(value) -> webRequest.ContentType <- value
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

// Sets cookies on HttpWebRequest.
// Mutates HttpWebRequest.
let private setCookies (cookies:NameValue list option) url (webRequest:HttpWebRequest) =
    if cookies.IsSome then
        let domain = Uri(url).Host
        cookies.Value
        |> List.iter (fun cookie ->
            webRequest.CookieContainer.Add(new System.Net.Cookie(cookie.name, cookie.value, Path="/", Domain=domain)))

// Sets proxy on HttpWebRequest.
// Mutates HttpWebRequest.
let setProxy proxy (webRequest:HttpWebRequest) =
    proxy |> Option.iter (fun proxy ->
        let webProxy = WebProxy(proxy.Address, proxy.Port)

        match proxy.Credentials with
        | ProxyCredentials.Custom { username = name; password = pwd} -> 
            webProxy.Credentials <- NetworkCredential(name, pwd)
        | ProxyCredentials.Default -> webProxy.UseDefaultCredentials <- true
        | ProxyCredentials.None -> webProxy.Credentials <- null

        webRequest.Proxy <- webProxy)

// Sets body on HttpWebRequest.
// Mutates HttpWebRequest.
let setBody (body:string option) (encoding:string option) (webRequest:HttpWebRequest) =
    if body.IsSome then

        if encoding.IsNone then
            failwith "Body Character Encoding not set"

        let bodyBytes = Encoding.GetEncoding(encoding.Value).GetBytes(body.Value)

        // Getting the request stream seems to be actually connecting to the internet in some way
        use requestStream = webRequest.GetRequestStream() 
        requestStream.AsyncWrite(bodyBytes, 0, bodyBytes.Length) |> Async.RunSynchronously

// The nasty business of turning a Request into an HttpWebRequest
let private toHttpWebRequest request =

    let url = request.Url + (request |> getQueryString)
    let webRequest = HttpWebRequest.Create(url) :?> HttpWebRequest

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
    webRequest |> setBody request.Body request.BodyCharacterEncoding

    webRequest.KeepAlive <- request.KeepAlive

    webRequest

// Uses the HttpWebRequest to get the response.
// HttpWebRequest throws an exception on anything but a 200-level response,
// so we handle such exceptions and return the response.
let private getResponseNoException (request:HttpWebRequest) = async {
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

let private getCookiesAsMap (response:HttpWebResponse) = 
    let cookieArray = Array.zeroCreate response.Cookies.Count
    response.Cookies.CopyTo(cookieArray, 0)
    cookieArray |> Array.fold (fun map cookie -> map |> Map.add cookie.Name cookie.Value) Map.empty

// Get the header as a ResponseHeader option.  Is an option because there are some headers we don't want to set.
let private getResponseHeader headerName =
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

// Gets the headers from the passed response as a map of ResponseHeader and string.
let private getHeadersAsMap (response:HttpWebResponse) =
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

let private mapEncoding (encoding:string) =
    match responseEncodingMappings.TryFind(encoding.ToLower()) with
        | Some(mappedEncoding) -> mappedEncoding
        | None -> encoding

let private readBody encoding (response:HttpWebResponse) = async {
    let charset = 
        match encoding with
        | None -> 
            match response.CharacterSet with
            | null -> Encoding.GetEncoding(ISO_Latin_1)
            | responseCharset -> Encoding.GetEncoding(responseCharset |> mapEncoding)
        | Some(enc) -> Encoding.GetEncoding(enc:string)
    use responseStream = new AsyncStreamReader(response.GetResponseStream(),charset)
    let! body = responseStream.ReadToEnd()
    return body
}

let private readAsRaw (response:HttpWebResponse) = async {
        use ms = new MemoryStream()
        do! response.GetResponseStream().CopyToAsync(ms) |> Async.AwaitIAsyncResult |> Async.Ignore
        return ms.ToArray()
    }

/// Sends the HTTP request and returns the response code as an integer, asynchronously.
let getResponseCodeAsync request = async {
    use! response = request |> toHttpWebRequest |> getResponseNoException
    return response.StatusCode |> int
}

/// Sends the HTTP request and returns the response code as an integer.
let getResponseCode request =
    getResponseCodeAsync request |> Async.RunSynchronously

/// Sends the HTTP request and returns the response body as a string, asynchronously.
///
/// Gives an empty string if there's no response body.
let getResponseBodyAsync request = async {
    use! response = request |> toHttpWebRequest |> getResponseNoException
    let! body = response |> readBody request.ResponseCharacterEncoding
    return body
}

/// Sends the HTTP request and returns the response body as raw bytes, asynchronously.
///
/// Gives an empty array if there's no response body.
let getResponseBytesAsync request = async {
    use! response = request |> toHttpWebRequest |> getResponseNoException
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
    use! response = request |> toHttpWebRequest |> getResponseNoException

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
    use response = request |> toHttpWebRequest |> getResponseNoException |> Async.RunSynchronously
    use responseStream = response.GetResponseStream()
    streamConsumer (responseStream)
