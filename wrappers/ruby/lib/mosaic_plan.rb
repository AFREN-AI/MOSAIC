# frozen_string_literal: true

require "json"

module MosaicPlan
  class StepReference
    attr_reader :step_id

    def initialize(step_id)
      MosaicPlan.require_name(step_id, "reference step id")
      @step_id = step_id
      freeze
    end
  end

  def self.ref(step_id)
    StepReference.new(step_id)
  end

  def self.require_name(value, label)
    raise ArgumentError, "#{label} cannot be empty" unless value.is_a?(String) && !value.strip.empty?
  end
  class WorkflowBuilder
    def initialize
      @steps = []
    end

    def add_step(id, tool, arguments)
      MosaicPlan.require_name(id, "step id")
      MosaicPlan.require_name(tool, "tool name")
      raise ArgumentError, "arguments must be a Hash" unless arguments.is_a?(Hash)
      raise ArgumentError, "duplicate step id: #{id}" if @steps.any? { |step| step[:id] == id }

      earlier = @steps.map { |step| step[:id] }.to_set
      validate_references(arguments, earlier)
      @steps << { id: id, tool: tool, arguments: encode(arguments) }
      self
    end

    def to_json(output_step)
      MosaicPlan.require_name(output_step, "output step")
      raise ArgumentError, "workflow needs at least one step" if @steps.empty?
      raise ArgumentError, "output must name a declared step" unless @steps.any? { |step| step[:id] == output_step }

      JSON.generate({ steps: @steps, output: output_step })
    end

    private

    def validate_references(value, earlier)
      case value
      when StepReference
        raise ArgumentError, "references must point to an earlier step" unless earlier.include?(value.step_id)
      when Hash
        value.each_value { |child| validate_references(child, earlier) }
      when Array
        value.each { |child| validate_references(child, earlier) }
      end
    end

    def encode(value)
      case value
      when StepReference then { "$ref" => value.step_id }
      when Hash then value.transform_values { |child| encode(child) }
      when Array then value.map { |child| encode(child) }
      else value
      end
    end
  end
end

require "set"
