namespace HttpFs

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Runtime.CompilerServices

[<assembly: InternalsVisibleTo "HttpFs.IntegrationTests">]
[<assembly: InternalsVisibleTo "HttpFs.UnitTests">]
()

[<AutoOpen>]
module internal Prelude =

  open System
  open System.IO
  open System.Threading.Tasks
  open System.Threading

  let flip f a b = f b a

  type Microsoft.FSharp.Control.Async with
    /// Raise an exception on the async computation/workflow.
    static member AsyncRaise (e : exn) =
      Async.FromContinuations(fun (_,econt,_) -> econt e)

    /// Await a task asynchronously
    static member AwaitTask (t : Task) =
      let flattenExns (e : AggregateException) = e.Flatten().InnerExceptions.[0]
      let rewrapAsyncExn (it : Async<unit>) =
        async { try do! it with :? AggregateException as ae -> do! Async.AsyncRaise (flattenExns ae) }
      let tcs = new TaskCompletionSource<unit>(TaskCreationOptions.None)
      t.ContinueWith((fun t' ->
        if t.IsFaulted then tcs.SetException(t.Exception |> flattenExns)
        elif t.IsCanceled then tcs.SetCanceled ()
        else tcs.SetResult(())), TaskContinuationOptions.ExecuteSynchronously)
      |> ignore
      tcs.Task |> Async.AwaitTask |> rewrapAsyncExn

    static member map f value =
      async {
        let! v = value
        return f v
      }

    static member bind f value =
      async {
        let! v = value
        return! f v
      }

  type Microsoft.FSharp.Control.AsyncBuilder with

    /// An extension method that overloads the standard 'Bind' of the 'async' builder. The new overload awaits on
    /// a standard .NET task
    member x.Bind(t : Task<'T>, f:'T -> Async<'R>) : Async<'R> = async.Bind(Async.AwaitTask t, f)

    /// An extension method that overloads the standard 'Bind' of the 'async' builder. The new overload awaits on
    /// a standard .NET task which does not commpute a value
    member x.Bind(t : Task, f : unit -> Async<'R>) : Async<'R> = async.Bind(Async.AwaitTask t, f)

  module String =
    let toLowerInvariant (s : string) =
      s.ToLowerInvariant()

    let toLower (s : string) =
      s.ToLower()

  module Option =
    let orDefault def =
      Option.fold (fun s t -> t) def

  module ASCII =
    open System.Text

    let bytes (s : string) =
      Encoding.ASCII.GetBytes s

  module UTF8 =
    open System
    open System.Text

    let bytes (s : string) =
      Encoding.UTF8.GetBytes s

  module Map =
    let put k v m =
      match m |> Map.tryFind k with
      | Some _ -> m |> Map.remove k |> Map.add k v
      | None   -> m |> Map.add k v

module Logging =

  /// The log levels specify the severity of the message.
  [<CustomEquality; CustomComparison>]
  type LogLevel =
    /// The most verbose log level, more verbose than Debug.
    | Verbose
    /// Less verbose than Verbose, more verbose than Info
    | Debug
    /// Less verbose than Debug, more verbose than Warn
    | Info
    /// Less verbose than Info, more verbose than Error
    | Warn
    /// Less verbose than Warn, more verbose than Fatal
    | Error
    /// The least verbose level. Will only pass through fatal
    /// log lines that cause the application to crash or become
    /// unusable.
    | Fatal
    with
      /// Convert the LogLevel to a string
      override x.ToString () =
        match x with
        | Verbose -> "verbose"
        | Debug -> "debug"
        | Info -> "info"
        | Warn -> "warn"
        | Error -> "error"
        | Fatal -> "fatal"

      /// Converts the string passed to a Loglevel.
      [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
      static member FromString str =
        match str with
        | "verbose" -> Verbose
        | "debug" -> Debug
        | "info" -> Info
        | "warn" -> Warn
        | "error" -> Error
        | "fatal" -> Fatal
        | _ -> Info

      /// Turn the LogLevel into an integer
      [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
      member x.ToInt () =
        (function
        | Verbose -> 1
        | Debug -> 2
        | Info -> 3
        | Warn -> 4
        | Error -> 5
        | Fatal -> 6) x

      /// Turn an integer into a LogLevel
      [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
      static member FromInt i =
        (function
        | 1 -> Verbose
        | 2 -> Debug
        | 3 -> Info
        | 4 -> Warn
        | 5 -> Error
        | 6 -> Fatal
        | _ as i -> failwithf "rank %i not available" i) i

      static member op_LessThan (a, b) = (a :> IComparable<LogLevel>).CompareTo(b) < 0
      static member op_LessThanOrEqual (a, b) = (a :> IComparable<LogLevel>).CompareTo(b) <= 0
      static member op_GreaterThan (a, b) = (a :> IComparable<LogLevel>).CompareTo(b) > 0
      static member op_GreaterThanOrEqual (a, b) = (a :> IComparable<LogLevel>).CompareTo(b) >= 0

      override x.Equals other = (x :> IComparable).CompareTo other = 0

      override x.GetHashCode () = x.ToInt ()

      interface IComparable with
        member x.CompareTo other =
          match other with
          | null -> 1
          | :? LogLevel as tother ->
            (x :> IComparable<LogLevel>).CompareTo tother
          | _ -> failwith <| sprintf "invalid comparison %A to %A" x other

      interface IComparable<LogLevel> with
        member x.CompareTo other =
          compare (x.ToInt()) (other.ToInt())

      interface IEquatable<LogLevel> with
        member x.Equals other =
          x.ToInt() = other.ToInt()

  /// When logging, write a log line like this with the source of your
  /// log line as well as a message and an optional exception.
  type LogLine =
    { /// the level that this log line has
      level     : LogLevel
      /// the source of the log line, e.g. 'ModuleName.FunctionName'
      path      : string
      /// the message that the application wants to log
      message   : string
      /// any key-value based data to log
      data      : Map<string, obj>
      /// timestamp when this log line was created
      timestamp : DateTimeOffset }

  /// The primary Logger abstraction that you can log data into
  type Logger =
    abstract Verbose : (unit -> LogLine) -> unit
    abstract Debug : (unit -> LogLine) -> unit
    abstract Log : LogLine -> unit

  let NoopLogger =
    { new Logger with
        member x.Verbose f_line = ()
        member x.Debug f_line = ()
        member x.Log line = () }

  let private logger =
    ref ((fun () -> DateTimeOffset.UtcNow), fun (name : string) -> NoopLogger)

  [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
  module LogLine =

    let mk (clock : unit -> DateTimeOffset) path level data message =
      { message   = message
        level     = level
        path      = path
        data      = data
        timestamp = clock () }

    let private message data message =
      { message   = message
        level     = Verbose
        path      = ""
        data      = data |> Map.ofList
        timestamp = (fst !logger) () }

    let sprintf data =
      Printf.kprintf (message data)

  let configure clock fLogger =
    logger := (clock, fLogger)

  let getLoggerByName name =
    (!logger |> snd) name

  [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
  module Logger =

    let log (logger : Logger) (line : LogLine) =
      logger.Log line

    let debug (logger : Logger) f_line =
      logger.Debug f_line

    let verbose (logger : Logger) f_line =
      logger.Verbose f_line

/// <summary>
/// Implements a TextReader-like API that asynchronously reads characters from
/// a byte stream in a particular encoding.
/// </summary>
[<Sealed>]
type AsyncStreamReader(stream:Stream, encoding:Encoding, detectEncodingFromByteOrderMarks:bool, bufferSize:int, owns:bool) =
    static let defaultBufferSize = 0x2000 // Byte buffer size
    static let defaultFileStreamBufferSize = 4096
    static let minBufferSize = 128

    // Creates a new StreamReader for the given stream. The
    // character encoding is set by encoding and the buffer size,
    // in number of 16-bit characters, is set by bufferSize.
    //
    // Note that detectEncodingFromByteOrderMarks is a very
    // loose attempt at detecting the encoding by looking at the first
    // 3 bytes of the stream.  It will recognize UTF-8, little endian
    // unicode, and big endian unicode text, but that's it.  If neither
    // of those three match, it will use the Encoding you provided.
    //

    do  if (stream=null || encoding=null) then
            raise <| new ArgumentNullException(if (stream=null) then "stream" else "encoding");

        if not stream.CanRead then
            invalidArg "stream" "stream not readable";
#if FX_NO_FILESTREAM_ISASYNC
#else
        match stream with
        | :? System.IO.FileStream as fs when not fs.IsAsync ->
            invalidArg "stream" "FileStream not asynchronous. AsyncStreamReader should only be used on FileStream if the IsAsync property returns true. Consider passing 'true' for the async flag in the FileStream constructor"
        | _ ->
            ()
#endif
        if (bufferSize <= 0) then
            raise <| new ArgumentOutOfRangeException("bufferSize");

    let mutable stream = stream
    let mutable decoder = encoding.GetDecoder();
    let mutable encoding = encoding
    let bufferSize = max bufferSize  minBufferSize;

    // This is the maximum number of chars we can get from one call to
    // readBuffer.  Used so readBuffer can tell when to copy data into
    // a user's char[] directly, instead of our internal char[].
    let mutable _maxCharsPerBuffer = encoding.GetMaxCharCount(bufferSize)
    let mutable byteBuffer = Array.zeroCreate<byte> bufferSize;
    let mutable charBuffer = Array.zeroCreate<char> _maxCharsPerBuffer;
    let preamble = encoding.GetPreamble();   // Encoding's preamble, which identifies this encoding.
    let mutable charPos = 0
    let mutable charLen = 0
    // Record the number of valid bytes in the byteBuffer, for a few checks.
    let mutable byteLen = 0
    // This is used only for preamble detection
    let mutable bytePos = 0

    // We will support looking for byte order marks in the stream and trying
    // to decide what the encoding might be from the byte order marks, IF they
    // exist.  But that's all we'll do.
    let mutable _detectEncoding = detectEncodingFromByteOrderMarks;

    // Whether we must still check for the encoding's given preamble at the
    // beginning of this file.
    let mutable _checkPreamble = (preamble.Length > 0);

    let readerClosed() = invalidOp "reader closed"
    // Trims n bytes from the front of the buffer.
    let compressBuffer(n) =
        Debug.Assert(byteLen >= n, "compressBuffer was called with a number of bytes greater than the current buffer length.  Are two threads using this StreamReader at the same time?");
        Buffer.BlockCopy(byteBuffer, n, byteBuffer, 0, byteLen - n);
        byteLen <- byteLen - n;

    // Trims the preamble bytes from the byteBuffer. This routine can be called multiple times
    // and we will buffer the bytes read until the preamble is matched or we determine that
    // there is no match. If there is no match, every byte read previously will be available
    // for further consumption. If there is a match, we will compress the buffer for the
    // leading preamble bytes
    let isPreamble() =
        if not _checkPreamble then _checkPreamble else

        Debug.Assert(bytePos <= preamble.Length, "_compressPreamble was called with the current bytePos greater than the preamble buffer length.  Are two threads using this StreamReader at the same time?");
        let len = if (byteLen >= (preamble.Length)) then (preamble.Length - bytePos) else (byteLen  - bytePos);

        let mutable fin = false
        let mutable i = 0
        while i < len && not fin do
            if (byteBuffer.[bytePos] <> preamble.[bytePos]) then
                bytePos <- 0;
                _checkPreamble <- false;
                fin <- true
            if not fin then
                i <- i + 1
                bytePos <- bytePos + 1

        Debug.Assert(bytePos <= preamble.Length, "possible bug in _compressPreamble.  Are two threads using this StreamReader at the same time?");

        if (_checkPreamble) then
            if (bytePos = preamble.Length) then
                // We have a match
                compressBuffer(preamble.Length);
                bytePos <- 0;
                _checkPreamble <- false;
                _detectEncoding <- false;

        _checkPreamble;


    let detectEncoding() =
        if (byteLen >= 2) then
            _detectEncoding <- false;
            let mutable changedEncoding = false;
            if (byteBuffer.[0]=0xFEuy && byteBuffer.[1]=0xFFuy) then
                // Big Endian Unicode

                encoding <- new UnicodeEncoding(true, true);
                compressBuffer(2);
                changedEncoding <- true;
#if FX_NO_UTF32ENCODING
#else
            elif (byteBuffer.[0]=0xFFuy && byteBuffer.[1]=0xFEuy) then
                // Little Endian Unicode, or possibly little endian UTF32
                if (byteLen >= 4 && byteBuffer.[2] = 0uy && byteBuffer.[3] = 0uy) then
                    encoding <- new UTF32Encoding(false, true);
                    compressBuffer(4);
                else
                    encoding <- new UnicodeEncoding(false, true);
                    compressBuffer(2);
                changedEncoding <- true;
#endif
            elif (byteLen >= 3 && byteBuffer.[0]=0xEFuy && byteBuffer.[1]=0xBBuy && byteBuffer.[2]=0xBFuy) then
                // UTF-8
                encoding <- Encoding.UTF8;
                compressBuffer(3);
                changedEncoding <- true;
#if FX_NO_UTF32ENCODING
#else
            elif (byteLen >= 4 && byteBuffer.[0] = 0uy && byteBuffer.[1] = 0uy && byteBuffer.[2] = 0xFEuy && byteBuffer.[3] = 0xFFuy) then
                // Big Endian UTF32
                encoding <- new UTF32Encoding(true, true);
                changedEncoding <- true;
#endif
            elif (byteLen = 2) then
                _detectEncoding <- true;
            // Note: in the future, if we change this algorithm significantly,
            // we can support checking for the preamble of the given encoding.

            if (changedEncoding) then
                decoder <- encoding.GetDecoder();
                _maxCharsPerBuffer <- encoding.GetMaxCharCount(byteBuffer.Length);
                charBuffer <- Array.zeroCreate<char> _maxCharsPerBuffer;

    let readBuffer() = async {
        charLen <- 0;
        charPos <- 0;

        if not _checkPreamble then
            byteLen <- 0;

        let fin = ref false
        while (charLen = 0 && not !fin) do
            if (_checkPreamble) then
                Debug.Assert(bytePos <= preamble.Length, "possible bug in _compressPreamble.  Are two threads using this StreamReader at the same time?");
                let! len = stream.AsyncRead(byteBuffer, bytePos, byteBuffer.Length - bytePos);
                Debug.Assert(len >= 0, "Stream.Read returned a negative number!  This is a bug in your stream class.");

                if (len = 0) then
                    // EOF but we might have buffered bytes from previous
                    // attempts to detecting preamble that needs to decoded now
                    if (byteLen > 0) then
                        charLen <-  charLen + decoder.GetChars(byteBuffer, 0, byteLen, charBuffer, charLen);

                    fin := true

                byteLen <- byteLen + len;
            else
                Debug.Assert((bytePos = 0), "bytePos can be non zero only when we are trying to _checkPreamble.  Are two threads using this StreamReader at the same time?");
                let! len = stream.AsyncRead(byteBuffer, 0, byteBuffer.Length);
                byteLen <- len
                Debug.Assert(byteLen >= 0, "Stream.Read returned a negative number!  This is a bug in your stream class.");

                if (byteLen = 0)  then // We're at EOF
                    fin := true

            // Check for preamble before detect encoding. This is not to override the
            // user suppplied Encoding for the one we implicitly detect. The user could
            // customize the encoding which we will loose, such as ThrowOnError on UTF8
            if not !fin then
                if not (isPreamble()) then
                    // If we're supposed to detect the encoding and haven't done so yet,
                    // do it.  Note this may need to be called more than once.
                    if (_detectEncoding && byteLen >= 2) then
                        detectEncoding();

                    charLen <- charLen + decoder.GetChars(byteBuffer, 0, byteLen, charBuffer, charLen);

            if (charLen <> 0) then
                fin := true

        return charLen

    }


    let cleanup() =
      if not owns then ()
      else
            // Dispose of our resources if this StreamReader is closable.
            // Note that Console.In should not be closable.
            try
                // Note that Stream.Close() can potentially throw here. So we need to
                // ensure cleaning up internal resources, inside the finally block.
                if (stream <> null) then
                    stream.Close()

            finally
                if (stream <> null) then
                    stream <- null
                    encoding <- null
                    decoder <- null
                    byteBuffer <- null
                    charBuffer <- null
                    charPos <- 0
                    charLen <- 0

    // StreamReader by default will ignore illegal UTF8 characters. We don't want to
    // throw here because we want to be able to read ill-formed data without choking.
    // The high level goal is to be tolerant of encoding errors when we read and very strict
    // when we write. Hence, default StreamWriter encoding will throw on error.

    new (stream) = new AsyncStreamReader(stream, true)

    new (stream, detectEncodingFromByteOrderMarks:bool) = new AsyncStreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks, defaultBufferSize, true)

    new (stream, encoding:Encoding) = new AsyncStreamReader(stream, encoding, true, defaultBufferSize, true)

    new (stream, encoding, detectEncodingFromByteOrderMarks) = new AsyncStreamReader(stream, encoding, detectEncodingFromByteOrderMarks, defaultBufferSize, true)

    member x.Close() = cleanup()

    interface System.IDisposable with
        member x.Dispose() = cleanup()

    member x.CurrentEncoding  = encoding
    member x.BaseStream = stream

    // DiscardBufferedData tells StreamReader to throw away its internal
    // buffer contents.  This is useful if the user needs to seek on the
    // underlying stream to a known location then wants the StreamReader
    // to start reading from this new point.  This method should be called
    // very sparingly, if ever, since it can lead to very poor performance.
    // However, it may be the only way of handling some scenarios where
    // users need to re-read the contents of a StreamReader a second time.
    member x.DiscardBufferedData() =
        byteLen <- 0;
        charLen <- 0;
        charPos <- 0;
        decoder <- encoding.GetDecoder();

    member x.EndOfStream = async {
        if (stream = null) then
            readerClosed();

        if (charPos < charLen) then
            return false
        else
            let! numRead = readBuffer();
            return numRead = 0;
    }

    member x.Peek() =
        async {
            let! emp = x.EndOfStream
            return (if emp then -1 else int charBuffer.[charPos])
        }

    member x.Read() = async {
        if (stream = null) then
            readerClosed();

        if (charPos = charLen) then
            let! n = readBuffer()
            if n = 0 then
                return char -1;
            else
                let result = charBuffer.[charPos];
                charPos <- charPos + 1;
                return result;
        else
            let result = charBuffer.[charPos];
            charPos <- charPos + 1;
            return result;
    }

    // Returns only when count characters have been read or the end of the file was reached.
    member x.ReadExactly(buffer:char[], index, count) = async {
        let i = ref 0
        let n = ref 0
        let count = ref count
        let first = ref true
        while !first || (!i > 0 && !n < !count) do
            let! j = x.Read(buffer, index + !n, !count - !n)
            i := j
            n := !n + j
            first := false
        return !n;
    }

    member x.Read(buffer:char[], index, count) = async {
        if (stream = null) then
            readerClosed();
        if (buffer=null) then
            raise <| new ArgumentNullException("buffer");
        if (index < 0 || count < 0) then
            raise <| new ArgumentOutOfRangeException((if (index < 0) then "index" else "count"), (* Environment.GetResourceString *)("ArgumentOutOfRange_NeedNonNegNum"));
        if (buffer.Length - index < count) then
            raise <| new ArgumentException("index")

        let charsRead = ref 0;
        let charsReqd = ref count;
        let fin = ref false
        while (!charsReqd > 0) && not !fin do
            let! charsAvail = if (charLen = charPos) then readBuffer() else async { return charLen - charPos }
            if (charsAvail = 0) then
                // We're at EOF
                fin := true
            else
                let charsConsumed = min charsAvail !charsReqd
                Buffer.BlockCopy(charBuffer, charPos * 2, buffer, (index + !charsRead) * 2, charsConsumed*2);
                charPos <- charPos + charsConsumed;
                charsRead := !charsRead + charsConsumed;
                charsReqd := !charsReqd - charsConsumed;

        return !charsRead;
    }

    member x.ReadToEnd() = async {
        if (stream = null) then
            readerClosed();

        // Call readBuffer, then pull data out of charBuffer.
        let sb = new StringBuilder(charLen - charPos);
        let readNextChunk =
            async {
                sb.Append(charBuffer, charPos, charLen - charPos) |> ignore;
                charPos <- charLen;  // Note we consumed these characters
                let! _ = readBuffer()
                return ()
            }
        do! readNextChunk
        while charLen > 0 do
            do! readNextChunk
        return sb.ToString();
    }


    // Reads a line. A line is defined as a sequence of characters followed by
    // a carriage return ('\r'), a line feed ('\n'), or a carriage return
    // immediately followed by a line feed. The resulting string does not
    // contain the terminating carriage return and/or line feed. The returned
    // value is null if the end of the input stream has been reached.
    //
    member x.ReadLine() = async {

        let! emp = x.EndOfStream
        if emp then return null else
        let sb = new StringBuilder()
        let fin1 = ref false
        while not !fin1 do
            let i = ref charPos;
            let fin2 = ref false
            while (!i < charLen) && not !fin2 do
                let ch = charBuffer.[!i];
                // Note the following common line feed chars:
                // \n - UNIX   \r\n - DOS   \r - Mac
                if (ch = '\r' || ch = '\n') then
                    sb.Append(charBuffer, charPos, !i - charPos) |> ignore;
                    charPos <- !i + 1;
                    if ch = '\r' then
                        let! emp = x.EndOfStream
                        if not emp && (charBuffer.[charPos] = '\n') then
                            charPos <- charPos + 1;
                    // Found end of line, done
                    fin2 := true
                    fin1 := true
                else
                    i := !i + 1;

            if not !fin1 then
                i := charLen - charPos;
                sb.Append(charBuffer, charPos, !i) |> ignore;

                let! n = readBuffer()
                fin1 := (n <= 0)

        return sb.ToString();

    }

module Client =

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
