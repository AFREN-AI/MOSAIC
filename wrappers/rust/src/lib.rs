//! Build MOSAIC workflow plan JSON. Execution remains in the trusted .NET host.

use serde::Serialize;
use serde_json::{Map, Value, json};
use std::collections::HashSet;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PlanError(pub String);

#[derive(Debug, Clone)]
pub struct WorkflowBuilder {
    steps: Vec<Step>,
}

#[derive(Debug, Clone, Serialize)]
struct Step {
    id: String,
    tool: String,
    arguments: Map<String, Value>,
}

impl WorkflowBuilder {
    pub fn new() -> Self { Self { steps: Vec::new() } }

    pub fn add_step(&mut self, id: &str, tool: &str, arguments: Map<String, Value>) -> Result<&mut Self, PlanError> {
        if id.trim().is_empty() || tool.trim().is_empty() {
            return Err(PlanError("step id and tool are required".into()));
        }
        if self.steps.iter().any(|step| step.id == id) {
            return Err(PlanError(format!("duplicate step id: {id}")));
        }
        let earlier: HashSet<String> = self.steps.iter().map(|step| step.id.clone()).collect();
        validate_references(&Value::Object(arguments.clone()), &earlier)?;
        self.steps.push(Step { id: id.into(), tool: tool.into(), arguments });
        Ok(self)
    }

    pub fn to_json(&self, output: &str) -> Result<String, PlanError> {
        if self.steps.is_empty() || !self.steps.iter().any(|step| step.id == output) {
            return Err(PlanError("output must name a declared step".into()));
        }
        serde_json::to_string(&json!({"steps": self.steps, "output": output}))
            .map_err(|error| PlanError(format!("could not serialize plan: {error}")))
    }
}

impl Default for WorkflowBuilder { fn default() -> Self { Self::new() } }

pub fn reference(step_id: &str) -> Value { json!({"$ref": step_id}) }

fn validate_references(value: &Value, earlier: &HashSet<String>) -> Result<(), PlanError> {
    match value {
        Value::Object(object) => {
            if object.len() == 1 && object.contains_key("$ref") {
                let id = object["$ref"].as_str().ok_or_else(|| PlanError("reference id must be a string".into()))?;
                if !earlier.contains(id) { return Err(PlanError("references must point to an earlier step".into())); }
                return Ok(());
            }
            for child in object.values() { validate_references(child, earlier)?; }
        }
        Value::Array(items) => for child in items { validate_references(child, earlier)?; },
        _ => {}
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn emits_shared_plan_shape_and_nested_reference() {
        let mut builder = WorkflowBuilder::new();
        builder.add_step("lookup", "catalog.lookup", Map::from_iter([("key".into(), json!("a1"))])).unwrap();
        builder.add_step("format", "text.format", Map::from_iter([("record".into(), json!({"nested": reference("lookup")}))])).unwrap();
        let plan: Value = serde_json::from_str(&builder.to_json("format").unwrap()).unwrap();
        assert_eq!(plan["steps"][1]["arguments"]["record"]["nested"]["$ref"], "lookup");
    }

    #[test]
    fn rejects_forward_reference() {
        let mut builder = WorkflowBuilder::new();
        let args = Map::from_iter([("value".into(), reference("future"))]);
        assert!(builder.add_step("first", "tool", args).is_err());
    }
}
