"""Build MOSAIC plan JSON. Plans are executed by the trusted .NET host."""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class StepReference:
    step_id: str


def ref(step_id: str) -> StepReference:
    _require_name(step_id, "reference step id")
    return StepReference(step_id)


class WorkflowBuilder:
    def __init__(self) -> None:
        self._steps: list[dict[str, Any]] = []

    def add_step(self, step_id: str, tool: str, arguments: dict[str, Any]) -> WorkflowBuilder:
        _require_name(step_id, "step id")
        _require_name(tool, "tool name")
        if any(step["id"] == step_id for step in self._steps):
            raise ValueError(f"Duplicate step id: {step_id}")
        earlier = {step["id"] for step in self._steps}
        _validate_references(arguments, earlier)
        self._steps.append({"id": step_id, "tool": tool, "arguments": _encode(arguments)})
        return self

    def to_json(self, output_step: str) -> str:
        _require_name(output_step, "output step")
        if not self._steps:
            raise ValueError("A workflow needs at least one step")
        if output_step not in {step["id"] for step in self._steps}:
            raise ValueError("Output must name a declared step")
        return json.dumps({"steps": self._steps, "output": output_step}, separators=(",", ":"), ensure_ascii=False)


def _require_name(value: str, label: str) -> None:
    if not value.strip():
        raise ValueError(f"{label} cannot be empty")


def _validate_references(value: Any, earlier: set[str]) -> None:
    if isinstance(value, StepReference):
        if value.step_id not in earlier:
            raise ValueError("References must point to an earlier step")
        return
    if isinstance(value, dict):
        for child in value.values():
            _validate_references(child, earlier)
    if isinstance(value, (list, tuple)):
        for child in value:
            _validate_references(child, earlier)


def _encode(value: Any) -> Any:
    if isinstance(value, StepReference):
        return {"$ref": value.step_id}
    if isinstance(value, dict):
        return {key: _encode(child) for key, child in value.items()}
    if isinstance(value, (list, tuple)):
        return [_encode(child) for child in value]
    return value
