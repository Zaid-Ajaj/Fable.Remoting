namespace Fable.Remoting.Server 

[<RequireQualifiedAccess>]
module ThreadSafeCell = 

    type CellOperations<'t> = 
    | Set of 't  
    | Get of AsyncReplyChannel<Option<'t>> 

    let create() = 
        MailboxProcessor.Start <| fun inbox -> 
          let rec loop state = async {
            let! msg = inbox.Receive() 
            match msg with 
            | Set value -> 
                return! loop (Some value)   
            | Get channel -> 
                channel.Reply state   
                return! loop state 
          }   
        
          loop None 

    let set (value: 't) (cell: MailboxProcessor<CellOperations<'t>>) = 
        cell.Post (Set value) 

    let get (cell: MailboxProcessor<CellOperations<'t>>) = 
        cell.PostAndAsyncReply Get

    /// Takes in an expensive synchronous function, runs it once and returns a function that retrieves the computed value asynchronously
    let computeOnce (f: unit -> 't) : (unit -> Async<'t>) = 
        let atom = create() 
        let computed = f()
        fun () -> async {
            let! value = get atom 
            match value with 
            | None -> 
                set computed atom 
                return computed 
            | Some previouslyComputedValue ->
                return previouslyComputedValue
        }