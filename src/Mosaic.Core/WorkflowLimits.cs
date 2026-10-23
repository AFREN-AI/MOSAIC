namespace Mosaic.Core;

/// <summary>Resource bounds applied by the trusted host for each workflow.</summary>
public sealed record WorkflowLimits(int MaximumSteps = 12, int MaximumInputBytes = 32_768, int MaximumOutputBytes = 65_536)
{
    internal void Validate()
    {
        if (MaximumSteps is < 1 or > 100 || MaximumInputBytes is < 1 or > 1_048_576 || MaximumOutputBytes is < 1 or > 1_048_576)
        {
            throw new ArgumentOutOfRangeException(nameof(WorkflowLimits), "Workflow limits are outside the supported range.");
        }
    }
}
