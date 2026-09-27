using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class FamilyOnboardingLifecycleServiceTests
{
    private IFamilyOnboardingStore _onboarding = null!;
    private IProductFamilyStore _families = null!;
    private FamilyOnboardingLifecycleService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _onboarding = Substitute.For<IFamilyOnboardingStore>();
        _families = Substitute.For<IProductFamilyStore>();
        _service = new FamilyOnboardingLifecycleService(_onboarding, _families);
    }

    [Test]
    public async Task Should_return_the_activated_family_after_a_successful_approval()
    {
        _onboarding.Approve(1, Arg.Any<CancellationToken>()).Returns(true);
        var activated = new ProductFamilyView(1, "k", "N", "k", DateTime.UtcNow, null, State: FamilyState.Active);
        _families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(activated);

        var result = await _service.Approve(1, CancellationToken.None);

        Assert.That(result, Is.SameAs(activated));
    }

    [Test]
    public async Task Should_return_null_when_approval_fails_because_the_onboarding_row_is_missing()
    {
        _onboarding.Approve(1, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _service.Approve(1, CancellationToken.None);

        Assert.That(result, Is.Null);
        await _families.DidNotReceive().GetFamily(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_delegate_rejection_to_the_onboarding_store()
    {
        _onboarding.Reject(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _service.Reject(1, CancellationToken.None);

        Assert.That(result, Is.True);
    }
}
