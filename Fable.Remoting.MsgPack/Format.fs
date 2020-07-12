module Fable.Remoting.MsgPack.Format

[<Literal>]
let Nil = 0xc0uy
[<Literal>]
let False = 0xc2uy
[<Literal>]
let True = 0xc3uy

let inline fixposnum value = byte value
let inline fixnegnum value = byte value ||| 0b11100000uy
[<Literal>]
let Uint8 = 0xccuy
[<Literal>]
let Uint16 = 0xcduy
[<Literal>]
let Uint32 = 0xceuy
[<Literal>]
let Uint64 = 0xcfuy

[<Literal>]
let Int8 = 0xd0uy
[<Literal>]
let Int16 = 0xd1uy
[<Literal>]
let Int32 = 0xd2uy
[<Literal>]
let Int64 = 0xd3uy

let inline fixstr len = 160uy + byte len
[<Literal>]
let Str8 = 0xd9uy
[<Literal>]
let Str16 = 0xdauy
[<Literal>]
let Str32 = 0xdbuy

[<Literal>]
let Float32 = 0xcauy
[<Literal>]
let Float64 = 0xcbuy

let inline fixarr len = 144uy + byte len
[<Literal>]
let Array16 = 0xdcuy
[<Literal>]
let Array32 = 0xdduy

[<Literal>]
let Bin8 = 0xc4uy
[<Literal>]
let Bin16 = 0xc5uy
[<Literal>]
let Bin32 = 0xc6uy

let inline fixmap len = 128uy + byte len
[<Literal>]
let Map16 = 0xdeuy
[<Literal>]
let Map32 = 0xdfuy
