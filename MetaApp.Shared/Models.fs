module Shared

type IServer = { 
    getLength : string -> Async<int>
}