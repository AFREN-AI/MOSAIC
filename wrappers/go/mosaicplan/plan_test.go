package mosaicplan

import (
	"encoding/json"
	"testing"
)

func TestPlanUsesSharedJSONShape(t *testing.T) {
	var builder Builder
	if err := builder.AddStep("lookup", "catalog.lookup", map[string]any{"key": "a1"}); err != nil { t.Fatal(err) }
	if err := builder.AddStep("format", "text.format", map[string]any{"record": map[string]any{"nested": Ref("lookup")}}); err != nil { t.Fatal(err) }
	encoded, err := builder.JSON("format")
	if err != nil { t.Fatal(err) }
	var plan map[string]any
	if err := json.Unmarshal(encoded, &plan); err != nil { t.Fatal(err) }
	steps := plan["steps"].([]any)
	arguments := steps[1].(map[string]any)["arguments"].(map[string]any)
	nested := arguments["record"].(map[string]any)["nested"].(map[string]any)
	if nested["$ref"] != "lookup" { t.Fatalf("unexpected reference: %#v", nested) }
}

func TestRejectsForwardReference(t *testing.T) {
	var builder Builder
	err := builder.AddStep("first", "tool", map[string]any{"ref": Ref("later")})
	if err == nil { t.Fatal("expected forward reference rejection") }
}
