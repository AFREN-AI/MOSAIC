export type StepReference = { readonly $ref: string };
export type JsonValue = null | boolean | number | string | JsonValue[] | { [key: string]: JsonValue | StepReference };
export type StepArguments = { readonly [key: string]: JsonValue | StepReference };

type WorkflowStep = { id: string; tool: string; arguments: StepArguments };

/** Builds the shared MOSAIC JSON plan format; execution remains in the trusted .NET host. */
export class WorkflowBuilder {
  private readonly steps: WorkflowStep[] = [];

  addStep(id: string, tool: string, args: StepArguments): this {
    requireName(id, "step id");
    requireName(tool, "tool name");
    if (this.steps.some((step) => step.id === id)) throw new Error(`Duplicate step id: ${id}`);
    validateReferences(args, new Set(this.steps.map((step) => step.id)));
    this.steps.push({ id, tool, arguments: structuredClone(args) });
    return this;
  }

  toJSON(outputStep: string): string {
    requireName(outputStep, "output step");
    if (!this.steps.some((step) => step.id === outputStep)) throw new Error("Output must name a declared step");
    if (this.steps.length === 0) throw new Error("A workflow needs at least one step");
    return JSON.stringify({ steps: this.steps, output: outputStep });
  }
}

/** Creates a reference to the complete result of an earlier step. */
export function ref(stepId: string): StepReference {
  requireName(stepId, "reference step id");
  return { $ref: stepId };
}

function requireName(value: string, label: string): void {
  if (!value.trim()) throw new Error(`${label} cannot be empty`);
}

function validateReferences(value: unknown, earlierSteps: Set<string>): void {
  if (Array.isArray(value)) {
    for (const item of value) validateReferences(item, earlierSteps);
    return;
  }
  if (value !== null && typeof value === "object") {
    const record = value as Record<string, unknown>;
    if (Object.keys(record).length === 1 && "$ref" in record) {
      if (typeof record["$ref"] !== "string" || !earlierSteps.has(record["$ref"])) {
        throw new Error("References must point to an earlier step");
      }
      return;
    }
    for (const child of Object.values(record)) validateReferences(child, earlierSteps);
  }
}
