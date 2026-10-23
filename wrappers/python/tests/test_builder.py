import json
import pytest

from mosaic_plan import WorkflowBuilder, ref


def test_serializes_nested_reference_in_shared_format():
    plan = WorkflowBuilder().add_step("lookup", "catalog.lookup", {"key": "a1"}).add_step(
        "format", "text.format", {"record": {"nested": ref("lookup")}}
    ).to_json("format")
    assert json.loads(plan) == {
        "steps": [
            {"id": "lookup", "tool": "catalog.lookup", "arguments": {"key": "a1"}},
            {"id": "format", "tool": "text.format", "arguments": {"record": {"nested": {"$ref": "lookup"}}}},
        ],
        "output": "format",
    }


def test_rejects_forward_reference():
    with pytest.raises(ValueError, match="earlier step"):
        WorkflowBuilder().add_step("first", "tool", {"ref": ref("later")})
