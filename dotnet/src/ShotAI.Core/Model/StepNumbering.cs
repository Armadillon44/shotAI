namespace ShotAI.Core.Model;

/// <summary>
/// The step numbers the report and every export show (spec 01 2.12), the rule both
/// platforms agree on (<c>src/main/unknown-callout.test.ts:26-34</c>).
/// </summary>
public static class StepNumbering
{
    /// <summary>
    /// One entry per step: null for a text step whose callout is a known kind, otherwise the
    /// next number from 1 in array order. An unknown callout is numbered (#90).
    /// </summary>
    public static int?[] Numbers(IReadOnlyList<ProjectStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var numbers = new int?[steps.Count];
        var n = 0;
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            numbers[i] = step.IsText && CalloutKinds.IsCalloutKind(step.CalloutRaw) ? null : ++n;
        }
        return numbers;
    }
}
