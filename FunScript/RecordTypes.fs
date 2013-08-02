﻿module internal FunScript.RecordTypes

open AST
open Quote
open Microsoft.FSharp.Quotations
open System.Reflection

let genComparisonFunc t =
   let fields = Objects.getFields t
   let that = Var("that", typeof<obj>)
   let diff = Var("diff", typeof<obj>)
      
   let body =
      List.foldBack (fun (name, t) acc ->
         let thisField = PropertyGet(This, name)
         let thatField = PropertyGet(Reference that, name)
         let compareDecls, compareExpr = 
            Comparison.compareCall t thisField t thatField |> Option.get
         [  
            yield! compareDecls
            yield Assign(Reference diff, compareExpr)
            yield IfThenElse(
               BinaryOp(Reference diff, "!=", Number 0.),
               Block [ Return <| Reference diff ],
               Block acc)
         ]) fields [ Return <| Number 0. ]
      
   Lambda(
      [that],
      Block <| DeclareAndAssign(diff, Number 0.) :: body
   )

let genComparisonMethods t =
    // TODO: What about overriden comparability
    let func = genComparisonFunc t
    [ "CompareTo", func ]

let private getRecordVars recType =
   Objects.getFields recType
   |> Seq.map fst
   |> Seq.map (fun name -> Var(name, typeof<obj>))
   |> Seq.toList

let private createConstructor recType compiler =
   let vars = getRecordVars recType
   vars, Block [  
      for var in vars do yield Assign(PropertyGet(This, var.Name), Reference var)
   ]

let private creation =
   CompilerComponent.create <| fun (|Split|) compiler returnStategy ->
      function
      | Patterns.NewRecord(recType, exprs) when recType.Name = typeof<Ref<obj>>.Name ->
         let decls, refs = 
            exprs 
            |> List.map (fun (Split(valDecl, valRef)) -> valDecl, valRef)
            |> List.unzip
         let propNames = getRecordVars recType |> List.map (fun v -> v.Name)
         let fields = List.zip propNames refs
         [  yield! decls |> Seq.concat 
            yield returnStategy.Return <| Object fields
         ]
      | PatternsExt.NewRecord(recType, exprs) ->
         let decls, refs = 
            exprs 
            |> List.map (fun (Split(valDecl, valRef)) -> valDecl, valRef)
            |> List.unzip
         let name = Reflection.getRecordConstructorName compiler recType
         let cons = 
            compiler.DefineGlobal name (fun var -> 
               [ 
                  yield Assign(Reference var, Lambda <| createConstructor recType compiler) 
                  let comparisonMethods = genComparisonMethods recType
                  let proto = PropertyGet(Reference var, "prototype")
                  for name, lambda in comparisonMethods do
                     yield Assign(PropertyGet(proto, name), lambda)
               ]
            )
         [ yield! decls |> Seq.concat 
           yield returnStategy.Return <| New(cons.Name, refs)
         ]
      | _ -> []

let components = [ creation ]