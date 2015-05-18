[<AutoOpen>]
module internal HttpFs.Prelude

open System
open System.IO
open System.Threading.Tasks
open System.Threading

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


module ASCII =
  open System.Text

  let bytes (s : string) =
    Encoding.ASCII.GetBytes s

module UTF8 =
  open System
  open System.Text

  let bytes (s : string) =
    Encoding.UTF8.GetBytes s