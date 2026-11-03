namespace eShop.BusinessRules.API.Validation;

public class CreateDraftRequestValidator : AbstractValidator<CreateDraftRequest>
{
    public CreateDraftRequestValidator(RuleTypeRegistry registry)
    {
        RuleFor(r => r.RuleTypeId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(id => registry.Find(id) is not null)
            .WithMessage("Rule type '{PropertyValue}' is not registered.");

        // Per-offering scoping is a spec non-goal, so only the default scope is accepted.
        RuleFor(r => r.Scope)
            .Equal(RuleScopes.Default)
            .WithMessage($"Scope must be '{RuleScopes.Default}'.");

        RuleFor(r => r.EffectiveTo)
            .Must((request, effectiveTo) => effectiveTo is null || effectiveTo > request.EffectiveFrom)
            .WithMessage("EffectiveTo must be later than EffectiveFrom.");

        RuleFor(r => r.Parameters)
            .Custom((parameters, context) => AddParameterFailures(registry.Find(context.InstanceToValidate.RuleTypeId ?? string.Empty), parameters, context));
    }

    /// <summary>Adds the rule type's own validation errors for <paramref name="parameters"/> under <c>Parameters</c>.</summary>
    internal static void AddParameterFailures<T>(IRuleType? type, JsonElement parameters, ValidationContext<T> context)
    {
        const string propertyName = nameof(CreateDraftRequest.Parameters);

        if (parameters.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            context.AddFailure(propertyName, "Parameters are required.");
            return;
        }

        if (type is null)
        {
            return;
        }

        foreach (var error in type.Validate(parameters))
        {
            context.AddFailure(propertyName, error);
        }
    }
}
