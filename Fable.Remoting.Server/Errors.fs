namespace Fable.Remoting.Server 


module Errors = 
  let unhandled (funcName: string) = 
    { error = sprintf "Error occured while running the function %s" funcName
      ignored = true 
      handled = false } 

  let ignored (funcName: string) = 
    { error = sprintf "Error occured while running the function %s" funcName
      ignored = true 
      handled = true } 

  let propagated (value: obj) = 
    { error = value
      ignored = false 
      handled = true } 