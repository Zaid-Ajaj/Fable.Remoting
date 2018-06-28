# Limitations and Workaround

Although using quotations allowed for maximum flexibility, they have a *tiny* drawback: we have to compile them by ourself. The quotation expression is a piece of the code that the compiler left out for you to deal with in the form of F# Abstract Syntax Tree (AST) after parsing and lexing.

This is fine most of the time for the proxy implementation, except for when you are **passing parameters that must be computed**: 
```fs
// this won't work
proxy.call <@ fun server -> server.echoNumber (1 + 1) @> 
// nor this
proxy.call <@ fun server -> server.sumAll [1 .. 10] @> 
```
both sub-expressions `(1 + 1)` and `[1 .. 10]` must be computed before sending them to the server, but inside a quoutation expression, it is just there as parsed code that is yet to be compiled and the proxy doesn't always know how to evaluate them, there are many types of these expression that won't work right out of the box but the workaround is very simple: **bind the parameter to a value before passing it to the quotation expression**. So to make them work, just get the inline parameter out to it's own value:
```fs
// this works now
let input = 1 + 1
proxy.call <@ fun server -> server.echoNumber input @> 
// this works too
let numbers = [1 .. 10]
proxy.call <@ fun server -> server.sumAll numbers @> 
```
