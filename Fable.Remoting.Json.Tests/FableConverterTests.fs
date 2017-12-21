module JsonConverterTests 

open Newtonsoft.Json
open Newtonsoft.Json.Linq
open System
open Fable.Remoting.Json
open Expecto
open Types

let converter = new FableJsonConverter()
let deserialize<'a> (json : string) = 
    if typeof<'a> = typeof<string> then unbox<'a> (box json)
    else JsonConvert.DeserializeObject(json, typeof<'a>, converter) :?> 'a
let serialize (value: 'a) = JsonConvert.SerializeObject(value, converter)

let equal x y = Expect.equal true (x = y) (sprintf "%A = %A" x y)
let pass() = Expect.equal true true ""   
let fail () = Expect.equal false true ""
let converterTest = 
    testList "Converter Tests" [
        testCase "DateTime conversion works" <| fun () -> 
            let date = new DateTime(2017, 03, 23, 18, 30, 0)
            let serialized = serialize date
            let deserialized = deserialize<DateTime> serialized
            Expect.equal 30 deserialized.Minute "Minutes are equal"
            Expect.equal 18 deserialized.Hour "Hours are equal"
            Expect.equal 23 deserialized.Day "Days are equal"
            Expect.equal 3 deserialized.Month "Months are equal"
            Expect.equal 2017 deserialized.Year "Years are equal"

        testCase "Option<string> convertion works" <| fun () ->
            let opt = Some "value"
            let serialized = serialize opt
            let deserialized = deserialize<Option<string>> serialized
            match deserialized with
            | Some "value" -> pass()
            | otherwise -> fail()
    
        testCase "Single case union is unwrapped when serialized" <| fun () -> 
          let customer = { Id = CustomerId(5) }
          let serialized = serialize customer
          // assert that the resulting json has shape { Id: 5 }
          let json = JObject.Parse(serialized)
          let prop = json.Property("Id").Value
          match prop.Value<int>()  with
          | 5 -> pass()
          | otherwise -> fail()

        testCase "Single case union is deserialized correctly" <| fun () ->
          // assert that deserialization works
          let serialized = serialize { Id = CustomerId(5) }
          match deserialize<Customer> serialized with
          | { Id = CustomerId(5) } -> pass()
          | otherwise -> fail()

        testCase "Single case union without types is serialized correctly" <| fun () ->
          let serialized = serialize { Color = ColorType Red }
          // assert that the resulting json has shape { Color: 'Red' }
          let json = JObject.Parse(serialized)
          let prop = json.Property("Color").Value
          match prop.Value<string>()  with
          | "Red" -> pass() 
          | otherwise -> fail()     

        testCase "Single case union without types is deserialized correctly" <| fun () ->
          let serialized = serialize { Color = ColorType Red }
          match deserialize<ColorRecord> serialized with
          | { Color = ColorType Red } -> pass()
          | otherwise -> fail()   

        testCase "Option<int> conversion works" <| fun () -> 
            let opt = Some 5
            let serialized = serialize opt
            let deserialized = deserialize<Option<int>> serialized
            match deserialized with
            | Some 5 -> pass()
            | otherwise -> fail()
    
        testCase "Option<int> deserialization from raw json works" <| fun () -> 
            // what Fable outputs
            match deserialize<Option<int>> "5" with
            | Some 5 -> pass()
            | otherwise -> fail()   

            match deserialize<Option<int>> "null" with
            | None -> pass()
            | otherwise -> fail()
      
        testCase "Nested options conversion works" <| fun () -> 
            let nested = Some(Some (Some 5))
            let serialized = serialize nested
            equal "5" serialized
            let deserialized = deserialize<Option<Option<Option<int>>>> serialized
            match deserialized with
            | Some (Some (Some 5)) -> pass()
            | otherwise -> fail()

        testCase "Deserialize string from string works" <| fun () -> 
            let input = "\"my-test-string\""
            let deserialized = deserialize<string> input
            equal input deserialized

        testCase "Record conversion works" <| fun () ->
            let input : Record = { Prop1 = "value"; Prop2 = 5; Prop3 = None }
            let deserialized = deserialize<Record> (serialize input)
            equal "value" deserialized.Prop1
            equal 5 deserialized.Prop2
            match deserialized.Prop3 with
            | None -> pass()
            | otherwise -> fail()

        testCase "Record deserialization from raw json works" <| fun () -> 
            // let input : Record = { Prop1 = "value"; Prop2 = 5; Prop3 = None }
            // Fable serializes above record to:
            // "{\"Prop1\":\"value\",\"Prop2\":5,\"Prop3\":null}"
            let serialized = "{\"Prop1\":\"value\",\"Prop2\":5,\"Prop3\":null}"
            let deserialized = deserialize<Record> serialized
            equal "value" deserialized.Prop1
            equal 5 deserialized.Prop2
            match deserialized.Prop3 with
            | None -> pass()
            | otherwise -> fail()
         
        testCase "Generic union types conversion works" <| fun () -> 
            let input = Just "value"
            let serialized = serialize input
            let deserialized = deserialize<Maybe<string>> serialized
            match deserialized with
            | Just "value" -> pass()
            | otherwise -> fail()

        testCase "Generic union types deserialization from raw json works" <| fun () -> 
            // toJson (Just 5) = "{\"Just\":5}"
            // toJson Nothing = "\"Nothing\""
            // above is Fable output 
            match deserialize<Maybe<int>> "{\"Just\":5}" with
            | Just 5 -> pass()
            | otherwise -> fail()

            match deserialize<Maybe<int>> "\"Nothing\"" with
            | Nothing -> pass()
            | otherwise -> fail()
            
            // Serialized "Nothing" is generic
            match deserialize<Maybe<string>> "\"Nothing\"" with
            | Nothing -> pass()
            | otherwise -> fail()
            
        testCase "Deserialization with provided type at runtime works" <| fun () -> 
            let inputType = typeof<Option<int>>
            let json = "5"
            let parameterTypes = [| typeof<string>; typeof<System.Type> ; typeof<JsonConverter array> |]
            let deserialize = typeof<JsonConvert>.GetMethod("DeserializeObject", parameterTypes) 
            equal true ((not << isNull) deserialize) 

            let result = deserialize.Invoke(null, [| json; inputType; [| converter |] |])
            match result with
            | :? Option<int> as opt -> 
                  match opt with 
                  | Some 5 -> pass()
                  | otherwise -> fail()
            | otherwise -> fail()
    ]