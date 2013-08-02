﻿[<FunScript.JS>]
[<NUnit.Framework.TestFixture>] 
module FunScript.Tests.Reflection

open FunScript
open NUnit.Framework

[<JS>]
module X =
   open Microsoft.FSharp.Reflection

   let printName<'a>() = 
      typeof<'a>.FullName
   
   let printListName<'a>() = typeof<'a list>.FullName

   let printName2 (x : 'a) = x.GetType().FullName

   let printNames<'a, 'b>() =
      printName2 "abc" + printName<'a>() + printName<'b>()

   let printName3 (x : 'a) (f : 'a -> 'b) =
      printName<'b>() + printName<'a>()

   type Json =
      | JNull
      | JNumber of float
      | JString of string
      | JObject of (string * Json) list
      | JArray of Json list
      member json.Serialize() =
         let rec toStr indent json =
            let innerIndent = indent + "  "
            match json with
            | JNull -> "null"
            | JNumber x -> indent + (x.ToString())
            | JString x -> indent + "\"" + x + "\""
            | JArray xs ->
                  let rows =
                     xs |> List.map (fun x -> indent + toStr innerIndent x)
                     |> List.reduce (fun acc line -> acc + ",\n" + line)
                  indent + "[\n" +
                  rows +
                  indent + "]\n"
            | JObject xs ->
                  let hasPrev = ref false
                  let rows =
                     xs |> List.map (fun (propName, propValue) ->
                        innerIndent + "\"" + propName + "\"" + " : " + toStr "" propValue
                     ) |> List.reduce (fun acc line -> acc + ",\n" + line)
                  indent + "{\n" + 
                  rows + 
                  indent + "}\n"
         toStr "" json

   let rec toJson (t : System.Type) (x : obj) =
      if FSharpType.IsUnion(t) then
         let readTag = FSharpValue.PreComputeUnionTagReader t
         let ucis = FSharpType.GetUnionCases t
         let tagIndex = readTag x
         let uci = ucis.[tagIndex]
         let fields = uci.GetFields()
         let propVals =
            fields |> Array.map (fun pi ->
               let propJson = toJson pi.PropertyType (pi.GetValue(x, [||]))
               pi.Name, propJson)
            |> Array.toList
         let tagVal = "Tag", JNumber(float uci.Tag)
         let allFields = tagVal :: propVals
         JObject allFields
      elif FSharpType.IsRecord(t) then
         let fields = FSharpType.GetRecordFields t
         let propVals =
            fields |> Array.map (fun pi ->
               let propJson = toJson pi.PropertyType (pi.GetValue(x, [||]))
               pi.Name, propJson)
            |> Array.toList
         JObject propVals
      elif t.FullName = typeof<int>.FullName then
         JNumber(float (unbox<int> x))
      elif t.FullName = typeof<float>.FullName then
         JNumber(unbox<float> x)
      elif t.FullName = typeof<string>.FullName then
         JString(unbox x)
      else failwith "Unsupported type"
         
   // Note: This will work in the wild: [<JSEmit("return JSON.parse({0});")>]
   //       but we use the following emit for JInt compatability.
   [<JSEmit("return eval(\"(\" + {0} + \")\");")>]
   let parse(str : string) : obj = failwith "never"
     
   
   // Note: This will work in the wild: [<JSEmit("return {0} != undefined;")>]
   //       but we use the following emit for JInt compatability.
   [<JSEmit("return true;")>] 
   let isDefined (obj : obj) : bool = failwith "never"

   [<JSEmit("return {1}[{0}];")>]
   let tryGet (prop : string) (obj : obj) : obj = failwith "never"
      
   let get prop obj =
      let value = tryGet prop obj
      if isDefined value then value
      else failwith("Couldn't find property: " + prop)

   let rec fromJson (t:System.Type) (jsonObj : obj) =
      if FSharpType.IsUnion t then
         let ucis = FSharpType.GetUnionCases t
         let tag = jsonObj |> get "Tag" :?> int
         let uci = ucis.[tag]
         let args =
            uci.GetFields() |> Array.map (fun pi -> 
               jsonObj |> get pi.Name |> fromJson pi.PropertyType)
         FSharpValue.MakeUnion(uci, args)
      elif FSharpType.IsRecord t then
         let fields = FSharpType.GetRecordFields t
         let args =
            fields |> Array.map (fun pi ->
               jsonObj |> get pi.Name |> fromJson pi.PropertyType)
         FSharpValue.MakeRecord(t, args)
      elif t.FullName = typeof<int>.FullName then
         jsonObj
      elif t.FullName = typeof<float>.FullName then
         jsonObj
      elif t.FullName = typeof<string>.FullName then
         jsonObj
      else failwith ("Unsupported type: " + t.Name)

   let parseJson<'a> (jsonStr : string) =
      let jsonObj = parse jsonStr
      let t = typeof<'a>
      fromJson t jsonObj :?> 'a

[<Test>]
let ``typeof<ConcreteT>.FullName works``() =
   check  
      <@@ 
         typeof<float>.FullName
      @@>

[<Test>]
let ``typeof<ConcreteCollection<ConcreteT>>.FullName works``() =
   check  
      <@@ 
         typeof<list<float>>.FullName
      @@>

[<Test>]
let ``typeof<'genericT>.FullName works``() =
   check  
      <@@ 
         X.printName<float>() + X.printName<bool>()
      @@>

[<Test>]
let ``typeof<'genericT list>.FullName works``() =
   check  
      <@@ 
         X.printListName<float>() + X.printListName<bool>()
      @@>

[<Test>]
let ``typeof<'genericT>.FullName works when 'genericT is a generic collection``() =
   check  
      <@@ 
         X.printName<float list>() + X.printName<bool list>()
      @@>

[<Test>]
let ``threaded typeof<'genericT>.FullName works``() =
   check  
      <@@ 
         X.printNames<float,bool>()
      @@>

[<Test>]
let ``threaded partially applied typeof<'genericT>.FullName works``() =
   check  
      <@@ 
         X.printName3 1 (fun x -> float x)
      @@>
//
//[<Test>]
//let ``type test works``() =
//   check  
//      <@@ 
//         match box 1 with
//         | :? int as x -> x
//         | _ -> 99
//      @@>

[<Test>]
let ``GetType on concrete argument works``() =
   check  
      <@@ 
         (1.).GetType().FullName + (true).GetType().FullName
      @@>

[<Test>]
let ``GetType on generic argument works``() =
   check  
      <@@ 
         X.printName2 1. + X.printName2 true
      @@>

[<Test>]
let ``GetType on generic collection works``() =
   check  
      <@@ 
         [1.].GetType().FullName + [true].GetType().FullName
      @@>

type Company = { Name : string; Address : string }

type Occupation =
   | Academic
   | Unemployed
   | Employed of Company

type Person = { Name : string; Occupation : Occupation; Age : int }


let createPerson() =
   {
      Name = "Bob Diamonte"
      Age = 1000000
      Occupation = Employed { Name = "Big Bank"; Address = "Centre of the Universe" }
   }

open X

[<Test>]
let ``Serialization works using reflection API``() =
   check  
      <@@ 
         let person = createPerson()
         (toJson typeof<Person> person).Serialize()
      @@>

[<Test>]
let ``Deserialization works using reflection API``() =
   checkAreEqual true 
      <@@ 
         let personA = createPerson()
         let jsonStr = (toJson typeof<Person> personA).Serialize()
         let personB = parseJson<Person> jsonStr
         personA.Name = personB.Name &&
         personA = personB
      @@>



// TODO: Test recursive types serialization