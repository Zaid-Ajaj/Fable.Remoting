module SharedModelsOne

// Generic discriminated unions
type Maybe<'t> = 
    | Just of 't
    | Nothing

// Complex record types
type Person = { 
    Name: string
    Age: int
    Birthday: System.DateTime
}

type IServer = {
    echoMaybe : Maybe<string> -> Async<Maybe<int>>
    personsName : Person -> Async<string> 
    addTwenty : int -> Async<int>
    sumSeq : seq<int> -> Async<int>
    minimumAge : seq<Person> -> Async<int>
    oldestPerson : seq<Person> -> Async<Person>
    getPerson : unit -> Async<Person>
} 