module HttpClient

open System
open System.IO
open System.Net
open System.Text
open System.Web
open Microsoft.FSharp.Control
open Microsoft.FSharp.Control.CommonExtensions
open Microsoft.FSharp.Control.WebExtensions

type HttpMethod = Options | Get | Head | Post | Put | Delete | Trace | Connect

// Same as System.Net.DecompressionMethods, but I didn't want to expose that
type DecompressionScheme = 
    | None = 0
    | GZip = 1
    | Deflate = 2

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
    | ContentMD5 
    | ContentDisposition 
    | ContentRange 
    | ContentType 
    | Date 
    | ETag 
    | Expires 
    | LastModified 
    | Link 
    | Location 
    | P3P 
    | Pragma 
    | ProxyAuthenticate 
    | Refresh 
    | RetryAfter 
    | Server 
    | StrictTransportSecurity 
    | Trailer 
    | TransferEncoding 
    | Vary 
    | Via 
    | Warning 
    | WWWAuthenticate 
    | NonStandard of string

// short name for qualified access (needed as some request & response
// headers have the same name)
type Resp = ResponseHeader 

// some headers can't be set with HttpWebRequest, or are set automatically, so are not included.
// others, such as transfer-encoding, just haven't been implemented.
type RequestHeader =
    // TODO: Decide what to do about request & response headers sometimes having the same names
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

type Request = {
    Url: string
    Method: HttpMethod
    CookiesEnabled: bool
    AutoFollowRedirects: bool
    AutoDecompression: DecompressionScheme
    Headers: RequestHeader list option
    Body: string option
    QueryStringItems: NameValue list option
    Cookies: NameValue list option
}

type Response = {
    StatusCode: int
    EntityBody: string option
    ContentLength: int64
    Cookies: Map<string,string>
    Headers: Map<ResponseHeader,string>
}

let private getMethodAsString request =
    match request.Method with
        | Options -> "OPTIONS"
        | Get -> "GET"
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

let private setCookies (cookies:NameValue list option) url (webRequest:HttpWebRequest) =
    if cookies.IsSome then
        let domain = Uri(url).Host
        cookies.Value
        |> List.iter (fun cookie ->
            webRequest.CookieContainer.Add(new System.Net.Cookie(cookie.name, cookie.value, Path="/", Domain=domain)))

// adds an element to a list which may be none
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

let createRequest httpMethod url = {
    Url = url; 
    Method = httpMethod;
    CookiesEnabled = true;
    AutoFollowRedirects = true;
    AutoDecompression = DecompressionScheme.None;
    Headers = None; 
    Body = None;
    QueryStringItems = None;
    Cookies = None;
    }

let withCookiesDisabled request = 
    {request with CookiesEnabled = false }

let withAutoFollowRedirectsDisabled request = 
    {request with AutoFollowRedirects = false }

let withHeader header (request:Request) =
    {request with Headers = request.Headers |> appendHeaderNoRepeat header}

let withBasicAuthentication username password (request:Request) =
    let authHeader = Authorization ("Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(username + ":" + password)))
    {request with Headers = request.Headers |> appendHeaderNoRepeat authHeader}

let withAutoDecompression decompressionSchemes request =
    {request with AutoDecompression = decompressionSchemes}

let withBody body request =
    {request with Body = Some(body)}

let withQueryStringItem item request =
    {request with QueryStringItems = request.QueryStringItems |> append item}

let withCookie cookie request =
    if not request.CookiesEnabled then failwithf "Cannot add cookie %A - cookies disabled" cookie.name
    {request with Cookies = request.Cookies |> append cookie}

let private toHttpWebRequest request =

    let url = request.Url + (request |> getQueryString)
    let webRequest = HttpWebRequest.Create(url) :?> HttpWebRequest

    webRequest.Method <- request |> getMethodAsString
    webRequest.ProtocolVersion <- HttpVersion.Version11

    if request.CookiesEnabled then
        webRequest.CookieContainer <- CookieContainer()

    webRequest.AllowAutoRedirect <- request.AutoFollowRedirects

    // this relies on the DecompressionScheme enum values being the same as those in System.Net.DecompressionMethods
    webRequest.AutomaticDecompression <- enum<DecompressionMethods> <| int request.AutoDecompression

    webRequest |> setHeaders request.Headers
    webRequest |> setCookies request.Cookies request.Url

    webRequest.KeepAlive <- true

    if request.Body.IsSome then
        // TODO: Allow other encodings
        let bodyBytes = Encoding.GetEncoding(1252).GetBytes(request.Body.Value)
        // Getting the request stream seems to be actually connecting to the internet in some way
        use requestStream = webRequest.GetRequestStream() 
        requestStream.AsyncWrite(bodyBytes, 0, bodyBytes.Length) |> Async.RunSynchronously
        
    webRequest

// Uses the HttpWebRequest to get the response.
// HttpWebRequest throws an exception on anything but a 200-level response,
// so we handle such exceptions and return the response.
let private getResponseNoException (request:HttpWebRequest) = async {
    try
        let! response = request.AsyncGetResponse() 
        return response :?> HttpWebResponse
    with
        | :? WebException as wex -> if wex.Response <> null then 
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
    | "Content-MD5" -> Some(ResponseHeader.ContentMD5)
    | "Content-Disposition" -> Some(ContentDisposition)
    | "Content-Range" -> Some(ContentRange)
    | "Content-Type" -> Some(ResponseHeader.ContentType)
    | "Date" -> Some(ResponseHeader.Date)
    | "ETag" -> Some(ETag)
    | "Expires" -> Some(Expires)
    | "Last-Modified" -> Some(LastModified)
    | "Link" -> Some(Link)
    | "Location" -> Some(Location)
    | "P3P" -> Some(P3P)
    | "Pragma" -> Some(ResponseHeader.Pragma)
    | "Proxy-Authenticate" -> Some(ProxyAuthenticate)
    | "Refresh" -> Some(Refresh)
    | "Retry-After" -> Some(RetryAfter)
    | "Server" -> Some(Server)
    | "Set-Cookie" -> None
    | "Strict-Transport-Security" -> Some(StrictTransportSecurity)
    | "Trailer" -> Some(Trailer)
    | "Transfer-Encoding" -> Some(TransferEncoding)
    | "Vary" -> Some(Vary)
    | "Via" -> Some(ResponseHeader.Via)
    | "Warning" -> Some(ResponseHeader.Warning)
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

let private readBody (response:HttpWebResponse) = async {
    let encoding = Encoding.GetEncoding(response.CharacterSet)

    use responseStream = new AsyncStreamReader(response.GetResponseStream(),encoding)
    let! body = responseStream.ReadToEnd()
    return body
}

let getResponseCodeAsync request = async {
    use! response = request |> toHttpWebRequest |> getResponseNoException
    return response.StatusCode |> int
}

let getResponseCode request =
    getResponseCodeAsync request |> Async.RunSynchronously

let getResponseBodyAsync request = async {
    use! response = request |> toHttpWebRequest |> getResponseNoException
    let! body = response |> readBody
    return body
}

let getResponseBody request =
    getResponseBodyAsync request |> Async.RunSynchronously

let getResponseAsync request = async {
    use! response = request |> toHttpWebRequest |> getResponseNoException

    let code = response.StatusCode |> int
    let! body = response |> readBody

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

let getResponse request =
    getResponseAsync request |> Async.RunSynchronously