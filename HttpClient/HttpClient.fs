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

type RequestHeader =
    | UserAgent of string
    | Accept of string
    | Referer of string
    | ContentType of string
    | AcceptLanguage of string
    | Custom of NameValue

type ResponseHeader =
    | ContentEncoding
    | NonStandard of string

type Request = {
    Url: string;
    Method: HttpMethod;
    CookiesEnabled: bool;
    AutoFollowRedirects: bool;
    AutoDecompression: DecompressionScheme;
    Headers: RequestHeader list option;
    Body: string option;
    QueryStringItems: NameValue list option;
    Cookies: NameValue list option;
}

type Response = {
    StatusCode: int;
    EntityBody: string option;
    Cookies: Map<string,string>;
    Headers: Map<ResponseHeader,string>;
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
            | UserAgent(value) -> webRequest.UserAgent <- value
            | Referer(value) -> webRequest.Referer <- value
            | ContentType(value) -> webRequest.ContentType <- value
            | Accept(value) -> webRequest.Accept <- value
            | AcceptLanguage(value) -> webRequest.Headers.Add("Accept-Language", value)
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

let withAutoDecompression decompressionSchemes request =
    {request with AutoDecompression = decompressionSchemes}

let withBody body request =
    {request with Body = Some(body)}

let withQueryStringItem item request =
    {request with QueryStringItems = request.QueryStringItems |> append item}

let withCookie cookie request =
    if not request.CookiesEnabled then failwithf "Cannot add cookie %A - cookies disabled" cookie.name
    {request with Cookies = request.Cookies |> append cookie}

let private toHttpWebrequest request =

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

    if request.Body.IsSome then
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

let getResponseCodeAsync request = async {
    use! response = request |> toHttpWebrequest |> getResponseNoException
    return response.StatusCode |> int
}

let getResponseCode request =
    getResponseCodeAsync request |> Async.RunSynchronously

let private readBody (response:HttpWebResponse) = async {
    use responseStream = new AsyncStreamReader(response.GetResponseStream(),Encoding.GetEncoding(1252))
    let! body = responseStream.ReadToEnd()
    return body
}

let getResponseBodyAsync request = async {
    use! response = request |> toHttpWebrequest |> getResponseNoException
    let! body = response |> readBody
    return body
}

let getResponseBody request =
    getResponseBodyAsync request |> Async.RunSynchronously

let private getCookiesAsMap (response:HttpWebResponse) = 
    let cookieArray = Array.zeroCreate response.Cookies.Count
    response.Cookies.CopyTo(cookieArray, 0)
    cookieArray |> Array.fold (fun map cookie -> map |> Map.add cookie.Name cookie.Value) Map.empty

let private getResponseHeader headerName =
    match headerName with
    | "Content-Encoding" -> ContentEncoding
    | _ -> NonStandard headerName

let private getHeadersAsMap (response:HttpWebResponse) =
    let headerArray = Array.zeroCreate response.Headers.Count
    for index = 0 to response.Headers.Count-1 do
        headerArray.[index] <- (getResponseHeader response.Headers.Keys.[index], response.Headers.Item(response.Headers.Keys.[index]) )
    Map.ofArray headerArray

let getResponseAsync request = async {
    use! response = request |> toHttpWebrequest |> getResponseNoException

    let code = response.StatusCode |> int
    let! body = response |> readBody

    let entityBody = 
        match body.Length > 0 with
        | true -> Some(body)
        | false -> None

    let cookies = response |> getCookiesAsMap
    let headers = response |> getHeadersAsMap

    return {   
        StatusCode = code;
        EntityBody = entityBody;
        Cookies = cookies;
        Headers = headers;
    }
}

let getResponse request =
    getResponseAsync request |> Async.RunSynchronously