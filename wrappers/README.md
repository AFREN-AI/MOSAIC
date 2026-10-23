# MOSAIC language wrappers

The wrappers are plan-construction libraries that emit the JSON contract consumed by the MOSAIC .NET executor. They do not perform tool execution, capability checks, or communicate with a .NET process. The host remains the security boundary.

| Language | Location | Entry point |
| --- | --- | --- |
| TypeScript and JavaScript | `typescript/` | `WorkflowBuilder`, `ref()` |
| Python | `python/` | `WorkflowBuilder`, `ref()` |
| Go | `go/mosaicplan/` | `Builder`, `Ref()` |
| C | `c/` | `mosaic_plan_write()` |
| C++ | `cpp/` | `mosaic::WorkflowBuilder` |
| Ruby | `ruby/` | `MosaicPlan::WorkflowBuilder`, `MosaicPlan.ref()` |
| Haskell | `haskell/` | `addStep`, `reference`, `renderPlan` |
| Rust | `rust/` | `WorkflowBuilder`, `reference()` |

The shared schema is `../../conformance/plan.schema.json`. All packages are currently version `0.1.0`; the format is stable across these first wrappers, but has not yet reached a `1.0` compatibility commitment. Every wrapper is exercised in hosted CI with a small shared-style conformance test. Package releases and registries are intentionally separate from repository CI.
