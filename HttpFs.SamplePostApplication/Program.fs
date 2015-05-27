open HttpClient

open System.Web // TODO: remember to add reference to project ->

/// Any Key-value pair to store values. 
type KeyValuePair = {Key:string; Value:string}

[<EntryPoint>]
let main argv = 

    // Set up parameters for small pizza with bacon and onions.
    let bodyParams :KeyValuePair list = [
        {Key = "custname"; Value = "John Doe"}
        {Key = "custtel"; Value = "123456789"}
        {Key = "custemail"; Value = "example@example.com"}
         // Radio button
        {Key = "size"; Value = "small"}
         // Checkboxes
        {Key = "topping"; Value = "bacon"}
        {Key = "topping"; Value = "cheese"}

        {Key = "delivery"; Value = "4:00"}
        {Key = "comments"; Value = ""}
        ]

    /// Function to convert parameter list to Body.
    let bodyFunc (acc:string) (tArg:KeyValuePair) = 
        // Encode characters like spaces before transmitting.
        let rName = tArg.Key |> HttpUtility.UrlEncode
        let rValue = tArg.Value |> HttpUtility.UrlEncode

        let mix = HttpUtility.UrlEncode rName + "=" + rValue
        match acc with
        | "" -> mix // Skip & for first one.
        | _ -> acc + "&" + mix  // application/x-www-form-urlencoded
    
    // Create body.
    // custname=John+Doe&custtel=123456789&custemail=example%40example.com&size=small&topping=bacon&topping=cheese&delivery=4%3a00&comments=
    let body = bodyParams |> Seq.fold bodyFunc ""

    // List of common html headers.
    // http://en.wikipedia.org/wiki/List_of_HTTP_header_fields
    try
        let request =
            createRequest Post "http://httpbin.org/post"
            |> withBody body
            |> withHeader (Referer "https://github.com/relentless/Http.fs")
            |> withHeader (UserAgent "Mozilla/5.0 (Windows NT 6.3; WOW64; rv:36.0) Gecko/20100101 Firefox/36.0")    
            |> withHeader (Accept "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")       
            |> withHeader (AcceptLanguage "Accept-Language: en-US")
            |> withHeader (ContentType "application/x-www-form-urlencoded")    
            |> withHeader (Custom {name="DNT"; value="1"}) // Custom header. Do Not Track Enabled 
            |> withAutoDecompression (DecompressionScheme.GZip ||| DecompressionScheme.Deflate) // Accept both.
            |> withResponseCharacterEncoding "utf-8"
            |> withKeepAlive true
    
        let resp = request |> getResponse
        printfn "StatusCode: %d" resp.StatusCode
    with
        // You may want to handle WebException. Happens often during debug because of Timeout.
        | :? System.Net.WebException as ex -> 
            printfn "%s" ex.Message
            reraise()
        | _ -> reraise()

    0 // return an integer exit code
