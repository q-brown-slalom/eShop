namespace eShop.BusinessRules.API.Validation;

/// <summary>
/// Validates a draft update. The draft's rule type is not part of the request, so callers pass it in
/// <see cref="ValidationContext{T}.RootContextData"/> under <see cref="RuleTypeIdKey"/>.
/// </summary>
public class UpdateDraftRequestValidator : AbstractValidator<UpdateDraftRequest>
{
    public const string RuleTypeIdKey = "RuleTypeId";

    public UpdateDraftRequestValidator(RuleTypeRegistry registry)
    {
        RuleFor(r => r.EffectiveTo)
            .Must((request, effectiveTo) => effectiveTo is null || effectiveTo > request.EffectiveFrom)
            .WithMessage("EffectiveTo must be later than EffectiveFrom.");

        RuleFor(r => r.Parameters)
            .Custom((parameters, context) =>
            {
                if (!context.RootContextData.TryGetValue(RuleTypeIdKey, out var ruleTypeId) || ruleTypeId is not string id)
                {
                    throw new InvalidOperationException($"'{RuleTypeIdKey}' must be supplied in the validation context.");
                }

                var type = registry.Find(id);
                if (type is null)
                {
                    context.AddFailure(RuleTypeIdKey, $"Rule type '{id}' is not registered.");
                }

                CreateDraftRequestValidator.AddParameterFailures(type, parameters, context);
            });
    }
}
