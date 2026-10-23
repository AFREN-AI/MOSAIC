// Package mosaicplan builds the shared MOSAIC plan format for a .NET host.
package mosaicplan

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
)

type StepReference struct {
	StepID string
}

func Ref(stepID string) StepReference { return StepReference{StepID: stepID} }

type Step struct {
	ID        string         `json:"id"`
	Tool      string         `json:"tool"`
	Arguments map[string]any `json:"arguments"`
}

type Plan struct {
	Steps  []Step `json:"steps"`
	Output string `json:"output"`
}

type Builder struct{ steps []Step }

func (builder *Builder) AddStep(id, tool string, arguments map[string]any) error {
	if id == "" || tool == "" || arguments == nil {
		return errors.New("step id, tool and arguments are required")
	}
	for _, step := range builder.steps {
		if step.ID == id {
			return fmt.Errorf("duplicate step id: %s", id)
		}
	}
	known := make(map[string]bool, len(builder.steps))
	for _, step := range builder.steps {
		known[step.ID] = true
	}
	if err := validateReferences(arguments, known); err != nil {
		return err
	}
	builder.steps = append(builder.steps, Step{ID: id, Tool: tool, Arguments: arguments})
	return nil
}

func (builder *Builder) JSON(output string) ([]byte, error) {
	if len(builder.steps) == 0 || output == "" {
		return nil, errors.New("workflow steps and output are required")
	}
	found := false
	for _, step := range builder.steps {
		found = found || step.ID == output
	}
	if !found {
		return nil, errors.New("output must name a declared step")
	}
	return json.Marshal(Plan{Steps: builder.steps, Output: output})
}

func validateReferences(value any, earlier map[string]bool) error {
	switch typed := value.(type) {
	case StepReference:
		if !earlier[typed.StepID] { return errors.New("references must point to an earlier step") }
	case map[string]any:
		if len(typed) == 1 {
			if raw, exists := typed["$ref"]; exists {
				id, ok := raw.(string)
				if !ok || !earlier[id] { return errors.New("references must point to an earlier step") }
				return nil
			}
		}
		for _, child := range typed { if err := validateReferences(child, earlier); err != nil { return err } }
	case []any:
		for _, child := range typed { if err := validateReferences(child, earlier); err != nil { return err } }
	case []StepReference:
		for _, child := range typed { if err := validateReferences(child, earlier); err != nil { return err } }
	}
	return nil
}

// MarshalJSON makes references usable inside interface-valued argument maps.
func (reference StepReference) MarshalJSON() ([]byte, error) {
	var buffer bytes.Buffer
	err := json.NewEncoder(&buffer).Encode(map[string]string{"$ref": reference.StepID})
	return bytes.TrimSpace(buffer.Bytes()), err
}
