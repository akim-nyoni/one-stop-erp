using One4All.Application.Abstractions.Licensing;
using One4All.Core.Common;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Licensing;

/// <summary>Refuses what the installed licence's feature flags do not cover.</summary>
public static class LicenceFeatureGate
{
    /// <summary>Whether a feature may be used, or the refusal.</summary>
    /// <param name="licence">The licence service (null: not licensed here, nothing refused).</param>
    /// <param name="feature">The feature flag.</param>
    /// <param name="what">What was attempted, for the message.</param>
    /// <returns>Success, or why not.</returns>
    public static Result Check(ILicenceService? licence, string feature, string what)
        => licence is null || LicenceFeatures.Allows(licence.Enforced, licence.Current.Payload?.Features, feature) ? Result.Success()
            : Result.Failure(Error.Forbidden("Licence.Feature", $"{what} needs the {feature} feature, which this licence does not include."));
}
