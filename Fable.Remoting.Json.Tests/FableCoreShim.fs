namespace Fable.Core

// Shim attributes for testing the FSharpPojoDUConverter and
// FSharpStringEnumConverter in Fable.Remoting.Json without taking a
// Fable.Core paket reference into the server-side test project. The STJ
// converter (and the Newtonsoft converter it mirrors) detects these
// attributes by FullName match, so any attribute whose FullName is
// `Fable.Core.PojoAttribute` or `Fable.Core.StringEnumAttribute` triggers
// the relevant dispatch path. Real Fable consumers reference the actual
// Fable.Core package; for these tests, the shims suffice.

type PojoAttribute() =
    inherit System.Attribute()

type StringEnumAttribute() =
    inherit System.Attribute()
