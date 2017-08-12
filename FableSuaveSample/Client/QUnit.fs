module QUnit

open System
open Fable.Core
open Fable.Core.JsInterop

type ModuleHooks = 
    abstract before : unit -> unit
    abstract beforeEach : unit -> unit
    abstract afterEach : unit -> unit
    abstract after : unit -> unit

type TestResult<'b> = 
    abstract result : bool
    abstract actual : 'b
    abstract expected : 'b
    abstract message : string

type LogResult = 
    /// The boolean result of an assertion, true means passed, false means failed.
    abstract result : bool
    /// One side of a comparision assertion. Can be undefined when ok() is used.
    abstract actual : obj
    /// One side of a comparision assertion. Can be undefined when ok() is used.
    abstract expected : obj
    /// A string description provided by the assertion.
    abstract message : string
    /// The associated stacktrace, either from an exception or pointing to the source of the assertion. Depends on browser support for providing stacktraces, so can be undefined.
    abstract source : string
    /// The test block name of the assertion.
    abstract name : string 
    /// The time elapsed in milliseconds since the start of the containing QUnit.test(), including setup.
    abstract runtime : float
    /// Indicates whether or not this assertion was part of a todo test.
    abstract todo : bool
    abstract testId : string
    abstract negative : bool

type AsyncResult = unit -> unit

type Asserter = 
    /// Instruct QUnit to wait for an asynchronous operation.
    abstract async : unit -> AsyncResult
    /// Instruct QUnit to wait for an asynchronous operation.
    [<Emit("$0.async($1)")>]
    abstract asyncMany : int -> AsyncResult
    /// The equal assertion uses the simple comparison operator (==) to compare the actual and expected arguments. When they are equal, the assertion passes; otherwise, it fails. When it fails, both actual and expected values are displayed in the test result, in addition to a given message.
    [<Emit("$0.equal($1, $2)")>]
    abstract equal : 'a -> 'b -> unit
    /// The equal assertion uses the simple comparison operator (==) to compare the actual and expected arguments. When they are equal, the assertion passes; otherwise, it fails. When it fails, both actual and expected values are displayed in the test result, in addition to a given message.
    [<Emit("$0.equal($1, $2, $3)")>]
    abstract equalWithMsg : 'a -> 'b -> string -> unit 
    /// To ensure that an explicit number of assertions are run within any test, use assert.expect( number ) to register an expected count. If the number of assertions run does not match the expected count, the test will fail.
    abstract expect : int -> unit
    /// The notEqual assertion uses the simple inverted comparison operator (!=) to compare the actual and expected arguments. When they aren’t equal, the assertion passes; otherwise, it fails. When it fails, both actual and expected values are displayed in the test result, in addition to a given message.
    [<Emit("$0.notEqual($1, $2)")>]
    abstract notEqual : 'a -> 'b -> unit
    /// The notEqual assertion uses the simple inverted comparison operator (!=) to compare the actual and expected arguments. When they aren’t equal, the assertion passes; otherwise, it fails. When it fails, both actual and expected values are displayed in the test result, in addition to a given message.
    [<Emit("$0.notEqual($1, $2, $3)")>]
    abstract notEqualWithMsg : 'a -> 'b -> string -> unit 
    /// The most basic assertion in QUnit, ok() requires just one argument. If the argument evaluates to true, the assertion passes; otherwise, it fails. If a second message argument is provided, it will be displayed in place of the result.
    [<Emit("$0.ok($1, $2)")>]
    abstract ok : 'a -> string -> unit
    /// Registers a passing test
    [<Emit("$0.ok(true)")>]
    abstract pass : unit -> unit
    [<Emit("$0.ok(true, $1)")>]
    abstract passWith : string -> unit
    /// Registers a failing test
    [<Emit("$0.ok(false)")>]
    abstract fail : unit -> unit
    [<Emit("$0.ok(false, $1)")>]
    abstract failWith : string -> unit
    [<Emit("$0.ok($1)")>]
    abstract isTrue : bool -> unit
    [<Emit("$0.notOk($1)")>]
    abstract isFalse : bool -> unit
    [<Emit("$0.ok($1 === undefined)")>]
    abstract isUndefined : obj -> unit
    /// The step() assertion registers a passing assertion with a provided message. This makes it easy to check that specific portions of code are being executed, especially in asynchronous test cases and when used with verifySteps(). A step will always pass unless a message is not provided.
    [<Emit("$0.step($1)")>]
    abstract step : string -> unit
    /// The verifySteps() assertion compares a given array of string values (representing steps) and compares them with the order and values of previous step() calls. This assertion is helpful for verifying the order of execution for asynchronous flows.
    [<Emit("$0.verifySteps($1)")>]
    abstract verifyStep : string[] -> unit
    /// The verifySteps() assertion compares a given array of string values (representing steps) and compares them with the order and values of previous step() calls. This assertion is helpful for verifying the order of execution for asynchronous flows.
    [<Emit("$0.verifySteps($1, $2)")>]
    abstract verifyStepWithMsg : string[] -> string -> unit
    [<Emit("$0.throws($1)")>]
    abstract throws : (unit -> unit) -> string -> unit
    [<Emit("$0.strictEqual($1, $2)")>]
    abstract strictEqual : 'a -> 'b -> unit
    [<Emit("$0.propEqual($1, $2)")>]
    abstract propEqual : 'a -> 'b -> unit
    [<Emit("$0.deepEqual($1, $2)")>]
    abstract deepEqual : 'a -> 'b -> unit

[<Emit("QUnit.module($0)")>]
let registerModule (name: string) : unit = jsNative
[<Emit("QUnit.module($0, $1)")>]
let registerModuleWithHooks (name: string) (hooks: ModuleHooks) : unit = jsNative
[<Emit("QUnit.todo($0, $1)")>]
let todo (name: string) (asserter: Asserter -> unit) : unit = jsNative
[<Emit("QUnit.test($0, $1)")>]
let test (name: string) (asserter: Asserter -> unit) : unit = jsNative
[<Emit("QUnit.test($0, $1)")>]
let testAsync (name: string) (asserter: Asserter -> unit) : Async<unit> = jsNative
/// Some test suites may need to express an expectation that is not defined by any of QUnit’s built-in assertions. This need may be met by encapsulating the expectation in a JavaScript function which returns a Boolean value representing the result; this value can then be passed into QUnit’s ok assertion.
[<Emit("QUnit.pushResult($0)")>]
let pushResult<'a> (result: TestResult<'a>) : unit = jsNative
[<Emit("QUnit.skip($0)")>]
let skip (name: string) : unit = jsNative
[<Emit("QUnit.log($0)")>]
let log (callback: LogResult -> unit) : unit = jsNative
/// Specify a global timeout in milliseconds after which all tests will fail with an appropriate message. Useful when async tests aren’t finishing, to prevent the testrunner getting stuck. Set to something high, e.g. 30000 (30 seconds) to avoid slow tests to time out by accident.
[<Emit("QUnit.config.testTimeout = $0")>]
let setTimeout (t: int) : unit = jsNative
