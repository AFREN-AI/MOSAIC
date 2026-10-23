# frozen_string_literal: true

require "minitest/autorun"
require "json"
require_relative "../lib/mosaic_plan"

class MosaicPlanTest < Minitest::Test
  def test_emits_shared_plan_shape_with_reference
    plan_json = MosaicPlan::WorkflowBuilder.new
      .add_step("lookup", "catalog.lookup", { key: "a1" })
      .add_step("format", "text.format", { record: { nested: MosaicPlan.ref("lookup") } })
      .to_json("format")
    plan = JSON.parse(plan_json)
    assert_equal "format", plan["output"]
    assert_equal "lookup", plan["steps"][1]["arguments"]["record"]["nested"]["$ref"]
  end

  def test_rejects_forward_reference
    assert_raises(ArgumentError) do
      MosaicPlan::WorkflowBuilder.new.add_step("first", "tool", { value: MosaicPlan.ref("later") })
    end
  end
end
