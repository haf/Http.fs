module HttpFs.Tests.Api

open System
open System.Net.Http
open System.Text
open Expecto
open HttpFs.Client
open HttpFs.Client.Request

let ValidUri = Uri "http://www"
let validRequest = create Get ValidUri

[<Tests>]
let api =
  testList "api" [
    testCase "a default request with Method and Url" <| fun _ ->
      let r = Request.createUrl Get "http://www.google.com"
      Expect.equal (r.url.ToString()) "http://www.google.com/" "has same url"
      Expect.equal r.method Get "has get method"
      Expect.equal r.body (BodyRaw [||]) "has empty body"
      Expect.equal r.bodyCharacterEncoding Encoding.UTF8 "has UTF8 body character encoding"
      Expect.isEmpty r.cookies "has no cookies"
      Expect.isTrue r.cookiesEnabled "has cookies enabled"
      Expect.isEmpty r.headers "has no special headers"
      Expect.isEmpty r.queryStringItems "has no query string"
      Expect.isNone r.responseCharacterEncoding "has no response char encoding"
      Expect.isNone r.proxy "has no proxy configured"

    testCase "withClient uses specified client" <| fun _ ->
      let client = new HttpClient()
      let request = Request.createWithClient client Get ValidUri
      Expect.equal client request.httpClient "httpClient should be set"

    testCase "withCookiesDisabled disables cookies" <| fun _ ->
      Expect.isFalse (validRequest |> Request.cookiesDisabled).cookiesEnabled "should disable cookies"

    testCase "withHeader adds header to the request" <| fun _ ->
      let expected = UserAgent "Mozilla/5.0"
      let header = (validRequest |> setHeader expected).headers |> Map.find expected.Key
      Expect.equal header expected "should set header"

    testCase "withHeader Custom adds a custom header to the request" <| fun _ ->
      let expected = Custom ("X-Hello-Mum", "Happy Birthday!")
      let header = (validRequest |> Request.setHeader expected).headers |> Map.find expected.Key
      Expect.equal header expected "should set header"

    testCase "multiple headers of different types can be added, including custom headers with different names" <| fun _ ->
      let headers = 
        validRequest
        |> setHeader (UserAgent "ua")
        |> setHeader (Referer "ref")
        |> setHeader (Custom ("c1", "v1"))
        |> setHeader (Custom ("c2", "v2"))
        |> (fun x -> x.headers)

      Expect.hasCountOf headers 4u (fun x -> true) "has four items"
      Expect.canPick headers (UserAgent "ua") "contains 'ua' UserAgent"
      Expect.canPick headers (Referer "ref") "contains a referrer"
      Expect.canPick headers (Custom ("c1", "v1")) "contains custom 1"
      Expect.canPick headers (Custom ("c2", "v2")) "contains custom 2"

    testCase "withBasicAuthentication sets the Authorization header with the username and password base-64 encoded" <| fun _ ->
      let headers =
        validRequest
        |> basicAuthentication "myUsername" "myPassword"
        |> (fun x -> x.headers |> Map.toList |> List.map snd )

      Expect.contains headers (Authorization "Basic bXlVc2VybmFtZTpteVBhc3N3b3Jk") "should contain auth header"

    testCase "withBasicAuthentication encodes the username and password with UTF-8 before converting to base64" <| fun _ ->
      let createdRequest =
        validRequest
        |> basicAuthentication "Ãµ¶" "汉语" // UTF-8 characters not present in ASCII
      Expect.canPick createdRequest.headers (Authorization "Basic w4PCtcK2OuaxieivrQ==") "contains auth header"

    testCase "uses latest added header when eq name" <| fun _ ->
      let req =
        validRequest
        |> setHeader (Custom ("c1", "v1"))
        |> setHeader (Custom ("c1", "v2"))

      req.headers
      |> Map.find "c1"
      |> function
      | (Custom (c1key, c1value)) -> Expect.equal c1value "v2" "should be equal"
      | _                         -> Tests.failtest "errrrrorrrrr"

    testCase "withQueryString adds the query string item to the list" <| fun _ ->
      let q =
        validRequest
        |> queryStringItem "f1" "v1"
        |> queryStringItem "f2" "v2"
        |> (fun r -> r.queryStringItems)

      Expect.hasCountOf q 2u (fun _ -> true) "has two items"
      Expect.canPick q ["v1"] "contains first item"
      Expect.canPick q ["v2"] "contains second item"

    testCase "withCookie throws an exception if cookies are disabled" <| fun _ ->
      Expect.throwsT<Exception> (fun () ->
        validRequest
        |> cookiesDisabled
        |> cookie (Cookie.create("message", "hi mum"))
        |> ignore)
        "should throw"

    testCase "a request with two cookies" <| fun _ ->
      let c =
        createUrl Get "http://www.google.com"
        |> cookie (Cookie.create("c1", "v1"))
        |> cookie (Cookie.create("c2", "v2"))
        |> (fun x -> x.cookies)

      Expect.hasCountOf c 2u (fun _ -> true) "should have two cookies"
      Expect.canPick c (Cookie.create("c1", "v1")) "should have first cookie"
      Expect.canPick c (Cookie.create("c2", "v2")) "should have second cookie"

    testCase "withResponseCharacterEncoding sets the response character encoding" <| fun _ ->
      let createdRequest =
        createUrl Get "http://www.google.com/"
        |> responseCharacterEncoding Encoding.UTF8

      Expect.equal createdRequest.responseCharacterEncoding.Value Encoding.UTF8 "encoding should be equal"

    testCase "a request withProxy" <| fun _ ->
        let p =
            validRequest
            |> proxy { Address = "proxy.com"; Port = 8080; Credentials = Credentials.None }
            |> (fun x -> x.proxy.Value)

        Expect.equal p.Address "proxy.com" "sets address"
        Expect.equal p.Port 8080 "sets port"

    testCase "withProxy can set proxy with custom credentials" <| fun _ ->
      let request = 
        validRequest 
        |> proxy { 
          Address = "proxy.com"; 
          Port = 8080; 
          Credentials = Credentials.Custom { username = "Tim"; password = "Password1" } }
        
      Expect.equal request.proxy.Value.Credentials (Credentials.Custom { username = "Tim"; password = "Password1" }) "credentials should be equal"

    testCase "withProxy can set proxy with default credentials" <| fun _ ->
      let request = 
        validRequest 
        |> proxy { Address = ""; Port = 0; Credentials = Credentials.Default }

      Expect.equal request.proxy.Value.Credentials Credentials.Default "credentials should be default"

    testCase "withProxy can set proxy with no credentials" <| fun _ ->
      let request = 
        validRequest 
        |> proxy { Address = ""; Port = 0; Credentials = Credentials.None }

      Expect.equal request.proxy.Value.Credentials Credentials.None "credentials should be none"
  ]