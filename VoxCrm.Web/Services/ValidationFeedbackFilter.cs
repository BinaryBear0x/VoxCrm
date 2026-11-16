using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace VoxCrm.Web.Services;

/// <summary>
/// Many forms are hand-written and neither show model errors nor refill what the user typed, so a rejected
/// save (duplicate phone, invalid value, business rule) looked like nothing happened and emptied the form.
/// When an action answers with a view while the model state has errors, this tells the user with a toast and
/// hands the submitted values to the page so the shared script can put them back.
/// </summary>
public sealed partial class ValidationFeedbackFilter : IResultFilter
{
    public const string AttemptedValuesKey = "__AttemptedValues";

    // Never echo secrets or one-time codes back into a page.
    [GeneratedRegex("password|token|secret|code|key|pin", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveName();

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not ViewResult view || context.Controller is not Controller controller) return;
        var modelState = controller.ModelState;
        if (modelState.IsValid) return;

        if (controller.TempData["Error"] is null)
        {
            var messages = modelState.Values
                .SelectMany(entry => entry.Errors)
                .Select(error => error.ErrorMessage)
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct()
                .Take(3)
                .ToList();
            if (messages.Count > 0)
                controller.TempData["Error"] = string.Join(" ", messages);
        }

        var attempted = new Dictionary<string, string>();
        foreach (var (key, entry) in modelState)
        {
            if (string.IsNullOrEmpty(key) || key.StartsWith("__", StringComparison.Ordinal) || SensitiveName().IsMatch(key)) continue;
            if (entry.AttemptedValue is { Length: > 0 } value && value.Length <= 4000)
                attempted[key] = value;
        }
        if (attempted.Count > 0)
            view.ViewData[AttemptedValuesKey] = JsonSerializer.Serialize(attempted);
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
