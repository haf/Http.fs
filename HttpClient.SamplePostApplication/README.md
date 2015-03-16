# How to make a POST request. #

Following code uses [httpbin](http://httpbin.org/) service to make a POST request.

There are two pages to look at.

1. [**/post**](http://httpbin.org/post) Returns POST data.

2. [**/forms/post**](http://httpbin.org/forms/post) HTML form that submits to **/post**.


Script is going to reproduce request made by **/forms/post** page.

Most common Content Type for forms is *application/x-www-form-urlencoded*.

	|> withHeader (ContentType "application/x-www-form-urlencoded")

Also, keep in mind that form may redirect you to another page, allow redirects by removing: 

	|> withAutoFollowRedirectsDisabled

Create a request: 

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

## Create request body. ##
KeyValuePair is a simple type to store form values.


	/// Any Key-value pair to store values. 
	type KeyValuePair = {Key:string; Value:string}

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


Form body must be encoded.

**HttpUtility.UrlEncode** is in the System.Web namespace, remember to add reference to the project.

    /// Function to convert parameter list to Body.
    let bodyFunc (acc:string) (tArg:KeyValuePair) = 
        // Encode characters like spaces before transmitting.
        let rName = tArg.Key |> HttpUtility.UrlEncode
        let rValue = tArg.Value |> HttpUtility.UrlEncode

        let mix = HttpUtility.UrlEncode rName + "=" + rValue
        match acc with
        | "" -> mix // Skip first.
        | _ -> acc + "&" + mix  // application/x-www-form-urlencoded

Use **Seq.fold** on bodyParams to make request body.    

    // Create body.
    let body = bodyParams |> Seq.fold bodyFunc ""

Actual request body:
 `custname=John+Doe&custtel=123456789&custemail=example%40example.com&size=small&topping=bacon&topping=cheese&delivery=4%3a00&comments=`

Make POST request.

	let resp = request |> getResponse

resp returns StatusCode 200 and EntityBody as following: 


    {
      "args": {}, 
      "data": "", 
      "files": {}, 
      "form": {
	    "comments": "", 
	    "custemail": "example@example.com", 
	    "custname": "John Doe", 
	    "custtel": "123456789", 
	    "delivery": "4:00", 
	    "size": "small", 
	    "topping": [
	      "bacon", 
	      "cheese"
	    ]
	      }, 
      "headers": {
	    "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8", 
	    "Accept-Encoding": "gzip, deflate", 
	    "Accept-Language": "Accept-Language: en-US", 
	    "Content-Length": "133", 
	    "Content-Type": "application/x-www-form-urlencoded", 
	    "Dnt": "1", 
	    "Host": "httpbin.org", 
	    "Referer": "https://github.com/relentless/Http.fs", 
	    "User-Agent": "Mozilla/5.0 (Windows NT 6.3; WOW64; rv:36.0) Gecko/20100101 Firefox/36.0"
      }, 
      "json": null, 
      "origin": "xx.xxx.xxx.xxx", 
      "url": "http://httpbin.org/post"
    }